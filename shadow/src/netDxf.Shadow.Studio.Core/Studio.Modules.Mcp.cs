using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private void RegisterModuleTools(McpToolRegistry registry)
    {
        RegisterModuleConnectivityTool(registry);
        RegisterModuleScheduleTool(registry);
        registry.Register(new("studio_modules", "Discover registered discipline modules, enablement, commands, fields and standards references. Disabled modules preserve their native DXF data.",
            McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, token) =>
        {
            token.ThrowIfCancellationRequested(); return ValueTask.FromResult(McpToolResult.FromObject(CadModules()));
        });
        registry.Register(new("studio_module_catalog", "Search an enabled discipline's versioned symbol library with units, insertion points, attributes, ports and standards references. Use an exact symbol version when placing.",
            McpJson.Parse("""{"type":"object","properties":{"module":{"type":"string"},"query":{"type":"string","maxLength":512},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":200}},"required":["module"],"additionalProperties":false}"""), true), (args, token) =>
        {
            token.ThrowIfCancellationRequested();
            var request = args.Deserialize<CadModuleCatalogRequest>(McpJson.Options) ?? throw new ArgumentException("Missing catalog request.");
            return ValueTask.FromResult(McpToolResult.FromObject(CadModuleSymbols(request)));
        });
        registry.Register(new("studio_module_analyze", "Analyze discipline connectivity, data, tags and references without editing. Discover CadModuleAnalysisRequest through studio_request_schema. Results identify exact source objects and report partial scans.",
            McpJson.Parse("""{"type":"object","properties":{"identity":{"type":"object","properties":{"documentId":{"type":"string"},"workspaceId":{"type":"string"},"viewVersion":{"type":"integer","minimum":0}},"required":["documentId","workspaceId","viewVersion"],"additionalProperties":false},"request":{"type":"object"}},"required":["identity","request"],"additionalProperties":false}"""), true), (args, token) =>
        {
            var identity = args.GetProperty("identity").Deserialize<StudioRequestIdentity>(McpJson.Options) ?? throw new ArgumentException("Missing identity.");
            var request = args.GetProperty("request").Deserialize<CadModuleAnalysisRequest>(McpJson.Options) ?? throw new ArgumentException("Missing analysis request.");
            return ValueTask.FromResult(McpToolResult.FromObject(ExecuteRequest(identity, () => CadAnalyzeModule(request, token))));
        });
    }
}
