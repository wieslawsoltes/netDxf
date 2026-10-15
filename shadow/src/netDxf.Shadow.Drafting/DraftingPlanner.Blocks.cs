using System.Collections.Immutable;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

public enum BlockSourceDisposition { Convert, Retain, Remove }

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
        DraftingBlocks.ValidateName(snapshot, request.Name); CheckPoint(request.BasePoint);
        if (!Enum.IsDefined(request.Disposition)) throw new ArgumentOutOfRangeException(nameof(request.Disposition));
        if (request.Description is null || request.Description.Length > 1024) throw new ArgumentException("Block descriptions are limited to 1024 characters.");
        if (request.Entities.IsDefaultOrEmpty || request.Entities.Length > limits.MaximumEntities)
            throw new ArgumentException("Select a bounded, nonempty set of source entities.");
        var ids = request.Entities.Distinct().ToImmutableArray(); var selected = ids.ToHashSet();
        foreach (var id in ids)
        {
            token.ThrowIfCancellationRequested();
            var entity = DraftingGuard.Entity(snapshot, id);
            if (entity["Owner"].Id != owner) throw new ArgumentException("All block source entities must belong to the current editing space.");
            // Native associations cannot cross the new block's ownership boundary. Owned
            // hatch boundary references are remapped by Clone only when their roots are selected.
            if (entity.Type == "Hatch" && entity["BoundaryPaths"].Items.SelectMany(p => p["Entities"].Items)
                .Any(e => e.Id is { } source && !selected.Contains(source)))
                throw new NotSupportedException("Select every associated hatch boundary before creating a block.");
        }
        var layer = Layer(snapshot, request.Layer);
        if (layer["IsLocked"].Boolean() || layer["IsFrozen"].Boolean()) throw new InvalidOperationException("Choose an unlocked, thawed insertion layer.");
        var edit = new ShadowGraphEdit(snapshot);
        var definition = edit.Create("netDxf.Blocks.Block", ShadowObjectKind.Block, new Dictionary<string, ShadowValue>
        {
            ["Name"] = ShadowValue.From(request.Name), ["Description"] = ShadowValue.From(request.Description),
            ["Origin"] = V(Vec3.Zero), ["Entities"] = new ShadowValue.Sequence([])
        });
        edit.ReplaceSequence(snapshot.Root, "Blocks", snapshot.Document["Blocks"].Items.Select(b => b.Value).Append(new ShadowValue.Reference(definition)));
        var budget = 0; var copies = Clone(snapshot, edit, ids, definition, token, ref budget);
        var copied = edit.Build().Apply(snapshot);
        var localized = AffineEditOperations.Plan(copied, copies, Affine3.Move(-request.BasePoint),
            new(limits.MaximumEntities, limits.MaximumClonedObjects), token).Apply(copied);
        edit = new ShadowGraphEdit(localized);
        ImmutableArray<ObjectId> created = [];
        if (request.Disposition == BlockSourceDisposition.Convert)
        {
            var reference = edit.AddEntity(owner, "netDxf.Entities.Insert", new Dictionary<string, ShadowValue>
            {
                ["Block"] = new ShadowValue.Reference(definition), ["Position"] = V(request.BasePoint), ["Scale"] = V(new(1, 1, 1)),
                ["Rotation"] = N(0), ["Normal"] = V(Vec3.UnitZ), ["Layer"] = layer.Value, ["IsVisible"] = ShadowValue.From(true),
                ["Lineweight"] = new ShadowValue.Atom("netDxf.Lineweight", "ByLayer"), ["LinetypeScale"] = N(1)
            });
            created = [reference];
        }
        if (request.Disposition != BlockSourceDisposition.Retain) edit.RemoveEntities(ids);
        token.ThrowIfCancellationRequested();
        return new("BLOCK " + request.Name, ShadowGraphPatch.Between(snapshot, edit.Build().Apply(localized)), created);
    }
}
