using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private static void AddDimensionGallery(DxfDocument document, Layer annotation)
    {
        var sheet = new Layout("Dimensions"); document.Layouts.Add(sheet);
        var style = new DimensionStyle("Shadow dimensions")
        {
            TextStyle = new TextStyle("Shadow dimension text", "Arial.ttf"),
            TextHeight = 3, ArrowSize = 2.5, TextOffset = 1.5, ExtLineOffset = 1, ExtLineExtend = 2,
            LengthPrecision = 1, AngularPrecision = 1
        };
        var dimensions = new Dimension[]
        {
            new AlignedDimension(new(5, 85), new(65, 95), 14, style),
            new LinearDimension(new(100, 85), new(160, 95), 17, 0, style),
            new Angular2LineDimension(new(205, 85), new(250, 85), new(205, 85), new(240, 120), 24, style),
            new Angular3PointDimension(new(25, 30), new(65, 30), new(45, 65), 26, style),
            new RadialDimension(new Vector2(120, 30), new Vector2(151, 46), style),
            new DiametricDimension(new Vector2(220, 30), new Vector2(248, 44), style),
            new OrdinateDimension(new Vector2(0, -30), new Vector2(45, -12), new Vector2(45, -45), OrdinateDimensionAxis.X, style),
            new ArcLengthDimension(new Vector2(140, -45), 25, 15, 145, 33, style)
        };
        foreach (var dimension in dimensions)
        {
            dimension.Layer = annotation;
            dimension.Color = new AciColor((byte)105, (byte)207, (byte)229);
            sheet.AssociatedBlock.Entities.Add(dimension);
        }
        dimensions[1].StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TextColor, AciColor.Yellow));
        sheet.AssociatedBlock.Entities.Add(new Text("SHADOW / Regenerated dimensions", new Vector2(0, 145), 7) { Layer = annotation });
        sheet.AssociatedBlock.Entities.Add(new Text("Edit a dimension property; every fragment keeps its source identity", new Vector2(0, 132), 3) { Layer = annotation });
    }
}
