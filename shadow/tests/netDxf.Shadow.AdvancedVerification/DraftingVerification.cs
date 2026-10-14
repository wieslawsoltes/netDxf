using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class DraftingVerification
{
    private sealed class Drawing
    {
        internal readonly DxfDocument Document = new();
        internal readonly ShadowSession Source;
        internal readonly EditorSession Editor;
        internal readonly DraftingPlanner Planner = new();
        internal Drawing() { Source = new(Document); Editor = new(Source); }
        internal DraftingPlan Run(DraftingCommand command)
        { var plan = Planner.Plan(Source.Current, command); plan.Execute(Editor); return plan; }
    }
    private static void Require(bool value, string message = "Drafting assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance = 1e-7)
    { Require(Math.Abs(actual - expected) <= tolerance, $"Expected {expected:R}, got {actual:R}"); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new InvalidOperationException("Expected rejection"); }

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("drafting/line chains are one canonical undo transaction", () =>
        {
            var f = new Drawing(); var before = f.Source.Current;
            var plan = f.Run(new("LINE", [new(0, 0), new(20, 0), new(20, 10)], Closed: true));
            Require(plan.Created.Length == 3 && f.Document.Entities.Lines.Count() == 3);
            Require(f.Editor.State.UndoCount == 1 && !before.DrawingEntities().Any());
            f.Editor.Undo(); Require(!f.Document.Entities.All.Any());
            f.Editor.Redo(); Require(f.Document.Entities.Lines.Count() == 3);
        }));
        tests.Add(("drafting/source creation materializes every advertised construction family", () =>
        {
            var f = new Drawing();
            DraftingCommand[] commands = [
                new("PLINE", [new(0, 0), new(10, 0), new(10, 20)]),
                new("RECTANG", [new(30, 0), new(45, 12)]),
                new("POLYGON", [new(60, 0)], Value: 10, Count: 7),
                new("CIRCLE", [new(80, 0)], Value: 8),
                new("ARC", [new(90, 0), new(100, 10), new(110, 0)]),
                new("ELLIPSE", [new(130, 0), new(150, 0), new(130, 8)]),
                new("POINT", [new(160, 0)]),
                new("TEXT", [new(0, 30)], Value: 3, Text: "Precision Ω ± Ø"),
                new("MTEXT", [new(0, 40)], Value: 3, Secondary: 40, Text: @"First\PSecond"),
                new("SPLINE", [new(40, 30), new(50, 50), new(60, 20), new(70, 40), new(80, 30)]),
                new("DIMALIGNED", [new(0, 0), new(10, 10), new(0, 10)]),
                new("DIMLINEAR", [new(0, 0), new(20, 10), new(5, 20)]) ];
            foreach (var command in commands)
            {
                var count = f.Document.Entities.All.Count(); var created = f.Run(command).Created;
                Require(created.Length == 1, command.Name); Require(f.Document.Entities.All.Count() == count + 1, command.Name);
            }
            Near(f.Document.Entities.Ellipses.Single().MajorAxis, 40);
            Near(f.Document.Entities.Ellipses.Single().MinorAxis, 16);
            Require(f.Document.Entities.Splines.Single().Degree == 3);
            Require(f.Document.Entities.Splines.Single().ControlPoints.Length == 5);
            using var stream = new MemoryStream(); Require(f.Document.Save(stream)); stream.Position = 0;
            var reloaded = DxfDocument.Load(stream)!;
            Require(reloaded.Entities.All.Count() == commands.Length);
            Near(reloaded.Entities.Ellipses.Single().MajorAxis, 40);
        }));
        tests.Add(("drafting/copy preserves geometry, true colors and independent owned vertices", () =>
        {
            var f = new Drawing(); var original = new Polyline2D([new Vector2(0, 0), new Vector2(20, 0), new Vector2(20, 10)])
                { Color = new AciColor((byte)23, (byte)137, (byte)219) };
            f.Document.Entities.Add(original); f.Source.Refresh(); f.Editor.ResetHistory();
            var id = f.Source.Current.DrawingEntities().Single().Id!.Value;
            var plan = f.Run(new("COPY", [new(0, 0), new(100, 50)], [id]));
            Require(f.Document.Entities.Polylines2D.Count() == 2 && plan.Created.Length == 1);
            var copy = f.Document.Entities.Polylines2D.Single(p => !ReferenceEquals(p, original));
            Near(copy.Vertexes[0].Position.X, 100); Near(copy.Vertexes[0].Position.Y, 50);
            Require(copy.Color.UseTrueColor && copy.Color.R == 23 && copy.Color.G == 137 && copy.Color.B == 219);
            Require(!ReferenceEquals(copy.Vertexes[0], original.Vertexes[0]));
            Near(original.Vertexes[0].Position.X, 0);
            f.Editor.Undo(); Require(f.Document.Entities.Polylines2D.Count() == 1);
            f.Editor.Redo(); Require(f.Document.Entities.Polylines2D.Count() == 2);
        }));
        tests.Add(("drafting/rectangular array is bounded and uses one history entry", () =>
        {
            var f = new Drawing(); var source = f.Run(new("CIRCLE", [new(0, 0)], Value: 2)).Created;
            var plan = f.Run(new("ARRAY", [new(0, 0), new(10, 20)], source, Value: 2, Count: 3));
            Require(plan.Created.Length == 5 && f.Document.Entities.Circles.Count() == 6);
            Require(f.Document.Entities.Circles.Any(c => c.Center.X == 20 && c.Center.Y == 20));
            f.Editor.Undo(); Require(f.Document.Entities.Circles.Count() == 1);
        }));
        tests.Add(("drafting/basepoint rotate scale and mirror preserve the immutable baseline", () =>
        {
            var f = new Drawing(); var id = f.Run(new("LINE", [new(10, 0), new(20, 0)])).Created;
            var baseline = f.Source.Current;
            f.Run(new("ROTATE", [new(10, 0)], id, Value: 90));
            Near(f.Document.Entities.Lines.Single().EndPoint.Y, 10);
            f.Run(new("SCALE", [new(10, 0)], id, Value: 2));
            Near(f.Document.Entities.Lines.Single().EndPoint.Y, 20);
            f.Run(new("MIRROR", [new(0, 0), new(10, 0)], id));
            Near(f.Document.Entities.Lines.Single().EndPoint.Y, -20);
            Near(baseline.Read(id[0])["EndPoint"].Vector().X, 20);
        }));
        tests.Add(("drafting/offset lines circles and mitered polylines", () =>
        {
            var f = new Drawing(); var line = f.Run(new("LINE", [new(0, 0), new(20, 0)])).Created;
            f.Run(new("OFFSET", [new(4, 5)], line, Value: 3));
            Require(f.Document.Entities.Lines.Any(v => Math.Abs(v.StartPoint.Y - 3) < 1e-8));
            var circle = f.Run(new("CIRCLE", [new(50, 0)], Value: 10)).Created;
            f.Run(new("OFFSET", [new(50, 0)], circle, Value: 2));
            Require(f.Document.Entities.Circles.Any(v => Math.Abs(v.Radius - 8) < 1e-8));
            var polyline = f.Run(new("RECTANG", [new(100, 0), new(120, 20)])).Created;
            var offset = f.Run(new("OFFSET", [new(110, 2)], polyline, Value: 2));
            var vertex = f.Source.Current.Read(offset.Created[0])["Vertexes"].Items.First()["Position"].Vector();
            Near(vertex.X, 102); Near(vertex.Y, 2);
        }));
        tests.Add(("drafting/trim and extend use actual boundary intersections", () =>
        {
            var f = new Drawing(); var target = f.Run(new("LINE", [new(0, 0), new(20, 0)])).Created[0];
            var boundary = f.Run(new("LINE", [new(10, -10), new(10, 10)])).Created[0];
            f.Run(new("TRIM", [new(2, 0)], [target, boundary]));
            Near(f.Source.Current.Read(target)["StartPoint"].Vector().X, 10);
            var next = f.Run(new("LINE", [new(30, -10), new(30, 10)])).Created[0];
            f.Run(new("EXTEND", [new(20, 0)], [target, next]));
            Near(f.Source.Current.Read(target)["EndPoint"].Vector().X, 30);
        }));
        tests.Add(("drafting/fillet solves true tangent geometry", () =>
        {
            var f = new Drawing(); var a = f.Run(new("LINE", [new(0, 0), new(20, 0)])).Created[0];
            var b = f.Run(new("LINE", [new(0, 0), new(0, 20)])).Created[0];
            f.Run(new("FILLET", Entities: [a, b], Value: 4));
            var arc = f.Document.Entities.Arcs.Single(); Near(arc.Center.X, 4); Near(arc.Center.Y, 4); Near(arc.Radius, 4);
            Near(f.Source.Current.Read(a)["StartPoint"].Vector().X, 4);
            Near(f.Source.Current.Read(b)["StartPoint"].Vector().Y, 4);
            f.Editor.Undo(); Require(!f.Document.Entities.Arcs.Any()); Near(f.Source.Current.Read(a)["StartPoint"].Vector().X, 0);
        }));
        tests.Add(("drafting/join and explode preserve closed topology", () =>
        {
            var f = new Drawing(); var ids = f.Run(new("LINE", [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], Closed: true)).Created;
            var joined = f.Run(new("JOIN", Entities: ids));
            Require(f.Document.Entities.Polylines2D.Single().IsClosed && !f.Document.Entities.Lines.Any());
            var exploded = f.Run(new("EXPLODE", Entities: joined.Created));
            Require(exploded.Created.Length == 4 && f.Document.Entities.Lines.Count() == 4 && !f.Document.Entities.Polylines2D.Any());
            f.Editor.Undo(); Require(f.Document.Entities.Polylines2D.Single().IsClosed);
        }));
        tests.Add(("drafting/memory plans are independent of mutable source objects", () =>
        {
            var f = new Drawing(); var initial = f.Source.Current; var branch = new EditorSession(new ShadowMemoryStore(initial));
            var plan = f.Planner.Plan(branch.Snapshot, new("RECTANG", [new(0, 0), new(10, 5)]));
            plan.Execute(branch); Require(branch.Snapshot.DrawingEntities().Count() == 1);
            Require(!f.Document.Entities.All.Any()); branch.Undo(); Require(!branch.Snapshot.DrawingEntities().Any());
            Require(ReferenceEquals(initial, f.Source.Current));
        }));
        tests.Add(("drafting/stale plans locked layers invalid input and quotas fail atomically", () =>
        {
            var f = new Drawing(); var stale = f.Planner.Plan(f.Source.Current, new("CIRCLE", [new(0, 0)], Value: 2));
            var ids = f.Run(new("LINE", [new(0, 0), new(10, 0)])).Created;
            Reject(() => stale.Execute(f.Editor)); var baseline = f.Source.Current;
            foreach (var command in new DraftingCommand[] { new("CIRCLE", [new(0, 0)], Value: -1),
                new("ARC", [new(0, 0), new(1, 0), new(2, 0)]), new("LINE", [new(0, 0), new(double.NaN, 0)]),
                new("SCALE", [new(0, 0)], ids, Value: 0), new("ARRAY", [new(0, 0), new(1, 1)], ids, Value: 1000, Count: 1000), new("BOGUS") })
                Reject(() => f.Run(command));
            Require(ReferenceEquals(baseline, f.Source.Current));
            f.Document.Layers["0"].IsLocked = true; f.Source.Refresh(); f.Editor.ResetHistory(); baseline = f.Source.Current;
            Reject(() => f.Run(new("MOVE", [new(0, 0), new(1, 1)], ids)));
            Reject(() => f.Run(new("CIRCLE", [new(0, 0)], Value: 2)));
            Require(ReferenceEquals(baseline, f.Source.Current));
        }));
        tests.Add(("drafting/three-point arcs remain stable far from the origin", () =>
        {
            var arc = PlanarGeometry.ThreePointArc(new(1e9 + 10, 1e9), new(1e9, 1e9 + 10), new(1e9 - 10, 1e9));
            Near(arc.Center.X, 1e9); Near(arc.Center.Y, 1e9); Near(arc.Radius, 10);
            Near(arc.StartDegrees, 0); Near(arc.EndDegrees, 180);
        }));
    }
}
