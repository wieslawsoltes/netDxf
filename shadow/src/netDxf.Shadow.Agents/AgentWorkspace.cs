using System.Text;
using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents;

public sealed record AgentFile(string Path, string Content, long Version, DateTimeOffset ModifiedAt);
public sealed record AgentFileInfo(string Path, long Version, int Characters, DateTimeOffset ModifiedAt,
    bool IsBinary = false, long? Bytes = null);
public sealed record AgentFileSlice(string Path, long Version, string Content, int Offset, int TotalCharacters, bool HasMore);
public sealed record AgentSearchMatch(string Path, int Line, string Text);
/// <summary>A null content deletes a file; version zero creates a previously absent file.</summary>
public sealed record AgentFileChange(string Path, string? Content, long ExpectedVersion);

/// <summary>A host can back the workspace with disk, a repository, IndexedDB or another store.</summary>
public interface IAgentWorkspace
{
    IReadOnlyList<AgentFileInfo> List();
    AgentFileSlice Read(string path, int offset = 0, int length = 16000);
    AgentFile Write(string path, string content, long? expectedVersion = null);
    void Delete(string path, long expectedVersion);
    IReadOnlyList<AgentSearchMatch> Search(string text, string? prefix = null, int limit = 100);
    IReadOnlyList<AgentFile> Export();
    void Import(IReadOnlyList<AgentFile> files);
}

/// <summary>Applies a group of version-checked edits atomically, including capacity validation.</summary>
public interface IAgentTransactionalWorkspace : IAgentWorkspace
{
    IReadOnlyList<AgentFileInfo> Apply(IReadOnlyList<AgentFileChange> changes);
}

/// <summary>Bounded, transactional UTF-8 workspace with compare-and-swap writes.</summary>
public sealed class MemoryAgentWorkspace(int maxFiles = 512, int maxBytes = 16 * 1024 * 1024) : IAgentTransactionalWorkspace
{
    private readonly object gate = new();
    private Dictionary<string, AgentFile> files = new(StringComparer.Ordinal);
    private long nextVersion;
    private int bytes;

    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > 512 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
            throw new ArgumentException("Use a relative workspace path with forward slashes.", nameof(path));
        var parts = path.Split('/');
        if (parts.Any(part => part is "" or "." or "..")) throw new ArgumentException("Empty, current and parent path segments are not allowed.", nameof(path));
        return path;
    }
    public IReadOnlyList<AgentFileInfo> List()
    {
        lock (gate) return files.Values.OrderBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => new AgentFileInfo(f.Path, f.Version, f.Content.Length, f.ModifiedAt)).ToArray();
    }
    public AgentFileSlice Read(string path, int offset = 0, int length = 16000)
    {
        path = NormalizePath(path);
        if (offset < 0 || length is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (gate)
        {
            if (!files.TryGetValue(path, out var file)) throw new FileNotFoundException("Workspace file does not exist.", path);
            if (offset > file.Content.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            var count = Math.Min(length, file.Content.Length - offset);
            return new(path, file.Version, file.Content.Substring(offset, count), offset, file.Content.Length, offset + count < file.Content.Length);
        }
    }
    public AgentFile Write(string path, string content, long? expectedVersion = null)
    {
        path = NormalizePath(path); ArgumentNullException.ThrowIfNull(content);
        if (content.Contains('\0')) throw new ArgumentException("Workspace files must be UTF-8 text without NUL characters.", nameof(content));
        lock (gate)
        {
            files.TryGetValue(path, out var before);
            if (expectedVersion.HasValue && (before?.Version ?? 0) != expectedVersion)
                throw new InvalidOperationException("Workspace file changed. Read its current version before editing.");
            var size = checked(bytes - (before is null ? 0 : Encoding.UTF8.GetByteCount(before.Content)) + Encoding.UTF8.GetByteCount(content));
            if (size > maxBytes || before is null && files.Count >= maxFiles) throw new InvalidOperationException("Workspace capacity exceeded. Export and remove unused files.");
            var file = new AgentFile(path, content, checked(++nextVersion), DateTimeOffset.UtcNow);
            files[path] = file; bytes = size; return file;
        }
    }
    public void Delete(string path, long expectedVersion)
    {
        path = NormalizePath(path);
        lock (gate)
        {
            if (!files.TryGetValue(path, out var before) || before.Version != expectedVersion)
                throw new InvalidOperationException("Workspace file changed or no longer exists.");
            files.Remove(path); bytes -= Encoding.UTF8.GetByteCount(before.Content);
        }
    }
    public IReadOnlyList<AgentSearchMatch> Search(string text, string? prefix = null, int limit = 100)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (text.Length > 8192 || limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var matches = new List<AgentSearchMatch>();
        lock (gate)
        {
            foreach (var file in files.Values.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                if (prefix is not null && !file.Path.StartsWith(prefix, StringComparison.Ordinal)) continue;
                using var reader = new StringReader(file.Content); string? line; var index = 0;
                while ((line = reader.ReadLine()) is not null)
                {
                    index++;
                    if (line.Contains(text, StringComparison.OrdinalIgnoreCase)) matches.Add(new(file.Path, index, line[..Math.Min(line.Length, 1000)]));
                    if (matches.Count >= limit) return matches;
                }
            }
        }
        return matches;
    }
    public IReadOnlyList<AgentFile> Export() { lock (gate) return files.Values.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(); }
    public IReadOnlyList<AgentFileInfo> Apply(IReadOnlyList<AgentFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count > maxFiles * 2) throw new ArgumentException("Too many workspace changes.", nameof(changes));
        lock (gate)
        {
            var candidate = new Dictionary<string, AgentFile>(files, StringComparer.Ordinal);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var next = nextVersion; var total = bytes; var now = DateTimeOffset.UtcNow;
            foreach (var change in changes)
            {
                ArgumentNullException.ThrowIfNull(change);
                var path = NormalizePath(change.Path);
                if (!paths.Add(path) || change.ExpectedVersion < 0) throw new ArgumentException("Duplicate path or invalid workspace version.", nameof(changes));
                candidate.TryGetValue(path, out var previous);
                if ((previous?.Version ?? 0) != change.ExpectedVersion)
                    throw new InvalidOperationException($"Workspace file changed: {path}. Read its current version before applying changes.");
                if (change.Content is null)
                {
                    if (previous is null) throw new InvalidOperationException($"Workspace file no longer exists: {path}.");
                    candidate.Remove(path); total -= Encoding.UTF8.GetByteCount(previous.Content);
                }
                else
                {
                    if (change.Content.Contains('\0')) throw new ArgumentException("Workspace files must be UTF-8 text without NUL characters.", nameof(changes));
                    total = checked(total - (previous is null ? 0 : Encoding.UTF8.GetByteCount(previous.Content)) + Encoding.UTF8.GetByteCount(change.Content));
                    candidate[path] = new(path, change.Content, checked(++next), now);
                }
            }
            // Check the final transaction, so a replacement can release capacity for another file.
            if (candidate.Count > maxFiles || total > maxBytes)
                throw new InvalidOperationException("Workspace capacity exceeded. Export and remove unused files.");
            files = candidate; bytes = total; nextVersion = next;
            return List();
        }
    }
    public void Import(IReadOnlyList<AgentFile> incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.Count > maxFiles) throw new ArgumentException("Too many workspace files.");
        var candidate = new Dictionary<string, AgentFile>(StringComparer.Ordinal); var total = 0;
        foreach (var file in incoming)
        {
            var path = NormalizePath(file.Path);
            if (file.Content is null || file.Content.Contains('\0') || file.Version < 1 || !candidate.TryAdd(path, file)) throw new ArgumentException("Invalid, binary or duplicate workspace file.");
            total = checked(total + Encoding.UTF8.GetByteCount(file.Content));
            if (total > maxBytes) throw new ArgumentException("Workspace import exceeds its byte budget.");
        }
        lock (gate)
        {
            // Never resurrect an old CAS version when replacing a live workspace.
            var installed = new Dictionary<string, AgentFile>(StringComparer.Ordinal);
            foreach (var file in candidate.Values) installed.Add(file.Path, file with { Version = checked(++nextVersion) });
            files = installed; bytes = total;
        }
    }
}

public static class AgentWorkspaceTools
{
    public static void Register(McpToolRegistry registry, IAgentWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(workspace);
        Register("workspace_list", "List the agent's workspace files, versions and sizes.", "{}", [], true, _ => workspace.List());
        Register("workspace_read", "Read a bounded character range. Use returned version for writes and offset for paging.",
            """{"path":{"type":"string"},"offset":{"type":"integer","minimum":0},"length":{"type":"integer","minimum":1,"maximum":1000000}}""", ["path"], true,
            a => workspace.Read(Text(a, "path"), Int(a, "offset", 0), Int(a, "length", 16000)));
        Register("workspace_write", "Create (expectedVersion 0) or replace a complete workspace file using its current version.",
            """{"path":{"type":"string"},"content":{"type":"string"},"expectedVersion":{"type":"integer","minimum":0}}""", ["path", "content", "expectedVersion"], false,
            a => { var f = workspace.Write(Text(a, "path"), Text(a, "content"), a.GetProperty("expectedVersion").GetInt64()); return new AgentFileInfo(f.Path, f.Version, f.Content.Length, f.ModifiedAt); });
        Register("workspace_patch", "Replace exactly one occurrence of oldText. Fails on missing or ambiguous matches. Requires current file version.",
            """{"path":{"type":"string"},"oldText":{"type":"string","minLength":1},"newText":{"type":"string"},"expectedVersion":{"type":"integer","minimum":1}}""", ["path", "oldText", "newText", "expectedVersion"], false,
            a =>
            {
                var path = Text(a, "path"); var oldText = Text(a, "oldText"); var replacement = Text(a, "newText");
                ArgumentException.ThrowIfNullOrEmpty(oldText);
                var file = workspace.Read(path, 0, 1000000);
                if (file.HasMore) throw new InvalidOperationException("Patch a file of at most one million characters, or replace it in full.");
                var offset = file.Content.IndexOf(oldText, StringComparison.Ordinal);
                if (offset < 0 || file.Content.IndexOf(oldText, offset + 1, StringComparison.Ordinal) >= 0)
                    throw new ArgumentException("oldText must identify exactly one occurrence. Include more surrounding lines.");
                var result = workspace.Write(path, file.Content[..offset] + replacement + file.Content[(offset + oldText.Length)..], a.GetProperty("expectedVersion").GetInt64());
                return new AgentFileInfo(result.Path, result.Version, result.Content.Length, result.ModifiedAt);
            });
        Register("workspace_search", "Search literal text across workspace files; bounded matches include paths and line numbers.",
            """{"text":{"type":"string","minLength":1},"prefix":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":1000}}""", ["text"], true,
            a => workspace.Search(Text(a, "text"), a.TryGetProperty("prefix", out var p) ? p.GetString() : null, Int(a, "limit", 100)));
        Register("workspace_delete", "Delete a workspace file after checking its version.",
            """{"path":{"type":"string"},"expectedVersion":{"type":"integer","minimum":1}}""", ["path", "expectedVersion"], false,
            a => { workspace.Delete(Text(a, "path"), a.GetProperty("expectedVersion").GetInt64()); return new { deleted = true }; });

        void Register(string name, string description, string properties, string[] required, bool readOnly, Func<JsonElement, object> action)
        {
            var schema = JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":" + properties + ",\"required\":" + AgentJson.String(required) + "}");
            registry.Register(new(name, description, schema, readOnly), (args, ct) =>
            { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new McpToolResult(AgentJson.String(action(args)))); });
        }
    }
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() ?? throw new ArgumentException($"Missing {property}.");
    private static int Int(JsonElement value, string property, int fallback) => value.TryGetProperty(property, out var p) ? p.GetInt32() : fallback;
}
