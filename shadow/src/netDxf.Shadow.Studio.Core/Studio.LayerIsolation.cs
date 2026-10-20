using System.Collections.Immutable;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Studio;

public sealed record CadLayerIsolationRequest(long Revision, LayerIsolationEdit Isolation);

public sealed partial class Studio
{
    public object CadLayerIsolation(CadLayerIsolationRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Isolation);
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Close the block editor before isolating global layers.");
            var isolation = request.Isolation;
            if (isolation.Action == "isolate" && isolation.Layers.IsDefaultOrEmpty)
            {
                var selected = DraftingGuard.Selection(Current, Scene(), editor.State.Selection, writable: false);
                isolation = isolation with { Layers = selected.Select(id => Current.Read(id)["Layer"].Id!.Value).Distinct().ToImmutableArray() };
            }
            DraftingLayerIsolation.Plan(Current, isolation, token).Execute(editor);
            RecordWorkflowOperation("layer-isolation", isolation); PublishChange(); return CadState();
        }
    }
}
