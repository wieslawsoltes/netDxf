using System.Collections.Immutable;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

public enum BlockSourceDisposition
{
    Retain = 0, ConvertToInsert = 1, Delete = 2,
    Convert = ConvertToInsert, Remove = Delete
}

/// <summary>Author a named shared definition from explicitly selected layout roots.
/// Redefine preserves the definition identity, so all existing INSERTs observe the new contents.</summary>
public sealed record BlockDefinitionRequest(string Name, Vec3 BasePoint, ImmutableArray<ObjectId> Entities,
    BlockSourceDisposition Disposition = BlockSourceDisposition.ConvertToInsert,
    ObjectId? Redefine = null, string? Description = null, string InsertLayer = "0");

public sealed partial class DraftingPlanner
{
    public DraftingPlan PlanBlockDefinition(ShadowSnapshot snapshot, BlockDefinitionRequest request,
        string layout = "Model", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var owner = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.");
        return PlanBlockDefinitionInOwner(snapshot, request, owner, cancellationToken);
    }

    /// <summary>Reuse definition creation/redefinition inside an explicit named editing space.</summary>
    public DraftingPlan PlanBlockDefinitionInDefinition(ShadowSnapshot snapshot, BlockDefinitionRequest request,
        ObjectId owner, CancellationToken cancellationToken = default)
    {
        DraftingBlocks.Definition(snapshot, owner);
        if (request?.Redefine == owner) throw new ArgumentException("Edit the open definition directly rather than redefining its own source selection.");
        return PlanBlockDefinitionInOwner(snapshot, request!, owner, cancellationToken);
    }

    private DraftingPlan PlanBlockDefinitionInOwner(ShadowSnapshot snapshot, BlockDefinitionRequest request,
        ObjectId owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Disposition)) throw new ArgumentOutOfRangeException(nameof(request));
        ValidateBlockName(request.Name); CheckPoint(request.BasePoint);
        if (request.Description?.Length > 4096)
            throw new ArgumentException("Block descriptions are limited to 4096 characters.");
        var ids = request.Entities.IsDefault ? [] : request.Entities.Distinct().ToImmutableArray();
        if (ids.IsEmpty || ids.Length > limits.MaximumEntities) throw new ArgumentException("Select a bounded, nonempty set of source entities.");
        var ownerMembers = snapshot.Read(owner)["Entities"].Items.Select(e => e.Id)
            .Concat(snapshot.Read(owner)["AttributeDefinitions"].Items.Select(entry => entry["Value"].Id)).ToHashSet();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DraftingGuard.Entity(snapshot, id, request.Disposition != BlockSourceDisposition.Retain);
            if (!ownerMembers.Contains(id)) throw new ArgumentException("Block sources must be direct entities of the active editing space.");
        }
        var blocks = snapshot.Document["Blocks"].Items.ToArray();
        var existing = request.Redefine is { } target ? blocks.FirstOrDefault(b => b.Id == target) : default;
        if (request.Redefine is not null && (!existing.Exists || existing["Name"].Text.StartsWith('*') || existing["IsXRef"].Boolean()))
            throw new ArgumentException("Only an existing local named block can be redefined.");
        if (blocks.Any(b => b.Id != request.Redefine && b["Name"].Text.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A block with that name already exists.");
        if (existing.Exists && existing["AttributeDefinitions"].Items.Any())
            throw new NotSupportedException("This replacement operation does not implicitly rewrite attribute definitions or attribute values.");
        if (existing.Exists && !existing["Name"].Text.Equals(request.Name, StringComparison.Ordinal))
            throw new ArgumentException("Redefine retains the definition name; use RenameBlock explicitly.");
        ValidateBlockDependencies(snapshot, ids, request.Redefine, cancellationToken);
        foreach (var id in ids)
            if (snapshot.Read(id).Type == "Insert" && snapshot.Read(id)["Block"].Id is { } referenced)
                DraftingBlocks.ValidateReference(snapshot, owner, referenced, limits.MaximumClonedObjects, cancellationToken);

        var edit = new ShadowGraphEdit(snapshot);
        var block = request.Redefine ?? edit.Create("netDxf.Blocks.Block", ShadowObjectKind.Block,
            new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(request.Name),
                ["Origin"] = V(Vec3.Zero), ["Description"] = ShadowValue.From(request.Description ?? ""),
                ["Entities"] = new ShadowValue.Sequence([]) });
        if (request.Redefine is null)
        {
            var record = edit.Create("netDxf.Blocks.BlockRecord", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            {
                ["Name"] = ShadowValue.From(request.Name),
                ["Units"] = snapshot.Layouts.Any(item => item["AssociatedBlock"].Id == owner)
                    ? snapshot.Document["DrawingVariables"]["InsUnits"].Value : snapshot.Read(owner)["Record"]["Units"].Value
            });
            edit.Set(block, "Record", new ShadowValue.Reference(record));
            edit.ReplaceSequence(snapshot.Root, "Blocks", blocks.Select(b => b.Value).Append(new ShadowValue.Reference(block)));
        }
        else
        {
            if (request.Description is not null) edit.Set(block, "Description", ShadowValue.From(request.Description));
            // Keep the existing origin. Existing references must not move when the definition changes.
        }
        var budget = 0;
        var copies = Clone(snapshot, edit, ids, block, cancellationToken, ref budget);
        if (existing.Exists)
            edit.RemoveEntities(existing["Entities"].Items.Select(e => e.Id!.Value));
        var intermediate = edit.Build().Apply(snapshot);
        var origin = existing.Exists ? existing["Origin"].Vector() : Vec3.Zero;
        var translated = AffineEditOperations.Plan(intermediate, copies,
            Affine3.Move(origin - request.BasePoint), cancellationToken: cancellationToken).Apply(intermediate);
        var final = new ShadowGraphEdit(translated);
        ImmutableArray<ObjectId> created = [];
        if (request.Disposition == BlockSourceDisposition.ConvertToInsert)
        {
            var layer = Layer(snapshot, request.InsertLayer);
            if (layer["IsLocked"].Boolean() || layer["IsFrozen"].Boolean()) throw new ArgumentException("Choose an unlocked, thawed insertion layer.");
            var insert = final.AddEntity(owner, "netDxf.Entities.Insert", new Dictionary<string, ShadowValue>
            { ["Block"] = new ShadowValue.Reference(block), ["Position"] = V(request.BasePoint),
                ["Scale"] = V(new(1, 1, 1)), ["Rotation"] = N(0), ["Normal"] = V(Vec3.UnitZ),
                ["Layer"] = layer.Value, ["IsVisible"] = ShadowValue.From(true) });
            created = [insert];
        }
        if (request.Disposition != BlockSourceDisposition.Retain) final.RemoveEntities(ids);
        cancellationToken.ThrowIfCancellationRequested();
        return new(request.Redefine is null ? "BLOCK" : "REDEFINE BLOCK",
            ShadowGraphPatch.Between(snapshot, final.Build().Apply(translated)), created);
    }

    public DraftingPlan RenameBlock(ShadowSnapshot snapshot, ObjectId id, string name)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ValidateBlockName(name);
        var blocks = snapshot.Document["Blocks"].Items.ToArray(); var block = blocks.FirstOrDefault(b => b.Id == id);
        if (!block.Exists || block["Name"].Text.StartsWith('*') || block["IsXRef"].Boolean())
            throw new ArgumentException("Choose a local named block.");
        if (blocks.Any(b => b.Id != id && b["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Block names must be unique, ignoring case.");
        var edit = new ShadowGraphEdit(snapshot); edit.Set(id, "Name", ShadowValue.From(name));
        return new("RENAME BLOCK", edit.Build(), []);
    }

    private static void ValidateBlockName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(c => char.IsControl(c) || "<>/\\\":;?*|,=`".Contains(c)))
            throw new ArgumentException("Block names require 1–255 printable characters without reserved DXF name characters.");
    }

    private void ValidateBlockDependencies(ShadowSnapshot snapshot, ImmutableArray<ObjectId> roots,
        ObjectId? target, CancellationToken token)
    {
        // Owned public data can contain entity references (for example associative hatch boundaries).
        // Reject an incomplete selection instead of constructing illegal cross-block relationships.
        var selected = roots.ToHashSet(); var visited = new HashSet<ObjectId>(); var pending = new Stack<ObjectId>(roots);
        var visits = 0;
        while (pending.TryPop(out var id))
        {
            token.ThrowIfCancellationRequested(); if (!visited.Add(id)) continue;
            if (++visits > limits.MaximumClonedObjects) throw new InvalidOperationException("Block dependency budget exceeded.");
            var node = snapshot.Objects[id];
            foreach (var reference in node.Properties.Where(p => p.Key is not ("Owner" or "Handle"))
                .SelectMany(p => p.Value.References()))
            {
                var dependency = snapshot.Objects[reference];
                if ((dependency.Kind & ShadowObjectKind.Entity) != 0 && !selected.Contains(reference))
                    throw new NotSupportedException("The block selection has an external entity dependency; include all dependent boundary entities.");
                if (dependency.Kind == ShadowObjectKind.Object) pending.Push(reference);
            }
        }
        if (target is null) return;
        visited.Clear(); pending = new Stack<ObjectId>(roots);
        while (pending.TryPop(out var id))
        {
            token.ThrowIfCancellationRequested(); if (!visited.Add(id)) continue;
            if (++visits > limits.MaximumClonedObjects) throw new InvalidOperationException("Block reference budget exceeded.");
            var entity = snapshot.Read(id);
            if (entity.Type == "Insert" && entity["Block"].Id is { } block)
            {
                if (block == target) throw new ArgumentException("Redefinition would create a recursive block reference.");
                foreach (var child in snapshot.Read(block)["Entities"].Items) if (child.Id is { } childId) pending.Push(childId);
            }
        }
    }
}
