using System.Collections.Immutable;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed partial class DraftingPlanner
{
    private void CheckInsertScale(Vec3 scale)
    {
        if (!scale.IsFinite || new[] { scale.X, scale.Y, scale.Z }.Any(v => Math.Abs(v) < limits.MinimumLength || Math.Abs(v) > limits.MaximumCoordinate))
            throw new ArgumentException("Each INSERT scale must be finite and nonzero, with absolute value between the drafting minimum length and maximum coordinate. Negative axes mirror the reference.");
    }

    private DraftingPlan Explode(ShadowSnapshot snapshot, DraftingCommand command, ObjectId owner, CancellationToken token)
    {
        Selected(command, 1);
        var inserts = command.Entities.Where(id => snapshot.Read(id).Type == "Insert").ToImmutableArray();
        var others = command.Entities.Except(inserts).ToImmutableArray();
        var working = snapshot;
        var created = ImmutableArray.CreateBuilder<ObjectId>();
        if (!others.IsEmpty)
        {
            var polylines = JoinExplode(working, command with { Entities = others }, owner);
            working = polylines.Patch.Apply(working); created.AddRange(polylines.Created);
        }
        var edit = new ShadowGraphEdit(working);
        var transforms = new List<AffineEntityEdit>();
        var budget = 0;
        foreach (var id in inserts)
        {
            token.ThrowIfCancellationRequested();
            var insert = DraftingGuard.Entity(working, id);
            var block = DraftingBlocks.Definition(working, insert["Block"].Id ?? throw new ArgumentException("INSERT has no block definition."));
            if (insert["Attributes"].Items.Any() || block["AttributeDefinitions"].Items.Any())
                throw new NotSupportedException("EXPLODE of attributed blocks requires an explicit attribute-to-text policy. The complete selection is unchanged; edit attribute values through Properties instead.");
            if (insert["XData"]["Values"].Items.Any()) throw new NotSupportedException("INSERT has XDATA; choose a metadata distribution policy before exploding it.");
            if (insert["RowCount"].Number(1) > 1 || insert["ColumnCount"].Number(1) > 1)
                throw new NotSupportedException("EXPLODE of MINSERT arrays is not supported. The complete selection is unchanged.");
            if (block["Record"]["AllowExploding"].Exists && !block["Record"]["AllowExploding"].Boolean())
                throw new InvalidOperationException("This block definition disallows exploding.");
            var roots = block["Entities"].Items.Select(e => e.Id ?? throw new NotSupportedException("Block entities need source identities.")).ToImmutableArray();
            if (roots.IsEmpty) throw new ArgumentException("The selected block contains no drawable entities.");
            if (created.Count + roots.Length > limits.MaximumEntities) throw new InvalidOperationException("Explode entity budget exceeded.");
            var copies = Clone(working, edit, roots, owner, token, ref budget);
            var transform = Affine3.Move(insert["Position"].Vector()) * Affine3.ObjectCoordinates(insert["Normal"].Vector(Vec3.UnitZ)) *
                Affine3.RotateZ(insert["Rotation"].Number() * Math.PI / 180) *
                Affine3.Scale(insert["Scale"].Vector(new(1, 1, 1)) * BlockUnitFactor(working, insert["Owner"].Id ?? owner, block)) *
                Affine3.Move(-block["Origin"].Vector());
            for (var i = 0; i < copies.Length; i++)
            {
                var original = working.Read(roots[i]); var copied = copies[i];
                // Resolve inherited presentation now that the child is a top-level entity.
                if (original["Layer"]["Name"].Text == "0") edit.Set(copied, "Layer", insert["Layer"].Value);
                if (original["Color"]["IsByBlock"].Boolean() || original["Color"]["Index"].Number(-1) == 0)
                    edit.Set(copied, "Color", insert["Color"].Value);
                if (original["Linetype"]["Name"].Text.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)) edit.Set(copied, "Linetype", insert["Linetype"].Value);
                if (original["Lineweight"].Text == "ByBlock") edit.Set(copied, "Lineweight", insert["Lineweight"].Value);
                if (original["Transparency"]["IsByBlock"].Boolean()) edit.Set(copied, "Transparency", insert["Transparency"].Value);
                if (original.Type == "Insert")
                {
                    // The nested reference changes owner here. Its implicit unit
                    // conversion must change with it; the enclosing affine transform
                    // already includes the old owner's conversion to the destination.
                    var nested = original["Block"];
                    var unitRatio = BlockUnitFactor(working, block.Id!.Value, nested) / BlockUnitFactor(working, owner, nested);
                    edit.Set(copied, "Scale", V(original["Scale"].Vector(new(1, 1, 1)) * unitRatio));
                }
                transforms.Add(new(copied, transform));
            }
            created.AddRange(copies);
        }
        working = edit.Build().Apply(working);
        if (transforms.Count > 0) working = AffineEditOperations.Plan(working, transforms,
            new(MaximumEntities: limits.MaximumEntities, MaximumCoordinates: limits.MaximumClonedObjects), token).Apply(working);
        var finish = new ShadowGraphEdit(working); finish.RemoveEntities(inserts);
        working = finish.Build().Apply(working);
        token.ThrowIfCancellationRequested();
        return new("EXPLODE", ShadowGraphPatch.Between(snapshot, working), created.ToImmutable());
    }

    private static double BlockUnitFactor(ShadowSnapshot snapshot, ObjectId owner, ShadowReader block)
    {
        var owning = snapshot.Read(owner);
        var source = block["Record"]["Units"].Text;
        var destination = owning["Record"]["Layout"].Id is not null ? snapshot.Document["DrawingVariables"]["InsUnits"].Text : owning["Record"]["Units"].Text;
        return DrawingUnitConversion.Factor(source, destination);
    }
}
