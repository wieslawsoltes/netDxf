using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Display-only composition: projected model copies never enter semantic graph analysis.</summary>
internal sealed partial class PaperSpaceCompiler
{
    private sealed record ViewEntry(ImmutableArray<ShadowDependency> Dependencies, PaperViewport View);
    private sealed record ItemEntry(RenderItem Source, PaperViewport View, bool PaperLinetypes, RenderItem? Result);
    private readonly Dictionary<(Guid, ObjectId, double, int), ViewEntry> views = new();
    private readonly Dictionary<(Guid, ObjectId, string), ItemEntry> items = new();

}
