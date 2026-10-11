using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

/// <summary>Maps a displayed selection into source coordinates before planning a single atomic edit.</summary>
internal static class SelectionTranslation
{
    internal static ShadowEditBatch Plan(ShadowSnapshot snapshot, RenderScene scene,
        ImmutableHashSet<string> selection, Vec3 delta, bool editDefinition)
    {
        if (!delta.IsFinite) throw new ArgumentOutOfRangeException(nameof(delta));
        var movements = new Dictionary<ObjectId, Vec3>();
        foreach (var item in scene.Items)
        {
            if (!selection.Contains(item.Key)) continue;
            var id = !editDefinition && !item.InsertPath.IsEmpty ? item.InsertPath[0] : item.Entity;
            Vec3 movement;
            if (editDefinition)
            {
                if (!item.SourceToWorld.TryInverse(out var inverse))
                    throw new NotSupportedException("The selected definition has a singular display transform.");
                movement = inverse.Direction(delta);
            }
            else movement = item.Projection?.PaperToModel.Direction(delta) ?? delta;
            if (!movement.IsFinite) throw new ArithmeticException("Selection movement exceeds the source coordinate range.");
            if (movements.TryGetValue(id, out var previous) && Vec3.Distance(previous, movement) > 1e-10 * Math.Max(1, movement.Length))
                throw new ShadowConflictException("The same source is selected through differently transformed views. Edit one occurrence at a time.");
            movements[id] = movement;
        }
        var changes = new Dictionary<(ObjectId, string), ShadowPropertyEdit>();
        foreach (var (id, movement) in movements)
            foreach (var edit in EditOperations.Translate(snapshot, [id], movement).Edits)
            {
                var key = (edit.Object, edit.Property);
                if (changes.TryGetValue(key, out var previous) && !ShadowValue.Equivalent(previous.After, edit.After))
                    throw new ShadowConflictException("The selected objects prescribe incompatible edits to shared source data.");
                changes[key] = edit;
            }
        return new(snapshot.DocumentId, snapshot.Revision, changes.Values.ToImmutableArray());
    }
}
