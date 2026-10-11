using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private static DxfDocument CreateDocument()
    {
        var document = new DxfDocument();
        var pipe = new Layer("Process piping") { Color = new AciColor((byte)72, (byte)188, (byte)224) };
        var equipment = new Layer("Equipment") { Color = new AciColor((byte)148, (byte)199, (byte)124) };
        var annotation = new Layer("Annotations") { Color = new AciColor((byte)205, (byte)211, (byte)225) };
        foreach (var (a, b) in new[]
        {
            (new Vector2(-130, 0), new Vector2(-60, 0)), (new Vector2(-60, 0), new Vector2(0, 0)),
            (new Vector2(0, 0), new Vector2(75, 0)), (new Vector2(75, 0), new Vector2(135, 0)),
            (new Vector2(-60, 0), new Vector2(-60, 55)), (new Vector2(-60, 55), new Vector2(75, 55)),
            (new Vector2(75, 55), new Vector2(75, 0)), (new Vector2(0, 0), new Vector2(0, -55))
        }) document.Entities.Add(new Line(a, b) { Layer = pipe });
        document.Entities.Add(new Circle(new Vector2(-130, 0), 18) { Layer = equipment });
        document.Entities.Add(new Circle(new Vector2(135, 0), 18) { Layer = equipment });
        document.Entities.Add(new Arc(new Vector2(0, -55), 18, 0, 180) { Layer = equipment });
        document.Entities.Add(new Line(new Vector2(-18, -55), new Vector2(18, -55)) { Layer = equipment });
        var valve = new Block("Valve");
        valve.Entities.Add(new Line(new Vector2(-9, -7), new Vector2(9, 7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(-9, 7), new Vector2(9, -7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(-9, -7), new Vector2(-9, 7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(9, -7), new Vector2(9, 7)) { Color = AciColor.ByBlock });
        document.Entities.Add(new Insert(valve, new Vector2(-30, 0)) { Layer = equipment });
        document.Entities.Add(new Insert(valve, new Vector2(35, 55)) { Layer = equipment });
        document.Entities.Add(new Insert(valve, new Vector2(0, -28)) { Rotation = 90, Layer = equipment });
        foreach (var (text, x, y) in new[] { ("SHADOW / Process loop", -148.0, 87.0), ("P-101", -141.0, -32.0), ("V-102", 122.0, -32.0), ("Supply", -122.0, 9.0), ("Return", 17.0, 65.0), ("Drain", 12.0, -44.0) })
            document.Entities.Add(new Text(text, new Vector2(x, y), 5) { Layer = annotation });
        AddAppearanceGallery(document, annotation);
        AddPaperGallery(document, annotation);
        return document;
    }

    private static void AddAppearanceGallery(DxfDocument document, Layer annotation)
    {
        var gallery = new Layout("Appearance"); document.Layouts.Add(gallery);
        document.Entities.ActiveLayout = gallery.Name;
        var ink = new AciColor((byte)80, (byte)185, (byte)225);
        Label("SHADOW / Appearance gallery", 0, 112, 6);
        Label("Shared immutable geometry and text layouts / SkiaSharp + SVG", 0, 101, 3);
        document.Entities.Add(new Polyline2D([
            new Polyline2DVertex(0, 67) { StartWidth = 2, EndWidth = 8 },
            new Polyline2DVertex(25, 67) { Bulge = 0.65, StartWidth = 8, EndWidth = 3 },
            new Polyline2DVertex(55, 67)]) { Color = ink });
        Label("Analytic tapered polyline", 0, 51);
        var pattern = new HatchPattern("SHADOW_GALLERY");
        pattern.LineDefinitions.Add(new HatchPatternLineDefinition { Angle = 45, Origin = new(0, 0), Delta = new(0, 2.5) });
        document.Entities.Add(new Hatch(pattern, [Boundary(70, 59, 110, 88), Boundary(82, 66, 98, 81)], false) { Color = ink });
        Label("Pattern + island clipping", 69, 51);
        var gradient = new HatchGradientPattern(new AciColor((byte)40, (byte)190, (byte)220),
            new AciColor((byte)180, (byte)100, (byte)220), HatchGradientPatternType.Cylinder) { Angle = 30 };
        document.Entities.Add(new Hatch(gradient, [Boundary(124, 59, 166, 88)], false));
        Label("Gradient region", 124, 51);
        var definition = new ImageDefinition(StudioAssets.CheckerReference, 128, 96, 128, 96, ImageResolutionUnits.Inches);
        var image = new Image(definition, new Vector3(183, 59, 0), 32, 29)
        {
            Clipping = true, ClippingBoundary = new(0, 0, 96, 128), Vvector = new(0.2, 1)
        };
        document.Entities.Add(image); Label("Clipped raster + shear", 180, 51);
        document.Entities.Add(new MText(@"{\fsans-serif|b1;\H1.5x;Immutable rich text}\P{\c5290465;Scoped RGB paint} / \Lunderline\l / \S1/2; + \S3#4;\P{\fserif|b1|i1;Bold italic serif} / Unicode Ω ± Ø / \Q15;oblique", new Vector2(0, 35), 4) { Layer = annotation });
        Label("Select and edit objects; retain a snapshot, then compare revisions.", 0, -7, 3);
        document.Entities.ActiveLayout = "Model";
        void Label(string value, double x, double y, double height = 3.2)
            => document.Entities.Add(new Text(value, new Vector2(x, y), height) { Layer = annotation });
    }
    private static HatchBoundaryPath Boundary(double x0, double y0, double x1, double y1) => new(new HatchBoundaryPath.Edge[]
    {
        new HatchBoundaryPath.Line { Start = new(x0, y0), End = new(x1, y0) },
        new HatchBoundaryPath.Line { Start = new(x1, y0), End = new(x1, y1) },
        new HatchBoundaryPath.Line { Start = new(x1, y1), End = new(x0, y1) },
        new HatchBoundaryPath.Line { Start = new(x0, y1), End = new(x0, y0) }
    });
}
