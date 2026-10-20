/** Source-reference diagrams are directed object relationships, never geometric topology. */
export const referenceLimits = Object.freeze({nodes:1000,edges:4000,pages:40,diagramNodes:120,diagramEdges:400,diagramColumns:17});
export const referenceIdentity = state => state ? JSON.stringify([state.serverId,state.documentId,state.workspaceId,state.viewVersion,state.revision]) : '';
export const referenceEdgeKey = edge => JSON.stringify([edge.source,edge.path,edge.target]);
export function referenceRequest(values,state,scene=null) {
  if(!state)throw new Error('Load a drawing before exploring references.');
  const roots=String(values.roots||'').split(/[\s,]+/).filter(Boolean);
  if(roots.length>200||roots.some(id=>id.length>512))throw new Error('Use at most 200 source object IDs.');
  const args={documentId:state.documentId,workspaceId:state.workspaceId,viewVersion:state.viewVersion,revision:state.revision,
    direction:values.direction||'outgoing',nodeLimit:100,edgeLimit:200};
  if(!['incoming','outgoing'].includes(args.direction))throw new Error('Unknown reference direction.');
  if(roots.length)args.roots=[...new Set(roots)];
  for(const [field,fallback,min,max] of [['depth',2,0,16],['maximumNodes',100,1,1000],['maximumEdges',500,1,4000]]){
    const n=Number(values[field]??fallback);if(!Number.isSafeInteger(n)||n<min||n>max)throw new Error(`Invalid ${field}.`);args[field]=n;
  }
  if(scene)Object.assign(args,{referenceKey:scene.referenceKey,nodeOffset:scene.nextNode??scene.nodeCount,edgeOffset:scene.nextEdge??scene.edgeCount});
  return {tool:'graph_reference_graph',arguments:args};
}
export function referenceData(response,state) {
  if(response?.isError)throw new Error(response.result?.error?.message||'Reference graph query failed.');
  const receipt=response?.result;
  if(!receipt?.data)throw new Error('Incomplete reference graph receipt.');
  for(const key of ['documentId','workspaceId','viewVersion','revision'])if(receipt[key]!==state[key])throw new Error('The drawing changed. Explore references again.');
  return receipt.data;
}
export function mergeReferenceGraph(previous,data) {
  if(typeof data?.referenceKey!=='string'||!data.referenceKey)throw new Error('Missing reference graph identity.');
  if(previous&&previous.referenceKey!==data.referenceKey)throw new Error('The reference scope changed between pages.');
  const nodes=new Map((previous?.nodes||[]).map(node=>[node.id,node])),edges=new Map((previous?.edges||[]).map(edge=>[referenceEdgeKey(edge),edge]));
  for(const [page,last,total,cap] of [[data.nodes,previous?.nextNode,previous?.nodeCount,referenceLimits.nodes],[data.edges,previous?.nextEdge,previous?.edgeCount,referenceLimits.edges]]){
    if(!page||!Array.isArray(page.items)||!Number.isSafeInteger(page.total)||page.total<0||page.total>cap||!Number.isSafeInteger(page.offset)||page.offset<0||
       page.offset+page.items.length>page.total||(total!==undefined&&total!==page.total)||(previous&&page.offset!==(last??total))||(!previous&&page.offset!==0)||
       (page.nextOffset!=null&&(!Number.isSafeInteger(page.nextOffset)||page.nextOffset<=page.offset||page.nextOffset!==page.offset+page.items.length||page.nextOffset>=page.total)))
      throw new Error('Invalid reference graph page cursor or count.');
  }
  for(const node of data.nodes.items){
    if(typeof node.id!=='string'||!node.id||!Number.isSafeInteger(node.depth)||node.depth<0||node.depth>16)throw new Error('Invalid reference node.');
    nodes.set(node.id,node);
  }
  for(const edge of data.edges.items){
    if(!['source','path','target'].every(k=>typeof edge[k]==='string'&&edge[k])||!edge.path.startsWith('/'))throw new Error('Invalid reference edge.');
    edges.set(referenceEdgeKey(edge),edge);
  }
  if(nodes.size>data.nodes.total||edges.size>data.edges.total)throw new Error('Reference pages contain inconsistent identities.');
  const partial=nodes.size<data.nodes.total||edges.size<data.edges.total;
  if(partial&&data.nodes.nextOffset==null&&data.edges.nextOffset==null)throw new Error('Reference pages ended before the reported totals.');
  return {...data,nodes:[...nodes.values()],edges:[...edges.values()],nodeCount:data.nodes.total,edgeCount:data.edges.total,
    nextNode:data.nodes.nextOffset??null,nextEdge:data.edges.nextOffset??null,partial};
}
export function referenceLayout(scene,selected=null) {
  const columns=new Map(),nodes=[...scene.nodes].sort((a,b)=>Number(b.id===selected||!!b.highlighted)-Number(a.id===selected||!!a.highlighted)).slice(0,referenceLimits.diagramNodes).map(node=>{
    const column=Math.min(node.depth,referenceLimits.diagramColumns-1),row=columns.get(column)||0;columns.set(column,row+1);
    return {...node,column,x:24+column*250,y:24+row*70,width:210,height:48,selected:node.id===selected};
  });
  const ids=new Set(nodes.map(n=>n.id)),edges=scene.edges.filter(e=>ids.has(e.source)&&ids.has(e.target)).slice(0,referenceLimits.diagramEdges);
  return {nodes,edges,width:Math.max(260,...nodes.map(n=>n.x+n.width+24)),height:Math.max(120,...nodes.map(n=>n.y+n.height+24)),
    truncated:nodes.length<scene.nodes.length||edges.length<scene.edges.length,rankCompressed:nodes.some(n=>n.column!==n.depth)};
}
export function hitReference(layout,x,y) {return layout.nodes.find(n=>x>=n.x&&x<=n.x+n.width&&y>=n.y&&y<=n.y+n.height)||null;}
const groupColors=['#5bc0eb','#a3be8c','#b48ead','#ebcb8b','#88c0d0','#d08770','#8fbcbb','#81a1c1'];
const groupColor=id=>Number.isSafeInteger(id)&&id>=0?groupColors[id%groupColors.length]:null;
const xml=value=>String(value??'').replace(/[<>&"']/g,c=>({'<':'&lt;','>':'&gt;','&':'&amp;','"':'&quot;',"'":'&apos;'}[c]));
export function referenceSvg(scene,selected=null) {
  const layout=referenceLayout(scene,selected),nodes=new Map(layout.nodes.map(n=>[n.id,n]));
  let content='';
  for(const edge of layout.edges){
    const a=nodes.get(edge.source),b=nodes.get(edge.target),x1=a.x+a.width,y1=a.y+a.height/2,x2=b.x,y2=b.y+b.height/2;
    const d=a===b?`M${x1-20} ${a.y}c60 -30 60 65 20 35`:`M${x1} ${y1}C${x1+30} ${y1} ${x2-30} ${y2} ${x2} ${y2}`;
    content+=`<path d="${d}" fill="none" stroke="${edge.highlighted?'#ffd166':'#738fa8'}" stroke-width="${edge.highlighted?'3':'1.4'}" marker-end="url(#arrow)"><title>${xml(`${edge.source} ${edge.path} → ${edge.target}`)}</title></path>`;
  }
  for(const n of layout.nodes){
    content+=`<g><title>${xml(`${n.type} ${n.id}${n.name?' '+n.name:''} · ${scene.condensed?'rank':'depth'} ${n.depth}`)}</title><rect x="${n.x}" y="${n.y}" width="${n.width}" height="${n.height}" rx="5" fill="${n.selected?'#324d65':n.root?'#1c4050':'#243343'}" stroke-width="${n.highlighted||n.cyclic?3:1}" stroke="${n.selected?'#ffffff':n.highlighted?'#ffd166':n.cyclic?'#ff9e64':n.exists===false?'#ff7d8a':groupColor(n.component)||(n.root?'#5bc0eb':'#738fa8')}"/><text x="${n.x+8}" y="${n.y+18}" fill="#edf6ff" font-size="12" font-family="monospace">${xml(String(n.type||'Object').split('.').at(-1).slice(0,25))}</text><text x="${n.x+8}" y="${n.y+36}" fill="#a9c3da" font-size="11" font-family="monospace">${xml(String(n.label||n.id).slice(0,27))}</text></g>`;
  }
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${layout.width}" height="${layout.height}" viewBox="0 0 ${layout.width} ${layout.height}" role="img" aria-label="Source object reference graph"><title>Source object reference graph</title><desc>${xml(`${layout.nodes.length} diagram nodes and ${layout.edges.length} directed references. ${layout.truncated?'Diagram display limit reached. ':''}${layout.rankCompressed?`Display columns are capped at ${referenceLimits.diagramColumns}; deeper ranks share the last column. Tooltips retain actual rank. `:''}${scene.partial?'Only loaded pages shown. ':''}${scene.analysis==='path'?(scene.status==='found'?'A shortest path is proven.':scene.status==='unreachable'?'No path exists after completed search.':'Search incomplete; reachability is unknown.'):(scene.explorationComplete?'Exploration complete.':'Exploration limited by configured bounds.')} ${scene.condensed?'Condensation DAG: columns show topological rank; each node is a strongly connected group.':'Columns show traversal depth; arrows always point from reference source to target.'} ${scene.analysis==='components'?'Cycles within a partial scope do not establish globally maximal groups.':''} ${scene.analysis==='path'?`Dependency path status: ${scene.status}.`:''} ${scene.analysis==='impact'?'Highlighted proof follows incoming references back to a seed; structural impact does not simulate an edit.':''}`)}</desc><defs><marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M0 0L10 5L0 10z" fill="#738fa8"/></marker></defs><rect width="100%" height="100%" fill="#17212b"/>${content}</svg>`;
}
