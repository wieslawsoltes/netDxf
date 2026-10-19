using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.NetDxf;

namespace netDxf.Shadow.Studio;

public sealed record CadInsertOperation(DraftingCommand Command, ImmutableArray<InsertAttributeAssignment> Attributes = default);
public sealed record CadInsertRequest(long Revision, CadInsertOperation Insert);

public sealed partial class Studio
{
    public object CadInsert(CadInsertRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Insert);
            var plan = PlanInsert(Current, request.Insert, token);
            return ApplyAuthoring(plan, CadSelected(Scene()), "insert", request);
        }
    }

    private DraftingPlan PlanInsert(ShadowSnapshot snapshot, CadInsertOperation request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request.Command);
        if (!string.Equals(request.Command.Name?.Trim(), "INSERT", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An insertion request requires an INSERT command.");
        return PlanCadCommand(snapshot, request.Command with { Entities = [] }, token, request.Attributes);
    }

    private DraftingPlan PlanCadCommand(ShadowSnapshot snapshot, DraftingCommand command, CancellationToken token,
        ImmutableArray<InsertAttributeAssignment> assignments = default)
    {
        var plan = activeBranch?.BlockRoot is { } root ? drafting.PlanInBlock(snapshot, command, root, token) : drafting.Plan(snapshot, command, layout, token);
        if (!command.Name.Trim().Equals("INSERT", StringComparison.OrdinalIgnoreCase) && !command.Name.Trim().Equals("BLOCK", StringComparison.OrdinalIgnoreCase)) return plan;
        return InitializeCreatedInserts(snapshot, plan, assignments, token);
    }

    private static DraftingPlan InitializeCreatedInserts(ShadowSnapshot snapshot, DraftingPlan plan,
        ImmutableArray<InsertAttributeAssignment> assignments, CancellationToken token)
    {
        var proposed = plan.Patch.Apply(snapshot);
        foreach (var insert in plan.Created)
            proposed = InsertAttributeConstruction.Plan(proposed, insert, assignments, token).Apply(proposed);
        return plan with { Patch = ShadowGraphPatch.Between(snapshot, proposed) };
    }
}
