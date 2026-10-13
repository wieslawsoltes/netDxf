# netDxf immutable shadow DOM

An additive .NET 8 workspace separating the mutable DXF database from persistent, detached snapshots and consumer-specific processing. The existing netDxf target frameworks are preserved. `HatchPattern.Style` now exposes its existing setter publicly so the adapter can edit island modes through the public source API.

```text
Mutable netDxf document <-> serialized adapter <-> immutable shadow snapshots
                                                       |
                           +---------------------------+------------------+
                           |                           |                  |
                   semantic geometry              graph analysis      editing
                           |
                compiled presentation + text layout
                           |
                  SkiaSharp / SVG / custom backend
```

## Build, verify and run

From the repository root:

```sh
dotnet build shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/tests/netDxf.Shadow.Verification/netDxf.Shadow.Verification.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification/netDxf.Shadow.AdvancedVerification.csproj -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj -c Release -p:TargetFrameworks=net8.0
```

Open `http://127.0.0.1:5080`. Studio is an ASP.NET local host with a browser interface, not an Avalonia application or a browser-hosted .NET runtime. Linux needs fontconfig and usable system fonts. `TargetFrameworks=net8.0` confines the existing multi-target netDxf reference during these builds.

Studio combines a drawing explorer, property inspector, camera pan/zoom, selection and dragging, structural/property undo/redo, DXF open/save, topology overlays and metrics, retained snapshot views, and runtime SkiaSharp/SVG switching. Enter commits an inspector value. Middle-button or Space-drag pans; the wheel zooms about the pointer; F fits; arrows nudge; Shift snaps dragging/nudging to 10 units.

Select **Hatch islands** for all nine Normal/Outer/Ignore solid, pattern and gradient combinations; see [HatchIslands.md](docs/HatchIslands.md). Select **Paragraphs** for shared indentation, alignment, tabs and spacing examples; see [Paragraphs.md](docs/Paragraphs.md).

The **Model** layout contains a process-loop drawing. Select **Appearance** to inspect tapered polylines, a pattern hatch with an island, a gradient region, clipped/sheared raster data and formatted MTEXT. Select **Paper viewports** for an orthographic model overview and a circular, rotated detail with frozen annotation layers. Select **SHX strokes** for native-independent stroke typography. Use **Load SHX/SHP** to register an explicitly selected resource (4 MiB sample limit); imported `.shp` names become `.shx` aliases. These example glyph instructions are generated in source, not bundled font files. Add/delete are immutable structural commands and participate in the same history as property changes. The explicit source-mutation demonstration resets stale history.

Studio shares one host-owned `SkiaTextLayoutEngine` between the render compiler and native backend. The SVG backend consumes the same prepared text layout. Its checkerboard is a generated, explicitly registered in-memory image; opening a drawing never authorizes arbitrary image-path access.

## Projects and boundaries

| Project | Purpose | Dependencies |
| --- | --- | --- |
| `netDxf.Shadow` | Detached values, object identity, snapshots, optimistic property batches, immutable graph patches, diffs, reverse references, isolated branches | BCL only |
| `netDxf.Shadow.NetDxf` | Cached public-model capture, source synchronization, object factories and collection-adoption plans | Core + existing netDxf |
| `netDxf.Shadow.Geometry` | Double-precision analytic curves, shared block occurrences, styles, presentation regions/stroke groups, hatch scan conversion, persistent BVH | Core |
| `netDxf.Shadow.Rendering` | Sampled render scenes, signed dash patterns, fill/stroke/image picking, paper-space composition, rich-text parser/layout, backend contracts, SVG | Geometry |
| `netDxf.Shadow.Rendering.Shx` | Optional bounded SHX/SHP decoding, analytic glyph VM, immutable resource catalogs, shape/text projection | Rendering; no native dependencies |
| `netDxf.Shadow.Rendering.SkiaSharp` | PNG/direct canvas output, gradient/image paints, HarfBuzz/native glyph metrics and native resource caches | Rendering + SkiaSharp/HarfBuzz |
| `netDxf.Shadow.Graph` | Toleranced connectivity, intersection splitting and graph algorithms | Geometry |
| `netDxf.Shadow.Editor` | Source-independent editing, immutable structural/property history and selection, snapping | Rendering |
| `netDxf.Shadow.Studio` | Integrated local web sample | Adapter + consumer projects |
| `netDxf.Shadow.Verification` | Original contracts, integration and capture/read smoke measurements | Sample + libraries |
| `netDxf.Shadow.AdvancedVerification` | Structural history, incremental processing, appearance, typography, paper viewports, SHX resources and gallery regression tests | Sample + libraries |

The core does not expose mutable netDxf objects, SKPath, SKPaint, native fonts or mutable arrays. Geometry is a neutral interchange projection, not a required geometry kernel. Other consumers can read the shadow graph directly without using the supplied geometry/render projects.

## Snapshot and synchronization contract

`ShadowSession.Current` is a lock-free O(1) reference read. Retaining a snapshot does not copy the document. Snapshots carry a document GUID and monotonically increasing revision. Reference objects are represented by identities; structs, public fields and collections are detached. Scalar atom text uses invariant formatting and preserves numeric precision. DXF handles are preferred identities; non-handle/new objects receive session-local identities. Identity is stable within the session, not across independently loaded documents.

The adapter traverses reachable **public instance state**, including layouts, blocks, tables, entities, ownership references and collection order. Unsupported entity data remains accessible independently of projection support. This is not a new DXF parser, a byte-preserving archive, private-field reflection, or a way to recover information already discarded by netDxf. Getter failures and capture budgets are explicit diagnostics/errors.

Full `Refresh()` checks the reachable public graph and removes unreachable nodes. Unchanged nodes are reference-shared; a no-op refresh retains the same snapshot object. `RefreshProperties(ids)` is a trusted dirty-object path only when ownership/collection membership has not changed. It must not be used for arbitrary structural mutations.

Property edits carry expected document/revision and before/after values. The default adapter policy validates the live public graph as well as snapshot expectations, converts values before mutation and records canonical source values. The explicit `EditedProperties` policy requires exclusive source ownership and validates touched properties on its fast path. Failed writes roll back in reverse order. An inability to restore the public graph fail-stops the session rather than publishing a false successful snapshot. Native source events are not suppressible database transactions: code observing the live source must participate in the same serialized access discipline.

`ShadowGraphPatch` and `ShadowGraphEdit` represent immutable object/membership/collection changes. `IShadowGraphStore` is implemented by the adapter and isolated memory store. The adapter plans factory construction, source setters, collection operations and ownership adoption before publishing; unsupported construction or collection shapes are rejected, not silently approximated. Structural undo/redo restores canonical managed snapshots without retaining live source objects in history.

netDxf has no complete property-notification stream. Arbitrary direct source changes are **not automatically observed**: serialize them and call `Refresh()`, or use `MutateSource(...)`. `MutateSource` remains an explicit, nontransactional escape hatch, not a substitute for immutable structural commands. `ReadSource` callbacks must not retain mutable references or mutate the source under a read contract.

## Integrated API example

The following uses the adapter, editor, graph and two renderer backends. References should point at the corresponding projects in the table above.

```csharp
using netDxf;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;

var document = new DxfDocument();
var session = new ShadowSession(document);
var editor = new EditorSession(session);

var lineId = editor.CreateEntity(
    "netDxf.Entities.Line",
    new Dictionary<string, ShadowValue>
    {
        ["StartPoint"] = ShadowValue.From(new Vec3(0, 0)),
        ["EndPoint"] = ShadowValue.From(new Vec3(100, 0))
    });

var retained = session.Current;
editor.SetProperty(lineId, "EndPoint", ShadowValue.From(new Vec3(100, 30)));
editor.Undo();
editor.Redo();
editor.DeleteEntities(new[] { lineId });
editor.Undo(); // Structural and scalar commands use the same revision-checked history.

using var typography = new SkiaTextLayoutEngine();
var compiler = new RenderCompiler(textLayout: typography);
var snapshot = session.Current;
var scene = compiler.Compile(snapshot);
var topology = new TopologyBuilder().Build(snapshot);

// Dispose the host before its borrowed typography service.
using var host = new RenderHost(
    new Dictionary<string, Func<IRenderBackend>>
    {
        ["skia"] = () => new SkiaRenderBackend(textLayout: typography),
        ["svg"] = () => new SvgRenderBackend()
    },
    "skia");

var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 1200, 800));
File.WriteAllBytes("drawing.png", host.Render(frame).Bytes);
host.Swap("svg");
File.WriteAllBytes("drawing.svg", host.Render(frame).Bytes);

Console.WriteLine($"Components: {topology.ConnectedComponents().Length}");
Console.WriteLine($"Retained revision: {retained.Revision}; current: {snapshot.Revision}");
```

An isolated branch is `new ShadowMemoryStore(snapshot)`. It supports property and graph-patch editing without touching netDxf. There is no automatic three-way merge; callers must choose a conflict policy and validate expected values.

## Incremental processing and performance

`ShadowReadSet` records precisely which object properties were read during projection. Local geometry, expanded root occurrences and transformed occurrences can be reused across revisions while their dependencies remain current. Cacheable custom projectors expose an explicit revision; untracked extensions disable snapshot-level reuse rather than silently returning stale output.

Render geometry is cached by immutable geometry identity and sampling options. Unchanged occurrences reuse render items. A persistent spatial index is refit for changed bounds when item order is stable, preserving the old index for retained snapshots. Text services carry their own revision; it is part of text cache keys, but font invalidation does not invalidate non-text geometry. Call `SkiaTextLayoutEngine.InvalidateFonts()` when font resolution changes, then recompile. Changes during a compilation are detected and require a retry.

This is not an O(1) end-to-end edit guarantee. Full source capture remains proportional to the public graph/payload, structural adoption may require broader validation, occurrence assembly still visits root entries, and very dense topology can produce many intersections. Tests distinguish geometry rebuilding from unavoidable enumeration.

The 10,000-entity incremental test checks that one scalar edit rebuilds one geometry and reuses 9,999 render items, and prints edit allocations/timing and compilation timing. The original suite reports full capture/refresh and one million snapshot acquisitions. These are reproducible smoke workloads, not million-entity or universal cross-machine performance guarantees.

## Semantic geometry versus presentation

`CadGeometry.Contours` retains semantic curves for graph analysis. Optional `DisplayContours` replaces presentation strokes, while `Regions` contains independently painted areas. This prevents a wide polyline's outline from becoming extra connectivity edges and prevents hatch paint segments from replacing its semantic boundary.

`CadSceneBuilder` expands shared block occurrences with instance keys, OCS, block origins, nested transforms and inherited style. Nonuniform/mirrored transforms preserve analytic ellipses and transform width regions after offset construction. Cycles/depth/occurrence limits are bounded. `IEntityProjector` extends entity conversion without touching capture.

The supplied projections include common line/point/conic entities, bulged polylines, control-point NURBS, text/attributes, faces/meshes, cached dimension blocks, hatch boundaries and raster placements. Variable widths use analytic tapered strips with butt caps and bevel joins. Nonzero fill unions adjacent ribbons; exact CAD miter behavior is not certified.

Hatch line families use active-edge scan conversion after a shared Normal/Outer/Ignore boundary selection. Solid fills, gradient fills, native/SVG rendering and picking use the same selection, while graph consumers keep every semantic loop. Signed dash lengths retain leading gaps, zero-length dots and phase through clipped intervals. Rational/periodic spline boundaries, bulges and conic boundaries are supported. Pattern boundary approximation is 0.01 drawing units by default; row/segment/point/intersection budgets prevent runaway generation. Outer/Ignore classify a bounded, winding-independent containment forest. Ambiguous sampled crossings/contacts retain semantic boundaries with a diagnostic and no presentation paint, rather than using an incorrect even/odd fallback. See [HatchIslands.md](docs/HatchIslands.md) for APIs, numerical limits and source editing.

Gradient paints retain authored RGB stops, transform and shift, with shared deterministic linear/cylindrical/radial display plans for the nine gradient names. The normalized transfer functions and shift mapping are **not certified pixel-equivalent to AutoCAD**.

## Text services

`RichText` produces immutable source-associated runs and diagnostics in a bounded single pass. Supported formatting includes nested scopes, Unicode/escaped characters, paragraph breaks, nonbreaking spaces, font/bold/italic changes, height/width/oblique/tracking changes, inline vertical alignment, true color, common indexed colors, underline/overline/strikeout, horizontal/diagonal fractions and stacked tolerances.

`RichTextLayout` measures words/runs once rather than repeatedly measuring growing row prefixes. Words retain formatting boundaries without introducing artificial wrap points; explicit tracking operates on Unicode text elements. Runs without tracking remain intact for contextual shaping. Unbreakable words can overflow the requested width rather than splitting inside a shaping cluster.

`ITextLayoutEngine` supplies font metrics and layout results without putting native resources into snapshots. The portable fallback explicitly diagnoses approximate metrics. `SkiaTextLayoutEngine` uses HarfBuzz shaping, native ascent/descent and positioned glyph ink bounds. The compiler transforms those bounds into world space; decorations/fractions participate. The backend and SVG renderer consume the same immutable positions. SVG can still differ in actual glyph appearance if its browser resolves a different font. Text picking uses the compiled ink-block bounds, not per-glyph outlines.

Shared paragraph composition supports first/left/right indentation, alignment and word justification, left/center/right tabs, paragraph gaps and baseline spacing; see [Paragraphs.md](docs/Paragraphs.md). Fields are displayed literally and never executed. Automatic columns, decimal tabs, glyph-level distribution, full bidirectional paragraph itemization, arbitrary font fallback and mixed native/SHX runs remain extension work. Unsupported/malformed controls produce diagnostics instead of disappearing silently.

## Renderer ownership and image security

`IRenderBackend` is the runtime backend boundary. `RenderHost.Swap(...)` serializes with rendering and disposes the replaced backend. `SkiaRenderBackend.Draw(SKCanvas, frame)` also supports caller-owned CPU/GPU canvases. The camera origin is subtracted in double precision before conversion to backend floats. Native caches are bounded and are not stored in the core.

Raster geometry preserves independent authored U/V directions, shear and image clipping polygons. Both renderers support normal/inverse clip regions and brightness/contrast/fade display mapping. Picking tests the image parallelogram and clip, not just its bounding box. Display-adjustment transfer curves are not claimed to reproduce a proprietary CAD implementation exactly.

`ISkiaImageResolver` returns borrowed images. A changing `IVersionedSkiaImageResolver.Revision` invalidates native pictures; unversioned resolvers bypass picture retention. The host must coordinate resolver access and keep borrowed images alive through rendering. `ISvgImageResolver` returns explicit PNG/JPEG/WebP bytes; SVG image content is rejected, encoded image size is bounded, and no source path triggers implicit disk/network I/O. The host owns every supplied resolver and typography service. A backend disposes only the typography service it created itself.

## Graph and editing

Topology uses a packed spatial index, explicit endpoint/curve tolerances, intersection/overlap splitting, optional elevation compatibility and union-find welding. Results retain source entity and occurrence identities. Algorithms include connected components, cycle rank, junctions/dead ends, Dijkstra shortest paths and iterative Tarjan bridges with parallel-edge handling. Curved intersections use sampled curves, not certified analytic predicates.

`EditorSession` supports property commands, entity creation/deletion, collection replacement, structural graph patches and unified undo/redo. Canonical snapshots are retained for structural commands. Intervening external source revisions invalidate history and require an explicit reset. Moving an inserted selection targets the outermost insert by default; definition editing changes shared geometry. Unsupported planners/factories fail rather than silently mutating unrelated data.


## Paper-space views and stroke resources

`RenderCompileOptions.IncludePaperViewports` defaults to true. For paper layouts,
the render compiler composes orthographic model views, preserving entity/instance
identity, camera transforms and shared clipping masks. Frozen layers, front/back
clipping, global `LtScale` and `PsLtScale` are supported. Rendering and picking use
the same masks. Paper copies and viewport frames never duplicate semantic graph
edges. A paper-space drag is mapped back to model/source coordinates; conflicting
moves of the same source through different views are rejected before mutation.
See [PaperViewports.md](docs/PaperViewports.md) for camera conventions and limits.

`ShxFontReader` reads SHAPES 1.0/1.1 and UNIFONT 1.0 binaries and textual SHP
instructions. `ShxGlyphCompiler` emits immutable analytic curves; `ShxEntityProjector`
uses them for `Shape`, `Text`, `MText` and attributes. Text is projected as
presentation-only `CadStrokeGroup` geometry with per-run paints and decoration
regions. Actual SHAPE entities retain semantic curves for graph analysis.
Skia and SVG consume the same sampled stroke geometry, including in paper views.

External resources are separate from the source snapshot. `ShxFontCatalog.Current`
is an immutable resource snapshot; pass it to `ShxEntityProjector` to pin font
content. The Studio pins its SHX font set with each retained source revision, so
replacing live resources cannot change the old SHX view. There is no implicit
file/network lookup or font-byte embedding. See [Shx.md](docs/Shx.md) for APIs,
limits, character mapping and format references.

## Verification and remaining specialized coverage

The existing consolidated build workflow runs Shadow verification on Linux, Windows and macOS. It does not add a third workflow or relax the existing release/conformance gates. The current suites contain 18 original tests and 335 advanced tests, including 76 hatch-island cases. Appearance checks include native image pixels, fill-aware picking, rational boundaries, all gradient display plans, dash phases, typography bounds, cache invalidation, borrowed-resource lifetime, XML escaping, and the gallery's structural edit/save/reload path. CI uploads generated PNG/SVG/DXF artifacts.

Capture coverage, editable-source coverage and render fidelity are separate contracts. Full specialized CAD parity remains unfinished: BIGFONT and vertical SHX paragraph layout, custom geometry and ACIS evaluation, complete paragraph/field typography, complex text/shape linetypes and all global scaling semantics, exact miter/gradient behavior and certified analytic island predicates, perspective viewport projection, complete viewport visual/layer overrides, complete dimension regeneration, arbitrary affine edit planners and specialized object factories. Metadata remains in the raw shadow graph even when a default consumer cannot interpret it.

Studio is local and single-user. It binds loopback, checks Host, requires a custom mutation header and does not enable CORS or arbitrary external resources. It is not a hardened untrusted-file service. Apply input/resource limits and an explicit trust policy before hosting it elsewhere.
