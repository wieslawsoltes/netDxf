using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Health;
using netDxf.Shadow.Modules;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record CadPreviewProperty(ObjectId Entity, string Property, JsonElement Value);
public sealed record CadAttributeSyncOperation(ObjectId Block, bool ConvertTextModes = false);
public sealed record CadAttributeSyncSummary(ObjectId Block, string BlockName, int ReferenceCount, int Added, int Retained, int Removed,
    int RemovedXData, int TextConversions, bool DetailsTruncated, ImmutableArray<AttributeSynchronizationReference> References);
public sealed record CadEditOperation(string Kind, DraftingCommand? Command = null, ResourceEdit? Resource = null,
    LayerEdit? Layer = null, CadPreviewProperty? Property = null, string? Module = null, CadModuleCommandRequest? ModuleCommand = null,
    CadAttributeSyncOperation? AttributeSync = null, CadAttributeDefinitionOperation? AttributeDefinitions = null, CadInsertOperation? Insert = null,
    CadBlockLibraryOperation? BlockLibrary = null);
public sealed record CadEditPreviewRequest(long Revision, string Description, ImmutableArray<CadEditOperation> Operations);
public sealed record CadEditPreviewTokenRequest(long Revision, string PreviewId);

public sealed partial class Studio
{
    private sealed record PendingEditPreview(ShadowEditPreview Preview, string Workspace, long View, long FontRevision,
        RenderCompileOptions ViewOptions, ImmutableHashSet<string> Selection, ImmutableArray<ObjectId> SelectionOrder, ImmutableArray<ObjectId> ResultSelection,
        DrawingHealthReport BeforeHealth, DrawingHealthReport AfterHealth, long ModuleVersion, ImmutableArray<string> Modules,
        CadEditPreviewRequest Request, ImmutableArray<CadAttributeSyncSummary> AttributeSynchronizations, ImmutableArray<CadBlockLibrarySummary> BlockLibraries);
    private readonly Dictionary<string, PendingEditPreview> editPreviews = new(StringComparer.Ordinal);
    public object CadEditPreview(CadEditPreviewRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (request.Operations.IsDefaultOrEmpty || request.Operations.Length > 128) throw new ArgumentException("A preview requires 1–128 typed operations.");
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 512) throw new ArgumentException("Provide a description of at most 512 characters.");
            var before = Current; var branch = new EditorSession(new ShadowMemoryStore(before));
            var selectionOrder = CadSelected(Scene()); var selected = selectionOrder;
            var attributeSynchronizations = ImmutableArray.CreateBuilder<CadAttributeSyncSummary>();
            var libraries = ImmutableArray.CreateBuilder<CadBlockLibrarySummary>();
            var recordedOperations = ImmutableArray.CreateBuilder<CadEditOperation>();
            foreach (var operation in request.Operations)
            {
                token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(operation);
                DraftingPlan? plan = null;
                var recordedOperation = operation;
                switch (operation.Kind)
                {
                    case "command" when operation.Command is { } command:
                        if (SelectionCommands.Contains(command.Name?.Trim() ?? ""))
                            command = command with { Entities = AuthoringSelection(command.Entities, selected) };
                        else command = command with { Entities = [] };
                        plan = PlanCadCommand(branch.Snapshot, command, token);
                        break;
                    case "insert" when operation.Insert is { } insertion:
                        plan = PlanInsert(branch.Snapshot, insertion, token); break;
                    case "block-library" when operation.BlockLibrary is { } library:
                        if (libraries.Count >= 4) throw new ArgumentException("A preview can import at most four definitions.");
                        var imported = PlanBlockLibrary(branch.Snapshot, library, token);
                        plan = imported.Plan.Edit;
                        recordedOperation = operation with { BlockLibrary = imported.Recorded };
                        libraries.Add(new(imported.Plan.Block, plan.Patch.Apply(branch.Snapshot).Read(imported.Plan.Block)["Name"].Text,
                            imported.Plan.Resources.Length > 500, imported.Plan.Resources.Take(500).ToImmutableArray()));
                        break;
                    case "module" when operation.Module is { } module && operation.ModuleCommand is { } moduleCommand:
                        if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Finish block editing before previewing discipline objects.");
                        moduleCommand = moduleCommand with { Entities = AuthoringSelection(moduleCommand.Entities, selected) };
                        plan = cadModules.Plan(module, branch.Snapshot, moduleCommand, layout, token); break;
                    case "resource" when operation.Resource is { } resource: plan = DraftingResources.Plan(branch.Snapshot, resource); break;
                    case "layer" when operation.Layer is { } layerEdit: plan = DraftingLayers.Plan(branch.Snapshot, layerEdit); break;
                    case "attribute-sync" when operation.AttributeSync is { } synchronization:
                        var sync = AttributeSynchronization.Plan(branch.Snapshot, synchronization.Block,
                            new(ConvertTextModes: synchronization.ConvertTextModes), token);
                        attributeSynchronizations.Add(AttributeSyncSummary(sync));
                        plan = new("ATTSYNC " + sync.BlockName, sync.Patch, []); break;
                    case "attribute-definitions" when operation.AttributeDefinitions is { } definitions:
                        var definitionPlan = PlanAttributeDefinitions(branch.Snapshot, definitions, token);
                        plan = definitionPlan.Plan;
                        if (definitionPlan.Sync is { } definitionSync) attributeSynchronizations.Add(AttributeSyncSummary(definitionSync));
                        break;
                    case "property" when operation.Property is { } property:
                        if (!selected.Contains(property.Entity)) throw new ShadowConflictException("Preview property targets must belong to the current selection.");
                        var entity = DraftingGuard.Entity(branch.Snapshot, property.Entity);
                        branch.SetProperty(property.Entity, property.Property, ConvertInput(entity[property.Property].Value, property.Value));
                        break;
                    default: throw new ArgumentException("Choose a typed command, insert, block-library, module, resource, layer, property, attribute-sync or attribute-definitions preview operation with its matching payload.");
                }
                recordedOperations.Add(recordedOperation);
                if (plan is not null)
                {
                    plan.Execute(branch);
                    selected = plan.Created.IsEmpty ? selected.Where(id => branch.Snapshot.Objects.ContainsKey(id)).ToImmutableArray() : plan.Created;
                }
                if (branch.Snapshot.Objects.Count > 250_000) throw new ArgumentException("Preview object budget exceeded.");
            }
            var preview = editor.Preview(request.Description, ShadowGraphPatch.Between(before, branch.Snapshot));
            // A caller may dispose its JsonDocument while a proposal awaits approval.
            // Keep detached property values for the later workflow recording.
            var recordedRequest = request with { Operations = recordedOperations.Select(operation => operation.Property is { } property
                ? operation with { Property = property with { Value = property.Value.Clone() } } : operation).ToImmutableArray() };
            var pending = new PendingEditPreview(preview, WorkspaceId, viewVersion, CurrentFonts.Revision, ViewOptions(), editor.State.Selection,
                selectionOrder, selected, AnalyzeHealth(before, new(MaximumIssues: 500), token), AnalyzeHealth(preview.Proposed, new(MaximumIssues: 500), token), moduleConfigurationVersion,
                request.Operations.Where(o => o.Kind == "module").Select(o => o.Module!).Distinct(StringComparer.Ordinal).ToImmutableArray(), recordedRequest,
                attributeSynchronizations.ToImmutable(), libraries.ToImmutable());
            foreach (var key in editPreviews.Where(p => !EditPreviewCurrent(p.Value)).Select(p => p.Key).ToArray()) editPreviews.Remove(key);
            if (editPreviews.Count >= 8) editPreviews.Remove(editPreviews.Keys.First());
            var id = Guid.NewGuid().ToString("N"); editPreviews.Add(id, pending);
            return EditPreviewReceipt(id, pending);
        }
    }
    public object CadEditPreviews()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return editPreviews.Where(p => p.Value.Workspace == WorkspaceId && p.Value.Preview.Target.DocumentId == Current.DocumentId)
                .Select(p => EditPreviewReceipt(p.Key, p.Value)).ToArray();
        }
    }
    private static CadAttributeSyncSummary AttributeSyncSummary(AttributeSynchronizationPlan plan)
    {
        bool truncated = plan.References.Length > 100;
        string Bound(string value) { if (value.Length <= 1024) return value; truncated = true; return value[..1024] + "…"; }
        var references = plan.References.Take(100).Select(r =>
        {
            truncated |= r.Added.Length > 50 || r.Retained.Length > 50 || r.Removed.Length > 50 || r.RemovedXData.Length > 50 || r.TextConversions.Length > 50;
            return r with { Added = r.Added.Take(50).ToImmutableArray(), Retained = r.Retained.Take(50).ToImmutableArray(),
                Removed = r.Removed.Take(50).Select(a => a with { FallbackValue = Bound(a.FallbackValue), MultilineValue = a.MultilineValue is { } v ? Bound(v) : null }).ToImmutableArray(),
                RemovedXData = r.RemovedXData.Take(50).ToImmutableArray(), TextConversions = r.TextConversions.Take(50).ToImmutableArray() };
        }).ToImmutableArray();
        return new(plan.Block, plan.BlockName, plan.References.Length, plan.References.Sum(r => r.Added.Length),
            plan.References.Sum(r => r.Retained.Length), plan.References.Sum(r => r.Removed.Length),
            plan.References.Sum(r => r.RemovedXData.Length), plan.References.Sum(r => r.TextConversions.Length), truncated, references);
    }
    private object EditPreviewReceipt(string id, PendingEditPreview pending) => new
    {
        previewId = id, description = pending.Preview.Description, documentId = pending.Preview.Target.DocumentId,
        workspaceId = pending.Workspace, viewVersion = pending.View, revision = pending.Preview.Target.Revision,
        current = EditPreviewCurrent(pending), validation = pending.Preview.Validation, summary = pending.Preview.Summary,
        beforeHealth = pending.BeforeHealth, afterHealth = pending.AfterHealth, attributeSynchronizations = pending.AttributeSynchronizations,
        blockLibraries = pending.BlockLibraries
    };
    private bool EditPreviewCurrent(PendingEditPreview pending) => ReferenceEquals(pending.Preview.Target, Current)
        && pending.Workspace == WorkspaceId && pending.View == viewVersion && pending.FontRevision == CurrentFonts.Revision
        && pending.ViewOptions == ViewOptions() && pending.Selection.SetEquals(editor.State.Selection)
        && pending.SelectionOrder.SequenceEqual(CadSelected(Scene()))
        && pending.ModuleVersion == moduleConfigurationVersion && pending.Modules.All(cadModules.IsEnabled);
    private PendingEditPreview FindEditPreview(string id, bool requireCurrent = true)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (id is null || !editPreviews.TryGetValue(id, out var pending) || pending.Preview.Target.DocumentId != Current.DocumentId || pending.Workspace != WorkspaceId)
            throw new ShadowConflictException("The edit preview is unavailable.");
        if (requireCurrent && !EditPreviewCurrent(pending)) throw new ShadowConflictException("The drawing, selection, view or resources changed. Create a new preview.");
        return pending;
    }
    public object CadEditPreviewApply(CadEditPreviewTokenRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); var pending = FindEditPreview(request.PreviewId);
            editor.ApplyPreview(pending.Preview);
            cadSelectionOrder = pending.ResultSelection.Where(id => Current.Objects.ContainsKey(id)).ToImmutableArray();
            editor.Select(DraftingQueries.Expand(Scene(), cadSelectionOrder)); editPreviews.Remove(request.PreviewId);
            RecordWorkflowOperation("edit-preview", pending.Request); PublishChange(); return CadState();
        }
    }
    public object CadEditPreviewDiscard(CadEditPreviewTokenRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); _ = FindEditPreview(request.PreviewId, false); editPreviews.Remove(request.PreviewId);
            return new { discarded = true, previewId = request.PreviewId };
        }
    }
    public RenderImage CadEditPreviewRender(string previewId, bool after, int width = 640, int height = 400,
        string backend = "svg", CancellationToken token = default, string? block = null)
    {
        lock (gate)
        {
            if (width is < 16 or > 2048 || height is < 16 or > 2048 || (long)width * height > 4_000_000) throw new ArgumentException("Preview frames require 16–2048 pixels per axis and at most 4 megapixels.");
            var pending = FindEditPreview(previewId); var services = FrozenServices(CurrentFonts);
            if (block is not null)
            {
                if (!after || !pending.BlockLibraries.Any(item => item.Block.Value == block)) throw new ArgumentException("Choose an imported definition from this preview.");
                var definition = services.Compiler.Compile(pending.Preview.Proposed, pending.ViewOptions with { RootBlock = new ObjectId(block) }, token);
                renderer.Swap(backend); return renderer.Render(new(definition, Viewport.Fit(definition.Bounds, width, height)), token);
            }
            var before = services.Compiler.Compile(pending.Preview.Target, pending.ViewOptions, token);
            var proposed = services.Compiler.Compile(pending.Preview.Proposed, pending.ViewOptions, token);
            var camera = Viewport.Fit(before.Bounds.Union(proposed.Bounds), width, height);
            renderer.Swap(backend); return renderer.Render(new(after ? proposed : before, camera), token);
        }
    }
}
