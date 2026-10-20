import {isolationExclusions} from './domain-isolation-model.mjs';
export const connectivityStreams = Object.freeze(['nodes','edges','issues','components','visits','strongComponents','portStructures','condensation','criticalConnections','loops','loopSteps','assessments','beforeVisits','afterVisits','exclusions']);
export const connectivityCaps = Object.freeze({nodes:50000,edges:100000,issues:5000,components:50000,visits:50000,strongComponents:50000,portStructures:50000,condensation:100000,criticalConnections:100000,loops:100,loopSteps:10000,assessments:50000,beforeVisits:50000,afterVisits:50000,exclusions:2000,pages:40});
export const connectivityOffsets = Object.freeze(Object.fromEntries(connectivityStreams.map(name=>[name,(name==='condensation'?name:name.slice(0,-1))+'Offset'])));
export function connectivityIdentity(client) {
  const s=client.state||{};return JSON.stringify([client.id,s.documentId,s.workspaceId,s.viewVersion,s.revision,s.layout,client.data?.moduleConfigurationVersion]);
}
export function connectivityRequest(module,values,state,previous=null) {
  if(!['network','path','trace','structure','isolation'].includes(values.operation)||!['both','forward','reverse'].includes(values.direction))throw new Error('Choose a connectivity operation and direction.');
  const integer=(value,min,max,label)=>{const n=Number(value);if(!Number.isSafeInteger(n)||n<min||n>max)throw new Error(`${label} must be ${min}–${max}.`);return n;};
  const request={revision:state.revision,module,operation:values.operation,direction:values.direction,maximumVisits:integer(values.maximumVisits,1,50000,'Visit limit'),maximumDepth:integer(values.maximumDepth,0,1000,'Depth limit'),limit:100,
    options:{maximumObjects:integer(values.maximumObjects,1,100000,'Object limit'),maximumNodes:20000,maximumEdges:40000,maximumIssues:500,connectionTolerance:Number(values.tolerance)}};
  if(!Number.isFinite(request.options.connectionTolerance)||request.options.connectionTolerance<=0||request.options.connectionTolerance>1e6)throw new Error('Connection tolerance must be positive and at most 1000000.');
  if(['path','trace','isolation'].includes(values.operation)){
    for(const key of values.operation==='path'?['source','target']:['source']){const id=String(values[key]||'').trim();if(!id||id.length>2048)throw new Error(`Choose a ${key} port from the loaded network.`);request[key]=id;}
  }
  if(values.operation==='isolation'){
    const target=String(values.target||'').trim();if(target.length>2048)throw new Error('Choose a target port within 2048 characters.');if(target)request.target=target;
    Object.assign(request,isolationExclusions(values.excludedConnections,values.excludedPorts));
    if(request.excludedPorts.includes(request.source))throw new Error('The source port cannot be excluded. Choose a retained source.');
    request.isolationOptions={maximumVisits:request.maximumVisits,maximumDepth:request.maximumDepth,maximumExaminedEdges:integer(values.maximumExaminedEdges??200000,1,400000,'Isolation edge examination limit')};
  }
  if(values.operation==='structure')request.structureOptions={maximumLoops:integer(values.maximumLoops??20,1,100,'Loop witness limit'),maximumProofSteps:integer(values.maximumProofSteps??4000,2,10000,'Loop proof step limit'),maximumSearchWork:integer(values.maximumSearchWork??100000,1,1000000,'Loop search work limit')};
  if(previous){request.graphKey=previous.graphKey;for(const name of connectivityStreams)request[connectivityOffsets[name]]=previous[name].length;}
  return request;
}
function receiptIdentity(data) {return JSON.stringify([data.graphKey,data.documentId,data.workspaceId,data.viewVersion,data.revision,data.layout,data.module,data.operation,data.options,data.structure,data.isolation]);}
export function mergeConnectivity(previous,data) {
  if(!data||typeof data.graphKey!=='string'||!data.graphKey||!['network','path','trace','structure','isolation'].includes(data.operation)||previous&&receiptIdentity(previous)!==receiptIdentity(data))throw new Error('Connectivity identity or query scope changed. Refresh the network.');
  const result={...data,totals:{},next:{}};
  for(const name of connectivityStreams){
    const stream=connectivityStreams.indexOf(name),optional=stream>=11?data.operation!=='isolation':stream>=5&&data.operation!=='structure';
    const p=data[name]??(optional?{items:[],total:0,offset:0,nextOffset:null}:null),old=previous?.[name]||[];
    if(!p||!Array.isArray(p.items)||!Number.isSafeInteger(p.total)||p.total<0||p.total>connectivityCaps[name]||p.offset!==old.length||p.items.length+p.offset>p.total||previous&&p.total!==previous.totals[name]||
      (p.nextOffset!=null&&(p.nextOffset!==p.offset+p.items.length||p.nextOffset<=p.offset||p.nextOffset>=p.total))||p.nextOffset==null&&p.offset+p.items.length!==p.total)throw new Error(`Invalid ${name} connectivity page.`);
    result[name]=[...old,...p.items];result.totals[name]=p.total;result.next[name]=p.nextOffset??null;
    if(name!=='issues'&&new Set(result[name].map(item=>item.id)).size!==result[name].length)throw new Error(`Duplicate ${name} in connectivity pages.`);
  }
  if(data.operation==='isolation'){
    const summary=data.isolation;
    if(!summary||typeof summary.source!=='string'||!summary.source||!['both','forward','reverse'].includes(summary.direction)||typeof summary.scopeComplete!=='boolean'||
      !Number.isSafeInteger(summary.options?.maximumVisits)||summary.options.maximumVisits<1||summary.options.maximumVisits>50000||
      !Number.isSafeInteger(summary.options?.maximumDepth)||summary.options.maximumDepth<0||summary.options.maximumDepth>1000||
      !Number.isSafeInteger(summary.options?.maximumExaminedEdges)||summary.options.maximumExaminedEdges<1||summary.options.maximumExaminedEdges>400000)throw new Error('Missing isolation analysis coverage.');
    for(const side of ['before','after']){
      const s=summary[side];if(!s||['complete','scopeComplete','visitLimitReached','depthLimitReached','edgeLimitReached'].some(k=>typeof s[k]!=='boolean')||s.scopeComplete!==summary.scopeComplete||
        s.complete!==(s.scopeComplete&&!s.visitLimitReached&&!s.depthLimitReached&&!s.edgeLimitReached)||
        !Number.isSafeInteger(s.examinedEdges)||s.examinedEdges<0||s.examinedEdges>summary.options.maximumExaminedEdges||!Number.isSafeInteger(s.visitedCount)||s.visitedCount<1||s.visitedCount>summary.options.maximumVisits||
        s.targetStatus!=null&&!['reachable','unreachable','unknown','excluded'].includes(s.targetStatus)||
        s.targetPathLength!=null&&(!Number.isSafeInteger(s.targetPathLength)||s.targetPathLength<0||s.targetPathLength>summary.options.maximumDepth))throw new Error('Invalid isolation traversal coverage.');
    }
    for(const a of data.assessments.items){
      const expected=a.after==='excluded'?'excluded':a.after==='reachable'?'reachable':a.before==='reachable'&&a.after==='unreachable'?'lost':a.before==='unreachable'&&a.after==='unreachable'?'unreachable':'unknown';
      if(typeof a.id!=='string'||!['reachable','unreachable','unknown'].includes(a.before)||!['reachable','unreachable','unknown','excluded'].includes(a.after)||
        a.status!==expected||a.before==='unreachable'&&!summary.before.complete||a.after==='unreachable'&&!summary.after.complete)throw new Error('Invalid isolation assessment.');
    }
    for(const e of data.exclusions.items)if(typeof e.id!=='string'||typeof e.value!=='string'||!['port','connection'].includes(e.kind)||e.id!==e.kind+':'+e.value)throw new Error('Invalid exclusion.');
    for(const name of ['beforeVisits','afterVisits'])for(const v of data[name].items)if(typeof v.id!=='string'||!Number.isSafeInteger(v.depth)||v.depth<0||v.depth>1000||v.predecessor!=null&&typeof v.predecessor!=='string'||v.connection!=null&&typeof v.connection!=='string')throw new Error('Invalid isolation predecessor.');
  }
  for(const n of data.nodes.items)if(typeof n.id!=='string'||typeof n.key!=='string'||typeof n.port!=='string'||!Number.isSafeInteger(n.component)||n.component<0||!Number.isFinite(n.position?.x)||!Number.isFinite(n.position?.y))throw new Error('Invalid connectivity port.');
  for(const e of data.edges.items)if(typeof e.id!=='string'||typeof e.source!=='string'||typeof e.target!=='string'||!['both','forward','reverse'].includes(e.direction)||!['connection','passage'].includes(e.kind))throw new Error('Invalid connectivity connection.');
  if(data.operation==='structure'&&!data.structure)throw new Error('Missing structural analysis coverage.');
  if(data.operation==='structure'){
    const integer=value=>Number.isSafeInteger(value)&&value>=0;
    for(const g of data.strongComponents.items)if(!integer(g.id)||!integer(g.portCount)||g.portCount===0||!integer(g.connectionCount)||!['found','absent','incomplete'].includes(g.loopStatus))throw new Error('Invalid strong component.');
    for(const n of data.portStructures.items)if(typeof n.id!=='string'||!integer(n.strongComponent)||typeof n.articulationPoint!=='boolean')throw new Error('Invalid structural port.');
    for(const e of data.condensation.items)if(typeof e.id!=='string'||!integer(e.source)||!integer(e.target)||e.source===e.target||!integer(e.connectionCount)||e.connectionCount===0)throw new Error('Invalid condensation link.');
    for(const e of data.criticalConnections.items)if(typeof e.id!=='string'||typeof e.source!=='string'||typeof e.target!=='string')throw new Error('Invalid cut link.');
    for(const loop of data.loops.items)if(!integer(loop.id)||!integer(loop.strongComponent)||!integer(loop.stepCount)||loop.stepCount<2||loop.stepCount>10000)throw new Error('Invalid loop witness.');
    for(const step of data.loopSteps.items)if(typeof step.id!=='string'||!integer(step.loop)||!integer(step.step)||typeof step.source!=='string'||typeof step.target!=='string'||typeof step.connection!=='string')throw new Error('Invalid loop step.');
  }
  result.partial=connectivityStreams.some(name=>result[name].length<result.totals[name]);return result;
}
export function connectivityProof(scene,id) {
  const visits=new Map(scene.visits.map(v=>[v.id,v])),nodes=[],edges=[],seen=new Set();let current=id;
  while(true){
    if(seen.has(current)||seen.size>=connectivityCaps.visits)throw new Error('Invalid connectivity proof cycle.');seen.add(current);
    const visit=visits.get(current);if(!visit)return {nodes,edges,complete:false};nodes.push(current);
    if(visit.predecessor==null){if(visit.depth!==0||visit.connection!=null||current!==scene.walk?.source)throw new Error('Invalid connectivity proof root.');nodes.reverse();edges.reverse();return {nodes,edges,complete:true};}
    if(typeof visit.connection!=='string'||visit.depth<1)throw new Error('Invalid connectivity proof link.');
    const parent=visits.get(visit.predecessor);if(parent&&parent.depth!==visit.depth-1)throw new Error('Invalid connectivity proof depth.');
    const edge=scene.edges.find(e=>e.id===visit.connection);if(edge&&!((edge.source===current&&edge.target===visit.predecessor)||(edge.target===current&&edge.source===visit.predecessor)))throw new Error('Connectivity proof has unrelated endpoints.');
    edges.push(visit.connection);current=visit.predecessor;
  }
}
export function connectivitySelectionIndex(scene,rows=[]) {return {scene,rows,loaded:new Map(scene.nodes.map(n=>[n.id,n])),roots:new Set(rows.map(r=>r.id))};}
export function connectivitySelection(scene,ids,rows=[],index=connectivitySelectionIndex(scene,rows)) {
  if(index.scene!==scene||index.rows!==rows)throw new Error('Selection index belongs to another receipt or source list.');
  const {loaded,roots}=index,entities=[...new Set(ids.map(id=>{const n=loaded.get(id);return typeof n?.entity==='string'?n.entity:n?.entity?.value;}))];
  if(!entities.length||entities.some(id=>!id||!roots.has(id)))return {entities:[],reason:'One or more source objects are outside the active scene root list (up to 5000 rows).'};
  if(entities.length>500)return {entities:[],reason:'Select at most 500 source objects at a time.'};
  return {entities,reason:null};
}
const xml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
export function connectivitySvg(scene,{component=null,selected=null,proof=null,strongComponent=null,critical=false}={}) {
  const membership=new Map((scene.portStructures||[]).map(n=>[n.id,n.strongComponent]));
  const visits=new Set(scene.visits.map(v=>v.id)),all=scene.nodes.filter(n=>(component==null||n.component===component)&&(strongComponent==null||membership.get(n.id)===strongComponent)&&(['network','structure','isolation'].includes(scene.operation)||visits.has(n.id)));
  const nodes=all.slice(0,180),columns=Math.max(1,Math.min(6,nodes.length)),map=new Map(nodes.map((n,i)=>[n.id,{...n,x:24+(i%columns)*190,y:42+Math.floor(i/columns)*70}]));
  const links=scene.edges.filter(e=>map.has(e.source)&&map.has(e.target)).slice(0,360),width=columns*190+24,height=Math.max(120,Math.ceil(nodes.length/columns)*70+40),high=new Set(proof?.edges||[]);
  const colors=['#2378c8','#a1459b','#16896f','#b66a13','#6b62bc','#b44156'];
  const cuts=new Set(critical?(scene.criticalConnections||[]).map(e=>e.id):[]),assessments=new Map((scene.assessments||[]).map(a=>[a.id,a.status]));
  const excluded=new Set((scene.exclusions||[]).filter(e=>e.kind==='connection').map(e=>e.value));
  // End at the card boundary so arrowheads remain visible when cards paint over links.
  // Long same-row/column links use the inter-card gap instead of running through unrelated cards.
  const linkPath=(a,b)=>{
    const ax=a.x+62.5,bx=b.x+62.5,dx=bx-ax,dy=b.y-a.y;
    if(dy===0&&Math.abs(dx)>190)return `M${ax} ${a.y+17} C${ax} ${a.y+55} ${bx} ${b.y+55} ${bx} ${b.y+17}`;
    if(dx===0&&Math.abs(dy)>70)return `M${a.x+125} ${a.y} C${a.x+155} ${a.y} ${b.x+155} ${b.y} ${b.x+125} ${b.y}`;
    const t=Math.min(dx===0?Infinity:62.5/Math.abs(dx),dy===0?Infinity:17/Math.abs(dy));
    return `M${ax+dx*t} ${a.y+dy*t} L${bx-dx*t} ${b.y-dy*t}`;
  };
  const body=links.map(e=>{const a=map.get(e.source),b=map.get(e.target),color=high.has(e.id)?'#dc2626':excluded.has(e.id)?'#9a7272':cuts.has(e.id)?'#c25700':e.kind==='passage'?'#657287':'#2378c8';return `<path d="${linkPath(a,b)}" fill="none" stroke="${color}" stroke-width="${high.has(e.id)||cuts.has(e.id)?4:2}" ${excluded.has(e.id)?'stroke-dasharray="2 5"':e.kind==='passage'?'stroke-dasharray="5 3"':''} ${e.direction==='forward'?'marker-end="url(#arrow)"':e.direction==='reverse'?'marker-start="url(#arrow)"':''}><title>${xml(e.kind)} · ${xml(e.direction)} · ${xml(e.id)}${excluded.has(e.id)?' · excluded assumption':''}</title></path>`;}).join('')+
    [...map.values()].map(n=>`<g><rect x="${n.x}" y="${n.y-17}" width="125" height="34" rx="6" fill="${n.id===selected?'#fff1ba':({reachable:'#ddf7e8',lost:'#ffe3e3',unknown:'#fff0cc',excluded:'#e5e7eb',unreachable:'#f2f4f7'})[assessments.get(n.id)]||'#fff'}" stroke="${colors[(membership.get(n.id)??n.component)%colors.length]}" stroke-width="${n.id===selected?3:2}"/><text x="${n.x+5}" y="${n.y-2}" fill="#172b41" font-size="10">${xml((n.tag||n.class).slice(0,18))}</text><text x="${n.x+5}" y="${n.y+10}" fill="#172b41" font-size="10">${xml(n.port.slice(0,20))}</text><title>${xml(n.id)} · component ${n.component}${membership.has(n.id)?' · strong group '+membership.get(n.id):''}${assessments.has(n.id)?' · '+assessments.get(n.id):''}</title></g>`).join('');
  const note=`Schematic port graph: ${nodes.length}/${all.length} loaded ports, ${links.length} links shown; solid external connections, dashed declared passages. Layout is not geometry or simulation. Receipt: ${scene.nodes.length}/${scene.totals?.nodes??scene.nodes.length} ports loaded; scan ${scene.complete?'complete':'partial'}, declarations ${scene.declarationsComplete?'complete':'incomplete'}, findings ${scene.issuesComplete?'complete':'limited'}, pages ${scene.partial?'partial':'complete'}.${scene.structure?` Structure scope ${scene.structure.scopeComplete?'complete':'incomplete'}, loop search ${scene.structure.loopSearchComplete?'complete':'limited'}; at most one connection-distinct witness per strong group, not all loops. Orange cuts apply only to retained weak connectivity.`:''}${proof?` Selected proof: ${links.filter(e=>high.has(e.id)).length}/${proof.edges.length} links shown.`:''}`;
  const isolation=scene.isolation?` Isolation assumptions: ${scene.exclusions.length}/${scene.totals.exclusions} loaded; before traversal ${scene.isolation.before.complete?'complete':'incomplete'}, after traversal ${scene.isolation.after.complete?'complete':'incomplete'}. Green=reachable, red=proven lost, amber=unknown, gray=excluded/unreachable. Dotted muted links=explicitly excluded connections; links incident to excluded ports are also omitted from the scenario traversal. These are graph removals, not valve states or physical simulation.`:'';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img"><title>${xml(scene.module)} declared connectivity</title><desc>${xml(note+isolation)}</desc><defs><marker id="arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse"><path d="M 0 0 L 10 5 L 0 10 z" fill="#657287"/></marker></defs><rect width="100%" height="100%" fill="#f8fafc"/>${body}</svg>`;
}
export function connectivityCsv(scene,kind='nodes') {
  const quote=value=>{const text=String(value??'');return '"'+(typeof value==='string'&&/^[\s]*[=+@-]/.test(text)?"'":'')+text.replaceAll('"','""')+'"';};
  const metadata=['graphKey','module','scanComplete','declarationsComplete','issuesComplete','pagesComplete','loadedRows','totalRows'];
  const prefix=[scene.graphKey,scene.module,scene.complete,scene.declarationsComplete,scene.issuesComplete,!scene.partial,scene[kind].length,scene.totals?.[kind]??scene[kind].length];
  const fields=kind==='edges'?['id','source','target','kind','direction','reciprocal','span']:kind==='issues'?['code','severity','message','entities','nodes']:['id','key','port','tag','class','kind','entity','component','degree','open','passagesDeclared','nominalSize','x','y','z','traceDepth','predecessor','connection'];
  const visits=new Map((scene.visits||[]).map(v=>[v.id,v]));
  return [[...metadata,...fields],...scene[kind].map(row=>[...prefix,...fields.map(key=>{
    const visit=visits.get(row.id),value=['x','y','z'].includes(key)?row.position?.[key]:key==='traceDepth'?visit?.depth:['predecessor','connection'].includes(key)?visit?.[key]:row[key];
    return Array.isArray(value)?value.map(v=>v?.value||v).join(';'):typeof value==='object'?value?.value:value;
  })])].map(row=>row.map(quote).join(',')).join('\r\n');
}
