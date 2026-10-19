using System.Text.Json;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class ModelDiscoveryControllerVerification
{
    public static async Task<int> RunAsync(string endpoint, string discoveryKey)
    {
        var checks = 0;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException("Model controller: " + label); checks++; }
        var controller = new StudioAgentController(_ => new McpToolRegistry());
        await using (controller)
        {
            controller.Configure(new() { Provider = "openai", Model = "unchanged-fixture-model", ApiKey = "configured-fixture-key", Endpoint = endpoint + "/v1" });
            var session = controller.Session;
            var before = controller.Export();
            var request = JsonSerializer.Serialize(new AgentModelDiscoveryRequest { Provider = "chatgpt", ApiKey = discoveryKey, Endpoint = endpoint }, McpJson.Options);
            var result = await controller.ModelsAsync(request);
            Check(result.Status == "available" && result.Models.Single().Id == "controller-fixture-model", "unsaved credentials reach the configured model list endpoint");
            Check(result.Models[0].ContextWindowTokens is null, "companion capacity remains unknown");
            Check(ReferenceEquals(session, controller.Session) && controller.Export() == before, "model discovery preserves session and checkpoint exactly");
            Check(!controller.Export().Contains(discoveryKey, StringComparison.Ordinal), "temporary discovery key is never checkpointed");
            Check(!JsonSerializer.Serialize(result).Contains(discoveryKey, StringComparison.Ordinal), "discovery response contains no credential");
            foreach (var malformed in new[] { "{\"apiKey\":\"" + discoveryKey, "null", new string('x', 16385) })
            {
                try { await controller.ModelsAsync(malformed); throw new InvalidOperationException("Malformed request was accepted."); }
                catch (ArgumentException error) { Check(!error.ToString().Contains(discoveryKey, StringComparison.Ordinal), "malformed credential request is rejected without disclosure"); }
            }
        }
        try { await controller.ModelsAsync("{}"); throw new InvalidOperationException("Disposed controller accepted discovery."); }
        catch (ObjectDisposedException) { checks++; }
        return checks;
    }
}
