using System.Text;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Samples;

public static partial class StudioSamples
{
    // Original synthetic strokes for this sample. These are generated instruction examples, not bundled font files.
    public static ShxFont DemoFont() => ShxFontReader.ReadAscii("""
        *0,4,Shadow synthetic strokes
        10,2,0,0
        *32,0,space
        2,8,8,0,0
        *65,0,A
        9,3,10,3,-10,0,0,2,8,-4,4,1,8,2,0,2,8,4,-4,0
        *66,0,B
        9,0,10,3,0,0,0,13,0,-5,-127,0,-5,-127,0,0,8,-3,0,2,8,8,0,0
        *67,0,C
        2,8,6,10,1,9,-6,0,0,-10,6,0,0,0,2,8,2,0,0
        *48,0,zero
        9,0,10,6,0,0,-10,-6,0,0,0,2,8,8,0,0
        *49,0,one
        2,8,3,0,1,8,0,10,2,8,5,-10,0
        *50,0,two
        2,8,0,10,1,9,6,0,0,-5,-6,0,0,-5,6,0,0,0,2,8,2,0,0
        *51,0,three
        2,8,0,10,1,9,6,0,0,-10,-6,0,0,0,2,8,0,5,1,8,6,0,2,8,2,-5,0
        """);

    private static void AddShxGallery(DxfDocument document, Layer annotation)
    {
        var sheet = new Layout("SHX strokes"); document.Layouts.Add(sheet);
        var style = new TextStyle("Shadow synthetic", "shadow-demo.shx");
        var entities = sheet.AssociatedBlock.Entities;
        entities.Add(new Text("SHADOW / SHX stroke typography", new Vector2(0, 85), 5) { Layer = annotation });
        entities.Add(new Text("ABC 0123", new Vector2(0, 55), 12, style));
        entities.Add(new Text("ABC 0123", new Vector2(0, 32), 8, style) { WidthFactor = 1.3, ObliqueAngle = 15 });
        entities.Add(new MText(@"{\c16744448;ABC} 0123\P\LABC\l  \S1/2;  \S2#3;", new Vector2(0, 15), 6, 180, style));
        entities.Add(new Text("Explicit font resources / native-independent paths / immutable snapshots", new Vector2(0, -22), 2.8) { Layer = annotation });
        AddLinetypeGallery(document, annotation, style);
    }
}
