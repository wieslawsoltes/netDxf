using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed record CadStyle(uint Argb, double Lineweight, ImmutableArray<double> Dashes, bool Visible, string Layer)
{
    public static CadStyle Default { get; } = new(0xffe4e8ef, 0.25, [], true, "0");
}
public sealed record CadContour(ImmutableArray<Curve3> Curves, bool Closed = false);
public sealed record CadText(string Content, Vec3 Origin, Vec3 XAxis, Vec3 YAxis, double Height,
    string FontFamily, string Alignment, bool Multiline, double Width);
public sealed record CadImage(string FileName, ImmutableArray<Vec3> Corners);
public sealed record CadGeometry(ImmutableArray<CadContour> Contours, bool Filled = false,
    CadText? Text = null, CadImage? Image = null)
{
    public static CadGeometry Empty { get; } = new([]);
    public CadGeometry Transform(Affine3 matrix) => this with
    {
        Contours = Contours.Select(c => c with { Curves = c.Curves.Select(p => p.Transform(matrix)).ToImmutableArray() }).ToImmutableArray(),
        Text = Text is { } text ? text with { Origin = matrix.Point(text.Origin), XAxis = matrix.Direction(text.XAxis), YAxis = matrix.Direction(text.YAxis) } : null,
        Image = Image is { } image ? image with { Corners = image.Corners.Select(matrix.Point).ToImmutableArray() } : null
    };
}
public sealed record CadOccurrence(string Key, ObjectId Entity, ImmutableArray<ObjectId> InsertPath,
    CadStyle Style, CadGeometry Geometry);
public sealed record CadScene(Guid DocumentId, long Revision, string Layout, ImmutableArray<CadOccurrence> Occurrences,
    ImmutableArray<ShadowDiagnostic> Diagnostics);
public sealed record SceneOptions(string Layout = "Model", int MaximumDepth = 64, int MaximumOccurrences = 1_000_000, bool IncludeInvisible = false);

/// <summary>Custom entity projection has access only to detached shadow values.</summary>
public interface IEntityProjector
{
    bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic);
}

/// <summary>Expands occurrences, never clones or mutates a netDxf entity.</summary>
public sealed class CadSceneBuilder
{
    private readonly ImmutableArray<IEntityProjector> projectors;
    public CadSceneBuilder(IEnumerable<IEntityProjector>? extensions = null) => projectors = (extensions ?? []).Append(new StandardEntityProjector()).ToImmutableArray();

    public CadScene Build(ShadowSnapshot snapshot, SceneOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.MaximumDepth < 1 || options.MaximumOccurrences < 1) throw new ArgumentOutOfRangeException(nameof(options));
        if (!snapshot.Layout(options.Layout).Exists) throw new ArgumentException($"Unknown layout '{options.Layout}'.", nameof(options));
        var output = ImmutableArray.CreateBuilder<CadOccurrence>();
        var diagnostics = snapshot.Diagnostics.ToBuilder();
        var activeBlocks = new HashSet<ObjectId>();
        var visits = 0;
        foreach (var entity in snapshot.DrawingEntities(options.Layout)) Visit(entity, Affine3.Identity, [], string.Empty, null, default, 0);
        return new(snapshot.DocumentId, snapshot.Revision, options.Layout, output.ToImmutable(), diagnostics.ToImmutable());

        void Visit(ShadowReader entity, Affine3 matrix, ImmutableArray<ObjectId> insertPath, string prefix, CadStyle? inherited, ShadowReader inheritedLayer, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visits > options.MaximumOccurrences) throw new InvalidOperationException("Scene occurrence budget exceeded.");
            if (depth > options.MaximumDepth) { diagnostics.Add(new("scene.depth", "Block nesting limit reached.", entity.Id)); return; }
            if (entity.Id is not { } id) return;
            var layer = entity["Layer"];
            if (layer["Name"].Text == "0" && inheritedLayer.Exists) layer = inheritedLayer;
            var style = ResolveStyle(entity, layer, inherited);
            if (!options.IncludeInvisible && !style.Visible) return;
            var key = prefix + id.Value;
            if (entity.Type == "Insert")
            {
                var block = entity["Block"];
                if (block.Id is not { } blockId) { diagnostics.Add(new("insert.block", "Insert has no block.", id)); return; }
                if (!activeBlocks.Add(blockId)) { diagnostics.Add(new("insert.cycle", "Cyclic block reference was not expanded.", id)); return; }
                try
                {
                    var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
                    var rotation = Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180);
                    var factor = UnitMeters(block["Record"]["Units"].Text) / UnitMeters(entity["Owner"]["Record"]["Units"].Text);
                    var local = basis * rotation * Affine3.Scale(entity["Scale"].Vector(new(1, 1, 1)) * factor) * Affine3.Move(-block["Origin"].Vector());
                    var rowValue = Math.Max(1, entity["RowCount"].Number(1));
                    var columnValue = Math.Max(1, entity["ColumnCount"].Number(1));
                    if (!double.IsFinite(rowValue * columnValue) || rowValue * columnValue > options.MaximumOccurrences)
                        throw new InvalidOperationException("Insert array exceeds occurrence budget.");
                    var rows = (int)rowValue; var columns = (int)columnValue;
                    for (var row = 0; row < rows; row++)
                        for (var column = 0; column < columns; column++)
                        {
                            var offset = (basis * rotation).Direction(new(column * entity["ColumnSpacing"].Number(), row * entity["RowSpacing"].Number()));
                            var instance = matrix * Affine3.Move(entity["Position"].Vector() + offset) * local;
                            var cellKey = key + $"[{row},{column}]/";
                            foreach (var child in block["Entities"].Items) Visit(child, instance, insertPath.Add(id), cellKey, style, layer, depth + 1);
                            // Attached ATTRIB positions are already in their owning insert's parent coordinate system.
                            foreach (var attribute in entity["Attributes"].Items)
                                Visit(attribute, matrix * Affine3.Move(offset), insertPath.Add(id), cellKey + "attribute/", style, layer, depth + 1);
                        }
                }
                finally { activeBlocks.Remove(blockId); }
                return;
            }
            // Dimensions carry a generated block in the source database when available.
            if (entity.Type.EndsWith("Dimension", StringComparison.Ordinal) && entity["Block"].Id is { } dimensionBlock)
            {
                if (!activeBlocks.Add(dimensionBlock)) { diagnostics.Add(new("dimension.cycle", "Cyclic dimension block.", id)); return; }
                try { foreach (var child in entity["Block"]["Entities"].Items) Visit(child, matrix, insertPath, key + "/", style, layer, depth + 1); }
                finally { activeBlocks.Remove(dimensionBlock); }
                return;
            }
            foreach (var projector in projectors)
            {
                try
                {
                    if (!projector.TryProject(entity, out var geometry, out var diagnostic)) continue;
                    if (diagnostic is not null) diagnostics.Add(new("geometry.approximation", diagnostic, id));
                    output.Add(new(key, id, insertPath, style, geometry.Transform(matrix)));
                    return;
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    diagnostics.Add(new("geometry.invalid", error.Message, id, DiagnosticSeverity.Error));
                    return;
                }
            }
            diagnostics.Add(new("geometry.unsupported", $"No semantic projector for {entity.Type}; all public source data remains in the snapshot.", id));
        }
    }

    private static CadStyle ResolveStyle(ShadowReader entity, ShadowReader layer, CadStyle? parent)
    {
        var color = entity["Color"];
        uint argb;
        if (color["IsByBlock"].Boolean()) argb = parent?.Argb ?? CadStyle.Default.Argb;
        else
        {
            if (!color.Exists || color["IsByLayer"].Boolean()) color = layer["Color"];
            argb = 0xff000000u | ((uint)color["R"].Number(228) << 16) | ((uint)color["G"].Number(232) << 8) | (uint)color["B"].Number(239);
            // ACI 7 follows a dark-background foreground convention in the sample.
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
        var dash = string.Equals(linetype["Name"].Text, "ByBlock", StringComparison.OrdinalIgnoreCase) ? parent?.Dashes ?? [] :
            linetype["Segments"].Items.Select(s => s["Length"].Number() * entity["LinetypeScale"].Number(1)).ToImmutableArray();
        var visible = entity["IsVisible"].Boolean(true) && layer["IsVisible"].Boolean(true) && !layer["IsFrozen"].Boolean() && (parent?.Visible ?? true);
        if (entity["Flags"].Text.Contains("Hidden", StringComparison.Ordinal)) visible = false;
        return new(argb, width, dash, visible, layer["Name"].Text);
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
