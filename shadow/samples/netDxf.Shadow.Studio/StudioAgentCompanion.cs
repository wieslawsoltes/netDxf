using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed partial class StudioAgentController
{
    private StudioAgentCompanion? companion;

    public async Task<object> CompanionConnect(string endpoint, string token)
    {
        lock (lifecycle) { EnsureIdle(); transitioning = true; }
        StudioAgentCompanion? candidate = null;
        try
        {
            candidate = await StudioAgentCompanion.Connect(endpoint, token).ConfigureAwait(false);
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                var previous = companion; companion = candidate;
                try
                {
                    InstallTools(documentId);
                }
                catch { companion = previous; throw; }
                candidate = null; previous?.Dispose();
                if (token.Length >= 8 && !knownCredentials.Contains(token, StringComparer.Ordinal))
                    Volatile.Write(ref knownCredentials, [.. knownCredentials, token]);
                return new { connected = true, toolCount = companion!.Tools.Count, tools = companion.Tools.Select(t => t.Name).ToArray() };
            }
        }
        finally { candidate?.Dispose(); lock (lifecycle) transitioning = false; }
    }

    public object CompanionDisconnect()
    {
        lock (lifecycle)
        {
            EnsureIdle();
            companion?.Dispose(); companion = null; executor.Set(BuildTools(documentId, allowUnavailableDrawing: true));
            return new { connected = false, providerDisconnected = false };
        }
    }
}

/// <summary>The browser attaches only an explicitly authenticated loopback workspace grant.</summary>
internal sealed class StudioAgentCompanion : IDisposable
{
    private readonly HttpClient client;
    private readonly string endpoint;
    private string token;
    public IReadOnlyList<McpToolDefinition> Tools { get; }
    private StudioAgentCompanion(HttpClient client, IReadOnlyList<McpToolDefinition> tools, string endpoint, string token)
    { this.client = client; Tools = tools; this.endpoint = endpoint; this.token = token; }

    public IChatClient CreateChatClient(ShadowAgentProviderOptions options) =>
        ShadowAgentProviders.CreateBrowserBridge(options, endpoint, token);

    public static async Task<StudioAgentCompanion> Connect(string endpoint, string token)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(token);
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use the companion's http://127.0.0.1:PORT address.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(token) || token.Length < 16 || token.Length > 8192 || token.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter the companion access token printed by AgentHost.", nameof(token));
        var client = new HttpClient { BaseAddress = uri, Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync("workspace/tools", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var json = await Read(response, timeout.Token).ConfigureAwait(false);
            var tools = JsonSerializer.Deserialize<McpToolDefinition[]>(json, McpJson.Options) ?? [];
            if (tools.Length > 64 || tools.Any(t => !t.Name.StartsWith("companion_", StringComparison.Ordinal)) || tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tools.Length)
                throw new InvalidOperationException("The companion returned an invalid workspace catalog.");
            return new(client, tools, uri.GetLeftPart(UriPartial.Authority), token);
        }
        catch { client.Dispose(); throw; }
    }

    public void Register(McpToolRegistry registry)
    {
        foreach (var tool in Tools)
        {
            var name = tool.Name;
            registry.Register(tool, async (arguments, cancellationToken) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "workspace/call")
                { Content = JsonContent.Create(new { name, arguments }, options: McpJson.Options) };
                // Never retry a native operation after a transport error: it may already have committed.
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var json = await Read(response, cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Deserialize<McpToolResult>(json, McpJson.Options) ?? throw new InvalidOperationException("The companion returned an empty result.");
            });
        }
    }

    private static async Task<string> Read(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream(); var chunk = new byte[16384];
        while (true)
        {
            var read = await input.ReadAsync(chunk, token).ConfigureAwait(false); if (read == 0) break;
            if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidOperationException("The companion response exceeds its transfer budget.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
    public void Dispose() { token = ""; client.Dispose(); }
}
