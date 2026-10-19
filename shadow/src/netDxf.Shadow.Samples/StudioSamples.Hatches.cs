using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Shadow.Samples;

public static partial class StudioSamples
{
    private static void AddHatchIslandGallery(DxfDocument document, Layer annotation)
    {
        var sheet = new Layout("Hatch islands"); document.Layouts.Add(sheet);
        var entities = sheet.AssociatedBlock.Entities;
        Label("SHADOW / Hatch island semantics", 0, 245, 7);
        Label("One boundary selection / SkiaSharp, SVG and picking / all semantic loops retained for graph analysis", 0, 233, 3.2);
        var colors = new[] { new AciColor((byte)70, (byte)190, (byte)220), new AciColor((byte)150, (byte)210, (byte)130), new AciColor((byte)230, (byte)170, (byte)100) };
        var names = new[] { "Normal / alternating", "Outer / first level only", "Ignore / no islands" };
        for (var column = 0; column < 3; column++)
        {
            var x = column * 95.0;
            Label(names[column], x, 217, 4);
            for (var row = 0; row < 3; row++)
            {
                var y = 150.0 - row * 80;
                HatchPattern pattern;
                if (row == 0) pattern = HatchPattern.Solid;
                else if (row == 1)
                {
                    pattern = new HatchPattern("SHADOW_ISLAND_GALLERY");
                    pattern.LineDefinitions.Add(new HatchPatternLineDefinition { Angle = 30, Origin = new(x, y), Delta = new(0, 1.6) });
                }
                else pattern = new HatchGradientPattern(colors[column], new AciColor((byte)90, (byte)60, (byte)160), HatchGradientPatternType.Linear) { Angle = 30 };
                pattern.Style = (HatchStyle)column;
                // Deliberately unordered: containment must not depend on DXF path order.
                entities.Add(new Hatch(pattern, [Boundary(x + 19, y + 17, x + 41, y + 43),
                    Boundary(x, y, x + 60, y + 60), Boundary(x + 25, y + 23, x + 35, y + 37),
                    Boundary(x + 10, y + 9, x + 50, y + 51)], false) { Color = colors[column] });
                Label(row == 0 ? "Solid" : row == 1 ? "Pattern" : "Gradient", x, y - 7, 3.2);
            }
        }
        Label("Select the hatch Pattern object in the inspector, edit Style, retain a revision, then undo or save/reload.", 0, -39, 3.2);
        Label("Graph overlays still show all four loops. Gradient transfer functions are display approximations.", 0, -49, 3.2);
        void Label(string value, double x, double y, double height) => entities.Add(new Text(value, new Vector2(x, y), height) { Layer = annotation });
    }
}
