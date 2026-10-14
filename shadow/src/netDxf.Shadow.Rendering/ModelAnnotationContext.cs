using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public enum ModelAnnotationMode { Drawing, Disabled, Enabled }

/// <summary>Named-scale units, not a parsed display label. For 1:50 use PaperUnits=1, DrawingUnits=50.</summary>
public sealed record AnnotationScale(double PaperUnits, double DrawingUnits)
{
    public double Multiplier
    {
        get
        {
            var value = DrawingUnits / PaperUnits;
            if (!(PaperUnits > 0 && DrawingUnits > 0 && value > 0) ||
                !double.IsFinite(PaperUnits) || !double.IsFinite(DrawingUnits) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(AnnotationScale), "Annotation units and their ratio must be positive and finite.");
            return value;
        }
    }
}

/// <summary>Immutable model-tab context. Explicit Scale overrides captured scale values; named scales
/// are supplied by the host, never guessed by parsing labels. Null RenderCompileOptions.ModelAnnotation
/// disables this layer entirely, as used internally for paper-viewport model compilation.</summary>
public sealed record ModelAnnotationContext(AnnotationScale? Scale = null, ModelAnnotationMode Mode = ModelAnnotationMode.Drawing)
{
    private static readonly ImmutableDictionary<string, AnnotationScale> EmptyScales
        = ImmutableDictionary<string, AnnotationScale>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
    public static ModelAnnotationContext Default { get; } = new();
    public ImmutableDictionary<string, AnnotationScale> NamedScales { get; init; }
        = EmptyScales;

    public ModelAnnotationResolution Resolve(ShadowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(Mode) || NamedScales is null) throw new ArgumentException("Invalid annotation context.");
        if (Scale is not null) _ = Scale.Multiplier;
        if (Mode == ModelAnnotationMode.Disabled) return new(false, 1, "disabled");
        var variables = snapshot.Document["DrawingVariables"];
        var mode = default(ShadowReader); var ratio = default(ShadowReader); var name = default(ShadowReader);
        var count = 0;
        foreach (var variable in variables["CustomVariables"].Items)
        {
            if (++count > 65_536) throw new InvalidOperationException("Annotation header scan budget exceeded.");
            switch (variable["Name"].Text.ToUpperInvariant())
            {
                case "$MSLTSCALE": Set(ref mode, variable); break;
                case "$CANNOSCALEVALUE": Set(ref ratio, variable); break;
                case "$CANNOSCALE": Set(ref name, variable); break;
            }
        }
        // Older drawings opened in AutoCAD start with MSLTSCALE=0; newly created modern drawings use 1.
        var modern = variables["AcadVer"].Text is not ("AutoCad12" or "AutoCad14" or "AutoCad2000" or "AutoCad2004" or "AutoCad2007");
        var enabled = Mode == ModelAnnotationMode.Enabled || (mode.Exists ? ReadMode(mode) : modern);
        if (!enabled) return new(false, 1, "drawing:$MSLTSCALE");
        if (Scale is not null) return new(true, Scale.Multiplier, "host:units");
        if (ratio.Exists)
        {
            if (ratio["GroupCode"].Number() != 40) throw new ArgumentException("$CANNOSCALEVALUE requires a real-valued header.");
            var value = ratio["Value"].Number(double.NaN);
            return new(true, new AnnotationScale(value, 1).Multiplier, "drawing:$CANNOSCALEVALUE");
        }
        if (name.Exists)
        {
            if (name["GroupCode"].Number() is not (1 or 2)) throw new ArgumentException("$CANNOSCALE requires a string-valued header.");
            var label = name["Value"].Text;
            if (NamedScales.TryGetValue(label, out var scale) && scale is not null)
                return new(true, scale.Multiplier, "host:named:" + label);
            // The standard default has known units. Arbitrary names, even '1:50', do not encode units.
            if (label == "1:1") return new(true, 1, "default:1:1");
            return new(true, 1, "unresolved:" + label,
                $"Annotation scale '{label}' is not present in the host's named scale catalog; linetypes remain unscaled.");
        }
        return new(true, 1, "default:1:1");

        static void Set(ref ShadowReader slot, ShadowReader variable)
        {
            if (slot.Exists) throw new ArgumentException("Duplicate annotation header variable.");
            slot = variable;
        }
        static bool ReadMode(ShadowReader variable)
        {
            var value = variable["Value"].Number(double.NaN);
            if (variable["GroupCode"].Number() != 70 || value is not (0 or 1))
                throw new ArgumentException("$MSLTSCALE requires an Int16 zero or one.");
            return value == 1;
        }
    }
}

public sealed record ModelAnnotationResolution(bool Enabled, double Multiplier, string Source, string? Diagnostic = null);
