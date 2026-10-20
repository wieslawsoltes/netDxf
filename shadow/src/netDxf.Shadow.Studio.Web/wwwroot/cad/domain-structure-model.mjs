const xml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&apos;'}[c]));
export function domainProofIndex(scene) {
  const steps=new Map();
  for(const step of scene.loopSteps){if(!steps.has(step.loop))steps.set(step.loop,[]);steps.get(step.loop).push(step);}
  return {scene,steps,sources:new Set(scene.nodes.map(n=>n.id)),links:new Map(scene.edges.map(e=>[e.id,e]))};
}
export function domainLoopProof(scene,id,index=domainProofIndex(scene)) {
  if(index.scene!==scene)throw new Error('Loop proof index belongs to another receipt.');
  const loop=scene.loops.find(loop=>loop.id===id);if(!loop)throw new Error('Unknown loop witness.');
  if(!Number.isSafeInteger(loop.stepCount)||loop.stepCount<2||loop.stepCount>10000)throw new Error('Invalid loop proof bound.');
  const steps=[...(index.steps.get(id)||[])].sort((a,b)=>a.step-b.step);
  if(steps.length<loop.stepCount)return {nodes:[],edges:[],complete:false};
  if(steps.length!==loop.stepCount)throw new Error('Invalid loop proof length.');
  const nodes=[],edges=[],known=new Set(),{sources,links}=index;
  let complete=true;
  for(let i=0;i<steps.length;i++){
    const step=steps[i],edge=links.get(step.connection);
    if(step.step!==i||typeof step.source!=='string'||typeof step.target!=='string'||known.has(step.connection)||i&&step.source!==nodes.at(-1))throw new Error('Invalid loop proof chain.');
    if(i===0)nodes.push(step.source);nodes.push(step.target);edges.push(step.connection);known.add(step.connection);
    if(!edge||!sources.has(step.source)||!sources.has(step.target)){complete=false;continue;}
    if(!(edge.source===step.source&&edge.target===step.target&&edge.direction!=='reverse'||edge.target===step.source&&edge.source===step.target&&edge.direction!=='forward'))throw new Error('Loop proof contradicts its declared direction.');
  }
  if(nodes[0]!==nodes.at(-1)||new Set(nodes.slice(0,-1)).size!==steps.length)throw new Error('Loop witness is not a closed simple cycle.');
  return {nodes,edges,complete};
}
export function domainStructureCsv(scene,kind) {
  const fields={
    strongComponents:['id','portCount','connectionCount','loopStatus'],
    portStructures:['id','strongComponent','articulationPoint'],
    condensation:['id','source','target','connectionCount'],
    criticalConnections:['id','source','target'],
    loops:['id','strongComponent','stepCount'],
    loopSteps:['id','loop','step','source','target','connection']
  }[kind];
  if(!fields)throw new Error('Unknown structure export.');
  const quote=value=>'"'+(typeof value==='string'&&/^[\s]*[=+@-]/.test(value)?"'":'')+String(value??'').replaceAll('"','""')+'"';
  const metadata=['graphKey','module','scopeComplete','loopSearchComplete','pagesComplete','loadedRows','totalRows'];
  const values=[scene.graphKey,scene.module,scene.structure.scopeComplete,scene.structure.loopSearchComplete,!scene.partial,scene[kind].length,scene.totals[kind]];
  return [[...metadata,...fields],...scene[kind].map(row=>[...values,...fields.map(field=>row[field])])].map(row=>row.map(quote).join(',')).join('\r\n');
}
/** Bounded condensation layout. Ranks after column seven share that column; actual DAG identities remain in exports. */
export function domainCondensationSvg(scene,selected=null) {
  const groups=scene.strongComponents.slice(0,120),ids=new Set(groups.map(g=>g.id));
  const edges=scene.condensation.filter(e=>ids.has(e.source)&&ids.has(e.target)).slice(0,240);
  const ranks=new Map(groups.map(g=>[g.id,0])),incoming=new Map(groups.map(g=>[g.id,0])),out=new Map(groups.map(g=>[g.id,[]]));
  for(const edge of edges){incoming.set(edge.target,incoming.get(edge.target)+1);out.get(edge.source).push(edge.target);}
  const queue=groups.filter(g=>incoming.get(g.id)===0).map(g=>g.id);
  for(let i=0;i<queue.length;i++)for(const target of out.get(queue[i])){ranks.set(target,Math.max(ranks.get(target),ranks.get(queue[i])+1));incoming.set(target,incoming.get(target)-1);if(!incoming.get(target))queue.push(target);}
  const rows=Array(8).fill(0),map=new Map();
  for(const group of groups){const column=Math.min(7,ranks.get(group.id));map.set(group.id,{...group,x:24+column*170,y:30+rows[column]++*58});}
  const width=Math.max(200,...[...map.values()].map(n=>n.x+155)),height=Math.max(100,...[...map.values()].map(n=>n.y+46));
  const paths=edges.map(e=>{const a=map.get(e.source),b=map.get(e.target),same=a.x===b.x;return `<path d="${same?`M${a.x+130} ${a.y+16} C${a.x+155} ${a.y+16} ${b.x+155} ${b.y+16} ${b.x+130} ${b.y+16}`:`M${a.x+130} ${a.y+16} L${b.x} ${b.y+16}`}" fill="none" stroke="#657287" marker-end="url(#dag-arrow)"><title>${e.connectionCount} declared connections</title></path>`;}).join('');
  const cards=[...map.values()].map(g=>`<g><rect x="${g.x}" y="${g.y}" width="130" height="32" rx="5" fill="${g.id===selected?'#fff1ba':'white'}" stroke="#2378c8"/><text x="${g.x+5}" y="${g.y+13}" font-size="11">Group ${g.id} · ${g.portCount} ports</text><text x="${g.x+5}" y="${g.y+26}" font-size="10">Loop ${xml(g.loopStatus)}</text></g>`).join('');
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img"><title>${xml(scene.module)} condensation of declared connectivity</title><desc>${groups.length}/${scene.totals.strongComponents} groups and ${edges.length}/${scene.totals.condensation} links shown. Bounded eight-column layout; later ranks share column seven. Scope ${scene.structure.scopeComplete?'complete':'incomplete'}, loop search ${scene.structure.loopSearchComplete?'complete':'limited'}, pages ${scene.partial?'partial':'complete'}. This is structural reachability, not simulation.</desc><defs><marker id="dag-arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10z" fill="#657287"/></marker></defs><rect width="100%" height="100%" fill="#f8fafc"/>${paths}${cards}</svg>`;
}
