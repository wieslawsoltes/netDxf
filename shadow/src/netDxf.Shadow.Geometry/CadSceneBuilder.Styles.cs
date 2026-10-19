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
        var byBlock = string.Equals(linetype["Name"].Text, "ByBlock", StringComparison.OrdinalIgnoreCase);
        var dash = byBlock ? (parent?.Dashes ?? []).Select(d => d * entityScale).ToImmutableArray()
            : linetype["Segments"].Items.Select(s => s["Length"].Number() * entityScale * globalScale).ToImmutableArray();
        var normal = entity["Normal"].Vector(Vec3.UnitZ);
        var complex = byBlock ? parent?.Linetype?.MultiplyScale(entityScale) : CadLinetype.Read(linetype, entityScale * globalScale, normal);
        if (complex is not null && complex.Normal != normal) complex = complex with { Normal = normal };
        var visible = entity["IsVisible"].Boolean(true) && layer["IsVisible"].Boolean(true) && !layer["IsFrozen"].Boolean() && (parent?.Visible ?? true);
        if (entity["Flags"].Text.Contains("Hidden", StringComparison.Ordinal)) visible = false;
        return new(argb, width, dash, visible, layer["Name"].Text) { Linetype = complex };
    }

    private static CadStyle ResolveLayerStyle(ShadowReader layer, double globalScale)
    {
        var color = layer["Color"];
        var rgb = (uint)color["R"].Number(228) << 16 | (uint)color["G"].Number(232) << 8 | (uint)color["B"].Number(239);
        if (!color["UseTrueColor"].Boolean() && color["Index"].Number() == 7) rgb = CadStyle.Default.Argb & 0xffffff;
        var alpha = (uint)Math.Round(255 * (1 - Math.Clamp(layer["Transparency"]["Value"].Number(), 0, 90) / 100));
        return new(rgb | alpha << 24, ParseLineweight(layer["Lineweight"].Text),
            layer["Linetype"]["Segments"].Items.Select(s => s["Length"].Number() * globalScale).ToImmutableArray(),
            layer["IsVisible"].Boolean(true) && !layer["IsFrozen"].Boolean(), layer["Name"].Text)
            { Linetype = CadLinetype.Read(layer["Linetype"], globalScale, Vec3.UnitZ) };
    }

    private static double ParseLineweight(string value)
    {
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) return number >= 0 ? number / 100 : 0.25;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return double.TryParse(digits, out number) ? number / 100 : 0.25;
    }
}
