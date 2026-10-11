using System.Collections.Immutable;

namespace netDxf.Shadow.Geometry;

public sealed partial class StandardEntityProjector
{
    private static CadGeometry ProjectPolyline(ShadowReader entity, Affine3 basis, out string? diagnostic)
    {
        diagnostic = null;
        var vertices = entity["Vertexes"].Items.ToArray();
        if (vertices.Length < 2) return CadGeometry.Empty;
        var closed = entity["IsClosed"].Boolean();
        var count = vertices.Length - (closed ? 0 : 1);
        var centerlines = ImmutableArray.CreateBuilder<Curve3>(count);
        var display = ImmutableArray.CreateBuilder<CadContour>();
        var regions = ImmutableArray.CreateBuilder<CadContour>();
        var offsets = new (Curve3 Left, Curve3 Right)?[count];
        var elevation = entity["Elevation"].Number();
        for (var i = 0; i < count; i++)
        {
            var vertex = vertices[i];
            var a = vertex["Position"].Vector() with { Z = elevation };
            var b = vertices[(i + 1) % vertices.Length]["Position"].Vector() with { Z = elevation };
            var startWidth = vertex["StartWidth"].Number(); var endWidth = vertex["EndWidth"].Number();
            if (!double.IsFinite(startWidth) || !double.IsFinite(endWidth) || startWidth < 0 || endWidth < 0)
                throw new ArgumentException("Polyline widths must be finite and nonnegative.");
            var center = Bulge(a, b, vertex["Bulge"].Number());
            centerlines.Add(center);
            if (startWidth == 0 && endWidth == 0 || a == b)
            {
                display.Add(new([center]));
                continue;
            }
            var left = new WidthBoundaryCurve(center, startWidth, endWidth, 1, Affine3.Identity);
            var right = new WidthBoundaryCurve(center, startWidth, endWidth, -1, Affine3.Identity);
            offsets[i] = (left, right);
            regions.Add(new([left, new LineCurve(left.End, right.End), new ReversedCurve(right), new LineCurve(right.Start, left.Start)], true));
        }
        // The bevel polygon fills the outside corner; nonzero winding unions adjacent
        // strips without even/odd cancellation or per-strip transparency darkening.
        for (var i = 0; i < count - (closed ? 0 : 1); i++)
        {
            var next = (i + 1) % count;
            if (offsets[i] is not { } a || offsets[next] is not { } b) continue;
            var hull = ConvexHull([a.Left.End, a.Right.End, b.Left.Start, b.Right.Start]);
            if (hull.Length >= 3) regions.AddRange(Polyline(hull, true).Contours);
        }
        var geometry = new CadGeometry([new(centerlines.ToImmutable(), closed)]);
        if (regions.Count > 0)
        {
            geometry = geometry with { DisplayContours = display.ToImmutable(), Regions = [new(regions.ToImmutable(), CadFillRule.NonZero)] };
            if (count > 1) diagnostic = "Variable-width polyline uses analytic tapered strips with butt caps and bevel joins; CAD miter parity is not certified.";
        }
        return geometry.Transform(basis);
    }

    private static Vec3[] ConvexHull(Vec3[] points)
    {
        var ordered = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (ordered.Length < 3) return ordered;
        var hull = new List<Vec3>();
        static double Cross(Vec3 a, Vec3 b, Vec3 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        foreach (var p in ordered)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        var lower = hull.Count;
        for (var i = ordered.Length - 2; i >= 0; i--)
        {
            while (hull.Count > lower && Cross(hull[^2], hull[^1], ordered[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(ordered[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        hull.Reverse(); // Clockwise, matching the analytic ribbon contours.
        return hull.ToArray();
    }

    private static CadGeometry ProjectHatch(ShadowReader entity, Affine3 basis, out string? diagnostic)
    {
        diagnostic = null;
        var contours = ImmutableArray.CreateBuilder<CadContour>();
        foreach (var boundary in entity["BoundaryPaths"].Items)
        {
            var curves = boundary["Edges"].Items.SelectMany(HatchEdge).ToImmutableArray();
            if (!curves.IsEmpty) contours.Add(new(curves, true));
        }
        var local = contours.ToImmutable();
        var transform = basis * Affine3.Move(new(0, 0, entity["Elevation"].Number()));
        var pattern = entity["Pattern"];
        var geometry = new CadGeometry(local) { DisplayContours = [] };
        if (pattern["GradientType"].Exists)
        {
            var rotation = Affine3.RotateZ(pattern["Angle"].Number() * Degrees);
            var inverse = Affine3.RotateZ(-pattern["Angle"].Number() * Degrees);
            var bounds = Bounds2.Empty;
            var points = 0;
            foreach (var curve in local.SelectMany(c => c.Curves))
                foreach (var point in CurveSampler.Sample(curve, new(0.01)))
                {
                    if (++points > 1_000_000) throw new InvalidOperationException("Gradient boundary budget exceeded.");
                    bounds = bounds.Include(inverse.Point(point));
                }
            if (!bounds.IsEmpty)
            {
                var frame = rotation * Affine3.Move(new(bounds.MinX, bounds.MinY))
                    * Affine3.Scale(new(Math.Max(bounds.Width, 1e-12), Math.Max(bounds.Height, 1e-12), 1));
                var gradient = new CadGradient(pattern["GradientType"].Text, Rgb(pattern["Color1"]), Rgb(pattern["Color2"]), frame, pattern["Shift"].Number());
                geometry = geometry with { Regions = [new(local, Gradient: gradient)] };
                diagnostic = "Gradient uses a normalized analytic display mapping; proprietary AutoCAD gradient transfer functions and shift blending are not pixel-certified.";
            }
        }
        else if (pattern["Fill"].Text == "SolidFill" || string.Equals(pattern["Name"].Text, "SOLID", StringComparison.OrdinalIgnoreCase))
            geometry = geometry with { Regions = [new(local)] };
        else
        {
            var scale = pattern["Scale"].Number(1);
            if (!(scale > 0) || !double.IsFinite(scale)) throw new ArgumentException("Hatch scale must be positive and finite.");
            var angle = pattern["Angle"].Number() * Degrees;
            var rotation = Affine3.RotateZ(angle);
            var origin = pattern["Origin"].Vector();
            var lines = ImmutableArray.CreateBuilder<CadPatternLine>();
            foreach (var line in pattern["LineDefinitions"].Items)
            {
                var definition = new CadPatternLine(origin + rotation.Direction(line["Origin"].Vector() * scale),
                    angle + line["Angle"].Number() * Degrees, line["Delta"].Vector() * scale,
                    line["DashPattern"].Items.Select(d => d.Number() * scale).ToImmutableArray());
                lines.Add(definition);
                if (pattern["IsDouble"].Boolean() && pattern["Type"].Text == "UserDefined")
                    lines.Add(definition with { Angle = definition.Angle + Math.PI / 2 });
            }
            if (lines.Count == 0)
            {
                geometry = geometry with { DisplayContours = local };
                diagnostic = "Hatch has no line definitions; boundaries are displayed.";
            }
            else
            {
                geometry = geometry with { DisplayContours = HatchPatternGenerator.Generate(local, lines.ToImmutable()) };
                diagnostic = "Pattern hatch clipping uses a 0.01 drawing-unit boundary approximation with even/odd island semantics.";
            }
        }
        var style = pattern["Style"].Text;
        if (style.Length > 0 && style != "Normal")
            diagnostic = (diagnostic is null ? "" : diagnostic + " ") + $"Hatch island style {style} currently uses even/odd display.";
        return geometry.Transform(transform);
    }

    private static uint Rgb(ShadowReader color) => 0xff000000u
        | (uint)Math.Clamp(color["R"].Number(), 0, 255) << 16
        | (uint)Math.Clamp(color["G"].Number(), 0, 255) << 8
        | (uint)Math.Clamp(color["B"].Number(), 0, 255);

    private static CadGeometry ProjectImage(ShadowReader entity, Affine3 basis)
    {
        var origin = entity["Position"].Vector();
        // U/V need not be perpendicular. Reconstructing V solely from Rotation loses authored shear.
        var u = basis.Direction(entity["Uvector"].Vector(Vec3.UnitX)) * entity["Width"].Number();
        var v = basis.Direction(entity["Vvector"].Vector(Vec3.UnitY)) * entity["Height"].Number();
        var corners = ImmutableArray.Create(origin, origin + u, origin + u + v, origin + v);
        var flags = entity["DisplayOptions"].Text.Split(',', StringSplitOptions.TrimEntries);
        bool Flag(string name) => flags.Contains(name, StringComparer.Ordinal);
        var clip = ImmutableArray<Vec3>.Empty;
        if (entity["Clipping"].Boolean() && (!entity["DisplayOptions"].Exists || Flag("UseClippingBoundary")))
        {
            var width = entity["Definition"]["Width"].Number();
            var height = entity["Definition"]["Height"].Number();
            if (!(width > 0 && height > 0)) throw new ArgumentException("Image clipping requires positive pixel dimensions.");
            var points = entity["ClippingBoundary"]["Vertexes"].Items.Select(p => p.Vector()).ToArray();
            if (points.Length == 2)
            {
                var a = points[0]; var b = points[1];
                points = [new(a.X, a.Y), new(b.X, a.Y), new(b.X, b.Y), new(a.X, b.Y)];
            }
            if (points.Length >= 3) clip = points.Select(p => origin + u * (p.X / width) + v * (p.Y / height)).ToImmutableArray();
        }
        return new([], Image: new(entity["Definition"]["File"].Text, corners)
        {
            ClipPolygon = clip, Visible = !entity["DisplayOptions"].Exists || Flag("ShowImage"),
            Brightness = entity["Brightness"].Number(50) / 100,
            Contrast = entity["Contrast"].Number(50) / 100,
            Fade = entity["Fade"].Number() / 100
        });
    }

    private static IEnumerable<Curve3> HatchEdge(ShadowReader edge)
    {
        var type = edge.Type.Split('+')[^1];
        switch (type)
        {
            case "Line": yield return new LineCurve(edge["Start"].Vector(), edge["End"].Vector()); break;
            case "Arc":
            {
                var ccw = edge["IsCounterclockwise"].Boolean(true);
                var start = edge["StartAngle"].Number() * Degrees * (ccw ? 1 : -1);
                var end = edge["EndAngle"].Number() * Degrees * (ccw ? 1 : -1);
                var sweep = ccw ? PositiveSweep(start, end) : -PositiveSweep(end, start);
                var radius = edge["Radius"].Number();
                if (!(radius > 0)) throw new ArgumentException("Hatch arc radius must be positive.");
                yield return new EllipseCurve(edge["Center"].Vector(), Vec3.UnitX * radius, Vec3.UnitY * radius, start, sweep); break;
            }
            case "Ellipse":
            {
                var major = edge["EndMajorAxis"].Vector();
                var ratio = edge["MinorRatio"].Number();
                if (!(major.Length > 0 && ratio > 0)) throw new ArgumentException("Hatch ellipse axes must be positive.");
                var minor = new Vec3(-major.Y, major.X) * ratio;
                var ccw = edge["IsCounterclockwise"].Boolean(true);
                double Parameter(double degrees)
                {
                    var angle = degrees * Degrees * (ccw ? 1 : -1);
                    return Math.Atan2(Math.Sin(angle), ratio * Math.Cos(angle));
                }
                var start = Parameter(edge["StartAngle"].Number()); var end = Parameter(edge["EndAngle"].Number());
                yield return new EllipseCurve(edge["Center"].Vector(), major, minor, start,
                    ccw ? PositiveSweep(start, end) : -PositiveSweep(end, start)); break;
            }
            case "Polyline":
            {
                var vertices = edge["Vertexes"].Items.Select(v => v.Vector()).ToArray();
                var closed = edge["IsClosed"].Boolean(true);
                for (var i = 0; i < vertices.Length - (closed ? 0 : 1); i++)
                {
                    var p = vertices[i]; var q = vertices[(i + 1) % vertices.Length];
                    yield return Bulge(p with { Z = 0 }, q with { Z = 0 }, p.Z);
                }
                break;
            }
            case "Spline":
            {
                var raw = edge["ControlPoints"].Items.Select(v => v.Vector()).ToImmutableArray();
                var knots = edge["Knots"].Items.Select(v => v.Number()).ToImmutableArray();
                var degree = (int)edge["Degree"].Number();
                var periodic = edge["IsPeriodic"].Boolean();
                if (degree < 1 || degree > 32) throw new ArgumentException("Invalid hatch spline degree.");
                if (periodic && knots.Length == raw.Length + 2 * degree + 1)
                {
                    if (raw.Length < degree) throw new ArgumentException("Invalid periodic hatch control overlap.");
                    raw = raw.TakeLast(degree).Concat(raw).ToImmutableArray();
                }
                var rational = edge["IsRational"].Boolean();
                yield return new NurbsCurve(raw.Select(p => p with { Z = 0 }).ToImmutableArray(),
                    raw.Select(p => rational ? p.Z : 1.0).ToImmutableArray(), knots, degree, periodic);
                break;
            }
            default: throw new NotSupportedException($"Hatch edge {type} needs a custom projector.");
        }
    }
}
