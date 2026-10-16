using System.Collections.Immutable;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed partial class EditorSession
{
    /// <summary>Moves selected sources by intersecting both paper coordinates with an explicit
    /// model-space work plane. All selected views must prescribe the same movement for a shared source.</summary>
    public ShadowSnapshot TranslatePerspective(RenderScene scene, Vec3 fromPaper, Vec3 toPaper,
        Vec3 planeOrigin, Vec3 planeNormal, bool editBlockDefinition = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        lock (gate)
        {
            EnsureCurrent(); var snapshot = store.Current;
            if (scene.DocumentId != snapshot.DocumentId || scene.Revision != snapshot.Revision)
                throw new ShadowConflictException("Selection belongs to an obsolete rendered snapshot.");
            var moves = new Dictionary<ObjectId, Vec3>();
            foreach (var item in scene.Items)
            {
                if (!selection.Contains(item.Key)) continue;
                var camera = item.Projection?.Perspective ?? throw new NotSupportedException("The selected item is not in a perspective view.");
                if (!camera.Ray(fromPaper).TryIntersectPlane(planeOrigin, planeNormal, out var a) ||
                    !camera.Ray(toPaper).TryIntersectPlane(planeOrigin, planeNormal, out var b))
                    throw new NotSupportedException("The picking ray does not intersect the forward work plane.");
                var delta = b - a;
                if (editBlockDefinition)
                {
                    if (!item.SourceToWorld.TryInverse(out var inverse)) throw new NotSupportedException("Singular source transform.");
                    delta = inverse.Direction(delta);
                }
                if (!delta.IsFinite || !double.IsFinite(delta.Length)) throw new ArithmeticException("Perspective translation overflow.");
                var id = !editBlockDefinition && !item.InsertPath.IsEmpty ? item.InsertPath[0] : item.Entity;
                if (moves.TryGetValue(id, out var previous) && Vec3.Distance(previous, delta) > 1e-10 * Math.Max(1, delta.Length))
                    throw new ShadowConflictException("Selected views prescribe incompatible source movements.");
                moves[id] = delta;
            }
            var edits = moves.SelectMany(pair => EditOperations.Translate(snapshot, [pair.Key], pair.Value).Edits).ToImmutableArray();
            return Execute("Translate through perspective work plane", new ShadowEditBatch(snapshot.DocumentId, snapshot.Revision, edits));
        }
    }
}
