using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>Explicit resource import, not a DXF edit. HTTP callers must also supply the complete
    /// Studio receipt; the source revision alone is not a workspace/view identity.</summary>
    public object LoadBigFontMap(long revision, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var file = ShxBigFontMapFile.Read(bytes, cancellationToken: cancellationToken);
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); Check(revision); RequireLive();
            var before = shxFonts.Current; var nextView = checked(viewVersion + 1);
            var after = shxFonts.ImportBigFontMap(file);
            if (ReferenceEquals(before, after)) return State();
            viewVersion = nextView;
            editor.Select([]);
            return UpdatedState();
        }
    }
}

internal static class BigFontMapHttp
{
    internal static void MapBigFontMaps(this WebApplication app)
    {
        app.MapPost("/api/bigfont-map", async (Studio studio, HttpRequest request, long revision, CancellationToken token) =>
        {
            const int limit = 4 * 1024 * 1024;
            // Validate receipt before accepting bytes and again under the same source gate at publication.
            StudioHttp.Execute(studio, request, () => true);
            if (request.ContentLength > limit) return Results.BadRequest(new { error = "BIGFONT map limit is 4 MiB." });
            using var memory = new MemoryStream(); var buffer = new byte[32 * 1024];
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, token); if (read == 0) break;
                if (read > limit - memory.Length) return Results.BadRequest(new { error = "BIGFONT map limit is 4 MiB." });
                memory.Write(buffer, 0, read);
            }
            return Results.Ok(StudioHttp.Execute(studio, request, () => studio.LoadBigFontMap(revision, memory.ToArray(), token)));
        });
    }
}
