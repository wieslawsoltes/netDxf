# netDxf immutable shadow DOM

An additive .NET 8 workspace that separates the mutable DXF database from persistent, detached snapshots and consumer-specific processing. The existing netDxf project and target frameworks are unchanged.

## Run

From the repository root:

```sh
dotnet build shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/tests/netDxf.Shadow.Verification/netDxf.Shadow.Verification.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj -c Release -p:TargetFrameworks=net8.0
```

Open `http://127.0.0.1:5080`. The sample is an ASP.NET local host with a browser interface, not a browser-hosted .NET runtime. Linux needs fontconfig and system fonts for Skia text; the workflow runner supplies them. `TargetFrameworks=net8.0` confines the existing multi-target netDxf reference during cross-platform builds.

The Studio combines an explorer, property inspector, camera pan/zoom, selection and dragging, undo/redo, DXF open/save, topology overlays and metrics, retained snapshot views, and SkiaSharp/SVG switching. Enter commits an inspector value. Middle-button or Space-drag pans; the wheel zooms about the pointer; F fits; arrows nudge; Shift snaps dragging/nudging to 10 units. Add/delete and the source-mutation demonstration reset property history explicitly.

## Projects and dependency boundaries

| Project | Purpose | Dependencies |
| --- | --- | --- |
| `netDxf.Shadow` | Detached value algebra, object identity, immutable snapshots, edit batches, diffs, branch store | BCL only |
| `netDxf.Shadow.NetDxf` | Cached public-model capture and synchronized netDxf writes | Core + existing netDxf |
| `netDxf.Shadow.Geometry` | Double-precision interchange curves, block occurrences, styles, spatial index | Core |
| `netDxf.Shadow.Rendering` | Tessellation, render scene, hit testing, backend contracts, SVG | Geometry |
| `netDxf.Shadow.Rendering.SkiaSharp` | Skia PNG/direct canvas output, HarfBuzz shaping, native resource caches | Rendering + SkiaSharp/HarfBuzz |
| `netDxf.Shadow.Graph` | Toleranced connectivity, intersection splitting and graph algorithms | Geometry |
| `netDxf.Shadow.Editor` | Source-independent editing, immutable history/selection, snapping | Rendering |
| `netDxf.Shadow.Studio` | Integrated local web sample | Adapter + consumer projects |
| `netDxf.Shadow.Verification` | Executable correctness and performance smoke suite | Sample + libraries |

The core does not expose netDxf entities, SKPath, SKPaint, native fonts, or mutable arrays. Geometry is a neutral interchange projection, not a required geometry kernel. Other engines can read the core directly or consume analytic curves and the sampled render scene.

## Snapshot and synchronization contract

`ShadowSession.Current` is a lock-free O(1) reference read. Retaining a snapshot does not copy the document. Every snapshot carries a document GUID and monotonically increasing revision. Source classes are represented by object identities; structs and collections are detached values. Scalar atom text is invariant and retains numeric precision. DXF handles are preferred identities; non-handle objects receive session-local IDs. These are not persistent identities across independent document reloads.

The adapter traverses reachable public instance state, including layout/block/table/entity references and collection order. It retains unsupported entity data without pretending to render it. Getter failures and capture budgets are explicit. This is not a new DXF parser, a binary archive, private-field reflection, or a way to recover information netDxf already discarded.

Full `Refresh()` checks the entire reachable public graph and removes unreachable nodes. Unchanged nodes are reference-shared; a no-op refresh retains the snapshot object. `RefreshProperties(ids)` is a trusted dirty-object fast path only when ownership and collections have not changed. It must not be used for arbitrary topology changes.

Edits specify expected document/revision, target object/property, before value and after value. The immutable batch is validated first; the adapter also checks live touched properties. Values are converted before mutation, source setters are exercised on clones when available, and failed writes are rolled back in reverse order. A rollback that cannot restore the public graph fail-stops the session rather than publishing false success. Source events are not suppressible transactions, and observers of the live netDxf graph must use the same gate.

netDxf has no complete public property-notification stream. Arbitrary direct source writes are **not automatically observed**: serialize them and call `Refresh()`, or use `MutateSource`. `MutateSource` is an explicit, nontransactional escape hatch for entity/table/collection changes. Consumers read snapshots, not the mutable document. `ReadSource` callbacks must not retain mutable references.

```csharp
using netDxf;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Graph;

var document = DxfDocument.Load("drawing.dxf")
    ?? throw new InvalidOperationException("Invalid drawing.");
var session = new ShadowSession(document);
var retained = session.Current;
var compiler = new RenderCompiler();
var scene = compiler.Compile(retained);
var topology = new TopologyBuilder().Build(retained);
var editor = new EditorSession(session);
var line = retained.DrawingEntities().First(entity => entity.Type == "Line");
editor.SetProperty(line.Id!.Value, "StartPoint", ShadowValue.From(new Vec3(10, 20)));
editor.Undo();
// retained is unchanged; session.Current is a newer immutable revision.
```

An isolated branch is `new ShadowMemoryStore(snapshot)`. Applying edits to it does not mutate netDxf. There is no automatic three-way merge: rebase/merge policy must validate expected values explicitly.

## Rendering and extensibility

`CadSceneBuilder` expands insert occurrences with shared source identity, path identity, nested transforms, OCS, block origins and inherited style. Nonuniform/mirrored inserts preserve analytic ellipses. Cyclic/deep block references and excessive occurrence counts are bounded. `IEntityProjector` extends semantic entity conversion without changing the source adapter.

The supplied projectors cover common lines/points, circles/arcs/ellipses, bulged polylines, control-point NURBS, text/attributes, faces/meshes, hatch boundaries and generated dimension blocks. `CurveSampler` applies explicit tolerance and point/depth budgets. The core retains full source data independently of these projectors.

`IRenderBackend` is the swappable backend boundary. `RenderHost.Swap("skia")` and `Swap("svg")` serialize with in-flight rendering before disposing the old backend. `SkiaRenderBackend.Draw(SKCanvas, frame)` also supports a caller-owned CPU/GPU canvas. Rendering subtracts the camera origin in double precision before converting to float. No image reference is read from disk/network implicitly; a host may supply a trusted `ISkiaImageResolver`.

## Graph and editor

Topology uses a packed spatial BVH for segment candidates, explicit endpoint/curve tolerances, intersection/overlap splitting, optional elevation compatibility and union-find welding. Results retain source entity and instance keys. Algorithms include connected components, cycle rank, dead ends/junctions, Dijkstra shortest paths and iterative Tarjan bridges with parallel-edge handling. Curved intersections are based on sampled curves, not certified analytic predicates.

Editor history records canonical immutable values. Undo/redo rejects intervening external source revisions. Moving an inserted selection targets the outermost insert by default; explicit definition editing affects shared source geometry. Unsupported edit planners fail before source mutation. Add/remove/collection operations are available through the adapter escape hatch, but are not yet general immutable structural commands with undo/redo.

## Performance and scope boundaries

Snapshot acquisition is O(1). Full capture/refresh is proportional to the public graph and value payload; persistent node/collection updates share unchanged data. Cached public getters avoid repeated reflection discovery. Trusted local edits avoid scanning unrelated source nodes. Render scenes are cached per snapshot/options; compiled BVH queries cull drawing candidates; native Skia pictures and fonts have bounded caches. The current consumer compiler rebuilds a scene for a changed revision; cross-revision geometry invalidation is not yet fully incremental.

The verification runner reports capture allocation/time, one million snapshot reads and no-op refresh for 10,000 lines. These are reproducible smoke measurements, not million-entity or cross-platform performance guarantees.

This is not full AutoCAD rendering/editor parity. Remaining specialized features include SHX glyph rendering, complete rich MTEXT/fields and font fallback, complex linetypes and exact global scaling, hatch patterns/gradients and all spline boundary variants, viewport projection/clipping, ACIS/custom objects, image clipping and complete dimension regeneration. Variable polyline widths currently render as diagnosed centerlines. Text culling/hit bounds are conservative, not exact glyph outlines. General affine/structural edit commands and read-only source collection mutation need specialized adapters. The raw snapshot preserves their public source metadata; projection coverage and diagnostics must be tested separately from capture coverage.

The sample is local and single-user. It binds loopback, checks Host, requires a custom mutation header and does not enable CORS or arbitrary external resources. It is not a hardened untrusted-file service. Limit input size and resource budgets before hosting it elsewhere.
