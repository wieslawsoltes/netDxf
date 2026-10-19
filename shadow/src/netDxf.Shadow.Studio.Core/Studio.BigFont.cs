using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>Retains the original source-compatible and binary-compatible entry point.</summary>
    public object LoadBigFontMap(long revision, byte[] bytes)
        => LoadBigFontMap(revision, bytes, CancellationToken.None);

    /// <summary>Import explicit interpretation for an uploaded BIGFONT resource. Publication is
    /// atomic, cancellable and source-neutral; retained workspaces keep their pinned font/map set.</summary>
    public object LoadBigFontMap(long revision, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var mapping = ShxBigFontMapFile.ReadStrict(bytes, cancellationToken: cancellationToken);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); Check(revision); RequireLive();
            var before = shxFonts.Current;
            var equivalent = before.BigFontMaps.TryGetValue(mapping.Resource, out var existing) && existing.Equals(mapping.Map);
            var nextView = equivalent ? viewVersion : checked(viewVersion + 1);
            var after = shxFonts.ImportBigFontMap(mapping, cancellationToken);
            if (ReferenceEquals(before, after)) return State();
            viewVersion = nextView;
            editor.Select([]);
            return UpdatedState();
        }
    }
}
