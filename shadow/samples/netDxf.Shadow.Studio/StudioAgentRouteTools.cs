using System.Text.Json.Nodes;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

internal static class StudioAgentRouteTools
{
    /// <summary>Apply the same routing boundary before native URI or browser route decoding.</summary>
    public static bool IsStudioPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 8192 || path.Contains('#') || path.Contains('\\') || path.Any(char.IsControl)) return false;
        var route = path.Split('?', 2)[0];
        // URI and ASP.NET routing normalize case and escapes at different layers. Validate
        // every decoding stage so aliases cannot call back into agent lifecycle/approval APIs.
        for (var pass = 0; pass < 5; pass++)
        {
            if (!route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || route.StartsWith("/api/agent", StringComparison.OrdinalIgnoreCase)
                || route.Contains('\\') || route.Contains('#') || route.Contains('?') || route.Any(char.IsControl)
                || route[1..].Split('/').Any(segment => segment is "" or "." or "..")) return false;
            string decoded;
            try { decoded = Uri.UnescapeDataString(route); }
            catch (UriFormatException) { return false; }
            if (decoded == route) return true;
            route = decoded;
        }
        return false;
    }

    public static void RegisterRead(McpToolRegistry registry) => registry.Register(new("studio_read",
        "Read any discovered GET Studio route, including detailed CAD state, geometry, graph, history, settings and exports. Use studio_routes to discover exact paths. This tool cannot submit a mutation.",
        McpJson.Parse("""{"type":"object","properties":{"path":{"type":"string"},"headers":{"type":"object","additionalProperties":{"type":"string"}}},"required":["path"],"additionalProperties":false}"""), true),
        (arguments, token) =>
        {
            var request = JsonNode.Parse(arguments.GetRawText())!.AsObject(); request["method"] = "GET";
            return registry.ExecuteAsync("studio_request", McpJson.Parse(request.ToJsonString()), token);
        });
}
