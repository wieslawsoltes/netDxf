# Graph workspace, visualization and analysis

Open the **Graph** ribbon or enter `GRAPH` / `TOPOLOGY`. **Visualize topology** loads source-linked geometry into the drawing; the Graph palette stays independent of Properties. Color connected components or topology features, show node/edge IDs, dim the underlying drawing, and fit the loaded graph. **Inspect in drawing** picks a node or edge without changing selection. Edge inspection identifies its leaf entity, occurrence and root INSERT; **Select source in drawing** explicitly selects that root for native Properties/editing. No source selection occurs just by loading or highlighting a graph.

The Graph ribbon exposes every topology operation below. Choose an operation, set its inputs and run analysis. Paths support **Pick start node** and **Pick end node** directly in the viewport. Escape cancels graph picking without clearing drawing selection. Component results load their visual projection; cycle results open their ordered geometry. Paths, traversal, node/edge and feature queries, cycles and spanning forests can highlight and fit their loaded results. JSON exports retain the loaded results, source identity, options and coverage. SVG exports retain the rendered subset, object/edge titles and descriptive coverage; they do not embed the complete document/workspace/revision/build identity.

A projection loads up to 2,000 nodes and 4,000 edges per request, shortened automatically to fit the host response budget. **Load more topology** follows the actual continuation cursors. **Load remaining topology (bounded)** follows up to 40 continuation pages per activation; repeat to continue, within the browser workspace's 10,000-node / 20,000-edge display limits. Clear overlay, hiding the palette or changing its inputs cancels pending continuation. Node and edge pages are independent; each edge carries endpoint coordinates even when its nodes are on another page. Component/entity/root entity/occurrence filters keep global graph IDs. Counts clearly distinguish loaded geometry from matching totals and partial coverage. A bounded full-result action loads at most 4,000 items / 40 pages; result cards display at most 200 items. Traversal keeps its own explicit visit/depth limits. Labels are bounded to 300 visible items. The overview thumbnail is limited to 600 nodes / 1,200 edges; the main viewport uses all loaded geometry. Tighten filters to inspect large drawings. Geometric topology is an XY visual projection of sampled semantic curves, not a discipline connectivity model or a 3D graph layout.

Analysis uses the current document, workspace, layout, block definition and font resources. Return from a retained snapshot to Current drawing before querying. Drawing edits, undo, view/layout/workspace changes and topology-option changes invalidate graph results; selection-only changes preserve geometry. Pending responses and source-selection actions additionally check the full current Studio identity. Implicit auto-hide waits while graph picking owns input. Explicit close or Escape cancels the pick; hiding the palette invalidates pending UI responses while retaining reusable data for reopening.

**View → Topology overlay** toggles the same bounded graph projection, with explicit full counts/truncation. Clearing the overlay fences pending reads and remains cleared when the palette reopens; Show overlay restores loaded geometry. The legacy read-only graph endpoint remains available to existing API consumers. See [Workspace layout](WorkspaceLayout.md) for simultaneous palettes and ribbon controls.

| Tool | Result and inputs |
| --- | --- |
| `graph_visualization` | Independent `nodeOffset`/`nodeLimit` (default 2,000, max 5,000) and `edgeOffset`/`edgeLimit` (default 4,000, max 10,000); optional `component`, `entity`, `rootEntity`, `occurrence`. Geometry includes components, cut/degree features, source roots and INSERT paths. Full graph summary counts accompany filtered page totals. |
| `graph_summary` | Counts, components, independent cycle rank, bridges, articulation points, dead ends, junctions, isolated nodes, total sampled length and diagnostic count. |
| `graph_nodes` | Coordinates and degrees; optional `node`, `component`, `minimumDegree`, `maximumDegree`; `offset`/`limit`. |
| `graph_edges` | Endpoints, length, source `entity`, `rootEntity`, `insertPath` and `occurrence`; optional `edge`, `node`, `component`, `entity`, `rootEntity`, `occurrence`; `offset`/`limit`. |
| `graph_components` | Stable component IDs, node/edge counts, lengths and cycle ranks; `offset`/`limit`. Query node/edge pages with a component ID for its members. |
| `graph_cycles` | Page an independent fundamental cycle basis: cycle ID, closing edge, ancestor/start node and edge count. |
| `graph_cycle` | Required `cycle` ID; ordered closed path, full length and paged `steps`, including the closing edge and repeated first node. |
| `graph_spanning_forest` | Minimum total sampled-length spanning forest; component count, total length and paged source-linked edges. Disconnected and isolated components are retained. |
| `graph_features` | Required `kind`: `deadEnds`, `junctions`, `isolated`, `bridges`, `articulationPoints`, `diagnostics`; `offset`/`limit`. |
| `graph_nearest` | Required `x`, `y`; optional `z`, `maximumDistance`. Nearest graph node in XYZ, or XY when elevation is ignored; ties choose the lowest ID. |
| `graph_path` | Required node `source`/`target`; weighted shortest path with `reachable`, complete length and counts, paged ordered `steps`. Each continuation retains its incoming edge. |
| `graph_traverse` | Required `source`; `depth` (default 8, at most 128), `limit` (default 100, at most 200); breadth-first visits and predecessor edges. Separate node/depth limit flags identify incomplete traversal. |

All tools accept an optional `graphKey` guard and `layout` (the active Studio layout, or Model in a standalone host), `endpointTolerance` (0.01), `curveTolerance` (0.05), `splitIntersections` (true), `respectElevation` (true), `maximumSegments` (100,000, at most 250,000), and `maximumCandidatePairs` (2,000,000, at most 20,000,000). Analysis pages default to 50 results and allow up to 200; use `nextOffset` until null. `graph_visualization` automatically shortens its node and edge pages to fit the host registry's serialized receipt budget (131,072 characters by default). Returned `nodeLimit`/`edgeLimit` report the effective caps; follow each returned `nextOffset`, not the requested page size. Counts, source links and graph keys remain intact. Each unfinished stream emits at least one item; a minimum page that still cannot fit returns `result_too_large` rather than a truncated success. Other analysis tools may require a smaller page size when the registry's result limit is reached. Build budget exhaustion returns an error and never publishes a partially constructed graph.

Graph IDs are scoped to a document/workspace revision, layout and complete option set. Preserve the returned `data.graphKey`, `documentId`, `workspaceId`, `viewVersion`, `revision`, `data.layout` and `data.options` for subsequent calls. The graph key additionally binds the exact cached topology build, options and view, so IDs from another build cannot silently refer to different nodes. Supplying stale identity fields returns a conflict; a mismatched graph key returns a tool error. Rebuild/discover IDs after edits, undo, layout or workspace changes. Topology reads never change selection, source data or undo history.

Edges represent sampled **semantic curves**, not text glyphs, linetype decorations, dimension strokes or other presentation-only geometry. Invisible occurrences are excluded. Curves and intersections are approximated using the chosen curve tolerance; lengths are sums of sampled 3D segments. Ignoring elevation merges/projects node positions into XY while edge lengths remain sampled source lengths. The graph is undirected and retains parallel edges and self loops. Independent cycle rank is `edges − nodes + components`; it does not enumerate every simple cycle. An articulation point disconnects a component when removed; a bridge disconnects it when that edge is removed.

`TopologyGraph.CycleBasis()` builds a reusable linear-space, iterative DFS index. It
retains one cycle per non-tree edge and expands an ordered closed path only when
`GraphCycleBasis.Path(id)` is called. This avoids materializing the potentially
quadratic number of cycle-edge memberships for dense graphs. Cycles include
one-edge self loops and two-edge parallel loops. IDs follow closing-edge order;
the basis is deterministic for the same graph but is not a minimum cycle basis or
an enumeration of every simple cycle. The palette lists cycles and opens each
cycle's paged geometry for highlighting.

`TopologyGraph.MinimumSpanningForest()` uses Kruskal's algorithm with sampled
length as weight and edge ID as the stable tie breaker. It excludes self loops
and chooses among parallel edges. Its result is an analysis overlay; it does not
delete drawing geometry or infer a physical piping/electrical design. Both
algorithms support cancellation and preserve source and history.

The palette exposes exact node, edge, component, source entity and occurrence
filters. Editing any query parameter invalidates cached and in-flight results,
so responses computed with earlier tolerances cannot replace the current result.

The palette's `POST /api/cad/{id}/graph/query` endpoint accepts `{ "tool": "graph_summary", "arguments": { ... } }`, checks the regular Studio identity headers, and permits only read-only `graph_*` tools. Its detached envelope is `{ "isError": false, "result": <MCP receipt> }`. Native and browser hosts share the implementation. `ShadowMcpContext.Layout` and `Topology` allow custom hosts to preserve their scene projection resources.

Verification (offline, no API credentials required):

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Graph.Verification
node --test shadow/tests/browser/graph*.test.mjs
```

The executable checks topology counts, multi-edge/self-loop cut analysis, articulation points against independent vertex-removal connectivity, cycle independence by GF(2) elimination, minimum spanning forests against exhaustive subset enumeration, 20,000-node chains and cycles, bounded traversal/path pagination, source linkage, invalid inputs/build budgets, cancellation, branch/edit/undo cache invalidation and Studio endpoint isolation. Browser tests cover request schemas, view receipts, query races, cycle/forest drill-down and sparse node-ID overlays.

`TopologyGraph.Visualize(GraphVisualizationOptions)` is a reusable library API in `netDxf.Shadow.Graph`. Its immutable pages include global feature annotations and exact source occurrence links, support cancellation, and cache the topology's component/cut index through a weak reference. `graph-visualization.mjs` is renderer-independent: normalization, bounded projection merging, styles, legend, screen-space hit testing and SVG export are shared by the Studio UI. `graph-ui.mjs` binds those helpers to host callbacks; native and WASM use the same modules and query route.

The shared real-browser graph fixture exercises actual projection and SVG/JSON export, endpoint picking, path source selection, cycle/forest highlights and option invalidation on both native and WASM hosts. Native HTTP checks exercise graph keys, missing/wrong identity, independent paging and budget rejection. No provider credentials or paid model calls are required.

## Source object references

**Graph → Object references**, the **References** palette, or `REFGRAPH` opens a
directed source-object graph. This complements geometric topology: a line's Layer
reference, an INSERT's block definition and collection membership are object
relationships rather than pipes or sampled curves. Start from the document root,
entered object IDs, selected drawing roots or a discovered layer/block/object.
Choose outgoing dependencies or incoming references. Arrows always point from
the owning source property to its target, including during incoming exploration.
Property paths use JSON Pointer escaping and retain collection indices, so parallel
references are individually inspectable.

`ReferenceGraphIndex` in `netDxf.Shadow.Graph` builds an immutable, cancellable
reference index. `Explore(ReferenceGraphOptions)` performs bounded breadth-first
exploration, retains cycles, self references and explicit missing targets, and
reports node, edge and depth limits independently. Defaults are depth 2, 100 nodes
and 500 edges; supported maxima are depth 16, 1,000 nodes and 4,000 edges. The index
refuses more than 100,000 objects or 2,000,000 captured values by default, and
bounds queued plus retained path text to 16 Mi characters (8,192 per path). These
index limits fail explicitly; exploration limits return a clearly partial result.

The read-only MCP `graph_reference_graph` tool and existing Studio graph-query
route share the implementation. Arguments are `roots` (up to 200 object IDs,
default document root), `direction` (`outgoing`/`incoming`), `depth`,
`maximumNodes`, `maximumEdges`, independent `nodeOffset`/`nodeLimit` and
`edgeOffset`/`edgeLimit`, plus optional `referenceKey`. Each continuation must retain
the roots, direction, budgets and key. The key binds the exact captured snapshot,
document, workspace and view. Paging adapts to the serialized host response budget.
Node/edge totals describe the bounded exploration; they are not full-document
counts. Source reads and diagram navigation do not create history entries.

The reusable `reference-graph-model.mjs` and `reference-graph-ui.mjs` provide the
same explorer in native and WASM Studio. The diagram limits itself to 120 nodes
and 400 references; lists and JSON use the loaded exploration. SVG explicitly
describes its displayed subset. Selection is an explicit action available only
for exact active drawing roots present in the current bounded scene listing.
Nested definition objects and resources remain inspectable without pretending
they are directly selectable drawing occurrences.

The References palette also exposes three directed analyses, with shortcuts in
the Graph ribbon and command search:

- **Cyclic dependency groups** partitions the bounded exploration into strongly
  connected components and reports references between components. A singleton
  with a self-reference is cyclic. Ownership and back-references often form
  legitimate cycles; this is structural information, not a drawing defect report.
  A partial exploration can split a larger component, so it cannot establish
  global acyclicity or maximal groups.
- **Dependency path** finds a path with the fewest references between two source
  objects, either outgoing or incoming. Every step retains its exact property
  path and stored source-to-target orientation. Results distinguish Found,
  Unreachable and Incomplete. A budget-exhausted search never claims absence or
  skips an earlier candidate and labels a longer route shortest.
- **Reference impact** follows incoming links from one or more seed objects.
  Each visit records a predecessor and exact property link. Inspecting a visit
  expands a shortest chain back to a seed, explaining why the object was reached.
  This is structural reachability; it does not simulate changes or predict
  engineering effects.

The reusable APIs are `ReferenceGraphIndex.StronglyConnectedComponents`,
`ShortestPath` and `Impact`. SCC analysis is iterative; impact stores a predecessor
forest rather than a separate full path for every visited node. The MCP tools
`graph_reference_components`, `graph_reference_path` and `graph_reference_impact`
use the same snapshot identity and response-budget contracts as exploration.
Queries run against the captured source index rather than the currently loaded
diagram page. Inputs, drawing changes and cancelled requests invalidate displayed
results. JSON exports retain the analysis, source identities, inputs and coverage
limits. SVG exports describe the rendered subset and coverage; a condensation
SVG uses component IDs rather than individual source-object identities.

Path queries require `source` and `target` object IDs and accept `direction`
(`outgoing` or `incoming`). Component queries accept `roots` and `direction`;
impact queries accept `roots` and always traverse incoming references. All three
support `depth`, `maximumNodes` and `maximumEdges`. Path defaults are depth 16,
1,000 nodes and 4,000 examined edges; component/impact defaults are depth 2,
100 nodes and 500 edges.

Analysis responses have four independent page streams: `nodes`, `edges`,
`results` (component summaries) and `links` (condensation edges). Continue each
using its returned `nextOffset` as `nodeOffset`, `edgeOffset`, `resultOffset` or
`linkOffset`, preserving inputs and `referenceKey`. Corresponding limits are
`nodeLimit`/`resultLimit` (default 100, maximum 200) and
`edgeLimit`/`linkLimit` (default 200, maximum 500). The host may shorten each stream
to fit its serialized response budget. Path and impact still return the empty
`results` and `links` streams. Exhausted streams use their total as the offset
while other streams continue.

## Discipline connectivity

Each enabled discipline's ribbon and Modules palette exposes **Connectivity**.
The network follows explicit named ports, declared external connections and
internal passages. Geometric crossings do not create connectivity. Associations
are reported separately. Only tagged roots in the active layout and selected
discipline are traversed; references to other modules remain scope boundaries.

`DomainConnectivity` in `netDxf.Shadow.Symbols` builds an immutable bounded graph
with weak components, minimum-link paths and directional traces. Every non-root reached
port has a predecessor connection for inspection. `DomainPassage` declarations
specify a pair of ports and `both`, `forward` or `reverse`. Legacy records with no
passage declarations retain separate terminals and report incomplete declarations.
Known-open boundaries remain explicit. This describes the authored schematic;
it does not calculate pressure, flow, switching states or electrical protection.

The shared UI lists ports, component membership, incident connections and
findings. It supports source/target selection, predecessor inspection, explicit
source-object selection and bounded JSON/SVG/CSV exports. The SVG is a schematic
layout of at most 180 loaded ports and 360 links; it is not the drawing geometry.
Exports report scan, declaration, finding and paging coverage separately.
The schematic retains readable label sizes and scrolls within narrow palettes.
Direction arrows end at port-card borders, and a fresh analysis clears the
previous path highlight. Open a wider floating Modules palette to inspect the
schematic beside a port's source and predecessor details.

Native and standalone WASM expose `POST /api/cad/{id}/modules/connectivity`.
The read-only MCP tool `studio_module_connectivity` uses the same implementation;
`studio_request_schema` exposes `CadModuleConnectivityRequest`. Supply the exact
revision and identity, module, operation (`network`, `path`, `trace`, `structure`, `isolation`), source and
optional target port ID, direction, scan limits, visit/depth bounds and tolerance.
Disabled modules and block-editor scopes are rejected.

Five streams (`nodes`, `edges`, `issues`, `components`, `visits`) have independent
cursors. Continue with each stream's actual `nextOffset` (or its total when
exhausted), retaining `graphKey` and the original query. Receipt size can shorten
individual pages. The key binds snapshot, workspace, view, layout, module
configuration and all query options; changing any of these requires a fresh
query. Partial graph/search coverage cannot establish absence of a connection.

**Network structure** in each discipline ribbon opens the structure operation.
Run it to inspect strongly connected groups (mutual directed reachability),
their acyclic condensation, weak cut links and articulation ports. A cut means
removing a connection or port separates the retained graph when direction is
ignored. Missing declarations can hide alternate routes; these classifications
do not measure operational risk or simulate equipment failure.

Loop evidence contains exact port and connection IDs. Each proof closes in the
declared direction and uses distinct declared connection IDs; reversing the same
bidirectional connection is not a loop. The search seeks at most one witness per
strong group, not every simple cycle. Defaults allow 20 witnesses, 4,000 proof
steps and 100,000 search operations; maxima are 100, 10,000 and 1,000,000.
Search, witness and proof limits are reported separately from source coverage
and unloaded pages. Loading pages retrieves retained evidence; increasing a
search bound requires running the analysis again.

Structure adds six independent streams: `strongComponents`, `portStructures`,
`condensation`, `criticalConnections`, `loops` and `loopSteps`. Their offset
fields are `strongComponentOffset`, `portStructureOffset`, `condensationOffset`,
`criticalConnectionOffset`, `loopOffset` and `loopStepOffset`. Keep the original
`structureOptions` and graph key for every continuation. Flat membership and
proof streams keep a large component or loop from exceeding one response.

The palette provides group/connection/loop inspectors, explicit source
selection, port and condensation diagrams, JSON, SVG and CSV exports.
`IndexedGraphAnalysis` in the reusable Graph package supplies the shared
iterative SCC and low-link kernels used by source-reference and discipline
analysis. It accepts up to 50,000 nodes, 100,000 physical connections or 200,000
directed arcs, with multigraph and cancellation support.


## Explicit isolation studies

**Isolation study** in a discipline ribbon opens a before/after reachability
comparison. Choose a source port, optional target and traversal direction, then
exclude declared connection IDs or port IDs. Inspectors can add these assumptions
without editing the drawing. Analysis compares the same captured graph before and
after the exclusions; it does not infer valve positions, equipment failures,
pressure, current or physical isolation safety.

`DomainConnectivity.AnalyzeIsolation` returns exact minimum-link predecessor
proofs, reached/lost/unreachable/unknown/excluded classifications and independent
before/after limits. Found routes remain evidence under a partial scan; absence
or loss requires complete graph declarations and traversal. Alternative retained
routes are shown with their actual connections. Excluding the source is invalid;
an excluded target is reported separately from an unreachable target.

Defaults permit 10,000 visits, depth 256 and 200,000 examined directed edges per
traversal; maxima are 50,000, 1,000 and 400,000. At most 2,000 combined exclusions
and 128,000 exclusion characters are accepted. Four more independent streams
(`assessments`, `beforeVisits`, `afterVisits`, `exclusions`) bring connectivity to
15 streams. Continue their actual cursors with the original graph key, query and
assumptions. The shared UI offers comparison filtering, target/per-port proofs,
explicit source selection and JSON, CSV and SVG exports. These read-only studies
never change drawing geometry, discipline metadata or undo history.
