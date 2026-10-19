using System.Text.Json;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed partial class StudioAgentController
{
    /// <summary>Discovers models from unsaved settings without configuring or interrupting the active session.</summary>
    public Task<AgentModelDiscoveryResult> ModelsAsync(string request, CancellationToken cancellationToken = default)
    {
        lock (lifecycle) ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(request) || request.Length > 16384)
            throw new ArgumentException("Model discovery requires a bounded configuration request.");
        AgentModelDiscoveryRequest options;
        try
        {
            options = JsonSerializer.Deserialize<AgentModelDiscoveryRequest>(request, McpJson.Options)
                ?? throw new ArgumentException("Missing model discovery configuration.");
        }
        catch (JsonException) { throw new ArgumentException("Invalid model discovery configuration."); }
        return ShadowAgentProviders.DiscoverModelsAsync(options, cancellationToken: cancellationToken);
    }
}
