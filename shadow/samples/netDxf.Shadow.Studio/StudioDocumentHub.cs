namespace netDxf.Shadow.Studio;

public sealed record CadNewDocumentRequest(string? Name = null, bool Demo = false);
public sealed record CadCloseDocumentRequest(long Revision, bool Discard = false);

/// <summary>Bounded native document lifetimes. A hub lock prevents close racing an operation.</summary>
public sealed class StudioDocumentHub : IDisposable
{
    private sealed class Entry(Studio studio, string name, bool owned)
    {
        public Studio Studio { get; } = studio;
        public string Name = name;
        public bool Owned { get; } = owned;
        public (string Document, long Revision) Saved = studio.CadStamp;
    }
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private bool disposed;
    public StudioDocumentHub(Studio initial) => entries.Add("demo", new(initial, "Shadow-Demo.dxf", false));
    public object Documents()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return entries.Select(e => new { id = e.Key, name = e.Value.Name,
                dirty = e.Value.Saved != e.Value.Studio.CadStamp || e.Value.Studio.CadHasUnsavedBranches }).ToArray();
        }
    }
    public object Create(CadNewDocumentRequest request)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (entries.Count >= 8) throw new ArgumentException("Close a drawing before opening another (eight-document limit).");
            var name = Name(request.Name ?? $"Drawing{entries.Count + 1}.dxf");
            var id = Guid.NewGuid().ToString("N");
            entries.Add(id, new(new Studio(request.Demo), name, true));
            return new { id, name };
        }
    }
    public T Use<T>(string id, Func<Studio, T> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!entries.TryGetValue(id, out var entry)) throw new ArgumentException("Unknown or closed drawing.");
            return operation(entry.Studio);
        }
    }
    public object Close(string id, HttpRequest http, CadCloseDocumentRequest request)
    {
        lock (gate)
        {
            return Use(id, studio =>
            {
                var entry = entries[id];
                studio.WithRequestState(http.Headers["X-Shadow-Document"].ToString(), http.Headers["X-Shadow-Workspace"].ToString(),
                    http.Headers["X-Shadow-View"].ToString(), () =>
                    {
                        if (request.Revision != studio.CadRevision) throw new ShadowConflictException("Drawing changed before close.");
                        if (!request.Discard && (entry.Saved != studio.CadStamp || studio.CadHasUnsavedBranches)) throw new ShadowConflictException("Save the drawing or explicitly discard its changes and isolated branches before closing.");
                        return true;
                    });
                entries.Remove(id); if (entry.Owned) studio.Dispose();
                return new { closed = id };
            });
        }
    }
    public byte[] Save(string id, HttpRequest http, long revision, Func<byte[], byte[]>? encode = null)
    {
        lock (gate)
        {
            return Use(id, studio => StudioHttp.Execute(studio, http, () =>
            {
                if (!studio.IsLiveWorkspace) throw new ArgumentException("Merge this branch into the live document before saving DXF.");
                var bytes = studio.Save(revision);
                // A failed export must not mark the native drawing as saved.
                if (encode is not null) bytes = encode(bytes);
                entries[id].Saved = studio.CadStamp; return bytes;
            }));
        }
    }
    public object Load(string id, HttpRequest http, Stream stream, long revision, string name)
    {
        lock (gate)
        {
            name = Name(name);
            return Use(id, studio => StudioHttp.Execute(studio, http, () =>
            {
                studio.Load(stream, revision); entries[id].Name = name; entries[id].Saved = studio.CadStamp;
                return studio.CadState();
            }));
        }
    }
    private static string Name(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl)) throw new ArgumentException("Invalid drawing name.");
        name = name.Replace('\\', '/').Split('/').Last();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Invalid drawing name.");
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
