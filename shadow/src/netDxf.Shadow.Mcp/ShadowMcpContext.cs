using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Mcp;

public sealed record ShadowMcpIdentity(Guid DocumentId, string WorkspaceId, long ViewVersion);

/// <summary>
/// Resolves host state for each invocation. A Studio host uses its own gate and supplies its
/// current editor, so document replacement and branch changes never leave stale tool bindings.
/// </summary>
public sealed class ShadowMcpContext(Func<EditorSession> editor, Func<ShadowSession?>? source = null)
{
    private readonly object gate = new();
    public Func<EditorSession> Editor { get; } = editor ?? throw new ArgumentNullException(nameof(editor));
    public Func<ShadowSession?>? Source { get; } = source;
    public Func<ShadowMcpIdentity>? Identity { get; init; }
    public Func<Func<McpToolResult>, McpToolResult>? Synchronize { get; init; }
    /// <summary>Optional host invalidation callback. Observer failures do not invalidate committed editor transactions.</summary>
    public Action? Changed { get; init; }
    /// <summary>Active semantic layout; defaults to Model when no host is supplied.</summary>
    public Func<string>? Layout { get; init; }
    /// <summary>Optional host topology builder preserving its active block root and projector resources.</summary>
    public Func<string, TopologyOptions, CancellationToken, TopologyGraph>? Topology { get; init; }
    public bool RequireWorkspaceIdentity { get; init; }

    internal ShadowMcpIdentity GetIdentity(ShadowSnapshot snapshot) => Identity?.Invoke() ?? new(snapshot.DocumentId, "live", 0);
    internal McpToolResult Run(Func<McpToolResult> operation)
    {
        if (Synchronize is { } synchronize) return synchronize(operation);
        lock (gate) return operation();
    }
}
