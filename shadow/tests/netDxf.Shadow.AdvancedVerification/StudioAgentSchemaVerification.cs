using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class StudioAgentSchemaVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("Studio agent schemas expose nested drafting wire contracts", () =>
        {
            using var studio = new Studio(); var tools = studio.CreateMcpRegistry();
            var schema = Read(tools, new { type = "CadCommandRequest" }).GetProperty("schema");
            var definitions = schema.GetProperty("$defs");
            var command = definitions.GetProperty("netDxf.Shadow.Drafting.DraftingCommand");
            Require(command.GetProperty("required").EnumerateArray().Any(v => v.GetString() == "name"));
            Require(command.GetProperty("properties").GetProperty("layer").GetProperty("default").GetString() == "0");
            var vector = definitions.GetProperty("netDxf.Shadow.Vec3").GetProperty("properties");
            Require(vector.GetProperty("x").GetProperty("type").GetString() == "number" && !vector.TryGetProperty("length", out _));
            Require(definitions.GetProperty("netDxf.Shadow.ObjectId").GetProperty("properties").GetProperty("value").GetProperty("type").GetString() == "string");
            var body = JsonSerializer.Deserialize<CadCommandRequest>("""{"revision":0,"command":{"name":"LINE","points":[{"x":1000,"y":1000},{"x":1100,"y":1000}]}}""", StudioJson.CreateOptions())!;
            var before = studio.CadRevision; studio.CadCommand(body); Require(studio.CadRevision == before + 1);
        }));
        tests.Add(("Studio agent schemas retain nullable arrays numeric enums and defaults", () =>
        {
            using var studio = new Studio(); var tools = studio.CreateMcpRegistry();
            var schema = Read(tools, new { type = "CadHatchAuthoringRequest" }).GetProperty("schema");
            var definitions = schema.GetProperty("$defs");
            var hatch = definitions.GetProperty("netDxf.Shadow.Drafting.HatchAuthoringRequest").GetProperty("properties");
            Require(hatch.GetProperty("polygons").GetProperty("items").GetProperty("type").GetString() == "array");
            Require(hatch.GetProperty("pattern").GetProperty("anyOf")[1].GetProperty("type").GetString() == "null");
            var pattern = definitions.GetProperty("netDxf.Shadow.Drafting.DraftingHatchPattern").GetProperty("properties");
            Require(pattern.GetProperty("type").GetProperty("enum").EnumerateArray().All(e => e.ValueKind == JsonValueKind.Number));
            Require(pattern.GetProperty("scale").GetProperty("default").GetInt32() == 1);
            var snap = Read(tools, new { type = "CadSnapRequest" }).GetProperty("schema").GetProperty("$defs")
                .GetProperty("netDxf.Shadow.Studio.CadSnapRequest").GetProperty("properties").GetProperty("modes");
            Require(snap.GetProperty("type").GetString() == "integer" && !snap.TryGetProperty("enum", out _));
            var layerState = Read(tools, new { type = "CadLayerStateRequest" }).GetProperty("schema").GetProperty("$defs")
                .GetProperty("netDxf.Shadow.Drafting.LayerStateEdit").GetProperty("properties");
            var fields = layerState.GetProperty("fields");
            Require(fields.GetProperty("type").GetString() == "integer" && !fields.TryGetProperty("enum", out _));
            Require(fields.GetProperty("default").GetInt32() == 991 && layerState.GetProperty("restoreCurrentLayer").GetProperty("default").GetBoolean());
        }));
        tests.Add(("Studio agent schema catalog is bounded searchable and complete", () =>
        {
            using var studio = new Studio(); var tools = studio.CreateMcpRegistry();
            var names = new HashSet<string>(); var offset = 0; var total = -1;
            while (true)
            {
                var page = Read(tools, new { offset, limit = 3 }); total = page.GetProperty("total").GetInt32();
                foreach (var item in page.GetProperty("items").EnumerateArray()) Require(names.Add(item.GetProperty("fullName").GetString()!));
                if (page.GetProperty("nextOffset").ValueKind == JsonValueKind.Null) break;
                offset = page.GetProperty("nextOffset").GetInt32();
            }
            Require(names.Count == total && names.Contains("netDxf.Shadow.Drafting.BlockDefinitionRequest"));
            var filtered = Read(tools, new { search = "Hatch" });
            Require(filtered.GetProperty("items").EnumerateArray().All(i => i.GetProperty("fullName").GetString()!.Contains("Hatch")));
            Require(tools.ExecuteAsync("studio_request_schema", McpJson.Element(new { type = "System.IO.File" })).GetAwaiter().GetResult().IsError);
            Require(tools.ExecuteAsync("studio_request_schema", McpJson.Element(new { limit = 0 })).GetAwaiter().GetResult().IsError);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { tools.ExecuteAsync("studio_request_schema", McpJson.EmptyObject, cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }));
        tests.Add(("Studio attribute schema exposes identity-bound optional instance edits", () =>
        {
            using var studio = new Studio(); var tools = studio.CreateMcpRegistry();
            var definitions = Read(tools, new { type = "CadAttributeRequest" }).GetProperty("schema").GetProperty("$defs");
            var request = definitions.GetProperty("netDxf.Shadow.Studio.CadAttributeRequest");
            Require(request.GetProperty("required").EnumerateArray().Select(v => v.GetString()).ToHashSet()
                .SetEquals(new[] { "revision", "insert", "attributes" }));
            var edit = definitions.GetProperty("netDxf.Shadow.Drafting.AttributeEdit");
            Require(edit.GetProperty("required").GetArrayLength() == 1 && edit.GetProperty("required")[0].GetString() == "attribute");
            foreach (var field in new[] { "value", "textStyle", "height", "color", "positionLocked", "lineSpacing" })
                Require(edit.GetProperty("properties").GetProperty(field).GetProperty("anyOf")
                    .EnumerateArray().Any(v => v.TryGetProperty("type", out var type) && type.GetString() == "null"));
        }));
    }
    private static JsonElement Read(McpToolRegistry tools, object args)
    {
        var result = tools.ExecuteAsync("studio_request_schema", McpJson.Element(args)).GetAwaiter().GetResult();
        if (result.IsError) throw new InvalidOperationException(result.Text);
        return McpJson.Parse(result.Text);
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Studio request discovery contract failed."); }
}
