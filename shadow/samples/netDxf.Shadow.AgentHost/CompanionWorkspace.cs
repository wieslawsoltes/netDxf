using System.Text.Json;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.AgentHost;

/// <summary>The startup directory grant is immutable; process execution is a separate explicit grant.</summary>
public sealed class CompanionWorkspace : IMcpToolExecutor
{
    public const string Prefix = "companion_";
    private readonly McpToolRegistry _registry = new();
    private readonly SemaphoreSlim _capacity = new(16, 16);

    public CompanionWorkspace(AgentHostOptions options)
    {
        if (options.Workspace is null) throw new ArgumentException("A native workspace must be granted at host startup.", nameof(options));
        AgentWorkspaceTools.Register(_registry, new DirectoryAgentWorkspace(options.Workspace,
            options.MaximumWorkspaceBytes, options.MaximumWorkspaceFiles));
        if (options.AllowProcesses) AgentCommandTools.Register(_registry, new LocalAgentCommandRunner(options.Workspace));
        Tools = _registry.Tools.Select(tool => tool with
        {
            Name = Prefix + tool.Name,
            Description = "Granted native companion directory. " + tool.Description,
        }).ToArray();
    }

    public IReadOnlyList<McpToolDefinition> Tools { get; }

    public async ValueTask<McpToolResult> ExecuteAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (name is null || !Tools.Any(tool => tool.Name == name))
            return McpToolResult.Error("unknown_tool", "This tool is not included in the companion's explicit workspace and process grants.");
        if (!await _capacity.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return McpToolResult.Error("companion_busy", "The companion's workspace call capacity is full. No operation was dispatched.");
        try { return await _registry.ExecuteAsync(name[Prefix.Length..], arguments, cancellationToken).ConfigureAwait(false); }
        finally { _capacity.Release(); }
    }
}
