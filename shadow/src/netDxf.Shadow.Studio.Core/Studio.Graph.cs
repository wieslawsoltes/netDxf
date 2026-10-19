using System.Text.Json;
using netDxf.Shadow.Graph;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed record GraphQueryRequest(string Tool, JsonElement Arguments);

public sealed partial class Studio
{
    private TopologyOptions? graphOptions;
    private ShadowSnapshot? graphSnapshot;
    private McpToolRegistry? graphQueryTools;

    private TopologyGraph BuildTopology(string requestedLayout, TopologyOptions options, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        if (!Current.Layout(requestedLayout).Exists) throw new ArgumentException("Unknown topology layout.");
        if (activeBranch?.BlockRoot is not null && requestedLayout != layout) throw new ArgumentException("The block editor topology uses the active layout.");
        if (graph is null || !ReferenceEquals(graphSnapshot, Current) || graphLayout != requestedLayout || graphFontRevision != CurrentFonts.Revision || graphOptions != options)
        {
            var scene = (activeBranch?.SceneBuilder ?? sceneBuilder).Build(Current, new(requestedLayout) { RootBlock = activeBranch?.BlockRoot }, token);
            var built = new TopologyBuilder().Build(scene, options, token);
            graph = built; graphSnapshot = Current; graphLayout = requestedLayout; graphFontRevision = CurrentFonts.Revision; graphOptions = options;
        }
        return graph;
    }

    /// <summary>Read-only palette endpoint sharing exactly the MCP graph schemas, algorithms and identity checks.</summary>
    public object GraphQuery(GraphQueryRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            graphQueryTools ??= CreateMcpRegistry();
            if (!graphQueryTools.Tools.Any(t => t.Name == request.Tool && t.Name.StartsWith("graph_", StringComparison.Ordinal) && t.ReadOnly))
                throw new ArgumentException("Choose a discovered graph analysis tool.");
            // Graph tools complete synchronously under this same reentrant gate.
            var result = graphQueryTools.ExecuteAsync(request.Tool, request.Arguments, token).GetAwaiter().GetResult();
            return new { result.IsError, result = McpJson.Parse(result.Text) };
        }
    }
}
