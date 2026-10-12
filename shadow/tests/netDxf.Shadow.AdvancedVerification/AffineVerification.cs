using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class AffineVerification
{
    private static void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Vec3 a, Vec3 b) => Require(Vec3.Distance(a, b) < 1e-7, $"Expected {b}, got {a}.");
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static Vec3 V(Vector3 value) => new(value.X, value.Y, value.Z);
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static Affine3 Shear => new(new(2, 0, 0.2), new(0.7, 1.5, 0.1), new(0.1, 0.2, 1.2), new(30, -15, 4));
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("affine/atomic source transform, snapshot retention and repeated history", () =>
        {
            var doc = new DxfDocument(); var line = new Line(new Vector3(1, 2, 3), new Vector3(8, 5, 3)); doc.Entities.Add(line);
            var store = new ShadowSession(doc); var old = store.Current; var editor = new EditorSession(store);
            var start = V(line.StartPoint); var end = V(line.EndPoint);
            editor.Transform([Id(line)], Shear); Near(V(line.StartPoint), Shear.Point(start)); Near(V(line.EndPoint), Shear.Point(end));
            Near(old.Read(Id(line))["StartPoint"].Vector(), start);
            for (var i = 0; i < 3; i++) { editor.Undo(); Near(V(line.StartPoint), start); editor.Redo(); Near(V(line.EndPoint), Shear.Point(end)); }
        }));
        tests.Add(("affine/readonly polyline and rational spline arrays replay in place", () =>
        {
            var doc = new DxfDocument(); var poly = new Polyline3D([new Vector3(0, 0, 0), new Vector3(4, 2, 1)]);
            var spline = new Spline([new Vector3(0, 0, 0), new Vector3(3, 4, 1), new Vector3(8, 0, 2)], [1.0, 0.7, 1.0], (short)2);
            doc.Entities.Add(poly); doc.Entities.Add(spline); var controls = spline.ControlPoints;
            var old = controls.Select(V).ToArray(); var store = new ShadowSession(doc); var editor = new EditorSession(store);
            editor.Transform([Id(poly), Id(spline)], Shear);
            Require(ReferenceEquals(controls, spline.ControlPoints), "Array identity was replaced.");
            for (var i = 0; i < old.Length; i++) Near(V(controls[i]), Shear.Point(old[i]));
            editor.Undo(); for (var i = 0; i < old.Length; i++) Near(V(controls[i]), old[i]);
            editor.Redo(); Near(V(poly.Vertexes[1]), Shear.Point(new(4, 2, 1)));
        }));
        tests.Add(("affine/ellipse SVD preserves sheared conic and arc endpoints", () =>
        {
            var doc = new DxfDocument(); var ellipse = new Ellipse(new Vector3(7, 8, 2), 10, 4) { StartAngle = 25, EndAngle = 280, Rotation = 23 };
            doc.Entities.Add(ellipse); var store = new ShadowSession(doc); var editor = new EditorSession(store);
            Curve3 Curve() => new CadSceneBuilder().Build(store.Current).Occurrences.Single().Geometry.Contours.Single().Curves.Single();
            var old = Curve(); var original = store.Current;
            editor.Transform([Id(ellipse)], Shear); var next = Curve();
            Near(next.Start, Shear.Point(old.Start)); Near(next.End, Shear.Point(old.End));
            var e = (EllipseCurve)next;
            for (var i = 0; i <= 30; i++)
            {
                var p = Shear.Point(old.At(i / 30.0)) - e.Center;
                var x = Vec3.Dot(p, e.U) / Vec3.Dot(e.U, e.U); var y = Vec3.Dot(p, e.V) / Vec3.Dot(e.V, e.V);
                Require(Math.Abs(x * x + y * y - 1) < 1e-8, "SVD changed the conic locus.");
            }
            editor.Undo(); Near(Curve().Start, old.Start); Require(ellipse.MajorAxis == 10 && ellipse.MinorAxis == 4);
            Require(original.Read(Id(ellipse))["MajorAxis"].Number() == 10);
        }));
        tests.Add(("affine/mirrored bulges preserve analytic direction and widths", () =>
        {
            var doc = new DxfDocument(); var poly = new Polyline2D([new Vector2(0, 0), new Vector2(10, 0)]);
            poly.Vertexes[0].Bulge = 0.6; poly.Vertexes[0].StartWidth = 2; poly.Vertexes[0].EndWidth = 4; doc.Entities.Add(poly);
            var store = new ShadowSession(doc); var original = new CadSceneBuilder().Build(store.Current).Occurrences.Single().Geometry.Contours[0].Curves[0];
            var matrix = Affine3.Move(new(10, 20, 2)) * Affine3.Scale(new(-2, 2, 2));
            var editor = new EditorSession(store); editor.Transform([Id(poly)], matrix);
            var curve = new CadSceneBuilder().Build(store.Current).Occurrences.Single().Geometry.Contours[0].Curves[0];
            for (var i = 0; i <= 8; i++) Near(curve.At(i / 8.0), matrix.Point(original.At(i / 8.0)));
            Require(poly.Vertexes[0].StartWidth == 4 && poly.Vertexes[0].EndWidth == 8);
            editor.Undo(); Require(poly.Vertexes[0].StartWidth == 2);
        }));
        tests.Add(("affine/rejects nonrepresentable circles and oblique extrusion before mutation", () =>
        {
            var doc = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0)); var circle = new Circle(Vector2.Zero, 4);
            doc.Entities.Add(line); doc.Entities.Add(circle); var store = new ShadowSession(doc); var old = store.Current; var editor = new EditorSession(store);
            Reject<NotSupportedException>(() => editor.Transform([Id(line), Id(circle)], Shear));
            Require(ReferenceEquals(old, store.Current)); Require(editor.State.UndoCount == 0); Near(V(line.StartPoint), default);
            circle.Thickness = 3; store.Refresh(); editor.ResetHistory(); old = store.Current;
            var oblique = new Affine3(Vec3.UnitX, Vec3.UnitY, new(1, 0, 1), Vec3.Zero);
            Reject<NotSupportedException>(() => editor.Transform([Id(circle)], oblique)); Require(ReferenceEquals(old, store.Current));
        }));
        tests.Add(("affine/isolated branch does not mutate netDxf", () =>
        {
            var doc = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0)); doc.Entities.Add(line);
            var source = new ShadowSession(doc); var branch = new ShadowMemoryStore(source.Current); var editor = new EditorSession(branch);
            editor.Transform([Id(line)], Shear); Near(branch.Current.Read(Id(line))["EndPoint"].Vector(), Shear.Point(new(10, 0)));
            Near(V(line.EndPoint), new(10, 0)); editor.Undo(); Near(branch.Current.Read(Id(line))["EndPoint"].Vector(), new(10, 0));
        }));
        tests.Add(("affine/source image shear preserves pixel clip coordinates", () =>
        {
            var doc = new DxfDocument(); var image = new Image(new ImageDefinition("trusted.png", 100, 96, 100, 96, ImageResolutionUnits.Inches), Vector3.Zero, 10, 10);
            image.Clipping = true; image.ClippingBoundary = new ClippingBoundary(10, 20, 70, 80); doc.Entities.Add(image);
            var store = new ShadowSession(doc); var old = new CadSceneBuilder().Build(store.Current).Occurrences.Single().Geometry.Image!;
            var editor = new EditorSession(store); editor.Transform([Id(image)], Shear);
            var after = new CadSceneBuilder().Build(store.Current).Occurrences.Single().Geometry.Image!;
            for (var i = 0; i < 4; i++) Near(after.Corners[i], Shear.Point(old.Corners[i]));
            for (var i = 0; i < old.ClipPolygon.Length; i++) Near(after.ClipPolygon[i], Shear.Point(old.ClipPolygon[i]));
            editor.Undo(); Near(V(image.Position), default);
        }));
        tests.Add(("affine/rotated block definition selection uses transform conjugation", () =>
        {
            var doc = new DxfDocument(); var block = new Block("A"); var line = new Line(new Vector2(1, 2), new Vector2(10, 2)); block.Entities.Add(line);
            doc.Entities.Add(new Insert(block, new Vector2(20, 30)) { Rotation = 40, Scale = new Vector3(2, 3, 1) });
            var store = new ShadowSession(doc); var scene = new RenderCompiler().Compile(store.Current); var item = scene.Items.Single(); var editor = new EditorSession(store);
            editor.Select([item.Key]); var matrix = Affine3.Move(new(4, -9)) * Affine3.RotateZ(0.3); var old = item.Contours[0].Points[0];
            editor.Transform(scene, matrix, editBlockDefinition: true);
            Near(new RenderCompiler().Compile(store.Current).Items.Single().Contours[0].Points[0], matrix.Point(old));
            editor.Undo(); Near(V(line.StartPoint), new(1, 2));
        }));
        tests.Add(("affine/insert frame and attached attributes transform together", () =>
        {
            var doc = new DxfDocument(); var block = new Block("ATTR"); block.Entities.Add(new Line(Vector2.Zero, new Vector2(8, 0)));
            block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Position = new Vector3(2, 3, 0), Height = 2 });
            var insert = new Insert(block, new Vector2(20, 30)); doc.Entities.Add(insert);
            var store = new ShadowSession(doc); var old = new CadSceneBuilder().Build(store.Current); var editor = new EditorSession(store);
            var matrix = Affine3.Move(new(5, -2)) * Affine3.RotateZ(0.4) * Affine3.Scale(new(2, 2, 2));
            var p = V(insert.Attributes[0].Position); editor.Transform([Id(insert)], matrix);
            Near(V(insert.Attributes[0].Position), matrix.Point(p));
            var next = new CadSceneBuilder().Build(store.Current); var a = old.Occurrences.First(o => o.Geometry.Contours.Length > 0); var b = next.Occurrences.First(o => o.Entity == a.Entity);
            Near(b.Geometry.Contours[0].Curves[0].Start, matrix.Point(a.Geometry.Contours[0].Curves[0].Start));
            editor.Undo(); Near(V(insert.Attributes[0].Position), p);
        }));
        tests.Add(("affine/cancellation, budgets, conflicting views and stale plans", () =>
        {
            var doc = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(1, 0)); doc.Entities.Add(line);
            var store = new ShadowSession(doc); var before = store.Current;
            Reject<OperationCanceledException>(() => AffineEditOperations.Plan(before, [Id(line)], Shear, cancellationToken: new(true)));
            Reject<InvalidOperationException>(() => AffineEditOperations.Plan(before, [Id(line)], Shear, new(MaximumCoordinates: 1)));
            Reject<ShadowConflictException>(() => AffineEditOperations.Plan(before, [new AffineEntityEdit(Id(line), Shear), new AffineEntityEdit(Id(line), Affine3.Identity)]));
            Reject<ArgumentException>(() => AffineEditOperations.Plan(before, [Id(line)], Affine3.Scale(new(0, 1, 1))));
            var patch = AffineEditOperations.Plan(before, [Id(line)], Shear); new EditorSession(store).SetProperty(Id(line), "IsVisible", ShadowValue.From(false));
            Reject<ShadowConflictException>(() => store.Apply(patch)); Near(V(line.StartPoint), default);
        }));
    }
}
