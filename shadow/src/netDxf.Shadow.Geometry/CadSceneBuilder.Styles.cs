using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed partial class CadSceneBuilder
{
    private static CadStyle ResolveStyle(ShadowReader entity, ShadowReader layer, CadStyle? parent, double globalScale)
    {
        var color = entity["Color"];
        uint argb;
        if (color["IsByBlock"].Boolean()) argb = parent?.Argb ?? CadStyle.Default.Argb;
        else
        {
            if (!color.Exists || color["IsByLayer"].Boolean()) color = layer["Color"];
            argb = 0xff000000u | ((uint)color["R"].Number(228) << 16) | ((uint)color["G"].Number(232) << 8) | (uint)color["B"].Number(239);
            if (!color["UseTrueColor"].Boolean() && color["Index"].Number() == 7) argb = CadStyle.Default.Argb;
        }
        var transparency = entity["Transparency"];
        var alpha = transparency["IsByBlock"].Boolean() ? (parent?.Argb >> 24 ?? 255u) :
            (uint)Math.Round(255 * (1 - Math.Clamp((transparency["IsByLayer"].Boolean() ? layer["Transparency"] : transparency)["Value"].Number(), 0, 90) / 100));
        argb = (argb & 0xffffffu) | alpha << 24;
        var weight = entity["Lineweight"];
        if (weight.Text == "ByLayer") weight = layer["Lineweight"];
        var width = weight.Text == "ByBlock" ? parent?.Lineweight ?? 0.25 : ParseLineweight(weight.Text);
        var linetype = entity["Linetype"];
        if (string.Equals(linetype["Name"].Text, "ByLayer", StringComparison.OrdinalIgnoreCase)) linetype = layer["Linetype"];
        var entityScale = entity["LinetypeScale"].Number(1);
        var dash = string.Equals(linetype["Name"].Text, "ByBlock", StringComparison.OrdinalIgnoreCase)
            ? (parent?.Dashes ?? []).Select(d => d * entityScale).ToImmutableArray()
            : linetype["Segments"].Items.Select(s => s["Length"].Number() * entityScale * globalScale).ToImmutableArray();
        var visible = entity["IsVisible"].Boolean(true) && layer["IsVisible"].Boolean(true) && !layer["IsFrozen"].Boolean() && (parent?.Visible ?? true);
        if (entity["Flags"].Text.Contains("Hidden", StringComparison.Ordinal)) visible = false;
        return new(argb, width, dash, visible, layer["Name"].Text);
    }

    private static CadStyle ResolveLayerStyle(ShadowReader layer, double globalScale)
    {
        var color = layer["Color"];
        var rgb = (uint)color["R"].Number(228) << 16 | (uint)color["G"].Number(232) << 8 | (uint)color["B"].Number(239);
        if (!color["UseTrueColor"].Boolean() && color["Index"].Number() == 7) rgb = CadStyle.Default.Argb & 0xffffff;
        var alpha = (uint)Math.Round(255 * (1 - Math.Clamp(layer["Transparency"]["Value"].Number(), 0, 90) / 100));
        return new(rgb | alpha << 24, ParseLineweight(layer["Lineweight"].Text),
            layer["Linetype"]["Segments"].Items.Select(s => s["Length"].Number() * globalScale).ToImmutableArray(),
            layer["IsVisible"].Boolean(true) && !layer["IsFrozen"].Boolean(), layer["Name"].Text);
    }

    private static double ParseLineweight(string value)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) return number >= 0 ? number / 100 : 0.25;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, out number) ? number / 100 : 0.25;
    }
    private static double UnitMeters(string unit) => unit switch
    {
        "Inches" => 0.0254, "Feet" => 0.3048, "Miles" => 1609.344, "Millimeters" => 0.001,
        "Centimeters" => 0.01, "Meters" => 1, "Kilometers" => 1000, "Microinches" => 2.54e-8,
        "Mils" => 2.54e-5, "Yards" => 0.9144, "Angstroms" => 1e-10, "Nanometers" => 1e-9,
        "Microns" => 1e-6, "Decimeters" => 0.1, "Decameters" => 10, "Hectometers" => 100,
        "Gigameters" => 1e9, "AstronomicalUnits" => 149597870700, "LightYears" => 9.4607304725808e15,
        "Parsecs" => 3.0856775814913673e16, _ => 1
    };
}
