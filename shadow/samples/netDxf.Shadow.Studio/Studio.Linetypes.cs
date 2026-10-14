using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private static void AddLinetypeGallery(DxfDocument document, Layer annotation, TextStyle strokeFont)
    {
        var sheet = new Layout("Complex linetypes"); document.Layouts.Add(sheet);
        var entities = sheet.AssociatedBlock.Entities;
        var textStyle = new TextStyle("Linetype labels", "Arial.ttf");
        Linetype TextPattern(string name, string text, TextStyle style, LinetypeSegmentRotationType rotation)
            => new(name, new LinetypeSegment[]
            {
                new LinetypeSimpleSegment(10),
                new LinetypeTextSegment(text, style, -6, new Vector2(-3, -1.5), rotation, 0, 3),
                new LinetypeSimpleSegment(-8)
            });
        var relative = TextPattern("DEMO_TEXT_RELATIVE", "GAS", textStyle, LinetypeSegmentRotationType.Relative);
        var upright = TextPattern("DEMO_TEXT_UPRIGHT", "GAS", textStyle, LinetypeSegmentRotationType.Upright);
        var absolute = TextPattern("DEMO_TEXT_ABSOLUTE", "GAS", textStyle, LinetypeSegmentRotationType.Absolute);
        var strokes = TextPattern("DEMO_SHX_TEXT", "ABC", strokeFont, LinetypeSegmentRotationType.Relative);
        var shapes = new Linetype("DEMO_SHAPES", new LinetypeSegment[]
        {
            new LinetypeSimpleSegment(8),
            new LinetypeShapeSegment("A", new ShapeStyle("Linetype symbols", "shadow-demo.shx"), -4,
                new Vector2(-1, -1.5), LinetypeSegmentRotationType.Relative, 0, 0.3) { ShapeNumber = 65 },
            new LinetypeSimpleSegment(-4)
        });
        void Label(string text, double y) => entities.Add(new Text(text, new Vector2(0, y), 2.8) { Layer = annotation });
        entities.Add(new Text("SHADOW / Complex linetypes", new Vector2(0, 125), 6) { Layer = annotation });
        Label("Native text: relative orientation", 112);
        entities.Add(new Line(new Vector2(0, 102), new Vector2(200, 102)) { Linetype = relative });
        Label("Reversed direction: upright orientation", 91);
        entities.Add(new Line(new Vector2(200, 81), new Vector2(0, 81)) { Linetype = upright });
        Label("Explicit SHX stroke text / pinned with branch snapshots", 70);
        entities.Add(new Line(new Vector2(0, 60), new Vector2(200, 60)) { Linetype = strokes });
        Label("Numeric SHX shape references / save without a local SHX file", 49);
        entities.Add(new Line(new Vector2(0, 39), new Vector2(200, 39)) { Linetype = shapes });
        Label("Continuous corner phase and closed seam", 28);
        entities.Add(new Polyline2D(new[]
        {
            new Polyline2DVertex(0, 13), new Polyline2DVertex(85, 13),
            new Polyline2DVertex(85, -30), new Polyline2DVertex(0, -30)
        }, true) { Linetype = shapes });
        entities.Add(new Arc(new Vector2(150, -7), 24, 0, 300) { Linetype = absolute });
        Label("Symbols are editable source occurrences, not extra graph edges. Switch Skia / SVG to compare.", -46);
    }
}
