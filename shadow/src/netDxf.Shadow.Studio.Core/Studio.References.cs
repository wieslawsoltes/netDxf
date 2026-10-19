using System.Text.Json;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private DrawingReferenceIdentity ReferenceIdentity => new(Current.DocumentId, WorkspaceId, viewVersion, Current.Revision);
    public DrawingReferenceSet CaptureReferences(DrawingReferenceCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Maximum is < 1 or > DrawingReferences.MaximumCount) throw new ArgumentException("Capture at most 32 selected objects.");
        lock (gate)
        {
            ExecuteRequest(new(request.DocumentId, request.WorkspaceId, request.ViewVersion), () => { Check(request.Revision); return true; });
            var selected = Scene().Items.Where(i => editor.State.Selection.Contains(i.Key)).GroupBy(DraftingQueries.Root).Select(group => group.First()).ToArray();
            if (selected.Length > request.Maximum) throw new ArgumentException("The selection exceeds the attachment limit. Select at most " + request.Maximum + " objects.");
            return new(ReferenceIdentity, selected.Select(ReferenceFor).ToArray());
        }
    }
    private DrawingObjectReference ReferenceFor(RenderItem item)
    {
        var id = DraftingQueries.Root(item); var source = Current.Read(id); var handle = source["Handle"].Text;
        return new(Current.DocumentId, WorkspaceId, viewVersion, Current.Revision, id.Value, string.IsNullOrEmpty(handle) ? null : handle,
            Current.Objects[id].TypeName, layout, item.Key, item.InsertPath.Select(p => p.Value).ToArray(), item.Entity == id ? null : item.Entity.Value);
    }
    public DrawingReferenceValidation ValidateReferences(DrawingReferenceValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var references = DrawingReferences.ValidateShape(request.References);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var scene = Scene(); var items = new List<DrawingReferenceResolution>(references.Length);
            foreach (var reference in references)
            {
                DrawingObjectReference? fresh = null; string status, message;
                if (reference.DocumentId != Current.DocumentId) { status = "wrongDocument"; message = "This reference belongs to another drawing."; }
                else if (reference.WorkspaceId != WorkspaceId) { status = "wrongWorkspace"; message = "Return to the referenced workspace or remove this attachment."; }
                else if (!Current.Objects.TryGetValue(new(reference.ObjectId), out var obj)) { status = "deleted"; message = "This object was deleted. Remove its attachment."; }
                else
                {
                    var occurrence = reference.OccurrenceKey is null ? null : scene.Items.FirstOrDefault(i => i.Key == reference.OccurrenceKey);
                    if (occurrence is null || DraftingQueries.Root(occurrence).Value != reference.ObjectId)
                    { status = "occurrenceMissing"; message = "The selected occurrence is no longer visible in this layout."; }
                    else
                    {
                        fresh = ReferenceFor(occurrence);
                        if (reference.Type != obj.TypeName || reference.Handle != fresh.Handle || reference.DefinitionObjectId != fresh.DefinitionObjectId
                            || !(reference.InsertPath ?? []).SequenceEqual(fresh.InsertPath ?? [], StringComparer.Ordinal) || reference.Layout != fresh.Layout)
                        { status = "identityMismatch"; message = "The object or occurrence identity differs. Capture the selection again."; fresh = null; }
                        else if (reference.Revision != Current.Revision || reference.ViewVersion != viewVersion)
                        { status = request.Refresh ? "refreshed" : "stale"; message = request.Refresh ? "Updated to the current drawing revision." : "The drawing changed. Refresh this attachment before sending."; }
                        else { status = "current"; message = "Current drawing object."; }
                    }
                }
                items.Add(new(reference, status, fresh, message));
            }
            return new(ReferenceIdentity, items.All(i => i.Status is "current" or "refreshed"), items);
        }
    }
    private void RegisterReferenceTools(McpToolRegistry registry)
    {
        registry.Register(new(DrawingReferences.ValidationTool, "Validate captured drawing object references against the active document, workspace, revision and INSERT occurrence. Refresh only on an explicit request; never replace a deleted object by its handle.",
            McpJson.Parse("""{"type":"object","properties":{"references":{"type":"array","maxItems":32,"items":{"type":"object"}},"refresh":{"type":"boolean"}},"required":["references"],"additionalProperties":false}"""), true),
            (args, token) => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(McpToolResult.FromObject(ValidateReferences(args.Deserialize<DrawingReferenceValidationRequest>(McpJson.Options)!))); });
    }
}
