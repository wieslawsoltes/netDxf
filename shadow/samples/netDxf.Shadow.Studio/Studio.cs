using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using netDxf.Entities;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;

namespace netDxf.Shadow.Studio;

public sealed record RevisionRequest(long Revision);
public sealed record PickRequest(long Revision, double X, double Y, double Tolerance, bool Extend = false);
public sealed record SelectRequest(long Revision, string Key, bool Extend = false);
public sealed record MoveRequest(long Revision, double X, double Y, bool Definition = false);
public sealed record PropertyRequest(long Revision, string Id, string Property, JsonElement Value);
public sealed record LayoutRequest(long Revision, string Name);
public sealed record AddRequest(long Revision, string Kind, double X, double Y);

/// <summary>Single-user host. The libraries themselves do not depend on ASP.NET or this sample.</summary>
public sealed partial class Studio : IDisposable
{
    private readonly object gate = new();
    private ShadowSession session;
    private EditorSession liveEditor;
    private readonly SkiaTextLayoutEngine textEngine = new();
    private readonly StudioAssets assets = new();
    private readonly RenderCompiler compiler;
    private readonly CadSceneBuilder sceneBuilder;
    private long graphFontRevision = -1;
    private readonly RenderHost renderer;
    private readonly SortedDictionary<long, ShadowSnapshot> liveRetained = new();
    private string liveLayout = "Model";
    private TopologyGraph? graph;
    private string? graphLayout;
    private double captureMilliseconds;
    private bool disposed;

    public Studio() : this(true) { }

    public Studio(bool demo)
    {
        shxFonts.Register("shadow-demo.shx", DemoFont());
        var shxProjector = new ShxEntityProjector(shxFonts);
        sceneBuilder = new([new DimensionEntityProjector(resources: [shxProjector]), shxProjector]);
        compiler = new(sceneBuilder, textEngine);
        renderer = new(new Dictionary<string, Func<IRenderBackend>>
        {
            ["skia"] = () => new SkiaRenderBackend(assets, textEngine),
            ["svg"] = () => new SvgRenderBackend(assets)
        }, "skia");
        try
        {
            var timer = Stopwatch.StartNew(); session = new(demo ? CreateDocument() : new DxfDocument());
            captureMilliseconds = timer.Elapsed.TotalMilliseconds; liveEditor = new(session);
        }
        catch { renderer.Dispose(); assets.Dispose(); textEngine.Dispose(); throw; }
    }
    private RenderScene Scene(ShadowSnapshot? snapshot = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return (activeBranch?.Compiler ?? compiler).Compile(snapshot ?? Current, ViewOptions());
    }
    private void Check(long revision)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Current.Revision != revision) throw new ShadowConflictException("The document changed. Refresh before editing.");
    }
    public object State()
    {
        lock (gate)
        {
            var snapshot = Current; var scene = Scene(); var state = editor.State;
            return new
            {
                serverId, changeVersion, viewVersion, workspaceId = WorkspaceId, workspaceName = activeBranch?.Name ?? "Live document",
                sourceRevision = session.Current.Revision, ancestorRevision = activeBranch?.Ancestor.Revision,
                branches = branches.Values.OrderBy(b => b.Name, StringComparer.Ordinal).Select(b => new { b.Id, b.Name, revision = b.Editor.Snapshot.Revision, ancestorRevision = b.Ancestor.Revision }).ToArray(),
                snapshot.DocumentId, snapshot.Revision, layout, linetypeAlignment = PatternAlignment.ToString(),
                annotationMode = Annotation.Mode.ToString(), annotationPaperUnits = Annotation.Scale?.PaperUnits ?? 1,
                annotationDrawingUnits = Annotation.Scale?.DrawingUnits ?? 1, objects = snapshot.Objects.Count, entities = snapshot.EntityIds.Count,
                occurrences = scene.Items.Length, bounds = scene.Bounds.IsEmpty ? new Bounds2(-50, -50, 50, 50) : scene.Bounds,
                state.UndoCount, state.RedoCount, selection = state.Selection, captureMilliseconds,
                layouts = snapshot.Layouts.Select(l => l["Name"].Text).ToArray(), backends = renderer.Backends,
                snapshots = retained.Keys.ToArray(), fonts = CurrentFonts.Fonts.Keys.Order().ToArray(), fontRevision = CurrentFonts.Revision,
                diagnostics = scene.Diagnostics.Take(200).Select(d => new { d.Code, d.Message, id = d.Object?.Value, severity = d.Severity.ToString() }).ToArray(),
                items = scene.Items.Take(2000).Select(i => new { i.Key, id = i.Entity.Value, type = snapshot.Objects[i.Entity].Type, i.Style.Layer, inserted = !i.InsertPath.IsEmpty }).ToArray()
            };
        }
    }
    public RenderImage Render(int width, int height, double? cx, double? cy, double? scale, string backend, long? revision, CancellationToken token, string? merge = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (revision.HasValue && merge is not null) throw new ArgumentException("Choose a retained snapshot or a merge preview, not both.");
            if (revision is { } missing && !retained.ContainsKey(missing)) throw new ShadowConflictException("Retained snapshot is unavailable in this workspace.");
            var pending = merge is null ? null : FindPlan(merge);
            if (pending is not null) ValidatePlan(pending);
            var snapshot = pending?.Plan.Preview ?? (revision is { } r ? retained[r] : Current);
            var scene = revision is { } key && retainedCompilers.TryGetValue(key, out var frozen)
                ? frozen.Compile(snapshot, ViewOptions(), token) : pending is not null ? pending.Compiler.Compile(snapshot!, ViewOptions(), token) : Scene(snapshot);
            var viewport = cx.HasValue && cy.HasValue && scale.HasValue ? new Viewport(new(cx.Value, cy.Value), scale.Value, width, height) : Viewport.Fit(scene.Bounds, width, height);
            renderer.Swap(backend);
            return renderer.Render(new(scene, viewport, ReferenceEquals(snapshot, Current) ? editor.State.Selection : null), token);
        }
    }
    public object Graph(CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (graph is null || graph.Revision != Current.Revision || graphLayout != layout || graphFontRevision != CurrentFonts.Revision)
            { graph = new TopologyBuilder().Build((activeBranch?.SceneBuilder ?? sceneBuilder).Build(Current, new(layout) { RootBlock = activeBranch?.BlockRoot }, token), cancellationToken: token); graphLayout = layout; graphFontRevision = CurrentFonts.Revision; }
            return new
            {
                documentId = Current.DocumentId, workspaceId = WorkspaceId, viewVersion, layout, fontRevision = CurrentFonts.Revision, graph.Revision, components = graph.ConnectedComponents().Length, graph.CycleRank,
                bridges = graph.Bridges().Length, deadEnds = graph.DeadEnds.Count(), junctions = graph.Junctions.Count(),
                nodes = graph.Nodes.Take(20000).Select(n => new { n.Id, n.Position.X, n.Position.Y, degree = n.Edges.Length }).ToArray(),
                edges = graph.Edges.Take(30000).Select(e => new { e.A, e.B, e.Occurrence, e.Length }).ToArray()
            };
        }
    }
    public object Inspect(ObjectId id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!Current.Objects.TryGetValue(id, out var node)) throw new ArgumentException("Unknown object.");
            return new { documentId = Current.DocumentId, workspaceId = WorkspaceId, viewVersion, revision = Current.Revision, id = id.Value, node.Type, properties = node.Properties.OrderBy(p => p.Key).Select(p => new
            { name = p.Key, value = Display(p.Value), writable = node.WritableProperties.Contains(p.Key) && p.Value is ShadowValue.Atom or ShadowValue.Structure, kind = p.Value.GetType().Name }).ToArray() };
        }
    }
    private static object? Display(ShadowValue value) => value switch
    {
        ShadowValue.Atom atom => atom.Text,
        ShadowValue.Reference reference => new { reference = reference.Id.Value },
        ShadowValue.Structure structure => structure.Fields.ToDictionary(p => p.Key, p => Display(p.Value)),
        ShadowValue.Sequence sequence => new { count = sequence.Items.Length, preview = sequence.Items.Take(8).Select(Display).ToArray() },
        ShadowValue.Fault fault => new { error = fault.Message },
        _ => null
    };
    public object Pick(PickRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            var hit = RenderCompiler.HitTest(Scene(), new(request.X, request.Y), request.Tolerance);
            if (hit is null) { if (!request.Extend) editor.Select([]); }
            else if (request.Extend) editor.ToggleSelection(hit.Key); else editor.Select([hit.Key]);
            return UpdatedState();
        }
    }
    public object Select(SelectRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!Scene().Items.Any(i => i.Key == request.Key)) throw new ArgumentException("Unknown drawing occurrence.");
            if (request.Extend) editor.ToggleSelection(request.Key); else editor.Select([request.Key]);
            return UpdatedState();
        }
    }
    public object Translate(MoveRequest request)
    {
        lock (gate) { Check(request.Revision); editor.Translate(Scene(), new(request.X, request.Y), request.Definition); return UpdatedState(); }
    }
    public object SetProperty(PropertyRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            var id = new ObjectId(request.Id);
            if (!Current.Objects.TryGetValue(id, out var node)) throw new ArgumentException("Unknown object.");
            editor.SetProperty(id, request.Property, ConvertInput(node[request.Property], request.Value)); return UpdatedState();
        }
    }
    private static ShadowValue ConvertInput(ShadowValue before, JsonElement value)
    {
        if (before is ShadowValue.Atom atom) return atom with { Text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText() };
        if (before is ShadowValue.Structure structure && value.ValueKind == JsonValueKind.Object)
        {
            var fields = structure.Fields.ToBuilder();
            foreach (var property in value.EnumerateObject())
            {
                if (!fields.TryGetValue(property.Name, out var field)) throw new ArgumentException($"Unknown coordinate field {property.Name}.");
                fields[property.Name] = ConvertInput(field, property.Value);
            }
            return structure with { Fields = fields.ToImmutable() };
        }
        throw new NotSupportedException("This inspector edits scalar values and coordinate structures only.");
    }
    public object History(string operation, long revision)
    {
        lock (gate)
        {
            Check(revision);
            if (operation == "undo") editor.Undo(); else if (operation == "redo") editor.Redo(); else throw new ArgumentException("Unknown history operation.");
            return UpdatedState();
        }
    }
    public object SetLayout(LayoutRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Close the block editor before switching layouts.");
            if (!Current.Layout(request.Name).Exists) throw new ArgumentException("Unknown layout.");
            layout = request.Name; editor.Select([]); return UpdatedState();
        }
    }
    public object Add(AddRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!double.IsFinite(request.X) || !double.IsFinite(request.Y)) throw new ArgumentException("Coordinates must be finite.");
            var position = new Vec3(request.X, request.Y);
            (string Type, Dictionary<string, ShadowValue> Properties) specification = request.Kind switch
            {
                "line" => ("netDxf.Entities.Line", new()
                {
                    ["StartPoint"] = ShadowValue.From(position - new Vec3(20, 0)), ["EndPoint"] = ShadowValue.From(position + new Vec3(20, 0))
                }),
                "circle" => ("netDxf.Entities.Circle", new() { ["Center"] = ShadowValue.From(position), ["Radius"] = ShadowValue.From(12.0) }),
                "text" => ("netDxf.Entities.Text", new()
                {
                    ["Position"] = ShadowValue.From(position), ["Height"] = ShadowValue.From(5.0), ["Value"] = ShadowValue.From("Editable DXF text")
                }),
                _ => throw new ArgumentException("Unknown entity kind.")
            };
            var id = editor.CreateEntity(specification.Type, specification.Properties, layout);
            editor.Select(Scene().Items.Where(item => item.Entity == id).Select(item => item.Key)); return UpdatedState();
        }
    }
    public object Delete(long revision)
    {
        lock (gate)
        {
            Check(revision); var keys = editor.State.Selection;
            var ids = Scene().Items.Where(i => keys.Contains(i.Key)).Select(i => i.InsertPath.IsEmpty ? i.Entity : i.InsertPath[0]).Distinct().ToArray();
            editor.DeleteEntities(ids); return UpdatedState();
        }
    }
    public object SourceDemo(long revision)
    {
        lock (gate)
        {
            Check(revision); RequireLive();
            session.MutateSource(d =>
            {
                var line = d.Layouts[layout].AssociatedBlock.Entities.OfType<Line>().FirstOrDefault();
                if (line is not null) line.EndPoint += new Vector3(0, 5, 0);
            });
            editor.ResetHistory(); return UpdatedState();
        }
    }
    public object Retain(long revision)
    {
        lock (gate)
        {
            Check(revision); retained[revision] = Current; retainedCompilers[revision] = FrozenCompiler();
            while (retained.Count > 12) { var key = retained.Keys.First(); retained.Remove(key); retainedCompilers.Remove(key); } return UpdatedState();
        }
    }
    public void Load(Stream stream)
    {
        var document = DxfDocument.Load(stream) ?? throw new ArgumentException("netDxf could not read the supplied drawing.");
        var timer = Stopwatch.StartNew(); var next = new ShadowSession(document);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            session = next; liveEditor = new(next); activeBranch = null; branches.Clear(); mergePlans.Clear();
            viewVersion = checked(viewVersion + 1); liveLayout = "Model"; livePatternAlignment = CadLinetypeAlignment.Periodic; liveAnnotation = ModelAnnotationContext.Default; graph = null;
            liveRetained.Clear(); liveRetainedCompilers.Clear(); captureMilliseconds = timer.Elapsed.TotalMilliseconds;
            PublishChange();
        }
    }
    public byte[] Save(long? revision = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (revision is { } expected) Check(expected);
            RequireLive();
            using var stream = new MemoryStream();
            var before = session.Current;
            try
            {
                liveEditor.SynchronizeSource(() => session.MutateSource(document =>
                {
                    document.SaveWithDimensionPictures(stream);
                }));
                return stream.ToArray();
            }
            finally
            {
                if (!ReferenceEquals(before, session.Current)) PublishChange();
            }
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; CompleteSubscriptions(); activeBranch = null; branches.Clear(); mergePlans.Clear(); liveRetained.Clear(); liveRetainedCompilers.Clear(); renderer.Dispose(); assets.Dispose(); textEngine.Dispose();
        }
    }
}
