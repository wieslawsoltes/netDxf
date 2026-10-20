using System.Collections.Immutable;
using System.Globalization;
using netDxf.Objects;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Studio;

public sealed record CadLayerStatePreviewRequest(long Revision, string Text, LayerStateImportOptions? Options = null);
public sealed record CadLayerStateImportRequest(long Revision, string Token);
public sealed record CadLayerStateExport(string FileName, string Text);
public sealed record CadLayerStatePreview(string? Token, long Revision, string Name, string CurrentLayer,
    int LayerCount, int RestoreMask, bool ReplacesExisting, ImmutableArray<LayerStateImportConflict> Conflicts, string Notice);

public sealed partial class Studio
{
    private sealed record PendingLayerStateImport(ShadowSnapshot Target, string Workspace, long View, string Layout,
        DateTime Expires, DraftingPlan Plan, CadLayerStatePreviewRequest Request);
    private readonly Dictionary<string, PendingLayerStateImport> layerStateImports = new(StringComparer.Ordinal);

    public DraftingLayerStateDetails CadLayerStateDetails(string id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return DraftingLayerStates.Details(Current, new ObjectId(id));
        }
    }
    public CadLayerStateExport CadLayerStateExport(string id, CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var value = DraftingLayerStates.Export(Current, new ObjectId(id));
            return new(value.Name + ".las", NativeLayerState(value).ToLasText(token));
        }
    }
    public CadLayerStatePreview CadLayerStatePreview(CadLayerStatePreviewRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(request); Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Text);
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Finish block editing before importing saved layer states.");
            var native = LayerState.FromLasText(request.Text, token);
            var value = TransferLayerState(native);
            var options = request.Options ?? new();
            var analysis = DraftingLayerStates.AnalyzeImport(Current, value, options);
            string? id = null;
            foreach (var stale in layerStateImports.Where(p => !LayerStateImportCurrent(p.Value)).Select(p => p.Key).ToArray()) layerStateImports.Remove(stale);
            if (analysis.Conflicts.IsEmpty)
            {
                var plan = DraftingLayerStates.PlanImport(Current, value, options, token);
                // Canonical text/options are immutable and are the exact validated input used by the cached plan.
                var recorded = request with { Text = native.ToLasText(token), Options = options };
                if (layerStateImports.Count >= 4) layerStateImports.Remove(layerStateImports.Keys.First());
                id = Guid.NewGuid().ToString("N");
                layerStateImports.Add(id, new(Current, WorkspaceId, viewVersion, layout, DateTime.UtcNow.AddMinutes(5), plan, recorded));
            }
            return new(id, Current.Revision, analysis.State.Name, analysis.State.CurrentLayer, analysis.State.Entries.Length,
                analysis.State.RestoreMask, analysis.ReplacesExisting, analysis.Conflicts,
                "Imports saved properties only; drawing layers remain unchanged until Restore. Missing resources require explicit mapping or prior creation/loading. Per-viewport overrides and plot-style payloads are unsupported.");
        }
    }
    private bool LayerStateImportCurrent(PendingLayerStateImport pending) => ReferenceEquals(pending.Target, Current)
        && pending.Workspace == WorkspaceId && pending.View == viewVersion && pending.Layout == layout && pending.Expires > DateTime.UtcNow;
    public object CadLayerStateImport(CadLayerStateImportRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(request); Check(request.Revision);
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Finish block editing before importing saved layer states.");
            if (request.Token is null || request.Token.Length != 32 || !layerStateImports.TryGetValue(request.Token, out var pending) || !LayerStateImportCurrent(pending))
                throw new ShadowConflictException("The LAS preview expired or the drawing, workspace, layout or resources changed. Preview again.");
            token.ThrowIfCancellationRequested();
            pending.Plan.Execute(editor);
            layerStateImports.Remove(request.Token);
            RecordWorkflowOperation("layer-state-import", pending.Request);
            PublishChange(); return CadState();
        }
    }
    private static LayerStateTransfer TransferLayerState(LayerState state) => new(state.Name, state.Description, state.CurrentLayer,
        state.PaperSpace, (int)state.RestoreMask, state.Properties.Values.Select(p => new SavedLayerProperties(p.Name, (int)p.Flags,
            $"#{p.Color.R:X2}{p.Color.G:X2}{p.Color.B:X2}", p.Color.Index, p.Color.UseTrueColor, p.LinetypeName,
            p.Lineweight.ToString(), p.Transparency.Value, p.Transparency.StoredAlphaValue)).ToImmutableArray());
    private static LayerState NativeLayerState(LayerStateTransfer value)
    {
        var state = new LayerState(value.Name) { Description = value.Description, CurrentLayer = value.CurrentLayer,
            PaperSpace = value.PaperSpace, RestoreMask = (LayerStateRestoreMask)value.RestoreMask };
        foreach (var entry in value.Entries)
        {
            var color = entry.TrueColor ? AciColor.FromTrueColor(int.Parse(entry.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                : AciColor.FromCadIndex((short)entry.ColorIndex);
            var alpha = entry.StoredAlphaValue is { } packed
                ? packed == 0 && entry.Transparency == 0 ? Transparency.FromLayerStateAlphaValue(packed) : Transparency.FromAlphaValue(packed)
                : Transparency.FromCadIndex((short)entry.Transparency);
            state.Properties.Add(entry.Name, new LayerStateProperties(entry.Name) { Flags = (LayerPropertiesFlags)entry.Flags,
                Color = color, Transparency = alpha, LinetypeName = entry.Linetype, Lineweight = Enum.Parse<Lineweight>(entry.Lineweight) });
        }
        return state;
    }
}
