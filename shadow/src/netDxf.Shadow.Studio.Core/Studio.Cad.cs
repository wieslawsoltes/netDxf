using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record CadCommandRequest(long Revision, DraftingCommand Command);
public sealed record CadSelectionRequest(long Revision, string Mode, Vec3 First = default, Vec3 Second = default,
    double Tolerance = 1, bool Extend = false, bool Remove = false, string? Entity = null, ImmutableArray<ObjectId> Entities = default);
public sealed record CadGripRequest(long Revision, string Entity, string Grip, Vec3 Position);
public sealed record CadLayerRequest(long Revision, LayerEdit Layer);
public sealed record CadAssignLayerRequest(long Revision, string Layer);
public sealed record CadSnapRequest(Vec3 Point, double Tolerance, ObjectSnapMode Modes = ObjectSnapMode.Default, Vec3? Anchor = null, bool ExcludeSelection = false);

public sealed partial class Studio
{
    private readonly DraftingPlanner drafting = new();
    private ImmutableArray<ObjectId> cadSelectionOrder = [];
    private static readonly HashSet<string> SelectionCommands = new(StringComparer.OrdinalIgnoreCase)
        { "MOVE", "COPY", "ROTATE", "SCALE", "MIRROR", "ARRAY", "OFFSET", "TRIM", "EXTEND", "FILLET", "CHAMFER", "JOIN", "EXPLODE", "ERASE", "BLOCK" };
    public long CadRevision { get { lock (gate) return Current.Revision; } }
    public bool CadHasBranches { get { lock (gate) return branches.Count != 0; } }
    public (Guid Document, long Revision) CadStamp { get { lock (gate) return (session.Current.DocumentId, session.Current.Revision); } }

    private ImmutableArray<ObjectId> CadSelected(RenderScene scene)
    {
        var keys = editor.State.Selection;
        if (keys.IsEmpty) return cadSelectionOrder = [];
        var present = scene.Items.Where(i => keys.Contains(i.Key)).Select(DraftingQueries.Root).Distinct().ToHashSet();
        cadSelectionOrder = cadSelectionOrder.Where(present.Contains).Concat(present.Except(cadSelectionOrder).OrderBy(i => i.Value, StringComparer.Ordinal)).ToImmutableArray();
        return cadSelectionOrder;
    }

    public object CadState()
    {
        lock (gate)
        {
            var scene = Scene(); var selected = CadSelected(scene); var snapshot = Current;
            var metadata = CadMetadata(snapshot, scene);
            ImmutableArray<DraftingGrip> grips = []; string? gripDiagnostic = null;
            try { if (selected.Length <= 128) grips = DraftingGrips.Read(snapshot, selected); else gripDiagnostic = "Grips are hidden for selections over 128 objects."; }
            catch (InvalidOperationException error) { gripDiagnostic = error.Message; }
            return new
            {
                blockEditor = activeBranch?.BlockRoot is { } blockRoot ? new { id = blockRoot.Value,
                    name = snapshot.Read(blockRoot)["Name"].Text,
                    changed = !ShadowGraphPatch.Between(activeBranch.Ancestor, snapshot).Changes.IsEmpty } : null,
                state = State(), layers = metadata.Layers, selectedEntities = selected.Select(i => i.Value).ToArray(),
                moduleConfigurationVersion, modules = cadModules.States.Select(m => new { id = m.Descriptor.Id, name = m.Descriptor.Name, enabled = m.Enabled }).ToArray(),
                grips = grips.Select(g => new { g.Id, entity = g.Entity.Value, g.Property, g.Position, g.Kind, g.Index }), gripDiagnostic,
                items = metadata.Items, itemCount = metadata.ItemCount,
                blocks = metadata.Blocks, linetypes = metadata.Linetypes,
                currentLayer = DraftingResources.Current(snapshot, "layer"), currentLinetype = DraftingResources.Current(snapshot, "linetype"), currentTextStyle = DraftingResources.Current(snapshot, "textstyle"),
                properties = selected.Length == 1 ? Inspect(selected[0]) : null
            };
        }
    }

    public object CadCommand(CadCommandRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Command);
            var command = request.Command; var scene = Scene(); var selected = CadSelected(scene);
            if (SelectionCommands.Contains(command.Name?.Trim() ?? ""))
            {
                if (!command.Entities.IsDefaultOrEmpty && command.Entities.Any(id => !selected.Contains(id)))
                    throw new ShadowConflictException("Command entities must belong to the current source selection.");
                command = command with { Entities = command.Entities.IsDefaultOrEmpty ? selected : command.Entities };
            }
            else command = command with { Entities = [] };
            var plan = activeBranch?.BlockRoot is { } root ? drafting.PlanInBlock(Current, command, root, token) :
                drafting.Plan(Current, command, layout, token);
            plan.Execute(editor);
            cadSelectionOrder = plan.Created.IsEmpty ? selected.Where(id => Current.Objects.ContainsKey(id)).ToImmutableArray() : plan.Created;
            editor.Select(DraftingQueries.Expand(Scene(), cadSelectionOrder));
            RecordWorkflowOperation("command", command); PublishChange(); return CadState();
        }
    }

    public object CadSelect(CadSelectionRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentException.ThrowIfNullOrWhiteSpace(request.Mode); var scene = Scene(); var previous = CadSelected(scene);
            ImmutableArray<string> keys = request.Mode.ToLowerInvariant() switch
            {
                "pick" => DraftingQueries.Pick(scene, request.First, request.Tolerance),
                "window" or "crossing" => DraftingQueries.Select(scene,
                    new(Math.Min(request.First.X, request.Second.X), Math.Min(request.First.Y, request.Second.Y), Math.Max(request.First.X, request.Second.X), Math.Max(request.First.Y, request.Second.Y)),
                    request.Mode.Equals("window", StringComparison.OrdinalIgnoreCase) ? DraftingSelectionMode.Window : DraftingSelectionMode.Crossing, token: token),
                "all" => DraftingQueries.Expand(scene, (activeBranch?.BlockRoot is { } root ? Current.Read(root)["Entities"].Items : Current.DrawingEntities(layout)).Select(e => e.Id!.Value).Take(10_001)),
                "entity" when request.Entity is not null => DraftingQueries.Expand(scene, ValidateEntitySelection([new(request.Entity)])),
                "entities" => DraftingQueries.Expand(scene, ValidateEntitySelection(request.Entities)),
                "clear" => [],
                _ => throw new ArgumentException("Unknown selection mode.")
            };
            var keySet = keys.ToHashSet(StringComparer.Ordinal); var incoming = scene.Items.Where(i => keySet.Contains(i.Key)).Select(DraftingQueries.Root).Distinct().ToImmutableArray();
            if (request.Mode.Equals("entities", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit selection order defines target/boundary roles for TRIM, EXTEND and related commands.
                var visibleRoots = incoming.ToHashSet(); incoming = request.Entities.Where(visibleRoots.Contains).Distinct().ToImmutableArray();
            }
            var next = request.Remove ? previous.Except(incoming).ToImmutableArray() : request.Extend ? previous.Concat(incoming.Except(previous)).ToImmutableArray() : incoming;
            if (next.Length > 10_000) throw new ArgumentException("Selection is limited to 10,000 source entities.");
            cadSelectionOrder = next; editor.Select(DraftingQueries.Expand(scene, next)); RecordWorkflowOperation("select", request); PublishChange(); return CadState();
        }
    }

    private ImmutableArray<ObjectId> ValidateEntitySelection(ImmutableArray<ObjectId> entities)
    {
        if (entities.IsDefault || entities.Length > 10_000) throw new ArgumentException("Supply at most 10,000 source entity IDs.");
        foreach (var id in entities) DraftingGuard.Entity(Current, id, false);
        return entities.Distinct().ToImmutableArray();
    }

    public object? CadSnap(CadSnapRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            var scene = Scene(); return DraftingQueries.Snap(Current, scene, request.Point, request.Tolerance, request.Modes,
                request.Anchor, request.ExcludeSelection ? CadSelected(scene).ToHashSet() : null, token);
        }
    }
    public object CadGrip(CadGripRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); var id = new ObjectId(request.Entity);
            if (!CadSelected(Scene()).Contains(id)) throw new ShadowConflictException("The gripped entity is not selected.");
            DraftingGrips.Move(Current, id, request.Grip, request.Position).Execute(editor); RecordWorkflowOperation("grip", request); PublishChange(); return CadState();
        }
    }
    public object CadLayer(CadLayerRequest request)
    {
        lock (gate) { Check(request.Revision); DraftingLayers.Plan(Current, request.Layer).Execute(editor); RecordWorkflowOperation("layer", request.Layer); PublishChange(); return CadState(); }
    }
    public object CadAssignLayer(CadAssignLayerRequest request)
    {
        lock (gate) { Check(request.Revision); DraftingLayers.Assign(Current, CadSelected(Scene()), new(request.Layer)).Execute(editor); RecordWorkflowOperation("assign-layer", new { layer = new ObjectId(request.Layer) }); PublishChange(); return CadState(); }
    }
    public object CadProperty(PropertyRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); var selected = CadSelected(Scene());
            if (!selected.Contains(new ObjectId(request.Id))) throw new ShadowConflictException("The inspected entity is not selected.");
            DraftingGuard.Entity(Current, new(request.Id)); SetProperty(request); return CadState();
        }
    }
    public RenderImage CadRender(int width, int height, double cx, double cy, double scale, string backend, bool white,
        long revision, long? snapshot, CancellationToken token) =>
        CadRenderCore(width, height, cx, cy, scale, backend, white, revision, snapshot, token, false);

    /// <summary>Lossless straight RGBA for a local canvas transport. File exports continue
    /// to use PNG/SVG; this entry point applies the same camera, selection and source guards.</summary>
    public RenderImage CadRenderPixels(int width, int height, double cx, double cy, double scale, bool white,
        long revision, long? snapshot, CancellationToken token) =>
        CadRenderCore(width, height, cx, cy, scale, "skia", white, revision, snapshot, token, true);

    private RenderImage CadRenderCore(int width, int height, double cx, double cy, double scale, string backend, bool white,
        long revision, long? snapshot, CancellationToken token, bool pixels)
    {
        lock (gate)
        {
            Check(revision);
            if (width > 4096 || height > 4096 || (long)width * height > 16_000_000) throw new ArgumentException("CAD frames are limited to 4096 pixels per axis and 16 megapixels.");
            var source = snapshot is { } key ? retained.GetValueOrDefault(key) ?? throw new ShadowConflictException("Snapshot unavailable.") : Current;
            var scene = snapshot is { } saved && retainedCompilers.TryGetValue(saved, out var frozen) ? frozen.Compile(source, ViewOptions(), token) : Scene(source);
            var viewport = new Viewport(new(cx, cy), scale, width, height); viewport.Validate(); renderer.Swap(backend);
            var frame = new RenderFrame(scene, viewport, snapshot is null ? editor.State.Selection : null,
                white ? 0xffffffff : 0x00212830, 0xff58baff);
            return pixels ? renderer.RenderPixels(frame, token) : renderer.Render(frame, token);
        }
    }
}
