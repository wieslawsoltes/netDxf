using netDxf.Shadow.Samples;
using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private static DxfDocument CreateDocument() => StudioSamples.CreateDocument();
    private static ShxFont DemoFont() => StudioSamples.DemoFont();
}
