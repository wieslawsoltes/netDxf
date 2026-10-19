using netDxf.Shadow.Drafting;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Studio;

public sealed record CadAttributeDefinitionOperation(ObjectId Block, AttributeDefinitionChanges Changes,
    bool UpdateExisting = false, bool ConvertTextModes = false);
public sealed record CadAttributeDefinitionRequest(long Revision, CadAttributeDefinitionOperation Definition);

public sealed partial class Studio
{
    public object CadReadAttributeDefinitions(long revision, string? block = null)
    {
        lock (gate)
        {
            Check(revision); var selected = CadSelected(Scene());
            var id = block is { Length: > 0 } ? new ObjectId(block) : activeBranch?.BlockRoot ??
                (selected.Length == 1 ? Current.Read(selected[0])["Block"].Id : null) ??
                throw new ArgumentException("Choose a local block definition or select one block reference.");
            var root = DraftingBlocks.Definition(Current, id);
            var references = Current.EntityIds.Select(Current.Read).Where(e => e.Type == "Insert" && e["Block"].Id == id).ToArray();
            return new { revision = Current.Revision, block = id, name = root["Name"].Text,
                drawingVersion = Current.Document["DrawingVariables"]["AcadVer"].Text,
                referenceCount = references.Length, currentSpaceCount = references.Count(e => e["Owner"].Id == Current.Layout(layout)["AssociatedBlock"].Id),
                definitions = DraftingAttributeDefinitions.Read(Current, id) };
        }
    }

    public object CadEditAttributeDefinitions(CadAttributeDefinitionRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Definition);
            var (plan, _) = PlanAttributeDefinitions(Current, request.Definition, token);
            return ApplyAuthoring(plan, CadSelected(Scene()), "attribute-definitions", request);
        }
    }

    private static (DraftingPlan Plan, AttributeSynchronizationPlan? Sync) PlanAttributeDefinitions(ShadowSnapshot before,
        CadAttributeDefinitionOperation request, CancellationToken token)
    {
        var plan = DraftingAttributeDefinitions.Plan(before, request.Block, request.Changes, request.UpdateExisting, token);
        if (!request.UpdateExisting) return (plan, null);
        var proposed = plan.Patch.Apply(before);
        var sync = AttributeSynchronization.Plan(proposed, request.Block, new(ConvertTextModes: request.ConvertTextModes), token);
        proposed = sync.Patch.Apply(proposed);
        return (new("Edit definitions and synchronize " + sync.BlockName, ShadowGraphPatch.Between(before, proposed), []), sync);
    }
}
