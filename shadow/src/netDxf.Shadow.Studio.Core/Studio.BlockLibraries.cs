using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Studio;

public sealed record CadBlockLibraryOperation(string SourceName, string Name, string? LibraryId = null,
    BlockLibraryConflict BlockConflict = BlockLibraryConflict.Reject,
    BlockLibraryResourceConflict ResourceConflict = BlockLibraryResourceConflict.Reuse, string? ContentBase64 = null);
public sealed record CadBlockLibraryExportRequest(long Revision, ObjectId Block, bool Binary = false);
public sealed record CadBlockLibrarySummary(ObjectId Block, string Name, bool DetailsTruncated, ImmutableArray<BlockLibraryResource> Resources);

public sealed partial class Studio
{
    private sealed record OpenBlockLibrary(Guid Document, string Name, DrawingBlockLibrary Library, string ContentBase64);
    private readonly Dictionary<string, OpenBlockLibrary> blockLibraries = new(StringComparer.Ordinal);

    public object CadOpenBlockLibrary(long revision, string name, byte[] bytes, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(revision);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl)) throw new ArgumentException("Supply a library file name of at most 255 characters.");
            var library = DrawingBlockLibrary.Read(bytes, token);
            foreach (var key in blockLibraries.Where(pair => pair.Value.Document != Current.DocumentId).Select(pair => pair.Key).ToArray()) blockLibraries.Remove(key);
            if (blockLibraries.Count >= 2) blockLibraries.Remove(blockLibraries.Keys.First());
            var id = Guid.NewGuid().ToString("N");
            blockLibraries.Add(id, new(Current.DocumentId, name, library, Convert.ToBase64String(bytes)));
            return BlockLibraryReceipt(id, blockLibraries[id]);
        }
    }

    public object CadBlockLibraries()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return blockLibraries.Where(pair => pair.Value.Document == Current.DocumentId).Select(pair => BlockLibraryReceipt(pair.Key, pair.Value)).ToArray();
        }
    }
    private static object BlockLibraryReceipt(string id, OpenBlockLibrary library) => new
    {
        libraryId = id, name = library.Name, entries = library.Library.Entries,
        note = "Import the drawing's model space or one named definition and its referenced resources. Paper layouts are excluded."
    };

    private (BlockLibraryPlan Plan, CadBlockLibraryOperation Recorded) PlanBlockLibrary(ShadowSnapshot target, CadBlockLibraryOperation operation, CancellationToken token)
    {
        DrawingBlockLibrary library; string content;
        if (operation.ContentBase64 is { } inline)
        {
            if (operation.LibraryId is not null || inline.Length > (DrawingBlockLibrary.MaximumBytes + 2) / 3 * 4)
                throw new ArgumentException("Supply one bounded inline library or a loaded library ID.");
            library = DrawingBlockLibrary.Read(Convert.FromBase64String(inline), token); content = inline;
        }
        else
        {
            if (operation.LibraryId is not { } id || !blockLibraries.TryGetValue(id, out var found) || found.Document != Current.DocumentId)
                throw new ShadowConflictException("The loaded block library is unavailable. Load it again before creating the preview.");
            library = found.Library; content = found.ContentBase64;
        }
        var plan = library.Plan(target, operation.SourceName, operation.Name, operation.BlockConflict, operation.ResourceConflict, token);
        return (plan, operation with { LibraryId = null, ContentBase64 = content });
    }

    public byte[] CadExportBlockLibrary(CadBlockLibraryExportRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision);
            return DrawingBlockLibrary.Export(Current, request.Block, request.Binary, token);
        }
    }
}
