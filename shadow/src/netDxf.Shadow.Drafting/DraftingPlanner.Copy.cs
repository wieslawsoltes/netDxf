using System.Collections.Immutable;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private DraftingPlan Copy(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        Selected(command, 1); Exact(command.Points, 2);
        var columns = command.Name == "COPY" ? 2 : command.Count;
        var rows = command.Name == "COPY" ? 1 : (int)command.Value;
        if (command.Name == "ARRAY" && (command.Value != rows || rows < 1 || columns < 1 || rows * (long)columns < 2))
            throw new ArgumentException("Array requires positive integral row/column counts and at least two cells.");
        if (rows * (long)columns * command.Entities.Length > limits.MaximumEntities)
            throw new ArgumentException("Array exceeds the configured entity budget.");
        var delta = command.Points[1] - command.Points[0]; Length(delta);
        if (command.Name == "ARRAY" && (columns > 1 && Math.Abs(delta.X) < limits.MinimumLength || rows > 1 && Math.Abs(delta.Y) < limits.MinimumLength))
            throw new ArgumentException("Array cells require nonzero spacing along each populated axis.");
        var edit = new ShadowGraphEdit(snapshot); var created = ImmutableArray.CreateBuilder<ObjectId>();
        var transforms = new List<AffineEntityEdit>(); var budget = 0;
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                if (row == 0 && column == 0) continue;
                token.ThrowIfCancellationRequested();
                var displacement = command.Name == "COPY" ? delta : new Vec3(delta.X * column, delta.Y * row);
                CheckPoint(displacement);
                var copies = Clone(snapshot, edit, command.Entities, owner, token, ref budget);
                created.AddRange(copies); transforms.AddRange(copies.Select(id => new AffineEntityEdit(id, Affine3.Move(displacement))));
            }
        var intermediate = edit.Build().Apply(snapshot);
        var transformed = AffineEditOperations.Plan(intermediate, transforms, cancellationToken: token).Apply(intermediate);
        return new(command.Name, ShadowGraphPatch.Between(snapshot, transformed), created.ToImmutable());
    }

    /// <summary>Clones entity-owned public data, but preserves shared table and block definitions.
    /// IDs and owners are regenerated; arbitrary attributes/XDATA are not flattened to render geometry.</summary>
    private ImmutableArray<ObjectId> Clone(ShadowSnapshot snapshot, ShadowGraphEdit edit,
        ImmutableArray<ObjectId> roots, ObjectId owner, CancellationToken token, ref int budget)
    {
        var map = roots.ToDictionary(id => id, _ => new ObjectId("N:" + Guid.NewGuid().ToString("N")));
        var allocated = new HashSet<ObjectId>(); var rootSet = roots.ToHashSet(); var used = budget;
        foreach (var id in roots) CloneNode(id, 0);
        budget = used;
        return roots.Select(id => map[id]).ToImmutableArray();

        ObjectId CloneNode(ObjectId old, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64 || used >= limits.MaximumClonedObjects) throw new InvalidOperationException("Clone graph budget exceeded.");
            if (!map.TryGetValue(old, out var id)) map.Add(old, id = new("N:" + Guid.NewGuid().ToString("N")));
            if (!allocated.Add(old)) return id;
            used++;
            var source = snapshot.Objects[old];
            // These are source identity/ownership bookkeeping, never copied as authored values.
            var properties = source.Properties.Where(p => p.Key is not ("Handle" or "Owner"))
                .ToDictionary(p => p.Key, p => source.TypeName == "netDxf.Entities.Attribute" && p.Key == "Definition"
                    ? p.Value // The copied INSERT shares its block, including its attribute definitions.
                    : Value(p.Value, depth + 1), StringComparer.Ordinal);
            if (rootSet.Contains(old)) edit.AddEntity(owner, source.TypeName, properties, id);
            else edit.Create(source.TypeName, source.Kind, properties, id);
            return id;
        }
        ShadowValue Value(ShadowValue value, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Clone value nesting budget exceeded.");
            return value switch
            {
                ShadowValue.Reference reference when map.TryGetValue(reference.Id, out var mapped) => new ShadowValue.Reference(mapped),
                ShadowValue.Reference reference when snapshot.Objects[reference.Id].Kind == ShadowObjectKind.Object
                    => new ShadowValue.Reference(CloneNode(reference.Id, depth + 1)),
                ShadowValue.Reference => value, // Blocks, layers, styles and other entities remain shared references.
                ShadowValue.Sequence sequence => new ShadowValue.Sequence(sequence.Items.Select(v => Value(v, depth + 1)).ToImmutableArray()),
                ShadowValue.Structure structure => structure with { Fields = structure.Fields.ToImmutableDictionary(p => p.Key, p => Value(p.Value, depth + 1), StringComparer.Ordinal) },
                ShadowValue.Fault => throw new NotSupportedException("Unreadable source values cannot be copied losslessly."),
                _ => value
            };
        }
    }
}
