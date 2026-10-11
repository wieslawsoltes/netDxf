using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private static void AddPaperGallery(DxfDocument document, Layer annotation)
    {
        var sheet = new Layout("Paper viewports"); document.Layouts.Add(sheet);
        var border = new Layer("Viewport frames") { Color = new AciColor((byte)90, (byte)116, (byte)150) };
        var overview = new netDxf.Entities.Viewport
        {
            Center = new Vector3(105, 80, 0), Width = 190, Height = 120,
            ViewHeight = 200, ViewTarget = new Vector3(0, 15, 0), Layer = border
        };
        var detail = new netDxf.Entities.Viewport
        {
            ClippingBoundary = new Circle(new Vector2(255, 80), 42) { Layer = border },
            ViewHeight = 100, ViewTarget = new Vector3(-45, 28, 0), TwistAngle = 25, Layer = border
        };
        sheet.AssociatedBlock.Entities.Add(overview);
        sheet.AssociatedBlock.Entities.Add(detail);
        // Frozen layers must belong to the same document as the adopted viewport.
        detail.FrozenLayers.Add(annotation);
        sheet.AssociatedBlock.Entities.Add(new Text("SHADOW / Paper-space views", new Vector2(10, 155), 6) { Layer = annotation });
        sheet.AssociatedBlock.Entities.Add(new Text("Model overview", new Vector2(10, 10), 4) { Layer = annotation });
        sheet.AssociatedBlock.Entities.Add(new Text("Rotated detail / annotations frozen", new Vector2(215, 25), 3) { Layer = annotation });
    }
}
