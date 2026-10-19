using System.Text.Json;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Studio;

public sealed record StudioEditPreviewToolRequest(StudioRequestIdentity Identity, CadEditPreviewRequest Request);
public sealed record StudioApplyEditPreviewToolRequest(StudioRequestIdentity Identity, CadEditPreviewTokenRequest Request);

public sealed partial class Studio
{
    private void RegisterEditPreviewTools(McpToolRegistry registry)
    {
        const string identity = """{"type":"object","properties":{"documentId":{"type":"string"},"workspaceId":{"type":"string","maxLength":128},"viewVersion":{"type":"integer","minimum":0}},"required":["documentId","workspaceId","viewVersion"],"additionalProperties":false}""";
        var previewSchema = McpJson.Parse("{\"type\":\"object\",\"properties\":{\"identity\":" + identity + ",\"request\":" +
            """{"type":"object","properties":{"revision":{"type":"integer","minimum":0},"description":{"type":"string","minLength":1,"maxLength":512},"operations":{"type":"array","minItems":1,"maxItems":128,"items":{"type":"object"}}},"required":["revision","description","operations"],"additionalProperties":false}},"required":["identity","request"],"additionalProperties":false}""");
        registry.Register(new("studio_edit_preview", "Prepare a nonmutating, typed edit proposal for review. Use studio_request_schema for StudioEditPreviewToolRequest and CadEditOperation. The proposal binds the exact document/workspace/view/selection/resources; native validation runs only when applied. Inspect before/after images in Edit previews. No source edit or undo entry is created.", previewSchema, true), (args, token) =>
        {
            var input = args.Deserialize<StudioEditPreviewToolRequest>(McpJson.Options) ?? throw new ArgumentException("Missing preview request.");
            var response = ExecuteRequest(input.Identity, () =>
            {
                var receipt = JsonSerializer.SerializeToElement(CadEditPreview(input.Request, token), McpJson.Options);
                var id = receipt.GetProperty("previewId").GetString()!; var pending = FindEditPreview(id); var summary = pending.Preview.Summary;
                return new { previewId = id, input.Identity.DocumentId, input.Identity.WorkspaceId, input.Identity.ViewVersion, revision = pending.Preview.Target.Revision,
                    description = pending.Preview.Description, validation = pending.Preview.Validation,
                    summary = new { summary.Added, summary.Removed, summary.Changed, details = summary.Details.Take(50), detailsTruncated = summary.DetailsTruncated || summary.Details.Length > 50 },
                    health = new { beforeErrors = pending.BeforeHealth.Issues.Count(i => i.Severity == DiagnosticSeverity.Error), afterErrors = pending.AfterHealth.Issues.Count(i => i.Severity == DiagnosticSeverity.Error),
                        complete = pending.BeforeHealth.Complete && pending.AfterHealth.Complete, issues = pending.AfterHealth.Issues.Take(20) },
                    review = new { before = new { previewId = id, after = false }, after = new { previewId = id, after = true } } };
            });
            return ValueTask.FromResult(McpToolResult.FromObject(response));
        });
        var applySchema = McpJson.Parse("{\"type\":\"object\",\"properties\":{\"identity\":" + identity + ",\"request\":" +
            """{"type":"object","properties":{"revision":{"type":"integer","minimum":0},"previewId":{"type":"string","minLength":1,"maxLength":128}},"required":["revision","previewId"],"additionalProperties":false}},"required":["identity","request"],"additionalProperties":false}""");
        registry.Register(new("studio_apply_edit_preview", "Apply a reviewed edit proposal atomically as one undo command. This is a drawing write and requires approval under the agent's write policy. Supply the exact preview receipt and current identity; stale proposals are rejected without replanning or retrying.", applySchema, false), (args, token) =>
        {
            token.ThrowIfCancellationRequested(); var input = args.Deserialize<StudioApplyEditPreviewToolRequest>(McpJson.Options) ?? throw new ArgumentException("Missing apply request.");
            var result = ExecuteRequest(input.Identity, () => { CadEditPreviewApply(input.Request); return new { applied = true, input.Request.PreviewId, Current.DocumentId, revision = Current.Revision, workspaceId = WorkspaceId, viewVersion }; });
            return ValueTask.FromResult(McpToolResult.FromObject(result));
        });
    }
}
