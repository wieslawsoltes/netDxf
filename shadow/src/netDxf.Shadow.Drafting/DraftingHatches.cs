using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

/// <summary>Pattern-local offset uses along-line X and perpendicular Y, as in a DXF/PAT line family.</summary>
public sealed record DraftingHatchLine(double Angle, Vec3 Origin, Vec3 Delta, ImmutableArray<double> Dashes = default);
public enum DraftingHatchPatternType { UserDefined, Predefined, Custom }
public sealed record DraftingHatchPattern(string Name = "SOLID", double Scale = 1, double Angle = 0,
    Vec3 Origin = default, CadHatchIslandStyle Islands = CadHatchIslandStyle.Normal,
    ImmutableArray<DraftingHatchLine> Lines = default, string Description = "",
    DraftingHatchPatternType Type = DraftingHatchPatternType.Custom, bool IsDouble = false);
public sealed record HatchAuthoringRequest(ImmutableArray<ObjectId> Boundaries = default,
    ImmutableArray<ImmutableArray<Vec3>> Polygons = default, DraftingHatchPattern? Pattern = null, string Layer = "0");

/// <summary>Source-neutral, analytic HATCH authoring and copy-on-write pattern edits.
/// Boundary validation uses the shared bounded island resolver, not browser polygons.</summary>
public static class DraftingHatches
{
    public static ImmutableArray<string> PatternNames { get; } = ["SOLID", "LINE", "CROSS", "DOTS", "DASH"];
    private const int MaximumEdges = 10_000;
    private static ShadowValue N(double value) => ShadowValue.From(value);
    private static ShadowValue V(Vec3 value) => ShadowValue.From(value, true);
    private static ShadowValue R(ObjectId id) => new ShadowValue.Reference(id);
    private static ShadowValue E(string type, string value) => new ShadowValue.Atom("netDxf.Entities." + type, value);
    private static void Finite(Vec3 value)
    {
        if (!value.IsFinite || Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z))) > 1e12)
            throw new ArgumentException("Hatch coordinates exceed the finite drafting range.");
    }

    public static DraftingPlan Create(ShadowSnapshot snapshot, HatchAuthoringRequest request,
        string layout = "Model", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        var entities = request.Boundaries.IsDefault ? [] : request.Boundaries.Distinct().ToImmutableArray();
        var polygons = request.Polygons.IsDefault ? [] : request.Polygons;
        if (entities.Length + polygons.Length is < 1 or > 1024) throw new ArgumentException("Select 1–1024 closed hatch boundaries.");
        var owner = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown drawing layout.");
        var roots = snapshot.Read(owner)["Entities"].Items.Select(e => e.Id).ToHashSet();
        var layer = snapshot.Document["Layers"].Items.FirstOrDefault(l => l["Name"].Text.Equals(request.Layer, StringComparison.OrdinalIgnoreCase));
        if (!layer.Exists || layer["IsLocked"].Boolean() || layer["IsFrozen"].Boolean()) throw new ArgumentException("Choose an unlocked, thawed hatch layer.");
        var edit = new ShadowGraphEdit(snapshot); var paths = ImmutableArray.CreateBuilder<ShadowValue>();
        var contours = ImmutableArray.CreateBuilder<CadContour>(); var edgeCount = 0;
        var projector = new StandardEntityProjector();
        foreach (var id in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = DraftingGuard.Entity(snapshot, id, false); DraftingGuard.Planar(entity);
            if (!roots.Contains(id)) throw new ArgumentException("Hatch boundaries must be direct entities of the active layout.");
            string edgeType; Dictionary<string, ShadowValue> properties;
            switch (entity.Type)
            {
                case "Circle":
                    Finite(entity["Center"].Vector()); RequireXY(entity["Center"].Vector());
                    edgeType = "Arc"; properties = new() { ["Center"] = V(entity["Center"].Vector()),
                        ["Radius"] = N(entity["Radius"].Number()), ["StartAngle"] = N(0), ["EndAngle"] = N(360), ["IsCounterclockwise"] = ShadowValue.From(true) }; break;
                case "Ellipse" when entity["IsFullEllipse"].Boolean() || Math.Abs(entity["EndAngle"].Number() - entity["StartAngle"].Number()) >= 360 - 1e-9:
                    Finite(entity["Center"].Vector()); RequireXY(entity["Center"].Vector());
                    var angle = entity["Rotation"].Number() * Math.PI / 180; var major = entity["MajorAxis"].Number();
                    edgeType = "Ellipse"; properties = new() { ["Center"] = V(entity["Center"].Vector()),
                        ["EndMajorAxis"] = V(new(Math.Cos(angle) * major / 2, Math.Sin(angle) * major / 2)),
                        ["MinorRatio"] = N(entity["MinorAxis"].Number() / major), ["StartAngle"] = N(0),
                        ["EndAngle"] = N(360), ["IsCounterclockwise"] = ShadowValue.From(true) }; break;
                case "Polyline2D" when entity["IsClosed"].Boolean():
                    var vertices = entity["Vertexes"].Items.Take(MaximumEdges + 1).ToArray(); edgeCount += vertices.Length;
                    if (vertices.Length < 2 || edgeCount > MaximumEdges) throw new ArgumentException("Hatch boundary vertex budget exceeded or degenerate boundary.");
                    foreach (var vertex in vertices)
                    {
                        Finite(vertex["Position"].Vector());
                        if (!double.IsFinite(vertex["Bulge"].Number()) || Math.Abs(vertex["Bulge"].Number()) > 1e6) throw new ArgumentException("Invalid hatch bulge.");
                        if (vertex["StartWidth"].Number() != 0 || vertex["EndWidth"].Number() != 0)
                            throw new NotSupportedException("Choose a zero-width closed polyline; hatching the visible ribbon requires explicit region conversion.");
                    }
                    edgeType = "Polyline"; properties = new() { ["IsClosed"] = ShadowValue.From(true),
                        ["Vertexes"] = new ShadowValue.Sequence(vertices.Select(v => ShadowValue.From(new Vec3(v["Position"].Vector().X,
                            v["Position"].Vector().Y, v["Bulge"].Number()))).ToImmutableArray()) }; break;
                default: throw new NotSupportedException("HATCH accepts closed zero-width XY polylines, circles and full ellipses. JOIN a line chain or provide an explicit polygon first.");
            }
            if (++edgeCount > MaximumEdges) throw new ArgumentException("Hatch edge budget exceeded.");
            if (!projector.TryProject(entity, out var geometry, out _) || geometry.Contours.Length != 1 || !geometry.Contours[0].Closed)
                throw new ArgumentException("The source does not have a single closed analytic boundary.");
            contours.Add(geometry.Contours[0]); AddPath(edgeType, properties);
        }
        foreach (var polygon in polygons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (polygon.IsDefault || polygon.Length < 3 || (edgeCount += polygon.Length) > MaximumEdges)
                throw new ArgumentException("Hatch polygon requires at least three vertices within the edge budget.");
            foreach (var point in polygon) { Finite(point); RequireXY(point); }
            var curves = polygon.Select((p, i) => (Curve3)new LineCurve(p, polygon[(i + 1) % polygon.Length])).ToImmutableArray();
            contours.Add(new(curves, true));
            AddPath("Polyline", new() { ["IsClosed"] = ShadowValue.From(true),
                ["Vertexes"] = new ShadowValue.Sequence(polygon.Select(p => ShadowValue.From(p)).ToImmutableArray()) });
        }
        var pattern = request.Pattern ?? new();
        var topology = HatchIslandResolver.Analyze(contours.ToImmutable(),
            new(MaximumContours: 1024, MaximumBoundaryPoints: 100_000), cancellationToken);
        var patternId = Pattern(edit, pattern, topology.Select(pattern.Islands), cancellationToken);
        var hatch = edit.AddEntity(owner, "netDxf.Entities.Hatch", new Dictionary<string, ShadowValue>
        {
            ["Pattern"] = R(patternId), ["BoundaryPaths"] = new ShadowValue.Sequence(paths.ToImmutable()),
            ["Associative"] = ShadowValue.From(false), ["Elevation"] = N(0), ["Normal"] = ShadowValue.From(Vec3.UnitZ),
            ["Layer"] = layer.Value, ["IsVisible"] = ShadowValue.From(true)
        });
        cancellationToken.ThrowIfCancellationRequested();
        return new("HATCH", edit.Build(), [hatch]);

        void AddPath(string type, Dictionary<string, ShadowValue> values)
        {
            var edge = edit.Create("netDxf.Entities.HatchBoundaryPath+" + type, ShadowObjectKind.Object, values);
            var path = edit.Create("netDxf.Entities.HatchBoundaryPath", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue> { ["Edges"] = new ShadowValue.Sequence([R(edge)]) });
            paths.Add(R(path));
        }
    }

    /// <summary>Reads complete editable pattern data without retaining mutable source objects.</summary>
    public static DraftingHatchPattern ReadPattern(ShadowSnapshot snapshot, ObjectId id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var entity = DraftingGuard.Entity(snapshot, id, false);
        var pattern = entity["Pattern"];
        if (entity.Type != "Hatch" || pattern.Type != "HatchPattern")
            throw new NotSupportedException("This editor reads line-family and solid hatch patterns, not gradient definitions.");
        var definitions = pattern["LineDefinitions"].Items.Take(129).ToArray();
        if (definitions.Length > 128) throw new NotSupportedException("The source pattern exceeds the editing line-family budget.");
        var lines = ImmutableArray.CreateBuilder<DraftingHatchLine>();
        foreach (var line in definitions)
        {
            var dashes = line["DashPattern"].Items.Take(129).Select(v => v.Number()).ToImmutableArray();
            if (dashes.Length > 128) throw new NotSupportedException("The source pattern exceeds the editing dash budget.");
            lines.Add(new(line["Angle"].Number(), line["Origin"].Vector(), line["Delta"].Vector(), dashes));
        }
        if (!Enum.TryParse<CadHatchIslandStyle>(pattern["Style"].Text, out var style) || !Enum.IsDefined(style))
            throw new NotSupportedException("Unknown source hatch island policy.");
        if (!Enum.TryParse<DraftingHatchPatternType>(pattern["Type"].Text, out var type) || !Enum.IsDefined(type))
            throw new NotSupportedException("Unknown source hatch pattern type.");
        return new(pattern["Name"].Text, pattern["Scale"].Number(1), pattern["Angle"].Number(),
            pattern["Origin"].Vector(), style, lines.ToImmutable(), pattern["Description"].Text, type, pattern["IsDouble"].Boolean());
    }

    public static DraftingPlan ChangePattern(ShadowSnapshot snapshot, ImmutableArray<ObjectId> hatches,
        DraftingHatchPattern pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(pattern);
        if (hatches.IsDefaultOrEmpty || hatches.Length > 1024) throw new ArgumentException("Select 1–1024 hatches.");
        var edit = new ShadowGraphEdit(snapshot); var projector = new StandardEntityProjector();
        foreach (var id in hatches.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hatch = DraftingGuard.Entity(snapshot, id); DraftingGuard.Planar(hatch);
            if (hatch.Type != "Hatch" || !projector.TryProject(hatch, out var geometry, out _)) throw new ArgumentException("Every selected object must be a HATCH.");
            var topology = HatchIslandResolver.Analyze(geometry.Contours,
                new(MaximumContours: 1024, MaximumBoundaryPoints: 100_000), cancellationToken);
            edit.Set(id, "Pattern", R(Pattern(edit, pattern, topology.Select(pattern.Islands), cancellationToken)));
        }
        edit.PruneUnreachable(); return new("HATCHEDIT", edit.Build(), []);
    }

    public static ImmutableArray<DraftingHatchLine> ResolvePattern(DraftingHatchPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var name = pattern.Name?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("Invalid hatch pattern name.");
        if (!pattern.Lines.IsDefaultOrEmpty)
        {
            if (name == "SOLID" || pattern.Lines.Length > 128) throw new ArgumentException("Invalid custom hatch line inventory.");
            return pattern.Lines;
        }
        return name switch
        {
            "SOLID" => [],
            "LINE" => [new(45, Vec3.Zero, new(0, 1))],
            "CROSS" => [new(0, Vec3.Zero, new(0, 1)), new(90, Vec3.Zero, new(0, 1))],
            "DOTS" => [new(0, Vec3.Zero, new(0, 1), [0, -1])],
            "DASH" => [new(45, Vec3.Zero, new(.5, 1), [.5, -.5])],
            _ => throw new ArgumentException("Unknown pattern; provide explicit immutable line definitions for custom patterns.")
        };
    }

    private static ObjectId Pattern(ShadowGraphEdit edit, DraftingHatchPattern pattern,
        ImmutableArray<CadContour> boundaries, CancellationToken token)
    {
        if (!double.IsFinite(pattern.Scale) || pattern.Scale < 1e-9 || pattern.Scale > 1e9 || !double.IsFinite(pattern.Angle) ||
            !Enum.IsDefined(pattern.Islands) || !Enum.IsDefined(pattern.Type)) throw new ArgumentException("Invalid hatch scale, angle or island policy.");
        if (pattern.Description is null || pattern.Description.Length > 4096) throw new ArgumentException("Pattern descriptions are limited to 4096 characters.");
        Finite(pattern.Origin); RequireXY(pattern.Origin);
        var lines = ResolvePattern(pattern); var native = ImmutableArray.CreateBuilder<ShadowValue>();
        var compiled = ImmutableArray.CreateBuilder<CadPatternLine>();
        var angle = ((pattern.Angle % 360) + 360) % 360; var rotation = Affine3.RotateZ(angle * Math.PI / 180);
        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();
            if (line is null || !double.IsFinite(line.Angle)) throw new ArgumentException("Invalid hatch line angle.");
            Finite(line.Origin); RequireXY(line.Origin); Finite(line.Delta); RequireXY(line.Delta);
            var dashes = line.Dashes.IsDefault ? [] : line.Dashes;
            if (Math.Abs(line.Delta.Y) < 1e-9 || dashes.Length > 128 || dashes.Any(n => !double.IsFinite(n) || Math.Abs(n) > 1e12) ||
                !dashes.IsEmpty && (!(dashes.Sum(Math.Abs) > 0) || !double.IsFinite(dashes.Sum(Math.Abs))))
                throw new ArgumentException("Hatch families need nonzero perpendicular spacing and a bounded, nonzero dash period.");
            var id = edit.Create("netDxf.Entities.HatchPatternLineDefinition", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue> { ["Angle"] = N(line.Angle), ["Origin"] = V(line.Origin),
                    ["Delta"] = V(line.Delta), ["DashPattern"] = new ShadowValue.Sequence(dashes.Select(N).ToImmutableArray()) });
            native.Add(R(id));
            var definition = new CadPatternLine(pattern.Origin + rotation.Direction(line.Origin * pattern.Scale),
                (angle + line.Angle) * Math.PI / 180, line.Delta * pattern.Scale, dashes.Select(n => n * pattern.Scale).ToImmutableArray());
            compiled.Add(definition);
            if (pattern.IsDouble && pattern.Type == DraftingHatchPatternType.UserDefined)
                compiled.Add(definition with { Angle = definition.Angle + Math.PI / 2 });
        }
        // Use exactly the existing consumer's clipping budgets before publishing a pattern that cannot be displayed.
        if (compiled.Count != 0) HatchPatternGenerator.Generate(boundaries, compiled.ToImmutable(),
            new(MaximumRows: 100_000, MaximumSegments: 200_000, MaximumBoundaryPoints: 100_000), token);
        return edit.Create("netDxf.Entities.HatchPattern", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
        {
            ["Description"] = ShadowValue.From(pattern.Description),
            ["Name"] = ShadowValue.From(pattern.Name.Trim().ToUpperInvariant()), ["Scale"] = N(pattern.Scale), ["Angle"] = N(angle),
            ["Origin"] = V(pattern.Origin), ["Style"] = E("HatchStyle", pattern.Islands.ToString()),
            ["LineDefinitions"] = new ShadowValue.Sequence(native.ToImmutable()),
            ["Type"] = E("HatchType", pattern.Type.ToString()), ["IsDouble"] = ShadowValue.From(pattern.IsDouble)
        });
    }
    private static void RequireXY(Vec3 value)
    { if (Math.Abs(value.Z) > 1e-9) throw new NotSupportedException("Hatch authoring currently requires the world XY plane."); }
}
