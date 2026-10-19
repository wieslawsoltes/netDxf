using System.Text;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private const int MaximumFontBytes = 4 * 1024 * 1024;
    private readonly ShxFontCatalog shxFonts = new(32);
    private readonly Dictionary<long, RenderCompiler> liveRetainedCompilers = new();
    private RenderCompiler FrozenCompiler()
    {
        var shx = new ShxEntityProjector(CurrentFonts);
        return new(new CadSceneBuilder([new DimensionEntityProjector(resources: [shx]), shx]), textEngine);
    }

    /// <summary>Explicit upload only. No path stored in a drawing ever causes a font file to be opened.</summary>
    public object LoadFont(long revision, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaximumFontBytes) throw new ArgumentException("The sample font limit is 4 MiB.");
        var alias = ShxFontSet.FileName(fileName);
        var ascii = alias.EndsWith(".shp", StringComparison.OrdinalIgnoreCase);
        if (!ascii && !alias.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose a SHX or SHP resource.");
        if (ascii) alias = alias[..^4] + ".shx";
        var limits = new ShxReadOptions(MaximumBytes: MaximumFontBytes, MaximumInstructions: 250_000);
        var font = ascii ? ShxFontReader.ReadAscii(Encoding.Latin1.GetString(bytes), limits) : ShxFontReader.ReadBinary(bytes, limits);
        lock (gate)
        {
            Check(revision); RequireLive(); shxFonts.Register(alias, font); return UpdatedState();
        }
    }

}
