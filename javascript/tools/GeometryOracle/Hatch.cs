// Supplemental serialization only. Every operation executes the unchanged pinned assembly.
using System;
using System.Linq;
using System.Reflection;
using netDxf.Entities;
internal static partial class Program
{
    private static bool HatchEntityWire(EntityObject entity, object common, out object? result)
    {
        result = null;
        if (entity is not Hatch h) return false;
        result = new { common, pattern = Wire(h.Pattern), associative = h.Associative,
            elevation = Wire(h.Elevation), pixel = Wire(h.PixelSize),
            seeds = h.SeedPoints.Select(v => Wire(v)).ToArray(), paths = h.BoundaryPaths.Select(v => Wire(v)).ToArray() };
        return true;
    }
    private static bool HatchBoundaryWire(object value, out object? result)
    {
        result = null;
        if (value is HatchBoundaryPath path)
        {
            result = new { type = "HatchBoundaryPath", flags = (int)path.PathType,
                attached = typeof(HatchBoundaryPath).GetProperty("ContainingHatch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(path) != null,
                edges = path.Edges.Select(v => Wire(v)).ToArray(), entities = path.Entities.Select(v => Wire(v)).ToArray() };
            return true;
        }
        if (value is not HatchBoundaryPath.Edge edge) return false;
        string type = edge.GetType().Name; int kind = (int)edge.Type;
        if (edge is HatchBoundaryPath.Line line) result = new { type, kind, start = Wire(line.Start), end = Wire(line.End) };
        else if (edge is HatchBoundaryPath.Arc arc) result = new { type, kind, center = Wire(arc.Center), radius = Wire(arc.Radius),
            start = Wire(arc.StartAngle), end = Wire(arc.EndAngle), ccw = arc.IsCounterclockwise };
        else if (edge is HatchBoundaryPath.Ellipse ellipse) result = new { type, kind, center = Wire(ellipse.Center), axis = Wire(ellipse.EndMajorAxis),
            ratio = Wire(ellipse.MinorRatio), start = Wire(ellipse.StartAngle), end = Wire(ellipse.EndAngle), ccw = ellipse.IsCounterclockwise };
        else if (edge is HatchBoundaryPath.Polyline polyline) result = new { type, kind, closed = polyline.IsClosed, vertices = Wire(polyline.Vertexes) };
        else if (edge is HatchBoundaryPath.Spline spline) result = new { type, kind, degree = spline.Degree, rational = spline.IsRational,
            periodic = spline.IsPeriodic, knots = Wire(spline.Knots), controls = Wire(spline.ControlPoints),
            fits = spline.FitPoints.Select(v => Wire(v)).ToArray(), start = Wire(spline.StartTangent), end = Wire(spline.EndTangent) };
        else throw new ArgumentException("Unmapped HATCH edge " + type);
        return true;
    }
}
