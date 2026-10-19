using System.Collections.Immutable;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Studio;

public sealed record CadBlockDefinitionRequest(long Revision, BlockDefinitionRequest Definition);
public sealed record CadBlockRenameRequest(long Revision, ObjectId Block, string Name);
public sealed record CadHatchAuthoringRequest(long Revision, HatchAuthoringRequest Hatch);
public sealed record CadHatchPatternRequest(long Revision, DraftingHatchPattern Pattern, ImmutableArray<ObjectId> Entities = default);

public sealed partial class Studio
{
    public object CadDefineBlock(CadBlockDefinitionRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Definition);
            var selected = CadSelected(Scene());
            var definition = request.Definition with { Entities = AuthoringSelection(request.Definition.Entities, selected) };
            var plan = activeBranch?.BlockRoot is { } root ? drafting.PlanBlockDefinitionInDefinition(Current, definition, root, token) :
                drafting.PlanBlockDefinition(Current, definition, layout, token);
            plan = InitializeCreatedInserts(Current, plan, [], token);
            return ApplyAuthoring(plan, selected, "block-define", definition);
        }
    }
    public object CadRenameBlock(CadBlockRenameRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            return ApplyAuthoring(drafting.RenameBlock(Current, request.Block, request.Name), CadSelected(Scene()), "block-rename", request);
        }
    }
    public object CadCreateHatch(CadHatchAuthoringRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Hatch);
            var selected = CadSelected(Scene()); var hatch = request.Hatch;
            if (hatch.Polygons.IsDefaultOrEmpty || !hatch.Boundaries.IsDefaultOrEmpty)
                hatch = hatch with { Boundaries = AuthoringSelection(hatch.Boundaries, selected) };
            var plan = activeBranch?.BlockRoot is { } root ? DraftingHatches.CreateInDefinition(Current, hatch, root, token) :
                DraftingHatches.Create(Current, hatch, layout, token);
            return ApplyAuthoring(plan, selected, "hatch-create", hatch);
        }
    }
    public object CadReadHatchPattern(long revision)
    {
        lock (gate)
        {
            Check(revision); var selected = CadSelected(Scene());
            if (selected.Length != 1) return new { mixed = true, pattern = (DraftingHatchPattern?)null };
            return new { mixed = false, entity = selected[0].Value, pattern = DraftingHatches.ReadPattern(Current, selected[0]) };
        }
    }
    public object CadChangeHatchPattern(CadHatchPatternRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); var selected = AuthoringSelection(request.Entities, CadSelected(Scene()));
            return ApplyAuthoring(DraftingHatches.ChangePattern(Current, selected, request.Pattern, token), selected, "hatch-pattern", request with { Entities = selected });
        }
    }
    private static ImmutableArray<ObjectId> AuthoringSelection(ImmutableArray<ObjectId> requested, ImmutableArray<ObjectId> selected)
    {
        if (requested.IsDefaultOrEmpty) return selected;
        var permitted = selected.ToHashSet();
        if (requested.Any(id => !permitted.Contains(id))) throw new ShadowConflictException("Authoring sources must belong to the current source selection.");
        return requested;
    }
    private object ApplyAuthoring(DraftingPlan plan, ImmutableArray<ObjectId> previous, string? workflowOperation = null, object? workflowArguments = null)
    {
        plan.Execute(editor);
        cadSelectionOrder = plan.Created.IsEmpty ? previous.Where(id => Current.Objects.ContainsKey(id)).ToImmutableArray() : plan.Created;
        editor.Select(DraftingQueries.Expand(Scene(), cadSelectionOrder));
        if (workflowOperation is not null) RecordWorkflowOperation(workflowOperation, workflowArguments!);
        PublishChange(); return CadState();
    }
}
