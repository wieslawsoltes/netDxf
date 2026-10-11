using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Tables;

namespace netDxf.Shadow.Studio;

public sealed record RevisionRequest(long Revision);
public sealed record PickRequest(long Revision, double X, double Y, double Tolerance, bool Extend = false);
public sealed record SelectRequest(long Revision, string Key, bool Extend = false);
public sealed record MoveRequest(long Revision, double X, double Y, bool Definition = false);
public sealed record PropertyRequest(long Revision, string Id, string Property, JsonElement Value);
public sealed record LayoutRequest(long Revision, string Name);
public sealed record AddRequest(long Revision, string Kind, double X, double Y);

/// <summary>Single-user host. The libraries themselves do not depend on ASP.NET or this sample.</summary>
public sealed class Studio : IDisposable
{
    private readonly object gate = new();
    private ShadowSession session;
    private EditorSession editor;
    private readonly RenderCompiler compiler = new();
    private readonly RenderHost renderer = new(new Dictionary<string, Func<IRenderBackend>>
    { ["skia"] = () => new SkiaRenderBackend(), ["svg"] = () => new SvgRenderBackend() }, "skia");
    private readonly SortedDictionary<long, ShadowSnapshot> retained = new();
    private string layout = "Model";
    private TopologyGraph? graph;
    private string? graphLayout;
    private double captureMilliseconds;

    public Studio()
    {
        var timer = Stopwatch.StartNew();
        session = new(CreateDocument());
        captureMilliseconds = timer.Elapsed.TotalMilliseconds;
        editor = new(session);
    }
    private RenderScene Scene(ShadowSnapshot? snapshot = null) => compiler.Compile(snapshot ?? session.Current, new(layout));
    private void Check(long revision)
    {
        if (session.Current.Revision != revision) throw new ShadowConflictException("The document changed. Refresh before editing.");
    }
    public object State()
    {
        lock (gate)
        {
            var snapshot = session.Current;
            var scene = Scene();
            var state = editor.State;
            return new
            {
                snapshot.DocumentId, snapshot.Revision, layout, objects = snapshot.Objects.Count, entities = snapshot.EntityIds.Count,
                occurrences = scene.Items.Length, bounds = scene.Bounds.IsEmpty ? new Bounds2(-50, -50, 50, 50) : scene.Bounds,
                state.UndoCount, state.RedoCount, selection = state.Selection, captureMilliseconds,
                layouts = snapshot.Layouts.Select(l => l["Name"].Text).ToArray(), backends = renderer.Backends,
                snapshots = retained.Keys.ToArray(),
                diagnostics = scene.Diagnostics.Take(200).Select(d => new { d.Code, d.Message, id = d.Object?.Value, severity = d.Severity.ToString() }).ToArray(),
                items = scene.Items.Take(2000).Select(i => new { i.Key, id = i.Entity.Value, type = snapshot.Objects[i.Entity].Type, i.Style.Layer, inserted = !i.InsertPath.IsEmpty }).ToArray()
            };
        }
    }
    public RenderImage Render(int width, int height, double? cx, double? cy, double? scale, string backend, long? revision, CancellationToken token)
    {
        lock (gate)
        {
            var snapshot = revision is { } r && retained.TryGetValue(r, out var old) ? old : session.Current;
            var scene = Scene(snapshot);
            var viewport = cx.HasValue && cy.HasValue && scale.HasValue ? new Viewport(new(cx.Value, cy.Value), scale.Value, width, height) : Viewport.Fit(scene.Bounds, width, height);
            renderer.Swap(backend);
            return renderer.Render(new(scene, viewport, ReferenceEquals(snapshot, session.Current) ? editor.State.Selection : null), token);
        }
    }
    public object Graph(CancellationToken token)
    {
        lock (gate)
        {
            if (graph is null || graph.Revision != session.Current.Revision || graphLayout != layout)
            { graph = new TopologyBuilder().Build(session.Current, layout, cancellationToken: token); graphLayout = layout; }
            return new
            {
                graph.Revision, components = graph.ConnectedComponents().Length, graph.CycleRank,
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
            if (!session.Current.Objects.TryGetValue(id, out var node)) throw new ArgumentException("Unknown object.");
            return new { id = id.Value, node.Type, properties = node.Properties.OrderBy(p => p.Key).Select(p => new
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
            return State();
        }
    }
    public object Select(SelectRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!Scene().Items.Any(i => i.Key == request.Key)) throw new ArgumentException("Unknown drawing occurrence.");
            if (request.Extend) editor.ToggleSelection(request.Key); else editor.Select([request.Key]);
            return State();
        }
    }
    public object Translate(MoveRequest request)
    {
        lock (gate) { Check(request.Revision); editor.Translate(Scene(), new(request.X, request.Y), request.Definition); return State(); }
    }
    public object SetProperty(PropertyRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            var id = new ObjectId(request.Id);
            if (!session.Current.Objects.TryGetValue(id, out var node)) throw new ArgumentException("Unknown object.");
            editor.SetProperty(id, request.Property, ConvertInput(node[request.Property], request.Value));
            return State();
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
            return State();
        }
    }
    public object SetLayout(LayoutRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!session.Current.Layout(request.Name).Exists) throw new ArgumentException("Unknown layout.");
            layout = request.Name; editor.Select([]); return State();
        }
    }
    public object Add(AddRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            if (!double.IsFinite(request.X) || !double.IsFinite(request.Y)) throw new ArgumentException("Coordinates must be finite.");
            EntityObject entity = request.Kind switch
            {
                "line" => new Line(new Vector2(request.X - 20, request.Y), new Vector2(request.X + 20, request.Y)),
                "circle" => new Circle(new Vector2(request.X, request.Y), 12),
                "text" => new Text("Editable DXF text", new Vector2(request.X, request.Y), 5),
                _ => throw new ArgumentException("Unknown entity kind.")
            };
            session.MutateSource(d => { d.Entities.ActiveLayout = layout; d.Entities.Add(entity); });
            editor.ResetHistory(true); return State();
        }
    }
    public object Delete(long revision)
    {
        lock (gate)
        {
            Check(revision);
            var keys = editor.State.Selection;
            var ids = Scene().Items.Where(i => keys.Contains(i.Key)).Select(i => i.InsertPath.IsEmpty ? i.Entity : i.InsertPath[0]).Distinct().ToArray();
            session.MutateSource(d =>
            {
                d.Entities.ActiveLayout = layout;
                foreach (var id in ids)
                    if (id.Value.StartsWith("H:", StringComparison.Ordinal) && d.GetObjectByHandle(id.Value[2..]) is EntityObject entity) d.Entities.Remove(entity);
            });
            editor.ResetHistory(true); return State();
        }
    }
    public object SourceDemo(long revision)
    {
        lock (gate)
        {
            Check(revision);
            session.MutateSource(d =>
            {
                var line = d.Layouts[layout].AssociatedBlock.Entities.OfType<Line>().FirstOrDefault();
                if (line is not null) line.EndPoint += new Vector3(0, 5, 0);
            });
            editor.ResetHistory(); return State();
        }
    }
    public object Retain(long revision)
    {
        lock (gate)
        {
            Check(revision); retained[revision] = session.Current;
            while (retained.Count > 12) retained.Remove(retained.Keys.First());
            return State();
        }
    }
    public void Load(Stream stream)
    {
        var document = DxfDocument.Load(stream) ?? throw new ArgumentException("netDxf could not read the supplied drawing.");
        var timer = Stopwatch.StartNew(); var next = new ShadowSession(document);
        lock (gate)
        {
            session = next; editor = new(next); layout = "Model"; graph = null; retained.Clear();
            captureMilliseconds = timer.Elapsed.TotalMilliseconds;
        }
    }
    public byte[] Save()
    {
        lock (gate) return session.ReadSource(document =>
        {
            using var stream = new MemoryStream();
            if (!document.Save(stream)) throw new InvalidOperationException("netDxf could not save this drawing.");
            return stream.ToArray();
        });
    }
    public void Dispose() => renderer.Dispose();

    private static DxfDocument CreateDocument()
    {
        var document = new DxfDocument();
        var pipe = new Layer("Process piping") { Color = new AciColor((byte)72, (byte)188, (byte)224) };
        var equipment = new Layer("Equipment") { Color = new AciColor((byte)148, (byte)199, (byte)124) };
        var annotation = new Layer("Annotations") { Color = new AciColor((byte)205, (byte)211, (byte)225) };
        foreach (var (a, b) in new[]
        {
            (new Vector2(-130, 0), new Vector2(-60, 0)), (new Vector2(-60, 0), new Vector2(0, 0)),
            (new Vector2(0, 0), new Vector2(75, 0)), (new Vector2(75, 0), new Vector2(135, 0)),
            (new Vector2(-60, 0), new Vector2(-60, 55)), (new Vector2(-60, 55), new Vector2(75, 55)),
            (new Vector2(75, 55), new Vector2(75, 0)), (new Vector2(0, 0), new Vector2(0, -55))
        }) document.Entities.Add(new Line(a, b) { Layer = pipe });
        document.Entities.Add(new Circle(new Vector2(-130, 0), 18) { Layer = equipment });
        document.Entities.Add(new Circle(new Vector2(135, 0), 18) { Layer = equipment });
        document.Entities.Add(new Arc(new Vector2(0, -55), 18, 0, 180) { Layer = equipment });
        document.Entities.Add(new Line(new Vector2(-18, -55), new Vector2(18, -55)) { Layer = equipment });
        var valve = new Block("Valve");
        valve.Entities.Add(new Line(new Vector2(-9, -7), new Vector2(9, 7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(-9, 7), new Vector2(9, -7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(-9, -7), new Vector2(-9, 7)) { Color = AciColor.ByBlock });
        valve.Entities.Add(new Line(new Vector2(9, -7), new Vector2(9, 7)) { Color = AciColor.ByBlock });
        document.Entities.Add(new Insert(valve, new Vector2(-30, 0)) { Layer = equipment });
        document.Entities.Add(new Insert(valve, new Vector2(35, 55)) { Layer = equipment });
        document.Entities.Add(new Insert(valve, new Vector2(0, -28)) { Rotation = 90, Layer = equipment });
        foreach (var (text, x, y) in new[] { ("SHADOW / Process loop", -148.0, 87.0), ("P-101", -141.0, -32.0), ("V-102", 122.0, -32.0), ("Supply", -122.0, 9.0), ("Return", 17.0, 65.0), ("Drain", 12.0, -44.0) })
            document.Entities.Add(new Text(text, new Vector2(x, y), 5) { Layer = annotation });
        return document;
    }
}
