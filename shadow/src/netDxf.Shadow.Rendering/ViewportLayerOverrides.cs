using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace netDxf.Shadow.Rendering;

/// <summary>Optional layer values in a viewport. Null inherits the drawing value. Colors are 24-bit RGB,
/// lineweights are millimeters and transparency is a percentage in 0..90. Frozen=false only thaws a
/// viewport-local freeze; it never makes a globally frozen or invisible layer visible.</summary>
public sealed record ViewportLayerOverride(uint? Rgb = null, double? LineweightMillimeters = null,
    double? TransparencyPercent = null, ObjectId? Linetype = null, bool? Frozen = null, bool? Plot = null)
{
    internal void Validate()
    {
        if (Rgb is > 0xffffff || LineweightMillimeters is { } width && (!double.IsFinite(width) || width < 0 || width > 1000) ||
            TransparencyPercent is { } alpha && (!double.IsFinite(alpha) || alpha < 0 || alpha > 90) ||
            Linetype is { } id && string.IsNullOrEmpty(id.Value))
            throw new ArgumentException("Invalid viewport layer override.");
    }
}

/// <summary>An immutable layer profile. Structural equality lets equivalent viewports share model compilation.</summary>
public sealed class ViewportLayerProfile : IEquatable<ViewportLayerProfile>
{
    public static ViewportLayerProfile Empty { get; } = new([]);
    public ImmutableDictionary<ObjectId, ViewportLayerOverride> Layers { get; }
    private readonly int hash;
    public ViewportLayerProfile(IEnumerable<KeyValuePair<ObjectId, ViewportLayerOverride>> layers, int maximumLayers = 4096)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (maximumLayers < 1) throw new ArgumentOutOfRangeException(nameof(maximumLayers));
        var values = ImmutableDictionary.CreateBuilder<ObjectId, ViewportLayerOverride>();
        foreach (var (id, value) in layers)
        {
            if (string.IsNullOrEmpty(id.Value) || value is null) throw new ArgumentException("A layer identity and value are required.", nameof(layers));
            value.Validate();
            if (values.Count >= maximumLayers) throw new InvalidOperationException("Viewport layer profile budget exceeded.");
            if (!values.TryAdd(id, value)) throw new ArgumentException("Duplicate viewport layer identity.", nameof(layers));
            hash ^= HashCode.Combine(id, value);
        }
        Layers = values.ToImmutable();
        hash = HashCode.Combine(hash, Layers.Count);
    }
    public bool Equals(ViewportLayerProfile? other) => ReferenceEquals(this, other) || other is not null && hash == other.hash &&
        Layers.Count == other.Layers.Count && Layers.All(pair => other.Layers.TryGetValue(pair.Key, out var value) && pair.Value == value);
    public override bool Equals(object? obj) => obj is ViewportLayerProfile other && Equals(other);
    public override int GetHashCode() => hash;
}

public sealed record ViewportOverrideLimits(int MaximumProfiles = 256, int MaximumLayerEntries = 65_536);

/// <summary>Document-bound immutable viewport appearance. These are consumer settings, not source edits
/// or a decoder for producer-specific layer extension dictionaries. Hosts may translate those dictionaries
/// into profiles. No native resource or mutable source reference is retained.</summary>
public sealed class ViewportOverrideCatalog
{
    public Guid DocumentId { get; }
    public ImmutableDictionary<ObjectId, ViewportLayerProfile> Profiles { get; }
    /// <summary>When true, nonplotting layers are excluded from paper model views. Screen display defaults to false.</summary>
    public bool ForPlot { get; }
    public ViewportOverrideLimits Limits { get; }
    private sealed class Views { internal readonly Dictionary<ViewportLayerProfile, ShadowSnapshot> Values = new(); }
    private readonly ConditionalWeakTable<ShadowSnapshot, Views> views = new();

    public ViewportOverrideCatalog(Guid documentId, IEnumerable<KeyValuePair<ObjectId, ViewportLayerProfile>> profiles,
        bool forPlot = false, ViewportOverrideLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (documentId == Guid.Empty) throw new ArgumentException("A source document identity is required.", nameof(documentId));
        Limits = limits ?? new();
        if (Limits.MaximumProfiles < 1 || Limits.MaximumLayerEntries < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        var values = ImmutableDictionary.CreateBuilder<ObjectId, ViewportLayerProfile>();
        long total = 0;
        foreach (var (id, profile) in profiles)
        {
            if (string.IsNullOrEmpty(id.Value) || profile is null) throw new ArgumentException("A viewport identity and profile are required.", nameof(profiles));
            total += profile.Layers.Count;
            if (values.Count >= Limits.MaximumProfiles || total > Limits.MaximumLayerEntries)
                throw new InvalidOperationException("Viewport override catalog budget exceeded.");
            if (!values.TryAdd(id, profile)) throw new ArgumentException("Duplicate viewport profile.", nameof(profiles));
        }
        DocumentId = documentId; Profiles = values.ToImmutable(); ForPlot = forPlot;
    }

    internal ViewportLayerProfile For(ObjectId viewport) => Profiles.GetValueOrDefault(viewport, ViewportLayerProfile.Empty);

    internal void Validate(ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (snapshot.DocumentId != DocumentId) throw new ArgumentException("Viewport overrides belong to a different document.");
        foreach (var (viewport, profile) in Profiles)
        {
            token.ThrowIfCancellationRequested();
            if (snapshot.Read(viewport).Type != "Viewport") throw new ArgumentException($"Unknown viewport override target {viewport}.");
            foreach (var (layer, value) in profile.Layers)
            {
                token.ThrowIfCancellationRequested();
                if (snapshot.Read(layer).Type != "Layer") throw new ArgumentException($"Unknown layer override target {layer}.");
                if (value.Linetype is { } linetype)
                {
                    var reader = snapshot.Read(linetype);
                    var name = reader["Name"].Text;
                    if (reader.Type != "Linetype" || name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException($"A viewport layer needs an explicit existing linetype, not {linetype}.");
                }
            }
        }
    }

    internal PaperViewport Thaw(ShadowSnapshot snapshot, PaperViewport view, ViewportLayerProfile profile)
    {
        var frozen = view.FrozenLayers;
        foreach (var (id, value) in profile.Layers)
            if (value.Frozen == false) frozen = frozen.Remove(snapshot.Read(id)["Name"].Text);
        return ReferenceEquals(frozen, view.FrozenLayers) ? view : view with { FrozenLayers = frozen };
    }

    internal ShadowSnapshot CreateView(ShadowSnapshot snapshot, ViewportLayerProfile profile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (profile.Layers.IsEmpty && !ForPlot) return snapshot;
        var cached = views.GetValue(snapshot, _ => new());
        lock (cached)
        {
            if (cached.Values.TryGetValue(profile, out var retained)) return retained;
            var objects = snapshot.Objects.ToBuilder();
            foreach (var (id, value) in profile.Layers)
            {
                token.ThrowIfCancellationRequested();
                var node = objects[id]; var properties = node.Properties;
                if (value.Rgb is { } rgb)
                    properties = properties.SetItem("Color", new ShadowValue.Structure("netDxf.AciColor",
                        ImmutableDictionary<string, ShadowValue>.Empty
                            .Add("R", ShadowValue.From((double)((rgb >> 16) & 255)))
                            .Add("G", ShadowValue.From((double)((rgb >> 8) & 255)))
                            .Add("B", ShadowValue.From((double)(rgb & 255)))
                            .Add("UseTrueColor", ShadowValue.From(true))
                            .Add("IsByLayer", ShadowValue.From(false)).Add("IsByBlock", ShadowValue.From(false))));
                if (value.TransparencyPercent is { } transparency)
                    properties = properties.SetItem("Transparency", new ShadowValue.Structure("netDxf.Transparency",
                        ImmutableDictionary<string, ShadowValue>.Empty.Add("Value", ShadowValue.From(transparency))
                            .Add("IsByLayer", ShadowValue.From(false)).Add("IsByBlock", ShadowValue.From(false))));
                if (value.LineweightMillimeters is { } weight) properties = properties.SetItem("Lineweight", ShadowValue.From(weight * 100));
                if (value.Linetype is { } linetype) properties = properties.SetItem("Linetype", new ShadowValue.Reference(linetype));
                if (value.Frozen == true) properties = properties.SetItem("IsFrozen", ShadowValue.From(true));
                if (value.Plot is { } plot) properties = properties.SetItem("Plot", ShadowValue.From(plot));
                if (!ReferenceEquals(properties, node.Properties)) objects[id] = node with { Properties = properties };
            }
            if (ForPlot)
            {
                var count = 0;
                foreach (var layer in snapshot.Document["Layers"].Items)
                {
                    token.ThrowIfCancellationRequested();
                    if (++count > Limits.MaximumLayerEntries) throw new InvalidOperationException("Viewport plotting layer budget exceeded.");
                    if (layer.Id is not { } id) continue;
                    var node = objects[id];
                    var plot = profile.Layers.TryGetValue(id, out var value) && value.Plot is { } visible ? visible : layer["Plot"].Boolean(true);
                    if (!plot) objects[id] = node with { Properties = node.Properties.SetItem("IsFrozen", ShadowValue.From(true)) };
                }
            }
            // This is a private rendering view, never a source transaction. It deliberately retains the
            // source receipt and shares all unchanged nodes. No history or source revision is published.
            var result = snapshot with { Objects = objects.ToImmutable() };
            if (cached.Values.Count >= 32) cached.Values.Clear();
            cached.Values[profile] = result;
            return result;
        }
    }
}
