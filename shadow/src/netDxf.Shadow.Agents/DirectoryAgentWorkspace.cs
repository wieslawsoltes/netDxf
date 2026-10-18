using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace netDxf.Shadow.Agents;

/// <summary>
/// UTF-8 files in an explicitly granted native directory. Content stamps detect external edits;
/// individual writes replace staged files atomically. Import is transactional for this instance,
/// preserves unrelated files, and restores previous contents if a write fails.
/// </summary>
[UnsupportedOSPlatform("browser")]
public sealed class DirectoryAgentWorkspace : IAgentWorkspace
{
    private readonly string root;
    private readonly int maxBytes;
    private readonly int maxFiles;
    private readonly object gate = new();
    private readonly Dictionary<string, (string Hash, long Version)> versions = new(StringComparer.Ordinal);
    private long sequence;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public DirectoryAgentWorkspace(string directory, int maximumBytes = 16 * 1024 * 1024, int maximumFiles = 512)
    {
        if (OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Native workspaces require a native host.");
        root = Path.GetFullPath(directory); maxBytes = maximumBytes; maxFiles = maximumFiles;
        if (maxBytes < 1024 || maxFiles < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("The workspace root must not be a symbolic link.");
    }
    private IEnumerable<string> Paths()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = false };
        var pending = new Stack<string>(); pending.Push(root);
        var paths = new List<string>();
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", options))
            {
                var name = Path.GetFileName(entry);
                if (name is ".git" or "bin" or "obj" or "node_modules" || name.StartsWith(".shadow-agent-", StringComparison.Ordinal)) continue;
                if (Directory.Exists(entry)) { pending.Push(entry); continue; }
                paths.Add(Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/'));
                if (paths.Count > maxFiles) throw new InvalidOperationException("The native workspace has more files than the configured budget. Grant a narrower directory or increase maximumFiles.");
            }
        }
        paths.Sort(StringComparer.Ordinal); return paths;
    }
    private (AgentFileInfo Info, string? Content) Inspect(string path)
    {
        var local = AgentDirectory.Resolve(root, path); var info = new FileInfo(local);
        if (!info.Exists) throw new FileNotFoundException("Workspace file does not exist.", path);
        if (info.Length > maxBytes) throw new InvalidOperationException("The file exceeds the workspace byte budget.");
        var bytes = File.ReadAllBytes(local);
        string? content;
        try { content = bytes.Contains((byte)0) ? null : Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { content = null; }
        var hash = Convert.ToHexString(SHA256.HashData(bytes)) + ":" + info.LastWriteTimeUtc.Ticks;
        if (!versions.TryGetValue(path, out var stamp) || stamp.Hash != hash) versions[path] = stamp = (hash, ++sequence);
        return (new(path, stamp.Version, content?.Length ?? 0, info.LastWriteTimeUtc, content is null, bytes.LongLength), content);
    }
    private AgentFile Get(string path)
    {
        var result = Inspect(path);
        if (result.Content is null) throw new InvalidOperationException("This file is binary. Text workspace operations cannot decode it; use an explicitly granted native command for binary operations.");
        return new(path, result.Content, result.Info.Version, result.Info.ModifiedAt);
    }
    public IReadOnlyList<AgentFileInfo> List()
    {
        lock (gate) return Paths().Select(p => Inspect(p).Info).ToArray();
    }
    public AgentFileSlice Read(string path, int offset = 0, int length = 16000)
    {
        if (offset < 0 || length is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(offset));
        lock (gate)
        {
            var file = Get(path);
            if (offset > file.Content.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            var count = Math.Min(length, file.Content.Length - offset);
            return new(path, file.Version, file.Content.Substring(offset, count), offset, file.Content.Length, offset + count < file.Content.Length);
        }
    }
    public AgentFile Write(string path, string content, long? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(content); var local = AgentDirectory.Resolve(root, path);
        if (content.Contains('\0')) throw new ArgumentException("Text workspace files cannot contain NUL characters.", nameof(content));
        if (Utf8.GetByteCount(content) > maxBytes) throw new InvalidOperationException("File exceeds workspace byte budget.");
        lock (gate)
        {
            var before = File.Exists(local) ? Inspect(path).Info : null;
            if (expectedVersion.HasValue && expectedVersion != (before?.Version ?? 0)) throw new InvalidOperationException("The file changed. Read its current version before editing.");
            if (before is null && Paths().Count() >= maxFiles) throw new InvalidOperationException("The workspace file budget is exhausted.");
            long size = Utf8.GetByteCount(content);
            foreach (var existing in Paths()) if (existing != path) size += new FileInfo(AgentDirectory.Resolve(root, existing)).Length;
            if (size > maxBytes) throw new InvalidOperationException("Workspace byte budget exceeded.");
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            var staging = Path.Combine(Path.GetDirectoryName(local)!, ".shadow-agent-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllText(staging, content, Utf8); File.Move(staging, local, overwrite: true); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            return Get(path);
        }
    }
    public void Delete(string path, long expectedVersion)
    {
        lock (gate)
        {
            if (Inspect(path).Info.Version != expectedVersion) throw new InvalidOperationException("The file changed. Read its current version before deleting.");
            File.Delete(AgentDirectory.Resolve(root, path)); versions.Remove(path);
        }
    }
    public IReadOnlyList<AgentSearchMatch> Search(string text, string? prefix = null, int limit = 100)
    {
        lock (gate)
        {
            var snapshot = new MemoryAgentWorkspace(maxFiles, maxBytes);
            snapshot.Import(TextFiles(prefix));
            return snapshot.Search(text, prefix, limit);
        }
    }
    private AgentFile[] TextFiles(string? prefix = null) => Paths().Where(p => prefix is null || p.StartsWith(prefix, StringComparison.Ordinal))
        .Select(Inspect).Where(f => f.Content is not null).Select(f => new AgentFile(f.Info.Path, f.Content!, f.Info.Version, f.Info.ModifiedAt)).ToArray();
    /// <summary>Exports UTF-8 text files; binary files remain in the granted directory and are reported by List.</summary>
    public IReadOnlyList<AgentFile> Export() { lock (gate) return TextFiles(); }
    public void Import(IReadOnlyList<AgentFile> files)
    {
        // Validate every byte/path/duplicate before touching the granted directory.
        var validation = new MemoryAgentWorkspace(maxFiles, maxBytes); validation.Import(files);
        foreach (var file in files)
        {
            AgentDirectory.Resolve(root, file.Path);
            if (file.Content.Contains('\0')) throw new ArgumentException("Text workspace files cannot contain NUL characters.", nameof(files));
            _ = Utf8.GetByteCount(file.Content);
        }
        lock (gate)
        {
            var originals = files.ToDictionary(f => f.Path, f => File.Exists(AgentDirectory.Resolve(root, f.Path)) ? Get(f.Path) : null, StringComparer.Ordinal);
            var applied = new List<string>();
            try
            {
                foreach (var file in files) { Write(file.Path, file.Content); applied.Add(file.Path); }
            }
            catch (Exception failure)
            {
                var rollbackFailures = new List<Exception>();
                foreach (var path in applied.AsEnumerable().Reverse())
                    try
                    {
                        if (originals[path] is { } before) Write(path, before.Content);
                        else { File.Delete(AgentDirectory.Resolve(root, path)); versions.Remove(path); }
                    }
                    catch (Exception rollback) { rollbackFailures.Add(rollback); }
                if (rollbackFailures.Count > 0) throw new AggregateException("Workspace import failed and rollback was incomplete.", new[] { failure }.Concat(rollbackFailures));
                throw;
            }
        }
    }
}
