using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed record CadCommandRequest(long Revision, DraftingCommand Command);
public sealed record CadSelectionRequest(long Revision, string Mode, Vec3 First = default, Vec3 Second = default,
    double Tolerance = 1, bool Extend = false, bool Remove = false, string? Entity = null);
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
        var present = scene.Items.Where(i => keys.Contains(i.Key)).Select(DraftingQueries.Root).Distinct().ToHashSet();
        cadSelectionOrder = cadSelectionOrder.Where(present.Contains).Concat(present.Except(cadSelectionOrder).OrderBy(i => i.Value, StringComparer.Ordinal)).ToImmutableArray();
        return cadSelectionOrder;
    }

    public object CadState()
    {
        lock (gate)
        {
            var scene = Scene(); var selected = CadSelected(scene); var snapshot = Current;
            var roots = scene.Items.GroupBy(DraftingQueries.Root).ToArray();
            ImmutableArray<DraftingGrip> grips = []; string? gripDiagnostic = null;
            try { if (selected.Length <= 128) grips = DraftingGrips.Read(snapshot, selected); else gripDiagnostic = "Grips are hidden for selections over 128 objects."; }
            catch (InvalidOperationException error) { gripDiagnostic = error.Message; }
            return new
            {
                blockEditor = activeBranch?.BlockRoot is { } blockRoot ? new { id = blockRoot.Value,
                    name = snapshot.Read(blockRoot)["Name"].Text,
                    changed = !ShadowGraphPatch.Between(activeBranch.Ancestor, snapshot).Changes.IsEmpty } : null,
                state = State(), layers = DraftingLayers.Read(snapshot), selectedEntities = selected.Select(i => i.Value).ToArray(),
                grips = grips.Select(g => new { g.Id, entity = g.Entity.Value, g.Property, g.Position, g.Kind, g.Index }), gripDiagnostic,
                items = roots.Take(5000).Select(g => new { id = g.Key.Value, key = g.First().Key, type = snapshot.Read(g.Key).Type,
                    layer = snapshot.Read(g.Key)["Layer"]["Name"].Text, count = g.Count(), bounds = g.Aggregate(Bounds2.Empty, (b, item) => b.Union(item.Bounds)) }).ToArray(), itemCount = roots.Length,
                blocks = snapshot.Document["Blocks"].Items.Where(b => !b["Name"].Text.StartsWith('*')).Take(1024)
                    .Select(b => new { id = b.Id!.Value.Value, name = b["Name"].Text, entities = b["Entities"].Items.Count() }).ToArray(),
                linetypes = snapshot.Document["Linetypes"].Items.Select(l => l["Name"].Text).ToArray(),
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
            PublishChange(); return CadState();
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
                "entity" when request.Entity is not null => DraftingQueries.Expand(scene, [new(request.Entity)]),
                "clear" => [],
                _ => throw new ArgumentException("Unknown selection mode.")
            };
            var keySet = keys.ToHashSet(StringComparer.Ordinal); var incoming = scene.Items.Where(i => keySet.Contains(i.Key)).Select(DraftingQueries.Root).Distinct().ToImmutableArray();
            var next = request.Remove ? previous.Except(incoming).ToImmutableArray() : request.Extend ? previous.Concat(incoming.Except(previous)).ToImmutableArray() : incoming;
            if (next.Length > 10_000) throw new ArgumentException("Selection is limited to 10,000 source entities.");
            cadSelectionOrder = next; editor.Select(DraftingQueries.Expand(scene, next)); PublishChange(); return CadState();
        }
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
            DraftingGrips.Move(Current, id, request.Grip, request.Position).Execute(editor); PublishChange(); return CadState();
        }
    }
    public object CadLayer(CadLayerRequest request)
    {
        lock (gate) { Check(request.Revision); DraftingLayers.Plan(Current, request.Layer).Execute(editor); PublishChange(); return CadState(); }
    }
    public object CadAssignLayer(CadAssignLayerRequest request)
    {
        lock (gate) { Check(request.Revision); DraftingLayers.Assign(Current, CadSelected(Scene()), new(request.Layer)).Execute(editor); PublishChange(); return CadState(); }
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
        long revision, long? snapshot, CancellationToken token)
    {
        lock (gate)
        {
            Check(revision);
            if (width > 4096 || height > 4096 || (long)width * height > 16_000_000) throw new ArgumentException("CAD frames are limited to 4096 pixels per axis and 16 megapixels.");
            var source = snapshot is { } key ? retained.GetValueOrDefault(key) ?? throw new ShadowConflictException("Snapshot unavailable.") : Current;
            var scene = snapshot is { } saved && retainedCompilers.TryGetValue(saved, out var frozen) ? frozen.Compile(source, ViewOptions(), token) : Scene(source);
            var viewport = new Viewport(new(cx, cy), scale, width, height); viewport.Validate(); renderer.Swap(backend);
            return renderer.Render(new(scene, viewport, snapshot is null ? editor.State.Selection : null,
                white ? 0xffffffff : 0x00212830, 0xff58baff), token);
        }
    }
}
