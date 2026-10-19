using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Shadow.Samples;

public static partial class StudioSamples
{
    private static void AddParagraphGallery(DxfDocument document, Layer annotation)
    {
        var sheet = new Layout("Paragraphs");
        document.Layouts.Add(sheet);
        var entities = sheet.AssociatedBlock.Entities;
        entities.Add(new Text("SHADOW / Paragraph composition", new Vector2(0, 190), 6) { Layer = annotation });
        entities.Add(new Text("One immutable layout / SkiaSharp, SVG and SHX / source edits and retained revisions", new Vector2(0, 180), 3) { Layer = annotation });
        Panel(0, 162, "Hanging indentation", @"\pi-1,l1,r0.5;1. Indents follow the initial entity height. Long paragraphs wrap while scoped {\C1;colors} and {\Lunderlines\l} stay attached to their words.\P2. The next paragraph inherits the same settings.");
        Panel(120, 162, "Centered + right paragraphs", @"\pqc;Centered paragraph\P{\H1.5x;Larger title}\P\pqr;Right aligned paragraph\PThe declared column remains anchored when text overflows.");
        Panel(240, 162, "Justification", @"\pqj;Words share the available column width on wrapped lines. The final line is not stretched.\P\pqd;Distributed final line");
        Panel(0, 75, "Aligned tab fields", "\\pt8,r25;Item\tCode\tValue\\PA\tP-101\t12.5\\PB\tV-102\t250\\PC\tL-103\t0.75");
        Panel(120, 75, "Exact spacing + rich runs", @"\pse9;Exact baseline spacing\P{\H1.5x;Mixed heights}\P\LUnderlined\l  \S1/2;  \S3#4;\P\pb0.5,a0.5;Paragraph gaps add to line spacing.");
        var strokeStyle = new TextStyle("Shadow paragraph strokes", "shadow-demo.shx");
        Panel(240, 75, "SHX shares paragraph layout", "\\pt8,r20;A\t0123\\PB\t123\\PC\t23\\P\\pqc;ABC 0123", strokeStyle);
        entities.Add(new Text("Switch renderer, retain a revision, edit source text or spacing, then undo or save/reload the DXF.", new Vector2(0, -14), 3) { Layer = annotation });

        void Panel(double x, double y, string title, string content, TextStyle? style = null)
        {
            entities.Add(new Text(title, new Vector2(x, y + 5), 3.5) { Layer = annotation });
            entities.Add(new Polyline2D([new Vector2(x, y), new Vector2(x + 108, y),
                new Vector2(x + 108, y - 70), new Vector2(x, y - 70)], true)
            { Color = new AciColor((byte)75, (byte)85, (byte)100) });
            var text = style is null
                ? new MText(content, new Vector2(x + 3, y - 4), 3.5, 102)
                : new MText(content, new Vector2(x + 3, y - 4), 3.5, 102, style);
            text.Layer = annotation;
            entities.Add(text);
        }
    }
}
