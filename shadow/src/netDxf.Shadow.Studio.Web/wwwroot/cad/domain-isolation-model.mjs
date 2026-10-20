/** Exact declared-ID assumptions and proof/report helpers shared by the connectivity workspace. */
export const isolationStreams=Object.freeze(['assessments','beforeVisits','afterVisits','exclusions']);
export function isolationExclusions(connections='',ports='') {
  const parse=(text,max)=>{const items=String(text||'').split(/\r?\n/).map(s=>s.trim()).filter(Boolean);if(items.some(id=>id.length>max))throw new Error('An exclusion ID exceeds its supported length.');return [...new Set(items)];};
  const excludedConnections=parse(connections,4128),excludedPorts=parse(ports,2048),all=[...excludedConnections,...excludedPorts];
  if(all.length>2000||all.reduce((sum,id)=>sum+id.length,0)>128000)throw new Error('Use at most 2000 exclusions and 128000 ID characters.');
  return {excludedConnections,excludedPorts};
}
export function isolationProofIndex(scene) {
  return {scene,nodes:new Set(scene.nodes.map(n=>n.id)),edges:new Map(scene.edges.map(e=>[e.id,e])),
    before:new Map(scene.beforeVisits.map(v=>[v.id,v])),after:new Map(scene.afterVisits.map(v=>[v.id,v])),
    removedPorts:new Set(scene.exclusions.filter(e=>e.kind==='port').map(e=>e.value)),
    removedEdges:new Set(scene.exclusions.filter(e=>e.kind==='connection').map(e=>e.value))};
}
export function isolationProof(scene,port,after=true,index=isolationProofIndex(scene)) {
  if(index.scene!==scene||!scene.isolation)throw new Error('Isolation proof index belongs to another receipt.');
  const visits=after?index.after:index.before,nodes=[],edges=[],seen=new Set();let current=port;
  while(true){
    if(seen.has(current)||seen.size>=50000)throw new Error('Invalid isolation predecessor cycle.');seen.add(current);
    const visit=visits.get(current);if(!visit||!index.nodes.has(current))return {nodes,edges,complete:false};
    if(after&&index.removedPorts.has(current))throw new Error('Scenario proof includes an excluded port.');
    nodes.push(current);
    if(visit.predecessor==null){
      if(current!==scene.isolation.source||visit.depth!==0||visit.connection!=null)throw new Error('Invalid isolation proof root.');
      nodes.reverse();edges.reverse();
      return {nodes,edges,complete:scene.exclusions.length===scene.totals.exclusions};
    }
    const parent=visits.get(visit.predecessor),edge=index.edges.get(visit.connection);
    if(!edge||!parent)return {nodes,edges,complete:false};
    if(visit.depth!==parent.depth+1||after&&index.removedEdges.has(edge.id))throw new Error('Invalid isolation proof depth or excluded connection.');
    const forward=edge.source===visit.predecessor&&edge.target===current,reverse=edge.target===visit.predecessor&&edge.source===current;
    const direction=scene.isolation.direction;
    if(!(forward||reverse)||!(direction==='both'||edge.direction==='both'||(direction==='forward'?forward:reverse)===(edge.direction==='forward')))throw new Error('Isolation proof violates declared direction.');
    edges.push(edge.id);current=visit.predecessor;
  }
}
export function isolationCsv(scene,kind='assessments') {
  if(!isolationStreams.includes(kind)||!scene.isolation)throw new Error('Choose an isolation report stream.');
  const fields=kind==='assessments'?['id','before','after','status']:kind==='exclusions'?['id','kind','value']:['id','depth','predecessor','connection'];
  const s=scene.isolation;
  const metadata=['graphKey','module','source','target','direction','scopeComplete','beforeComplete','afterComplete','pagesComplete','loadedRows','totalRows','loadedExclusions','totalExclusions','maximumVisits','maximumDepth','maximumExaminedEdges',
    'beforeVisitLimitReached','beforeDepthLimitReached','beforeEdgeLimitReached','beforeExaminedEdges','afterVisitLimitReached','afterDepthLimitReached','afterEdgeLimitReached','afterExaminedEdges'];
  const headers=[...metadata,...fields];
  const prefix=[scene.graphKey,scene.module,s.source,s.target,s.direction,s.scopeComplete,s.before.complete,s.after.complete,!scene.partial,scene[kind].length,scene.totals[kind],scene.exclusions.length,scene.totals.exclusions,s.options.maximumVisits,s.options.maximumDepth,s.options.maximumExaminedEdges,
    s.before.visitLimitReached,s.before.depthLimitReached,s.before.edgeLimitReached,s.before.examinedEdges,s.after.visitLimitReached,s.after.depthLimitReached,s.after.edgeLimitReached,s.after.examinedEdges];
  const quote=v=>{const text=String(v??'');return '"'+(typeof v==='string'&&/^\s*[=+@-]/.test(text)?"'":'')+text.replaceAll('"','""')+'"';};
  return [headers,...scene[kind].map(item=>[...prefix,...fields.map(f=>item[f])])].map(row=>row.map(quote).join(',')).join('\r\n');
}
