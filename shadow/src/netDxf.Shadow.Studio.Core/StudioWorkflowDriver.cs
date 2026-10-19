using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Modules;
using netDxf.Shadow.Workflows;

namespace netDxf.Shadow.Studio;

/// <summary>Replays versioned workflow files against an owned, isolated Studio instance. Never calls a provider or arbitrary route.</summary>
public sealed class StudioWorkflowDriver(Func<Studio>? create = null) : IWorkflowDriver
{
    private Studio? studio;
    public ValueTask InitializeAsync(WorkflowBaseline baseline, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (studio is not null) throw new InvalidOperationException("Workflow drivers initialize once.");
        if (baseline.Kind != "studio-dxf-v1" || baseline.Data is null || Encoding.UTF8.GetByteCount(baseline.Data) > 12 * 1024 * 1024
            || !Studio.WorkflowHash(Encoding.UTF8.GetBytes(baseline.Data)).Equals(baseline.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unsupported workflow baseline or checksum mismatch.");
        var data = JsonSerializer.Deserialize<StudioWorkflowBaseline>(baseline.Data, StudioJson.CreateOptions()) ?? throw new ArgumentException("Missing Studio baseline.");
        var bytes = Convert.FromBase64String(data.Dxf); if (bytes.Length > 8 * 1024 * 1024) throw new ArgumentException("Workflow DXF baseline exceeds 8 MiB.");
        studio = create?.Invoke() ?? new Studio(false);
        studio.InitializeWorkflowBaseline(data, bytes); return ValueTask.CompletedTask;
    }
    public ValueTask<JsonElement> ExecuteAsync(string operation, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (studio is null) throw new InvalidOperationException("Initialize the workflow driver first.");
        return ValueTask.FromResult(studio.ExecuteWorkflowOperation(operation, arguments, cancellationToken));
    }
    public ValueTask DisposeAsync() { studio?.Dispose(); studio = null; return ValueTask.CompletedTask; }
}

public sealed partial class Studio
{
    internal void InitializeWorkflowBaseline(StudioWorkflowBaseline baseline, byte[] bytes)
    {
        lock (gate)
        {
            if (baseline.Fonts.IsDefault || baseline.Modules.IsDefault || baseline.Selection.IsDefault) throw new ArgumentException("Malformed workflow baseline resources.");
            foreach (var font in baseline.Fonts) if (!CurrentFonts.Fonts.ContainsKey(font))
                throw new NotSupportedException("Workflow requires host SHX resource '" + font + "'. Supply a driver factory with matching font resources.");
            Load(new MemoryStream(bytes));
            foreach (var state in cadModules.States) cadModules.SetEnabled(state.Descriptor.Id, baseline.Modules.Contains(state.Descriptor.Id));
            foreach (var id in baseline.Modules) if (!cadModules.IsEnabled(id)) throw new NotSupportedException("Workflow requires unavailable module '" + id + "'.");
            SetLayout(new(Current.Revision, baseline.Layout));
            SetLinetypeView(new(Current.Revision, baseline.LinetypeAlignment));
            SetAnnotationView(new(Current.Revision, baseline.AnnotationMode, baseline.AnnotationPaperUnits, baseline.AnnotationDrawingUnits));
            SetPerspectiveView(new(Current.Revision, baseline.PerspectiveMode, baseline.WorkPlaneZ));
            CadSelect(new(Current.Revision, "entities", Entities: baseline.Selection.Select(ResolveWorkflowReference).ToImmutableArray()));
        }
    }
    internal JsonElement ExecuteWorkflowOperation(string operation, JsonElement arguments, CancellationToken token)
    {
        lock (gate)
        {
            var decoded = DecodeWorkflowArguments(arguments);
            T Read<T>() => decoded.Deserialize<T>(StudioJson.CreateOptions()) ?? throw new ArgumentException("Missing workflow operation arguments.");
            switch (operation)
            {
                case "edit-preview":
                    var proposal = JsonSerializer.SerializeToElement(CadEditPreview(Read<CadEditPreviewRequest>() with { Revision = Current.Revision }, token), StudioJson.CreateOptions());
                    CadEditPreviewApply(new(Current.Revision, proposal.GetProperty("previewId").GetString()!)); break;
                case "command": CadCommand(new(Current.Revision, Read<DraftingCommand>()), token); break;
                case "layer": CadLayer(new(Current.Revision, Read<LayerEdit>())); break;
                case "layer-state": CadLayerState(new(Current.Revision, Read<LayerStateEdit>()), token); break;
                case "attributes": CadEditAttributes(Read<CadAttributeRequest>() with { Revision = Current.Revision }, token); break;
                case "attribute-definitions": CadEditAttributeDefinitions(Read<CadAttributeDefinitionRequest>() with { Revision = Current.Revision }, token); break;
                case "resource": CadResource(new(Current.Revision, Read<ResourceEdit>())); break;
                case "select": CadSelect(Read<CadSelectionRequest>() with { Revision = Current.Revision }, token); break;
                case "property": SetProperty(Read<PropertyRequest>() with { Revision = Current.Revision }); break;
                case "grip": CadGrip(Read<CadGripRequest>() with { Revision = Current.Revision }); break;
                case "assign-layer": CadAssignLayer(new(Current.Revision, decoded.GetProperty("layer").GetProperty("value").GetString()!)); break;
                case "block-create": CadCreateBlock(Read<CadBlockCreateRequest>() with { Revision = Current.Revision }, token); break;
                case "block-begin": CadBeginBlock(Read<CadBlockBeginRequest>() with { Revision = Current.Revision }); break;
                case "block-finish": CadFinishBlock(new(Current.Revision, session.Current.Revision, decoded.GetProperty("save").GetBoolean(), decoded.GetProperty("discard").GetBoolean()), token); break;
                case "history": History(decoded.GetProperty("operation").GetString()!, Current.Revision); break;
                case "layout": SetLayout(new(Current.Revision, decoded.GetProperty("name").GetString()!)); break;
                case "block-define": CadDefineBlock(new(Current.Revision, Read<BlockDefinitionRequest>()), token); break;
                case "block-rename": var rename = Read<CadBlockRenameRequest>(); CadRenameBlock(rename with { Revision = Current.Revision }); break;
                case "hatch-create": CadCreateHatch(new(Current.Revision, Read<HatchAuthoringRequest>()), token); break;
                case "hatch-pattern": CadChangeHatchPattern(Read<CadHatchPatternRequest>() with { Revision = Current.Revision }, token); break;
                case "module": var module = Read<CadModuleExecuteRequest>(); CadRunModule(module with { Revision = Current.Revision }, token); break;
                case "fork": Fork(new(Current.Revision, decoded.GetProperty("name").GetString()!)); break;
                case "switch": SwitchWorkspace(new(Current.Revision, WorkflowWorkspace(decoded.GetProperty("name").GetString()!))); break;
                case "discard": var branchId = WorkflowWorkspace(decoded.GetProperty("name").GetString()!); DiscardBranch(new(Current.Revision, branchId, FindBranch(branchId).Editor.Snapshot.Revision)); break;
                case "merge":
                    var branch = FindBranch(WorkflowWorkspace(decoded.GetProperty("name").GetString()!));
                    var plan = JsonSerializer.SerializeToElement(PreviewMerge(new(session.Current.DocumentId, branch.Id, session.Current.Revision, branch.Editor.Snapshot.Revision,
                        decoded.GetProperty("choice").GetString()!), token), StudioJson.CreateOptions());
                    if (plan.GetProperty("planId").ValueKind != JsonValueKind.String) throw new InvalidOperationException("Recorded merge now has unresolved conflicts.");
                    ApplyMerge(new(session.Current.DocumentId, plan.GetProperty("planId").GetString()!, session.Current.Revision, branch.Editor.Snapshot.Revision)); break;
                case "save": _ = Save(Current.Revision); break;
                case "roundtrip": CadWorkflowRoundTrip(new(Current.Revision)); break;
                default: throw new NotSupportedException("Unsupported workflow operation: " + operation);
            }
            token.ThrowIfCancellationRequested(); return WorkflowObservation();
        }
    }
    private string WorkflowWorkspace(string name) => name == "live" ? "live" : branches.Values.SingleOrDefault(b => b.Name == name)?.Id ?? throw new ArgumentException("Workflow branch is unavailable: " + name);
}
