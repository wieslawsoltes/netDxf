using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

/// <summary>Projects DXF semantic entities without a dependency on netDxf or native geometry.</summary>
public sealed partial class StandardEntityProjector : IEntityProjector
{
    private const double Degrees = Math.PI / 180;
    public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
    {
        geometry = CadGeometry.Empty;
        diagnostic = null;
        var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
        switch (entity.Type)
        {
            case "Polyline2D": geometry = ProjectPolyline(entity, basis, out diagnostic); return true;
            case "Hatch": geometry = ProjectHatch(entity, basis, out diagnostic); return true;
            case "Image": geometry = ProjectImage(entity, basis); return true;
            case "Line": geometry = Curve(new LineCurve(entity["StartPoint"].Vector(), entity["EndPoint"].Vector())); return true;
            case "Point":
                var point = entity["Position"].Vector();
                geometry = Curve(new LineCurve(point, point)); return true;
            case "Circle":
            case "Arc":
                var radius = entity["Radius"].Number();
                if (!(radius > 0)) throw new ArgumentException("Circle/arc radius must be positive.");
                var start = entity.Type == "Circle" ? 0 : entity["StartAngle"].Number() * Degrees;
                var sweep = entity.Type == "Circle" ? Math.Tau : PositiveSweep(start, entity["EndAngle"].Number() * Degrees);
                geometry = Curve(new EllipseCurve(entity["Center"].Vector(), basis.X * radius, basis.Y * radius, start, sweep)); return true;
            case "Ellipse":
                var rotated = basis * Affine3.RotateZ(entity["Rotation"].Number() * Degrees);
                var major = entity["MajorAxis"].Number() / 2;
                var minor = entity["MinorAxis"].Number() / 2;
                if (!(major > 0 && minor > 0)) throw new ArgumentException("Ellipse axes must be positive.");
                double Parameter(double degrees) { var a = degrees * Degrees; return Math.Atan2(major * Math.Sin(a), minor * Math.Cos(a)); }
                var ellipseStart = Parameter(entity["StartAngle"].Number());
                var ellipseSweep = entity["IsFullEllipse"].Boolean() || Math.Abs(entity["EndAngle"].Number() - entity["StartAngle"].Number()) >= 360 - 1e-9
                    ? Math.Tau : PositiveSweep(ellipseStart, Parameter(entity["EndAngle"].Number()));
                geometry = Curve(new EllipseCurve(entity["Center"].Vector(), rotated.X * major, rotated.Y * minor, ellipseStart, ellipseSweep)); return true;
            case "Polyline3D":
            case "Leader":
                var vertices3 = entity["Vertexes"].Items.Select(Position).ToArray();
                geometry = Polyline(vertices3, entity["IsClosed"].Boolean()); return true;
            case "Spline":
            case "Helix":
                var controls = entity["ControlPoints"].Items.Select(v => v.Vector()).ToImmutableArray();
                var weights = entity["Weights"].Items.Select(v => v.Number(1)).ToImmutableArray();
                var knots = entity["Knots"].Items.Select(v => v.Number()).ToImmutableArray();
                var degree = (int)entity["Degree"].Number(3);
                var periodic = entity["IsClosedPeriodic"].Boolean();
                if (periodic)
                {
                    if (controls.Length < degree) throw new ArgumentException("Insufficient periodic spline controls.");
                    if (weights.IsDefaultOrEmpty) weights = Enumerable.Repeat(1.0, controls.Length).ToImmutableArray();
                    controls = controls.TakeLast(degree).Concat(controls).ToImmutableArray();
                    weights = weights.TakeLast(degree).Concat(weights).ToImmutableArray();
                }
                geometry = Curve(new NurbsCurve(controls, weights, knots, degree, periodic || entity["IsClosed"].Boolean())); return true;
            case "Text":
            case "MText":
            case "Attribute":
            case "AttributeDefinition":
                var multiline = entity.Type == "MText";
                var axes = basis * Affine3.RotateZ(entity["Rotation"].Number() * Degrees);
                var x = axes.X * entity["WidthFactor"].Number(1) * (entity["IsBackward"].Boolean() ? -1 : 1);
                var y = (axes.Y + axes.X * Math.Tan(entity["ObliqueAngle"].Number() * Degrees)) * (entity["IsUpsideDown"].Boolean() ? -1 : 1);
                var family = entity["Style"]["FontFamilyName"].Text;
                if (family.Length == 0) family = "sans-serif";
                var content = entity["Value"].Text;
                var alignment = entity[multiline ? "AttachmentPoint" : "Alignment"].Text;
                geometry = new([], Text: new(content, entity["Position"].Vector(), x, y, entity["Height"].Number(1), family, alignment, multiline, entity["RectangleWidth"].Number(entity["Width"].Number())));
                if (entity["HasMText"].Boolean()) diagnostic = "Embedded multiline attribute metadata is retained; ordinary attribute text is projected.";
                if (entity["Style"]["IsShapeFile"].Boolean()) diagnostic = "SHX text requires an external font projector; the default backend substitutes a system font.";
                return true;
            case "Face3D":
            case "Solid":
            case "Trace":
                var face = new[] { entity["FirstVertex"].Vector(), entity["SecondVertex"].Vector(), entity["ThirdVertex"].Vector(), entity["FourthVertex"].Vector() };
                if (entity.Type != "Face3D")
                {
                    (face[2], face[3]) = (face[3], face[2]);
                    face = face.Select(v => basis.Point(v with { Z = entity["Elevation"].Number(v.Z) })).ToArray();
                }
                geometry = Polyline(face, true) with { Filled = true }; return true;
            case "Mesh":
            case "PolyfaceMesh":
                var meshVertices = entity["Vertexes"].Items.Select(Position).ToArray();
                var contours = ImmutableArray.CreateBuilder<CadContour>();
                foreach (var f in entity["Faces"].Items)
                {
                    var indices = (f["VertexIndexes"].Exists ? f["VertexIndexes"].Items : f.Items).Select(v => (int)v.Number()).ToArray();
                    var points = indices.Select(index => entity.Type == "PolyfaceMesh" ? Math.Abs(index) - 1 : index)
                        .Where(index => index >= 0 && index < meshVertices.Length).Select(index => meshVertices[index]).ToArray();
                    if (points.Length >= 3) contours.AddRange(Polyline(points, true).Contours);
                }
                geometry = new(contours.ToImmutable(), true); return true;
            default: return false;
        }
    }

    private static Vec3 Position(ShadowReader value) => value["Position"].Exists ? value["Position"].Vector() : value.Vector();
    private static CadGeometry Curve(Curve3 curve) => new([new([curve], curve.Closed)]);
    private static CadGeometry Polyline(IReadOnlyList<Vec3> points, bool closed)
    {
        var lines = ImmutableArray.CreateBuilder<Curve3>();
        for (var i = 0; i < points.Count - (closed ? 0 : 1); i++) lines.Add(new LineCurve(points[i], points[(i + 1) % points.Count]));
        return new([new(lines.ToImmutable(), closed)]);
    }
    private static double PositiveSweep(double start, double end)
    {
        var sweep = (end - start) % Math.Tau;
        return sweep <= 1e-14 ? sweep + Math.Tau : sweep;
    }
    public static Curve3 Bulge(Vec3 a, Vec3 b, double bulge)
    {
        var chord = b - a;
        var length = chord.Length;
        if (Math.Abs(bulge) < 1e-14 || length < 1e-14) return new LineCurve(a, b);
        var perpendicular = new Vec3(-chord.Y, chord.X) / length;
        var center = (a + b) / 2 + perpendicular * (length * (1 - bulge * bulge) / (4 * bulge));
        var radius = Vec3.Distance(a, center);
        return new EllipseCurve(center, Vec3.UnitX * radius, Vec3.UnitY * radius, Math.Atan2(a.Y - center.Y, a.X - center.X), 4 * Math.Atan(bulge));
    }
}
