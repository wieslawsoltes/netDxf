import { viewKey } from '../studio-state.mjs';

export const graphOperations = Object.freeze({
  summary: 'Summary', nodes: 'Nodes', edges: 'Edges', components: 'Components', deadEnds: 'Dead ends',
  junctions: 'Junctions', bridges: 'Bridges', articulationPoints: 'Articulation points', isolated: 'Isolated nodes',
  path: 'Shortest path', traverse: 'Traversal', nearest: 'Nearest node', diagnostics: 'Diagnostics',
  cycles: 'Independent cycles', cycle: 'Inspect cycle', spanning_forest: 'Minimum spanning forest'
});
const features = new Set(['deadEnds','junctions','bridges','articulationPoints','isolated','diagnostics']);
export function graphRequest(operation, values, state, offset = 0) {
  if (!Object.hasOwn(graphOperations, operation)) throw new Error('Unknown graph operation.');
  if (!state) throw new Error('Load a drawing before analyzing topology.');
  const args = { documentId:state.documentId, workspaceId:state.workspaceId, viewVersion:state.viewVersion, revision:state.revision, layout:state.layout };
  const numeric = (name, fallback, minimum, maximum, integer = false) => {
    const raw = values[name] ?? fallback; if (raw === '' || raw === undefined) return;
    const value = Number(raw);
    if (!Number.isFinite(value) || value < minimum || value > maximum || integer && !Number.isSafeInteger(value)) throw new Error(`Invalid ${name}.`);
    args[name] = value;
  };
  numeric('endpointTolerance', .01, Number.MIN_VALUE, Number.MAX_VALUE);
  numeric('curveTolerance', .05, Number.MIN_VALUE, Number.MAX_VALUE);
  numeric('maximumSegments', 100000, 1, 250000, true);
  numeric('maximumCandidatePairs', 2000000, 1, 20000000, true);
  args.splitIntersections = values.splitIntersections !== false; args.respectElevation = values.respectElevation !== false;
  if (!['summary','nearest'].includes(operation)) numeric('limit', 50, 1, 200, true);
  if (!['summary','nearest','traverse'].includes(operation)) args.offset = offset;
  if (['nodes','edges'].includes(operation)) { numeric('component', undefined, 0, 2147483647, true); numeric('node', undefined, 0, 2147483647, true); }
  if (operation === 'nodes') { numeric('minimumDegree', undefined, 0, 2147483647, true); numeric('maximumDegree', undefined, 0, 2147483647, true); }
  if (operation === 'edges') {
    numeric('edge', undefined, 0, 2147483647, true);
    if (values.entity?.trim()) args.entity = values.entity.trim();
    if (values.occurrence?.trim()) args.occurrence = values.occurrence.trim();
  }
  if (operation === 'cycle') numeric('cycle', 0, 0, 2147483647, true);
  if (operation === 'path' || operation === 'traverse') numeric('source', 0, 0, 2147483647, true);
  if (operation === 'path') numeric('target', 0, 0, 2147483647, true);
  if (operation === 'traverse') numeric('depth', 8, 0, 128, true);
  if (operation === 'nearest') { for (const axis of ['x','y','z']) numeric(axis, 0, -Number.MAX_VALUE, Number.MAX_VALUE); numeric('maximumDistance', undefined, 0, Number.MAX_VALUE); }
  if (features.has(operation)) args.kind = operation;
  return { tool:`graph_${features.has(operation) ? 'features' : operation}`, arguments:args };
}
export function graphData(response, state) {
  if (!response) return null;
  if (response.isError) throw new Error(response.result?.error?.message || 'Graph analysis failed.');
  const receipt = response.result;
  if (!receipt?.data) throw new Error('Incomplete graph result. Reduce the page size and try again.');
  if (receipt.documentId !== state.documentId || receipt.workspaceId !== state.workspaceId || receipt.viewVersion !== state.viewVersion || receipt.revision !== state.revision || receipt.data.layout !== state.layout)
    throw new Error('The drawing changed during graph analysis. Run the query again.');
  return receipt.data;
}
/** A page can contain non-contiguous IDs; never interpret graph IDs as page array offsets. */
export function graphOverlay(data) {
  const nodes = new Map(), edges = new Map();
  const addNode = node => { if (node && Number.isInteger(node.id) && Number.isFinite(node.x) && Number.isFinite(node.y)) nodes.set(node.id, node); };
  const addEdge = edge => { if (!edge || !Number.isInteger(edge.a) || !Number.isInteger(edge.b)) return; edges.set(edge.id,edge); addNode({...edge.start,id:edge.a}); addNode({...edge.end,id:edge.b}); };
  for (const item of data.steps?.items || data.items || data.nodes || []) {
    addNode(item.node || item); addEdge(item.viaEdge || item);
  }
  addNode(data.node);
  return {nodes:[...nodes.values()],edges:[...edges.values()],highlight:true};
}
export const graphViewKey = state => viewKey(state);
