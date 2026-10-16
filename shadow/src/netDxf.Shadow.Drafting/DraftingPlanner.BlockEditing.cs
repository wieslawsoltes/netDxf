using System.Collections.Immutable;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

/// <summary>Detached block authoring input. Definition geometry is localized to BasePoint;
/// Convert replaces the source selection with a reference at that point in one transaction.</summary>
public sealed record BlockCreation(string Name, Vec3 BasePoint, ImmutableArray<ObjectId> Entities,
    BlockSourceDisposition Disposition = BlockSourceDisposition.Convert, string Layer = "0", string Description = "");

/// <summary>Reusable definition validation, including finite, bounded reference-graph traversal.</summary>
public static class DraftingBlocks
{
    public static ShadowReader Definition(ShadowSnapshot snapshot, ObjectId id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var block = snapshot.Read(id);
        if (block.Object is not { Kind: ShadowObjectKind.Block } || string.IsNullOrWhiteSpace(block["Name"].Text) ||
            block["Name"].Text.StartsWith('*') || block["IsXRef"].Boolean() || block["IsForInternalUseOnly"].Boolean())
            throw new ArgumentException("Choose an editable named block definition, not an external reference or layout block.");
        if (!snapshot.Document["Blocks"].Items.Any(b => b.Id == id))
            throw new ArgumentException("The block is not a member of this document.");
        return block;
    }

    public static void ValidateName(ShadowSnapshot snapshot, string name, ObjectId? except = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(c => char.IsControl(c) || "<>/\\\":;?*|,=`".Contains(c)))
            throw new ArgumentException("Block names require 1–255 printable characters without reserved CAD separators.");
        if (snapshot.Document["Blocks"].Items.Any(b => b.Id != except && b["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A block with that name already exists (case-insensitive).");
    }

    /// <summary>Reject inserting a definition into itself or a descendant. Checks the existing
    /// graph as well; malformed imported recursion is never silently compounded by an edit.</summary>
    public static void ValidateReference(ShadowSnapshot snapshot, ObjectId owner, ObjectId definition,
        int maximumVisits = 100_000, CancellationToken cancellationToken = default)
    {
        if (maximumVisits < 1) throw new ArgumentOutOfRangeException(nameof(maximumVisits));
        Definition(snapshot, definition);
        var complete = new HashSet<ObjectId>(); var active = new HashSet<ObjectId>();
        var pending = new Stack<(ObjectId Id, bool Exit, int Depth)>(); pending.Push((definition, false, 0));
        var visits = 0;
        while (pending.TryPop(out var next))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (next.Exit) { active.Remove(next.Id); complete.Add(next.Id); continue; }
            if (next.Id == owner) throw new InvalidOperationException("An INSERT cannot create a recursive block definition.");
            if (complete.Contains(next.Id)) continue;
            if (++visits > maximumVisits || next.Depth > 64) throw new InvalidOperationException("Block reference validation budget exceeded.");
            if (!active.Add(next.Id)) throw new InvalidOperationException("The referenced block graph already contains a cycle.");
            var block = snapshot.Read(next.Id);
            if (block.Object is not { Kind: ShadowObjectKind.Block }) throw new ArgumentException("An INSERT references a missing block definition.");
            pending.Push((next.Id, true, next.Depth));
            foreach (var entity in block["Entities"].Items)
            {
                if (++visits > maximumVisits) throw new InvalidOperationException("Block reference validation budget exceeded.");
                if (entity.Type == "Insert" && entity["Block"].Id is { } child) pending.Push((child, false, next.Depth + 1));
            }
        }
    }

    public static DraftingPlan Rename(ShadowSnapshot snapshot, ObjectId block, string name)
    {
        Definition(snapshot, block); ValidateName(snapshot, name, block);
        var edit = new ShadowGraphEdit(snapshot); edit.Set(block, "Name", ShadowValue.From(name));
        return new("RENAME BLOCK", edit.Build(), []);
    }
}

public sealed partial class DraftingPlanner
{
    public DraftingPlan CreateBlock(ShadowSnapshot snapshot, BlockCreation request, string layout = "Model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CreateBlockInOwner(snapshot, request,
            snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown drawing layout."), cancellationToken);
    }

    public DraftingPlan CreateBlockInDefinition(ShadowSnapshot snapshot, BlockCreation request, ObjectId owner,
        CancellationToken cancellationToken = default)
    {
        DraftingBlocks.Definition(snapshot, owner);
        return CreateBlockInOwner(snapshot, request, owner, cancellationToken);
    }

    private DraftingPlan CreateBlockInOwner(ShadowSnapshot snapshot, BlockCreation request, ObjectId owner, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        if (request.Description is null || request.Description.Length > 1024)
            throw new ArgumentException("Block descriptions are limited to 1024 characters.");
        if (request.Entities.IsDefaultOrEmpty || request.Entities.Length > limits.MaximumEntities)
            throw new ArgumentException("Select a bounded, nonempty set of source entities.");
        foreach (var id in request.Entities)
        {
            token.ThrowIfCancellationRequested();
            DraftingGuard.Entity(snapshot, id);
        }
        return PlanBlockDefinitionInOwner(snapshot,
            new(request.Name, request.BasePoint, request.Entities, request.Disposition,
                Description: request.Description, InsertLayer: request.Layer), owner, token);
    }
}
