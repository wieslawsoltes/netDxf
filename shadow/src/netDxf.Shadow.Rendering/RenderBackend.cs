using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering;

public readonly record struct Viewport(Vec3 Center, double PixelsPerUnit, int Width, int Height)
{
    public Vec3 ToScreen(Vec3 point) => new Vec3((point.X - Center.X) * PixelsPerUnit + Width / 2.0, (Center.Y - point.Y) * PixelsPerUnit + Height / 2.0);
    public Vec3 ToWorld(Vec3 point) => new Vec3((point.X - Width / 2.0) / PixelsPerUnit + Center.X, Center.Y - (point.Y - Height / 2.0) / PixelsPerUnit);
    public Bounds2 WorldBounds => new Bounds2(Center.X - Width / (2 * PixelsPerUnit), Center.Y - Height / (2 * PixelsPerUnit), Center.X + Width / (2 * PixelsPerUnit), Center.Y + Height / (2 * PixelsPerUnit));
    public void Validate()
    {
        if (Width is < 1 or > 16384 || Height is < 1 or > 16384 || (long)Width * Height > 64_000_000 || !Center.IsFinite || !double.IsFinite(PixelsPerUnit) || PixelsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(Viewport), "Invalid render size or camera.");
    }
    public static Viewport Fit(Bounds2 bounds, int width, int height, double margin = 0.1)
    {
        if (bounds.IsEmpty) bounds = new Bounds2(-50, -50, 50, 50);
        var scale = Math.Min(width / Math.Max(bounds.Width, 1), height / Math.Max(bounds.Height, 1)) / (1 + Math.Max(0, margin) * 2);
        var result = new Viewport(bounds.Center, scale, width, height); result.Validate(); return result;
    }
}
public sealed record RenderFrame(RenderScene Scene, Viewport Viewport, ImmutableHashSet<string>? Selection = null,
    uint Background = 0xff151923, uint SelectionColor = 0xffffbc5c)
{
    public IEnumerable<RenderItem> VisibleItems() => Scene.Index.Query(Viewport.WorldBounds).Order().Select(i => Scene.Items[i]).Where(i => i.Style.Visible);
}
public sealed record RenderImage(byte[] Bytes, string ContentType);
public interface IRenderBackend : IDisposable
{
    string Id { get; }
    RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default);
}

/// <summary>Serializes rendering and backend replacement, so an in-flight backend is never disposed.</summary>
public sealed class RenderHost : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, Func<IRenderBackend>> factories;
    private IRenderBackend backend;
    private bool disposed;
    public RenderHost(IEnumerable<KeyValuePair<string, Func<IRenderBackend>>> factories, string initial)
    {
        this.factories = factories.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        backend = this.factories.TryGetValue(initial, out var factory) ? factory() : throw new ArgumentException("Unknown backend.", nameof(initial));
    }
    public string Backend { get { lock (gate) return backend.Id; } }
    public ImmutableArray<string> Backends => factories.Keys.Order(StringComparer.Ordinal).ToImmutableArray();
    public void Swap(string id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (string.Equals(id, backend.Id, StringComparison.OrdinalIgnoreCase)) return;
            if (!factories.TryGetValue(id, out var factory)) throw new ArgumentException("Unknown rendering backend.", nameof(id));
            var next = factory(); var old = backend; backend = next; old.Dispose();
        }
    }
    public RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); return backend.Render(frame, cancellationToken); }
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; backend.Dispose(); } }
}
