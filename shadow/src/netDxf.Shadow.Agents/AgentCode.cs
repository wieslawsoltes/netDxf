using System.Text;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

/// <summary>Portable input for an isolated JavaScript runtime. Credentials and host objects are never included.</summary>
public sealed record AgentCodeRequest(string Id, string Code, IReadOnlyList<AgentFile> Files,
    IReadOnlyList<string> Arguments, int TimeoutSeconds = 30, int MaximumOutputCharacters = 128000);
public sealed record AgentCodeChange(string Path, string? Content);
public sealed record AgentCodeResult(int ExitCode, string StandardOutput, string StandardError,
    bool Truncated, bool TimedOut, double ElapsedMilliseconds, JsonElement? Result,
    IReadOnlyList<AgentCodeChange> Changes);

/// <summary>
/// Runs code outside the agent's UI and credential context. The runner returns staged edits;
/// only AgentCodeTools commits them after successful completion and version validation.
/// </summary>
public interface IAgentCodeRunner
{
    ValueTask<AgentCodeResult> ExecuteAsync(AgentCodeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Browser-compatible coding tools independent of an operating-system process runner.</summary>
public static class AgentCodeTools
{
    public static void Register(McpToolRegistry registry, IAgentTransactionalWorkspace workspace, IAgentCodeRunner runner)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(runner);
        registry.Register(new("workspace_runtime", "Discover the browser JavaScript runtime, code format, file API and execution limits.",
            McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(McpToolResult.FromObject(new
            {
                runtime = "isolated-javascript-worker", languages = new[] { "JavaScript", "WebAssembly via JavaScript" },
                codeFormat = "Async function body. Use await and return; no Node.js globals, package imports or OS commands.",
                globals = new[] { "workspace.list(prefix?)", "workspace.read(path)", "workspace.write(path, content)", "workspace.delete(path)", "args", "console.log/info/warn/error", "assert(condition, message?)", "WebAssembly" },
                transaction = "Successful runs commit staged files atomically with original versions. Errors, cancellation, timeout or conflicts commit nothing.",
                isolation = "Separate worker in an opaque-origin frame; no Studio DOM, provider keys, browser storage or network. Use the Studio MCP tools for drawing and UI operations.",
                maximumTimeoutSeconds = 120, maximumOutputCharacters = 128000,
                example = "const data = JSON.parse(workspace.read('input.json'));\nassert(Array.isArray(data), 'Expected an array');\nconst total = data.reduce((sum, value) => sum + value, 0);\nworkspace.write('result.json', JSON.stringify({ total }, null, 2));\nconsole.log('Total:', total);\nreturn { total };"
            }));
        });
        registry.Register(new("workspace_run_javascript", "Run JavaScript in the browser worker. Supply exactly one of code (async function body) or path (workspace source file). Use workspace_runtime for the API. Staged file changes commit atomically only on success. Execution requires write permission and is never retried.",
            McpJson.Parse("""{"type":"object","properties":{"code":{"type":"string","minLength":1,"maxLength":1000000},"path":{"type":"string","minLength":1,"maxLength":512},"arguments":{"type":"array","items":{"type":"string","maxLength":16000},"maxItems":256},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":120}},"additionalProperties":false}"""), false), async (args, ct) =>
        {
            var hasCode = args.TryGetProperty("code", out var source); var hasPath = args.TryGetProperty("path", out var path);
            if (hasCode == hasPath) throw new ArgumentException("Supply exactly one of code or path.");
            var before = workspace.Export();
            var code = hasCode ? source.GetString() : before.FirstOrDefault(f => f.Path == MemoryAgentWorkspace.NormalizePath(path.GetString()!))?.Content
                ?? throw new FileNotFoundException("The JavaScript source file does not exist in the workspace.");
            if (string.IsNullOrWhiteSpace(code) || code.Length > 1000000) throw new ArgumentException("JavaScript source must contain 1 to 1,000,000 characters.");
            var arguments = args.TryGetProperty("arguments", out var values) ? values.Deserialize<string[]>(McpJson.Options)! : [];
            var timeout = args.TryGetProperty("timeoutSeconds", out var seconds) ? seconds.GetInt32() : 30;
            var request = new AgentCodeRequest(Guid.NewGuid().ToString("N"), code, before, arguments, timeout);
            var result = await runner.ExecuteAsync(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            result = ValidateResult(result);
            var changed = Array.Empty<AgentFileInfo>();
            if (result.ExitCode == 0 && !result.TimedOut && result.Changes.Count != 0)
            {
                var originals = before.ToDictionary(f => f.Path, StringComparer.Ordinal);
                var changes = result.Changes.Select(change => new AgentFileChange(change.Path, change.Content,
                    originals.TryGetValue(change.Path, out var previous) ? previous.Version : 0)).ToArray();
                var files = workspace.Apply(changes);
                var paths = changes.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
                changed = files.Where(file => paths.Contains(file.Path)).ToArray();
            }
            var output = result.StandardOutput ?? ""; var errors = result.StandardError ?? "";
            var truncated = result.Truncated || output.Length > request.MaximumOutputCharacters || errors.Length > request.MaximumOutputCharacters;
            var response = new
            {
                runtime = "isolated-javascript-worker", result.ExitCode,
                standardOutput = output[..Math.Min(output.Length, request.MaximumOutputCharacters)],
                standardError = errors[..Math.Min(errors.Length, request.MaximumOutputCharacters)],
                truncated, result.TimedOut, result.ElapsedMilliseconds, result.Result,
                committed = result.ExitCode == 0 && !result.TimedOut,
                changedFiles = changed,
                deletedFiles = result.ExitCode == 0 && !result.TimedOut ? result.Changes.Where(c => c.Content is null).Select(c => c.Path).ToArray() : []
            };
            return new(McpJson.Element(response).GetRawText(), result.ExitCode != 0 || result.TimedOut);
        });
    }

    private static AgentCodeResult ValidateResult(AgentCodeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!double.IsFinite(result.ElapsedMilliseconds) || result.ElapsedMilliseconds < 0)
            throw new InvalidOperationException("The code runtime returned invalid elapsed time.");
        if (result.Changes is null || result.Changes.Count > 1024)
            throw new InvalidOperationException("The code runtime returned an invalid change set.");
        var changes = result.Changes.ToArray();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var bytes = 0L;
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            var path = MemoryAgentWorkspace.NormalizePath(change.Path);
            if (!paths.Add(path)) throw new InvalidOperationException("The code runtime returned duplicate file changes.");
            if (change.Content is not null)
            {
                if (change.Content.Contains('\0')) throw new InvalidOperationException("Code file content must be text without NUL bytes.");
                bytes += Encoding.UTF8.GetByteCount(change.Content);
            }
            if (bytes > 16 * 1024 * 1024) throw new InvalidOperationException("Code changes exceed the workspace byte budget.");
        }
        // Validate and detach the value before committing files. Otherwise a disposed or
        // undefined JsonElement, excessive depth, or non-finite timing could fail receipt
        // serialization after the workspace has already changed.
        var detached = McpJson.Element(new { value = result.Result }).GetProperty("value").Clone();
        if (detached.GetRawText().Length > 200000)
            throw new InvalidOperationException("The code runtime result exceeds its value budget.");
        return result with { Result = detached, Changes = changes };
    }
}
