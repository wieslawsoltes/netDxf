using System.Collections.Immutable;
using netDxf.Collections;

namespace netDxf.Shadow.NetDxf;

/// <summary>
/// Shares detached scalar-only state without merging source object identities.
/// The bounded session cache never retains source objects or reference-bearing maps.
/// </summary>
internal sealed class ShadowCapturePropertyCache
{
    internal readonly record struct CacheKey(byte Kind, ulong State, int? StoredValue = null);

    private const int Capacity = 256;
    private readonly Dictionary<CacheKey, ImmutableDictionary<string, ShadowValue>> properties = new();
    private static readonly bool ColorShape = HasMembers(typeof(AciColor),
        "B", "G", "Index", "IsByBlock", "IsByLayer", "R", "UseTrueColor");
    private static readonly bool TransparencyShape = HasMembers(typeof(Transparency),
        "IsByBlock", "IsByLayer", "StoredAlphaValue", "Value");
    private static readonly bool EmptyDataShape = HasMembers(typeof(XDataDictionary),
        "AppIds", "Count", "IsReadOnly", "Values");

    internal static CacheKey Key(object source, Type type)
    {
        // Exact types are intentional: subclasses may add state or override getters.
        if (ColorShape && type == typeof(AciColor))
        {
            var color = (AciColor)source;
            return new(1, color.R | (ulong)color.G << 8 | (ulong)color.B << 16 |
                (ulong)(ushort)color.Index << 24 | (color.UseTrueColor ? 1UL << 40 : 0));
        }
        if (TransparencyShape && type == typeof(Transparency))
        {
            var transparency = (Transparency)source;
            return new(2, (ushort)transparency.Value, transparency.StoredAlphaValue);
        }
        if (EmptyDataShape && type == typeof(XDataDictionary) && ((XDataDictionary)source).Count == 0)
            return new(3, 0);
        return default;
    }

    internal bool TryGet(CacheKey key, out ImmutableDictionary<string, ShadowValue> fields)
    {
        if (key.Kind != 0 && properties.TryGetValue(key, out var cached))
        {
            fields = cached;
            return true;
        }
        fields = null!;
        return false;
    }

    internal void Add(CacheKey key, ImmutableDictionary<string, ShadowValue> fields)
    {
        if (key.Kind != 0 && properties.Count < Capacity) properties.TryAdd(key, fields);
    }

    // New public members automatically disable a specialization until its complete
    // state key is updated. Capture continues through the generic public-state path.
    private static bool HasMembers(Type type, params string[] names) => SourceAccessors.For(type)
        .Select(static accessor => accessor.Name).SequenceEqual(names, StringComparer.Ordinal);
}
