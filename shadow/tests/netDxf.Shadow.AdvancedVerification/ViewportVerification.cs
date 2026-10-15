using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using DxfViewport = netDxf.Entities.Viewport;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static partial class ViewportVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        RegisterProjection(tests); RegisterEditing(tests); RegisterVisibility(tests); RegisterStudio(tests);
        ViewportOverrideVerification.Register(tests);
    }
    private static (DxfDocument Document, DxfViewport View) Drawing()
    {
        var document = new DxfDocument(); var layout = new Layout("Sheet"); document.Layouts.Add(layout);
        var view = new DxfViewport { Center = Vector3.Zero, Width = 20, Height = 20, ViewHeight = 20 };
        layout.AssociatedBlock.Entities.Add(view); return (document, view);
    }
    private static IEnumerable<RenderItem> Projected(RenderScene scene) => scene.Items.Where(i => i.Projection is not null);
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < 1e-7, $"Expected {expected:R}, got {actual:R}.");
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
