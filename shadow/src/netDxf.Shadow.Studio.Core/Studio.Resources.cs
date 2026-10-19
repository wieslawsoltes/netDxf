using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Studio;

public sealed record CadResourceRequest(long Revision, ResourceEdit Resource);
public sealed record CadLayerStateRequest(long Revision, LayerStateEdit State);

public sealed partial class Studio
{
    public object CadResources()
    {
        lock (gate)
        {
            var snapshot = Current;
            return new
            {
                revision = snapshot.Revision, layers = DraftingLayers.Read(snapshot),
                layerStatus = snapshot.Document["Layers"].Items.Select(l => new DraftingResourceStatus(l.Id!.Value.Value, l["Name"].Text,
                    l["Name"].Text.Equals(DraftingResources.Current(snapshot, "layer"), StringComparison.OrdinalIgnoreCase), DraftingResources.Reserved(l, "layer"), DraftingResources.Uses(snapshot, l.Id.Value))).ToArray(),
                linetypes = DraftingResources.Linetypes(snapshot), textStyles = DraftingResources.TextStyles(snapshot),
                layerStates = DraftingLayerStates.Read(snapshot),
                shapeStyles = snapshot.Document["ShapeStyles"].Items.Select(s => new { id = s.Id!.Value.Value, name = s["Name"].Text, file = s["File"].Text }).ToArray()
            };
        }
    }

    public object CadResource(CadResourceRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Resource);
            DraftingResources.Plan(Current, request.Resource).Execute(editor); RecordWorkflowOperation("resource", request.Resource); PublishChange(); return CadState();
        }
    }

    public object CadLayerState(CadLayerStateRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.State);
            DraftingLayerStates.Plan(Current, request.State, layout, token).Execute(editor);
            RecordWorkflowOperation("layer-state", request.State); PublishChange(); return CadState();
        }
    }
}
