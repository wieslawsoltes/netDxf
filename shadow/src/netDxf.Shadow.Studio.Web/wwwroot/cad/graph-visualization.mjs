/** Renderer-independent graph presentation. Graph IDs are keys, never array indices. */
export const graphLimits = Object.freeze({nodes:10000,edges:20000,resultItems:4000,resultPages:40,labels:300});
export const graphColors = Object.freeze(['#5bc0eb','#ffba49','#9bc53d','#e879b9','#a99af5','#48d6bb','#fa9075','#a8c5ff','#d6d16e','#c39c79','#75cfec','#f7a8a8']);
export const graphModes = Object.freeze({components:'Components',features:'Topology features',result:'Analysis result',topology:'Uniform topology'});
const validId = value => Number.isSafeInteger(value) && value >= 0;
const position = p => p && Number.isFinite(p.x) && Number.isFinite(p.y) && (p.z===undefined || Number.isFinite(p.z));
const componentColor = id => graphColors[(Math.abs(Number(id)||0)) % graphColors.length];
export function graphIdentity(state) {
  return state ? JSON.stringify([state.serverId,state.documentId,state.workspaceId,state.viewVersion,state.revision,state.layout,state.fontRevision]) : '';
}
export function graphBounds(scene) {
  const points=[...(scene?.nodes||[]),...(scene?.edges||[]).flatMap(e=>[e.start,e.end])].filter(position);
  if(!points.length)return null;
  let minX=Infinity,minY=Infinity,maxX=-Infinity,maxY=-Infinity;
  for(const p of points){minX=Math.min(minX,p.x);minY=Math.min(minY,p.y);maxX=Math.max(maxX,p.x);maxY=Math.max(maxY,p.y);}
  return {minX,minY,maxX,maxY,isEmpty:false};
}
/** Merge independently paged projections without inventing complete coverage. */
export function mergeGraphProjection(previous, data) {
  if(!data || !Array.isArray(data.nodes?.items) || !Array.isArray(data.edges?.items))throw new Error('Incomplete graph visualization response.');
  if(typeof data.graphKey!=='string'||!data.graphKey||data.graphKey.length>2048)throw new Error('Missing graph build identity. Reload topology.');
  if(previous && (!data.graphKey || previous.graphKey!==data.graphKey))throw new Error('The topology changed between pages. Reload topology.');
  for(const [page,last] of [[data.nodes,previous?.nextNode],[data.edges,previous?.nextEdge]])if(page.nextOffset!=null&&(!validId(page.nextOffset)||last!=null&&page.nextOffset<=last))throw new Error('The topology repeated an invalid page cursor. Reload topology.');
  const nodes=new Map((previous?.nodes||[]).map(n=>[n.id,n])),edges=new Map((previous?.edges||[]).map(e=>[e.id,e]));
  for(const node of data.nodes.items){
    if(!validId(node.id)||!position(node))throw new Error('Invalid graph node coordinates.');
    if(nodes.has(node.id)||nodes.size<graphLimits.nodes)nodes.set(node.id,node);
  }
  for(const edge of data.edges.items){
    if(!validId(edge.id)||!validId(edge.a)||!validId(edge.b)||!position(edge.start)||!position(edge.end))throw new Error('Invalid graph edge coordinates.');
    if(edges.has(edge.id)||edges.size<graphLimits.edges)edges.set(edge.id,edge);
  }
  const nodeTotal=data.nodes.total,edgeTotal=data.edges.total;
  if(!validId(nodeTotal)||!validId(edgeTotal))throw new Error('Invalid graph projection totals.');
  const partial=nodes.size<nodeTotal||edges.size<edgeTotal;
  return {graphKey:data.graphKey,layout:data.layout,options:data.options,nodes:[...nodes.values()],edges:[...edges.values()],
    nodeTotal,edgeTotal,componentCount:data.componentCount,cycleRank:data.cycleRank,partial,
    nextNode:data.nodes.nextOffset??null,nextEdge:data.edges.nextOffset??null,
    capped:partial&&(nodes.size>=graphLimits.nodes||edges.size>=graphLimits.edges)};
}
export function graphResultItems(data){return data?.steps?.items||data?.items||data?.nodes||(data?.node?[data.node]:[]);}
export function graphResultScene(data) {
  const nodes=new Map(),edges=new Map();
  const addNode=node=>{if(node&&validId(node.id)&&position(node))nodes.set(node.id,{...nodes.get(node.id),...node});};
  const addEdge=edge=>{if(!edge||!validId(edge.id)||!validId(edge.a)||!validId(edge.b))return;edges.set(edge.id,edge);if(position(edge.start))addNode({...edge.start,id:edge.a});if(position(edge.end))addNode({...edge.end,id:edge.b});};
  for(const item of graphResultItems(data)){addEdge(item.viaEdge||item);addNode(item.node?{...item.node,depth:item.depth}:item);}
  return {nodes:[...nodes.values()],edges:[...edges.values()],highlight:true};
}
export function graphSourceLinks(scene) {
  const sources=new Map();
  for(const edge of scene?.edges||[])if(typeof edge.entity==='string'&&edge.entity){const occurrence=typeof edge.occurrence==='string'?edge.occurrence:'';sources.set(JSON.stringify([edge.entity,occurrence]),{entity:edge.entity,occurrence,rootEntity:edge.rootEntity||edge.entity,insertPath:edge.insertPath||[]});}
  return [...sources.values()];
}
export function graphPresentation(base,result,options={}) {
  const nodes=new Map((base?.nodes||[]).map(n=>[n.id,n])),edges=new Map((base?.edges||[]).map(e=>[e.id,e]));
  for(const node of result?.nodes||[])nodes.set(node.id,{...nodes.get(node.id),...node});
  for(const edge of result?.edges||[])edges.set(edge.id,{...edges.get(edge.id),...edge});
  return {...base,nodes:[...nodes.values()],edges:[...edges.values()],highlight:!!result,
    resultNodes:new Set((result?.nodes||[]).map(n=>n.id)),resultEdges:new Set((result?.edges||[]).map(e=>e.id)),
    mode:options.mode||'components',showNodes:options.showNodes!==false,showEdges:options.showEdges!==false,
    labels:options.labels||'none',dimDrawing:!!options.dimDrawing,selected:options.selected||null};
}
export function graphStyle(item,kind,scene) {
  const selected=scene.selected?.kind===kind&&scene.selected.id===item.id;
  const highlighted=kind==='node'?scene.resultNodes?.has(item.id):scene.resultEdges?.has(item.id);
  let color='#87cbe5',alpha=1,width=1.7,radius=3.5;
  if(scene.mode==='components')color=componentColor(item.component);
  if(scene.mode==='features')color=kind==='edge'?(item.bridge?'#d99cff':'#647e91'):
    item.isolated?'#cbd5e1':item.articulationPoint?'#d99cff':item.junction?'#ffba49':item.deadEnd?'#ff7d8a':'#64d8bd';
  if(scene.mode==='result'&&scene.highlight){color=highlighted?(Number.isFinite(item.depth)?componentColor(item.depth):'#ffcf66'):'#7593a5';alpha=highlighted?1:.22;width=highlighted?3:1;radius=highlighted?5:2.5;}
  if(selected){color='#ffffff';width=4;radius=7;alpha=1;}
  return {color,alpha,width,radius};
}
export function graphLegend(scene) {
  if(scene.mode==='components')return [...new Set([...(scene.nodes||[]),...(scene.edges||[])].map(x=>x.component).filter(validId))].sort((a,b)=>a-b).slice(0,24).map(id=>({color:componentColor(id),label:`Component ${id}`}));
  if(scene.mode==='features')return [{color:'#ff7d8a',label:'Dead end'},{color:'#ffba49',label:'Junction'},{color:'#d99cff',label:'Bridge / articulation'},{color:'#cbd5e1',label:'Isolated node'},{color:'#64d8bd',label:'Other node'}];
  if(scene.mode==='result')return [{color:'#ffcf66',label:'Analysis result (traversal colors = hops)'},{color:'#7593a5',label:'Other topology'}];
  return [{color:'#87cbe5',label:'Topology'},{color:'#ffffff',label:'Inspected item'}];
}
export function graphEdgePoints(edge,nodes) {return [position(edge.start)?edge.start:nodes.get(edge.a),position(edge.end)?edge.end:nodes.get(edge.b)];}
/** Screen-space hit testing is shared by the CAD viewport and miniature graph overview. */
export function hitGraph(scene,pixel,project,{kind='any',tolerance=10}={}) {
  if(!scene)return null;
  let best=null,bestDistance=tolerance;
  if(kind!=='edge'&&scene.showNodes!==false)for(const node of scene.nodes){const p=project(node),d=Math.hypot(p.x-pixel.x,p.y-pixel.y);if(d<=bestDistance){bestDistance=d;best={kind:'node',id:node.id,node,distance:d};}}
  if(best)return best;
  if(kind==='node'||scene.showEdges===false)return null;
  const nodes=new Map(scene.nodes.map(n=>[n.id,n]));
  for(const edge of scene.edges){const [a,b]=graphEdgePoints(edge,nodes);if(!position(a)||!position(b))continue;const p=project(a),q=project(b),dx=q.x-p.x,dy=q.y-p.y,length=dx*dx+dy*dy,t=length?Math.max(0,Math.min(1,((pixel.x-p.x)*dx+(pixel.y-p.y)*dy)/length)):0,d=Math.hypot(pixel.x-p.x-t*dx,pixel.y-p.y-t*dy);if(d<=bestDistance){bestDistance=d;best={kind:'edge',id:edge.id,edge,distance:d};}}
  return best;
}
export function drawGraph(context,scene,project,size) {
  if(!scene)return;
  context.save();
  if(scene.dimDrawing){context.globalAlpha=.65;context.fillStyle='#17212b';context.fillRect(0,0,size.w,size.h);}
  const nodes=new Map(scene.nodes.map(n=>[n.id,n]));let labels=0;
  const text=(value,p,color)=>{if(labels>=graphLimits.labels||p.x<0||p.x>size.w||p.y<0||p.y>size.h)return;labels++;context.globalAlpha=1;context.font='11px ui-monospace, monospace';context.lineWidth=3;context.strokeStyle='#17212b';context.strokeText(value,p.x+7,p.y-6);context.fillStyle=color;context.fillText(value,p.x+7,p.y-6);};
  if(scene.showEdges!==false)for(const edge of scene.edges){const [a,b]=graphEdgePoints(edge,nodes);if(!position(a)||!position(b))continue;const p=project(a),q=project(b),style=graphStyle(edge,'edge',scene);context.globalAlpha=style.alpha;context.strokeStyle=style.color;context.lineWidth=style.width;context.beginPath();context.moveTo(p.x,p.y);context.lineTo(q.x,q.y);context.stroke();if(scene.labels==='edges'||scene.labels==='all')text(`E${edge.id}`,{x:(p.x+q.x)/2,y:(p.y+q.y)/2},style.color);}
  if(scene.showNodes!==false)for(const node of scene.nodes){const p=project(node),style=graphStyle(node,'node',scene);if(p.x<-10||p.x>size.w+10||p.y<-10||p.y>size.h+10)continue;context.globalAlpha=style.alpha;context.fillStyle=style.color;context.strokeStyle='#15212b';context.lineWidth=1.5;context.beginPath();context.arc(p.x,p.y,style.radius,0,Math.PI*2);context.fill();context.stroke();if(scene.labels==='nodes'||scene.labels==='all')text(`N${node.id}`,p,style.color);}
  context.restore();
}
const xml=value=>String(value).replace(/[<>&"']/g,c=>({'<':'&lt;','>':'&gt;','&':'&amp;','"':'&quot;',"'":'&apos;'}[c]));
/** Standalone, bounded vector export of the displayed projection, with source metadata. */
export function graphSvg(scene,{title='Graph workspace',maximumEdges=graphLimits.edges,maximumNodes=graphLimits.nodes}={}) {
  const b=graphBounds(scene)||{minX:0,minY:0,maxX:100,maxY:100},width=Math.max(1,b.maxX-b.minX),height=Math.max(1,b.maxY-b.minY),pad=Math.max(width,height)*.05,unit=Math.max(width,height)/700;
  const nodes=new Map(scene.nodes.map(n=>[n.id,n]));let content='',labels='',labelCount=0,nodeCount=0,edgeCount=0;
  const label=(value,p,color)=>{if(labelCount>=graphLimits.labels)return;labelCount++;labels+=`<text x="${p.x+7*unit}" y="${-p.y-6*unit}" font-family="monospace" font-size="${11*unit}" fill="${color}" stroke="#17212b" stroke-width="${3*unit}" paint-order="stroke">${xml(value)}</text>`;};
  if(scene.showEdges!==false)for(const edge of scene.edges.slice(0,maximumEdges)){const [a,b]=graphEdgePoints(edge,nodes);if(!position(a)||!position(b))continue;edgeCount++;const s=graphStyle(edge,'edge',scene);content+=`<path d="M${a.x} ${-a.y}L${b.x} ${-b.y}" fill="none" stroke="${s.color}" stroke-width="${s.width*unit}" opacity="${s.alpha}"><title>${xml(`Edge ${edge.id} · ${edge.entity||''} · ${edge.occurrence||''}`)}</title></path>`;if(scene.labels==='edges'||scene.labels==='all')label(`E${edge.id}`,{x:(a.x+b.x)/2,y:(a.y+b.y)/2},s.color);}
  if(scene.showNodes!==false)for(const node of scene.nodes.slice(0,maximumNodes)){nodeCount++;const s=graphStyle(node,'node',scene);content+=`<circle cx="${node.x}" cy="${-node.y}" r="${s.radius*unit}" fill="${s.color}" opacity="${s.alpha}"><title>${xml(`Node ${node.id} · degree ${node.degree??'unknown'}`)}</title></circle>`;if(scene.labels==='nodes'||scene.labels==='all')label(`N${node.id}`,node,s.color);}
  const capped=scene.showEdges!==false&&edgeCount<scene.edges.length||scene.showNodes!==false&&nodeCount<scene.nodes.length;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${b.minX-pad} ${-b.maxY-pad} ${width+pad*2} ${height+pad*2}" role="img" aria-label="${xml(title)}"><title>${xml(title)}</title><desc>${xml(`${nodeCount} exported nodes, ${edgeCount} exported edges, ${labelCount} labels (maximum ${graphLimits.labels}). Loaded projection contains ${scene.nodes.length} nodes and ${scene.edges.length} edges. ${scene.partial?'Partial topology.':'Loaded topology.'}${capped?' Export geometry truncated by display limits.':''} XY projection; source coordinates retain Z in JSON export. Colors: ${graphLegend(scene).map(x=>x.label).join('; ')}.`)}</desc><rect x="${b.minX-pad}" y="${-b.maxY-pad}" width="${width+pad*2}" height="${height+pad*2}" fill="#17212b"/>${content}${labels}</svg>`;
}
