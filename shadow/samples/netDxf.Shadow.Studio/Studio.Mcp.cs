using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>
    /// Creates an extensible MCP catalog bound to this Studio. Each invocation resolves
    /// the active document and branch under the same gate used by all Studio operations.
    /// </summary>
    public McpToolRegistry CreateMcpRegistry()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return ShadowMcpTools.Create(new(() => editor, () => session)
            {
                Identity = () => new(Current.DocumentId, WorkspaceId, viewVersion),
                RequireWorkspaceIdentity = true,
                Synchronize = operation =>
                {
                    lock (gate)
                    {
                        ObjectDisposedException.ThrowIf(disposed, this);
                        return operation();
                    }
                },
                Changed = () => { graph = null; PublishChange(); }
            });
        }
    }
}
