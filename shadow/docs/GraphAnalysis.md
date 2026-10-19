# Topology analysis in Studio and MCP

Studio's **View → Graph analysis** palette (also the `GRAPH` / `TOPOLOGY` command) exposes the same read-only topology toolset as the Agents and MCP catalogs. Select an analysis, adjust its inputs, and run it. Results are paged. Node results can set the start/end IDs for shortest paths or traversal; component results open their members. Highlight a result page in the drawing, or focus an individual node. Graph analysis uses the current workspace, layout, block definition and font resources. Return from a retained snapshot to Current drawing before querying.

The existing topology overlay remains available under **View → Topology overlay**. Its receipt now includes full node/edge counts and an explicit truncation flag. Clipped edges whose endpoints are outside the overlay are omitted. The graph palette and MCP pages provide access to the complete built graph within the configured build budgets.

| Tool | Result and inputs |
| --- | --- |
| `graph_summary` | Counts, components, independent cycle rank, bridges, articulation points, dead ends, junctions, isolated nodes, total sampled length and diagnostic count. |
| `graph_nodes` | Coordinates and degrees; optional `node`, `component`, `minimumDegree`, `maximumDegree`; `offset`/`limit`. |
| `graph_edges` | Endpoints, length, source `entity` and `occurrence`; optional `edge`, `node`, `component`, `entity`, `occurrence`; `offset`/`limit`. |
| `graph_components` | Stable component IDs, node/edge counts, lengths and cycle ranks; `offset`/`limit`. Query node/edge pages with a component ID for its members. |
| `graph_features` | Required `kind`: `deadEnds`, `junctions`, `isolated`, `bridges`, `articulationPoints`, `diagnostics`; `offset`/`limit`. |
| `graph_nearest` | Required `x`, `y`; optional `z`, `maximumDistance`. Nearest graph node in XYZ, or XY when elevation is ignored; ties choose the lowest ID. |
| `graph_path` | Required node `source`/`target`; weighted shortest path with `reachable`, complete length and counts, paged ordered `steps`. Each continuation retains its incoming edge. |
| `graph_traverse` | Required `source`; `depth` (default 8, at most 128), `limit` (default 100, at most 200); breadth-first visits and predecessor edges. Separate node/depth limit flags identify incomplete traversal. |

All tools accept `layout` (the active Studio layout, or Model in a standalone host), `endpointTolerance` (0.01), `curveTolerance` (0.05), `splitIntersections` (true), `respectElevation` (true), `maximumSegments` (100,000, at most 250,000), and `maximumCandidatePairs` (2,000,000, at most 20,000,000). Paged tools default to 50 results and allow up to 200; use `nextOffset` until null. Reduce a page size if the registry's overall serialized result limit is reached. Build budget exhaustion returns an error and never publishes a partially constructed graph.

Graph IDs are scoped to a document/workspace revision, layout and complete option set. Preserve the returned `documentId`, `workspaceId`, `viewVersion`, `revision`, `data.layout` and `data.options` for subsequent calls. Supplying stale identity fields returns a conflict. Rebuild/discover IDs after edits, undo, layout or workspace changes. Topology reads never change selection, source data or undo history.

Edges represent sampled **semantic curves**, not text glyphs, linetype decorations, dimension strokes or other presentation-only geometry. Invisible occurrences are excluded. Curves and intersections are approximated using the chosen curve tolerance; lengths are sums of sampled 3D segments. Ignoring elevation merges/projects node positions into XY while edge lengths remain sampled source lengths. The graph is undirected and retains parallel edges and self loops. Independent cycle rank is `edges − nodes + components`; it does not enumerate every simple cycle. An articulation point disconnects a component when removed; a bridge disconnects it when that edge is removed.

The palette's `POST /api/cad/{id}/graph/query` endpoint accepts `{ "tool": "graph_summary", "arguments": { ... } }`, checks the regular Studio identity headers, and permits only read-only `graph_*` tools. Its detached envelope is `{ "isError": false, "result": <MCP receipt> }`. Native and browser hosts share the implementation. `ShadowMcpContext.Layout` and `Topology` allow custom hosts to preserve their scene projection resources.

Verification (offline, no API credentials required):

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Graph.Verification
node --test shadow/tests/browser/graph-analysis.test.mjs
```

The executable checks topology counts, multi-edge/self-loop cut analysis, articulation points against independent vertex-removal connectivity, a 20,000-node chain, bounded traversal/path pagination, source linkage, invalid inputs/build budgets, cancellation, branch/edit/undo cache invalidation and Studio endpoint isolation. Browser tests cover request schemas, view receipts and sparse node-ID overlays.
