using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

public sealed record AgentCommand(string Executable, IReadOnlyList<string> Arguments,
    string WorkingDirectory = "", int TimeoutSeconds = 120);
public sealed record AgentCommandResult(int ExitCode, string StandardOutput, string StandardError,
    bool Truncated, bool TimedOut, double ElapsedMilliseconds);

/// <summary>Explicit host capability. Browser-only sessions do not silently acquire a shell.</summary>
public interface IAgentCommandRunner
{
    ValueTask<AgentCommandResult> ExecuteAsync(AgentCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Native execution for an explicitly supplied directory; cancellation terminates the process tree.</summary>
[UnsupportedOSPlatform("browser")]
public sealed class LocalAgentCommandRunner : IAgentCommandRunner
{
    private readonly string root;
    private readonly int outputLimit;
    private readonly SemaphoreSlim execution = new(1, 1);
    public LocalAgentCommandRunner(string rootDirectory, int maximumOutputCharacters = 128000)
    {
        if (OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Native execution requires a native host.");
        root = Path.GetFullPath(rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory)));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The command workspace does not exist.");
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("The workspace root must not be a symbolic link.");
        if (maximumOutputCharacters is < 1024 or > 4000000) throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters));
        outputLimit = maximumOutputCharacters;
    }
    public async ValueTask<AgentCommandResult> ExecuteAsync(AgentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentException.ThrowIfNullOrWhiteSpace(command.Executable);
        if (command.Executable.Contains('\0') || command.Arguments is null || command.Arguments.Count > 256 || command.Arguments.Any(a => a is null || a.Contains('\0')) || command.TimeoutSeconds is < 1 or > 3600)
            throw new ArgumentException("Invalid command arguments or timeout.");
        var directory = command.WorkingDirectory.Length == 0 ? root : AgentDirectory.Resolve(root, command.WorkingDirectory, directory: true);
        await execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var start = new ProcessStartInfo(command.Executable)
            {
                WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true
            };
            foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
            using var process = new Process { StartInfo = start };
            var clock = Stopwatch.StartNew();
            if (!process.Start()) throw new InvalidOperationException("The command did not start.");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds));
            using var registration = timeout.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { /* The exit check below confirms whether termination succeeded. */ }
            });
            var stdout = ReadOutput(process.StandardOutput, outputLimit, timeout.Token);
            var stderr = ReadOutput(process.StandardError, outputLimit, timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                catch (TimeoutException error) { throw new InvalidOperationException("The host could not confirm that the command stopped. Inspect running processes before retrying.", error); }
            }
            var output = await stdout.ConfigureAwait(false); var errors = await stderr.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, output.Text, errors.Text, output.Truncated || errors.Truncated,
                timeout.IsCancellationRequested, clock.Elapsed.TotalMilliseconds);
        }
        finally { execution.Release(); }
    }
    private static async Task<(string Text, bool Truncated)> ReadOutput(StreamReader reader, int limit, CancellationToken token)
    {
        var output = new StringBuilder(); var buffer = new char[4096]; var truncated = false;
        while (true)
        {
            int count;
            try { count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { truncated = true; break; }
            if (count == 0) break;
            var keep = Math.Min(count, limit - output.Length);
            if (keep > 0) output.Append(buffer, 0, keep);
            truncated |= keep < count;
        }
        return (output.ToString(), truncated);
    }
}

public static class AgentCommandTools
{
    public static void Register(McpToolRegistry registry, IAgentCommandRunner runner)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(runner);
        registry.Register(new("workspace_execute", "Execute a native build, test, formatter, compiler, git or shell command using the host's explicitly granted process capability. Pass executable and argument array; mutations and shell commands require write permission. No automatic retries.",
            McpJson.Parse("""{"type":"object","properties":{"executable":{"type":"string","minLength":1},"arguments":{"type":"array","items":{"type":"string"},"maxItems":256},"workingDirectory":{"type":"string"},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":3600}},"required":["executable","arguments"],"additionalProperties":false}"""), false), async (args, ct) =>
        {
            var command = args.Deserialize<AgentCommand>(McpJson.Options) ?? throw new ArgumentException("Missing command.");
            var result = await runner.ExecuteAsync(command, ct).ConfigureAwait(false);
            return new McpToolResult(JsonSerializer.Serialize(result, McpJson.Options), result.ExitCode != 0 || result.TimedOut);
        });
    }
}

internal static class AgentDirectory
{
    internal static string Resolve(string root, string relative, bool directory = false)
    {
        MemoryAgentWorkspace.NormalizePath(relative);
        var candidate = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("The path leaves the granted workspace.");
        // Refuse links/reparse points along the entire path; never follow an imported path outside the grant.
        var current = root;
        foreach (var segment in relative.Split('/'))
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("Workspace paths cannot traverse symbolic links or reparse points.");
        }
        if (directory && !Directory.Exists(candidate)) throw new DirectoryNotFoundException("The working directory does not exist.");
        return candidate;
    }
}
