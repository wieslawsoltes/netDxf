import {referenceRequest,mergeReferenceGraph,referenceEdgeKey,referenceLimits} from './reference-graph-model.mjs';

export const referenceOperations=Object.freeze({explore:'Explore references',components:'Analyze cyclic groups',path:'Find dependency path',impact:'Explore reference impact'});
export function referenceAnalysisRequest(values,state,scene=null) {
  const operation=values.operation||'explore';
  if(!Object.hasOwn(referenceOperations,operation))throw new Error('Unknown reference analysis.');
  const request=referenceRequest({...values,roots:operation==='path'?'':values.roots,direction:operation==='impact'?'incoming':values.direction},state,scene);
  if(operation==='explore')return request;
  request.tool=`graph_reference_${operation}`;
  if(operation==='path'){
    for(const key of ['source','target']){
      const id=String(values[key]||'').trim();if(!id||id.length>512||/[\s,]/.test(id))throw new Error(`Enter one reference path ${key} object ID.`);
      request.arguments[key]=id;
    }
    delete request.arguments.roots;
  }
  Object.assign(request.arguments,{resultLimit:100,linkLimit:200});
  if(scene)Object.assign(request.arguments,{resultOffset:scene.nextResult??scene.resultCount,linkOffset:scene.nextLink??scene.linkCount});
  return request;
}
function mergePage(previous,data,name,key,cap) {
  const page=data[name],old=previous?.[name]||[],cursor=previous?.[`next${key}`],total=previous?.[`${key.toLowerCase()}Count`];
  if(!page||!Array.isArray(page.items)||!Number.isSafeInteger(page.total)||page.total<0||page.total>cap||!Number.isSafeInteger(page.offset)||page.offset<0||page.offset>page.total||
    page.offset+page.items.length>page.total||(total!==undefined&&page.total!==total)||(previous&&page.offset!==(cursor??total))||(!previous&&page.offset!==0)||
    (page.nextOffset!=null&&(page.nextOffset!==page.offset+page.items.length||page.nextOffset<=page.offset||page.nextOffset>=page.total)))throw new Error('Invalid reference analysis page.');
  const items=[...old,...page.items];
  if(items.length>page.total||(page.nextOffset==null&&items.length!==page.total))throw new Error('Reference analysis pages ended before their totals.');
  return {items,total:page.total,next:page.nextOffset??null};
}
export function mergeReferenceAnalysis(previous,data) {
  if(!data.analysis)return mergeReferenceGraph(previous,data);
  if(!['components','path','impact'].includes(data.analysis)||previous&&previous.analysis!==data.analysis)throw new Error('Reference analysis changed between pages.');
  const scene=mergeReferenceGraph(previous,data),results=mergePage(previous,data,'results','Result',referenceLimits.nodes),links=mergePage(previous,data,'links','Link',referenceLimits.edges);
  for(const result of results.items)if(!Number.isSafeInteger(result.id)||result.id<0||!Number.isSafeInteger(result.nodeCount)||result.nodeCount<1||typeof result.cyclic!=='boolean')throw new Error('Invalid reference component.');
  if(new Set(results.items.map(r=>r.id)).size!==results.items.length)throw new Error('Duplicate reference component.');
  for(const link of links.items)if(!Number.isSafeInteger(link.source)||!Number.isSafeInteger(link.target)||link.source<0||link.target<0||link.source===link.target||!Number.isSafeInteger(link.referenceCount)||link.referenceCount<1)throw new Error('Invalid condensation link.');
  if(new Set(links.items.map(e=>JSON.stringify([e.source,e.target]))).size!==links.items.length)throw new Error('Duplicate condensation link.');
  if(data.analysis==='path'&&!['found','unreachable','incomplete'].includes(data.status))throw new Error('Invalid dependency path status.');
  return {...scene,results:results.items,links:links.items,resultCount:results.total,linkCount:links.total,nextResult:results.next,nextLink:links.next,
    partial:scene.partial||results.items.length<results.total||links.items.length<links.total};
}
/** Expand only one loaded shortest proof; missing pages are explicit, never invented links. */
export function referenceImpactProof(scene,id) {
  const byId=new Map(scene.nodes.map(n=>[n.id,n])),seen=new Set(),nodes=[],edges=[];
  while(true){
    if(seen.has(id)||seen.size>=referenceLimits.nodes)throw new Error('Invalid impact predecessor cycle.');seen.add(id);
    const node=byId.get(id);if(!node)return {nodes,edges,complete:false,reason:'Load more object pages to complete this proof.'};nodes.push(id);
    if(node.predecessor==null){if(node.depth!==0||node.via!=null)throw new Error('Invalid impact root.');return {nodes,edges,complete:true};}
    const edge=node.via;
    if(!edge||edge.source!==id||edge.target!==node.predecessor||typeof edge.path!=='string'||!edge.path.startsWith('/')||node.depth<1)throw new Error('Invalid impact proof link.');
    const parent=byId.get(node.predecessor);if(parent&&parent.depth!==node.depth-1)throw new Error('Invalid impact predecessor depth.');
    edges.push(edge);id=node.predecessor;
  }
}
export function referenceAnalysisView(scene,{component=null,proof=null,condensed=false}={}) {
  if(condensed&&scene.analysis==='components'){
    const groups=scene.results||[],ids=new Set(groups.map(c=>c.id)),links=(scene.links||[]).filter(e=>ids.has(e.source)&&ids.has(e.target)),depth=new Map(groups.map(c=>[c.id,0])),degree=new Map(groups.map(c=>[c.id,0])),out=new Map(groups.map(c=>[c.id,[]]));
    for(const e of links){degree.set(e.target,degree.get(e.target)+1);out.get(e.source).push(e.target);}
    const queue=groups.filter(c=>degree.get(c.id)===0).map(c=>c.id);let visited=0;
    for(let i=0;i<queue.length;i++){const id=queue[i];visited++;for(const target of out.get(id)){depth.set(target,Math.max(depth.get(target),depth.get(id)+1));degree.set(target,degree.get(target)-1);if(degree.get(target)===0)queue.push(target);}}
    if(visited!==groups.length)throw new Error('Invalid condensation cycle.');
    return {...scene,condensed:true,nodes:groups.map(c=>({id:`component:${c.id}`,type:`Component ${c.id}`,name:`${c.nodeCount} objects${c.cyclic?' · cyclic':''}`,label:`${c.nodeCount} objects · ${c.internalEdgeCount} links`,depth:depth.get(c.id),rank:depth.get(c.id),component:c.id,cyclic:c.cyclic,highlighted:c.id===component,exists:true})),
      edges:links.map(e=>({source:`component:${e.source}`,target:`component:${e.target}`,path:`${e.referenceCount} captured references`,highlighted:e.source===component||e.target===component}))};
  }
  const nodeIds=new Set(proof?.nodes||[]),edgeIds=new Set((proof?.edges||[]).map(referenceEdgeKey)),members=new Set(component==null?[]:scene.nodes.filter(n=>n.component===component).map(n=>n.id));
  return {...scene,nodes:scene.nodes.map(n=>({...n,highlighted:scene.analysis==='path'||nodeIds.has(n.id)||component!=null&&n.component===component})),
    edges:scene.edges.map(e=>({...e,highlighted:scene.analysis==='path'||edgeIds.has(referenceEdgeKey(e))||members.has(e.source)&&members.has(e.target)}))};
}
