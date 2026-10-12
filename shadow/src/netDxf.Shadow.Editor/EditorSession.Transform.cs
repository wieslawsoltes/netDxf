using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Editor;

public sealed partial class EditorSession
{
    public ShadowSnapshot Transform(IEnumerable<ObjectId> entities, Affine3 transform,
        AffineEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            EnsureCurrent();
            return Execute("Transform entities", AffineEditOperations.Plan(store.Current, entities, transform, options, cancellationToken));
        }
    }

    public ShadowSnapshot Transform(RenderScene scene, Affine3 transform, bool editBlockDefinition = false,
        AffineEditOptions? options = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            EnsureCurrent();
            return Execute("Transform selection", AffineEditOperations.PlanSelection(store.Current, scene, selection,
                transform, editBlockDefinition, options, cancellationToken));
        }
    }
}
