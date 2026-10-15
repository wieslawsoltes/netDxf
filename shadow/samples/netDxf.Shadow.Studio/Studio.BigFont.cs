using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>Registers only an explicit map for an already uploaded resource. A retained branch
    /// keeps its previous font/map set. Identical maps are exact no-ops, including the view epoch.</summary>
    public object LoadBigFontMap(long revision, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var mapping = ShxBigFontMapFile.Read(bytes, MaximumFontBytes);
        lock (gate)
        {
            Check(revision); RequireLive();
            var alias = ShxFontSet.FileName(mapping.Resource);
            var font = shxFonts.Current.Resolve(alias) ?? throw new ArgumentException("Upload the named BIGFONT SHX/SHP resource before its map.");
            var before = shxFonts.Current;
            if (before.BigFontMaps.TryGetValue(alias, out var existing) && existing.Equals(mapping.Map)) return State();
            var nextView = checked(viewVersion + 1);
            shxFonts.RegisterBigFont(alias, font, mapping.Map);
            viewVersion = nextView;
            editor.Select([]);
            return UpdatedState();
        }
    }
}
