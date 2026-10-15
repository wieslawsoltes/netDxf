namespace netDxf.Shadow.Studio;

public sealed record CadNewDocumentRequest(string Name = "Drawing.dxf", bool Demo = false);
public sealed record CadCloseDocumentRequest(long Revision, bool Discard = false);

/// <summary>Bounded multi-document lifetime manager. Holding the hub gate prevents a close from
/// disposing a renderer while an API operation uses it. The legacy DI-owned sample is borrowed.</summary>
public sealed class StudioDocumentHub : IDisposable
{
    private sealed class Entry(string name, Studio studio, bool owned)
    {
        public string Name = name;
        public readonly Studio Studio = studio;
        public readonly bool Owned = owned;
        public (Guid Document, long Revision) Saved = studio.CadStamp;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private bool disposed;
    public StudioDocumentHub(Studio legacy) { entries.Add("demo", new("Process loop.dxf", legacy, false)); }
    public object Documents()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return entries.Select(p => new { id = p.Key, name = p.Value.Name, dirty = p.Value.Studio.CadStamp != p.Value.Saved || p.Value.Studio.CadHasBranches }).ToArray();
        }
    }
    public object Create(CadNewDocumentRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (entries.Count >= 8) throw new InvalidOperationException("The local workspace allows eight open drawings. Close one before opening another.");
            var name = Name(request.Name); var studio = new Studio(request.Demo); var id = Guid.NewGuid().ToString("N");
            entries.Add(id, new(name, studio, true)); return new { id, name };
        }
    }
    public T Use<T>(string id, Func<Studio, T> action)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(id, out var entry)) throw new ShadowConflictException("The drawing has been closed.");
            return action(entry.Studio);
        }
    }
    public object Close(string id, HttpRequest http, CadCloseDocumentRequest request)
    {
        lock (gate)
        {
            return Use(id, studio => StudioHttp.Execute(studio, http, () =>
            {
                var entry = entries[id]; var stamp = studio.CadStamp;
                if (studio.CadRevision != request.Revision) throw new ShadowConflictException("The drawing changed before close.");
                if (!request.Discard && (stamp != entry.Saved || studio.CadHasBranches)) throw new InvalidOperationException("Save or explicitly discard the drawing changes before closing.");
                entries.Remove(id); if (entry.Owned) entry.Studio.Dispose(); return Documents();
            }));
        }
    }
    // Retain the original public signature and all existing host/receipt behavior.
    public byte[] Save(string id, HttpRequest http, long revision) => Save(id, http, revision, static bytes => bytes);

    /// <summary>Encode detached DXF before advancing the saved stamp. Encoding failure
    /// preserves dirty state and is never retried implicitly.</summary>
    public byte[] Save(string id, HttpRequest http, long revision, Func<byte[], byte[]> encode)
    {
        ArgumentNullException.ThrowIfNull(encode);
        lock (gate)
        {
            return Use(id, studio => StudioHttp.Execute(studio, http, () =>
            {
                var bytes = studio.Save(revision);
                var encoded = encode(bytes);
                if (encoded is null || encoded.Length == 0) throw new InvalidOperationException("The drawing encoder returned no output.");
                entries[id].Saved = studio.CadStamp; return encoded;
            }));
        }
    }
    public object Load(string id, HttpRequest http, Stream stream, long revision, string name)
    {
        lock (gate)
        {
            var safeName = Name(name);
            return Use(id, studio => StudioHttp.Execute(studio, http, () =>
            {
                studio.Load(stream, revision); entries[id].Name = safeName; entries[id].Saved = studio.CadStamp; return studio.CadState();
            }));
        }
    }
    private static string Name(string value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(c => char.IsControl(c) || c is '/' or '\\'))
            throw new ArgumentException("Drawing names require 1–120 printable characters and no path separators.");
        return name;
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true;
            foreach (var entry in entries.Values) if (entry.Owned) entry.Studio.Dispose();
            entries.Clear();
        }
    }
}
