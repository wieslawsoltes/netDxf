using netDxf.Entities;

namespace netDxf.Shadow.NetDxf;

/// <summary>Public compound mutators for values with no independent writable setters.</summary>
internal sealed record SourceMethodPlan(Action Apply, Action Rollback)
{
    internal static SourceMethodPlan? Create(object source, ShadowObject before, ShadowObject after)
    {
        if (source is not Ellipse ellipse ||
            ShadowValue.Equivalent(before["MajorAxis"], after["MajorAxis"]) &&
            ShadowValue.Equivalent(before["MinorAxis"], after["MinorAxis"])) return null;
        double Number(string name) => after[name] is ShadowValue.Atom atom &&
            double.TryParse(atom.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value) ? value : throw new ArgumentException("Ellipse axes must be finite numeric values.");
        var major = Number("MajorAxis"); var minor = Number("MinorAxis");
        var clone = (Ellipse)ellipse.Clone(); clone.SetAxis(major, minor); // Preflight before live mutation.
        var oldMajor = ellipse.MajorAxis; var oldMinor = ellipse.MinorAxis;
        return new(() => ellipse.SetAxis(major, minor), () => ellipse.SetAxis(oldMajor, oldMinor));
    }
}
