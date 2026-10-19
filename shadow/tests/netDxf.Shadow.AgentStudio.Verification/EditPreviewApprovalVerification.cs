using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

internal static class EditPreviewApprovalVerification
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Edit preview approval verification failed: " + message); checks++; }
        using var studio = new Studio(false);
        await using var controller = new StudioAgentController(_ => studio.CreateMcpRegistry());
        JsonElement State() => McpJson.Element(studio.State());
        object Identity()
        {
            var state = State(); return new { documentId = state.GetProperty("documentId").GetGuid(), workspaceId = state.GetProperty("workspaceId").GetString(), viewVersion = state.GetProperty("viewVersion").GetInt64() };
        }
        async Task<JsonElement> Prepare()
        {
            var response = await controller.Call("studio_edit_preview", McpJson.Element(new { identity = Identity(), request = new {
                revision = studio.CadRevision, description = "Reviewed circle", operations = new[] { new { kind = "command", command = new { name = "CIRCLE", points = new[] { new { x = 5, y = 10 } }, value = 3 } } } } }));
            Check(!response.IsError, response.Text); return McpJson.Parse(response.Text);
        }
        JsonElement Apply(JsonElement receipt) => McpJson.Element(new { identity = Identity(), request = new { revision = studio.CadRevision, previewId = receipt.GetProperty("previewId").GetString() } });
        controller.Configure(new() { PermissionMode = "readOnly" });
        var proposal = await Prepare(); var args = Apply(proposal);
        Check((await controller.Call("studio_apply_edit_preview", args)).IsError && studio.CadRevision == 0, "ReadOnly blocks a real preview write without changing source.");
        controller.Configure(new() { PermissionMode = "ask" }); var approvals = 0; var approve = false;
        controller.Session.EventEmitted += entry =>
        {
            if (entry.Type != "approvalRequested") return; approvals++;
            controller.Approve(entry.Data!.Value.GetProperty("requestId").GetString()!, approve);
        };
        Check((await controller.Call("studio_apply_edit_preview", args)).IsError && approvals == 1 && studio.CadRevision == 0, "Ask denial leaves the exact preview and drawing unchanged.");
        approve = true;
        Check(!(await controller.Call("studio_apply_edit_preview", args)).IsError && approvals == 2, "Ask approval applies the reviewed token.");
        Check(State().GetProperty("entities").GetInt32() == 1 && State().GetProperty("undoCount").GetInt32() == 1, "Approved preview creates one native source edit and undo entry.");
        proposal = await Prepare(); args = Apply(proposal); studio.CadCommand(new(studio.CadRevision, new("LINE", [new(0, 0), new(20, 0)])));
        var revision = studio.CadRevision;
        Check((await controller.Call("studio_apply_edit_preview", args)).IsError && approvals == 3 && studio.CadRevision == revision, "Approval cannot make an expired proposal current or retry it.");
        Check(State().GetProperty("entities").GetInt32() == 2 && State().GetProperty("undoCount").GetInt32() == 2, "Expired approved proposal leaves both native geometry and history intact.");
        return checks;
    }
}
