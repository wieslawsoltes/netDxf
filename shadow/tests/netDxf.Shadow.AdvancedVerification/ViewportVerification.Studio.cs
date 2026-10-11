using System.Text;
using netDxf;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Studio;

internal static partial class ViewportVerification
{
    private static void RegisterStudio(List<(string Name, Action Run)> tests)
    {
        tests.Add(("viewport/studio multi-view sheet saves and reloads", () =>
        {
            using var studio = new Studio();
            var bytes = studio.Save();
            using var stream = new MemoryStream(bytes);
            var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Save/reload failed.");
            var session = new ShadowSession(document);
            using var typography = new SkiaTextLayoutEngine();
            var compiler = new RenderCompiler(textLayout: typography);
            var scene = compiler.Compile(session.Current, new("Paper viewports"));
            Require(Projected(scene).Select(i => i.Projection!.ViewportEntity).Distinct().Count() == 2);
            Require(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 1200, 720));
            using var skia = new SkiaRenderBackend(textLayout: typography);
            using var svg = new SvgRenderBackend();
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/paper-gallery.png", skia.Render(frame).Bytes);
            File.WriteAllBytes("artifacts/paper-gallery.svg", svg.Render(frame).Bytes);
            File.WriteAllBytes("artifacts/paper-gallery.dxf", bytes);
            Require(Encoding.UTF8.GetString(svg.Render(frame).Bytes).Contains("clipPath", StringComparison.Ordinal));
        }));
    }
}
