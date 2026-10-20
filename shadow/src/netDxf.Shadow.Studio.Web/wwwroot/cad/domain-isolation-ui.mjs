import {el} from './ui.mjs';
import {isolationProof,isolationProofIndex} from './domain-isolation-model.mjs';
import {connectivitySelection,connectivitySelectionIndex} from './domain-connectivity-model.mjs';

/** A bounded view of a single immutable before/after receipt. Callers own identity and lifecycle guards. */
export function renderIsolationReport(args) {
  const {scene,container,inspector,button,guard,showProof,select,sourceRows,exportFile,state,inspectPort}=args;
  container.replaceChildren();if(!scene?.isolation)return;
  const s=scene.isolation,index=isolationProofIndex(scene),selectionIndex=connectivitySelectionIndex(scene,sourceRows);
  const selection=ids=>connectivitySelection(scene,ids,sourceRows,selectionIndex);
  const rerender=()=>renderIsolationReport(args);
  const proofFor=(id,after)=>isolationProof(scene,id,after,index);
  function inspectProof(id,after) {
    const evidence=proofFor(id,after);if(!evidence.complete)throw new Error('Load every predecessor, connection, source and exclusion page needed for this proof.');
    showProof(evidence);
    const source=selection(evidence.nodes);
    inspector.replaceChildren(el('h3',{text:`${after?'Scenario':'Baseline'} path to ${id}`}),
      el('p',{text:`Exact declared predecessor proof: ${evidence.edges.length} links. Showing the first ${Math.min(100,evidence.edges.length)} steps; CSV/JSON exports preserve all loaded predecessor records. No operational behavior is implied.`}),
      ...evidence.edges.slice(0,100).map((edge,i)=>el('div',{class:'domain-connection'},[
        el('code',{text:`${i+1}. ${evidence.nodes[i]} → ${evidence.nodes[i+1]}`}),el('code',{text:edge}),
        button(`Inspect proof port ${i}`,guard(()=>inspectPort(evidence.nodes[i])))
      ])),
      button('Select isolation proof sources',guard(()=>select(evidence.nodes)),{disabled:!!source.reason,title:source.reason||'Select the explicit active source objects'}));
  }
  const proofButton=(id,after,text)=>{
    const visits=after?index.after:index.before,evidence=visits.has(id)?proofFor(id,after):null;
    return button(text,guard(()=>inspectProof(id,after)),{disabled:!evidence?.complete,title:evidence?.complete?'Inspect the exact source-linked predecessor chain':'No complete loaded proof; load remaining pages if this port was reached.'});
  };
  container.append(el('h3',{text:'Isolation and rerouting comparison'}),
    el('p',{class:'domain-result',text:`Declared scope ${s.scopeComplete?'complete':'incomplete'} · ${scene.assessments.length}/${scene.totals.assessments} comparisons · ${scene.exclusions.length}/${scene.totals.exclusions} explicit exclusions loaded`}),
    el('p',{class:'hint',text:'This scenario removes named declared ports/connections from a graph. It does not close a valve or simulate flow, pressure, supply, switching, equipment failure or circuit behavior. Found paths are evidence; missing paths are unknown until declarations and traversal are complete.'}),
    ...[['before','Baseline'],['after','Scenario']].map(([side,title])=>{
      const w=s[side],limits=[w.visitLimitReached?'visits':'',w.depthLimitReached?'depth':'',w.edgeLimitReached?'edge examinations':''].filter(Boolean);
      return el('p',{text:`${title}: ${w.visitedCount} reached ports · ${w.examinedEdges}/${s.options.maximumExaminedEdges} edge examinations · ${w.complete?'complete':'incomplete'}${limits.length?' · limits: '+limits.join(', '):''}${s.target?' · target '+w.targetStatus+(w.targetPathLength!=null?' ('+w.targetPathLength+' links)':''):''}`});
    }),
    ...(s.target?[el('div',{class:'module-actions'},[proofButton(s.target,false,'Inspect baseline target path'),proofButton(s.target,true,'Inspect scenario target path')]),
      el('p',{text:s.after.targetStatus==='reachable'?'A retained target path is proven under these exclusions. Compare the two proofs to inspect rerouting.':s.after.targetStatus==='excluded'?'The target is explicitly excluded; this is distinct from disconnection.':'No retained target path is proven. Check the comparison and coverage before interpreting absence.'})]:[]),
    el('p',{class:'hint',text:'Diagram: green = reachable after exclusions; red = proven lost; amber = unknown; gray = excluded or already unreachable. Red heavy links mark the selected proof. Dotted muted links are excluded connection assumptions.'}),
    el('details',{},[el('summary',{text:'Isolation exports'}),...[
      ['assessments','Export reachability CSV'],['beforeVisits','Export baseline predecessors CSV'],
      ['afterVisits','Export scenario predecessors CSV'],['exclusions','Export exclusion assumptions CSV']
    ].map(([kind,text])=>button(text,guard(()=>exportFile(kind))))]));
  const filter=el('select',{'aria-label':'Isolation comparison filter'},[
    ['all','All comparisons'],['lost','Proven lost'],['reachable','Reachable after exclusions'],['unknown','Unknown'],['excluded','Explicitly excluded'],['unreachable','Already unreachable']
  ].map(([value,text])=>el('option',{value,text})));filter.value=state.filter||'all';
  filter.addEventListener('change',()=>{guard(()=>{state.filter=filter.value;state.offset=0;rerender();})();});
  const matches=scene.assessments.filter(a=>filter.value==='all'||a.status===filter.value),counts={};
  for(const a of scene.assessments)counts[a.status]=(counts[a.status]||0)+1;
  state.offset=Math.min(state.offset||0,Math.max(0,Math.ceil(matches.length/40)-1)*40);
  container.append(el('p',{text:`Loaded comparison counts: ${Object.entries(counts).map(([name,count])=>`${name} ${count}`).join(' · ')||'none'}. Counts exclude unloaded comparison pages.`}),
    el('label',{class:'form-row'},[el('span',{text:'Show comparisons'}),filter]),
    el('p',{class:'hint',text:`${matches.length} matching loaded ports · showing ${Math.min(state.offset+1,matches.length)}–${Math.min(state.offset+40,matches.length)}`}),
    ...matches.slice(state.offset,state.offset+40).map(a=>{
      const source=selection([a.id]);
      return el('article',{class:'domain-connection'},[el('code',{text:a.id}),
        el('strong',{text:`${a.status} · before ${a.before} → after ${a.after}`}),
        el('div',{class:'module-actions'},[button('Inspect comparison '+a.id,guard(()=>inspectPort(a.id)),{disabled:!index.nodes.has(a.id)}),
          proofButton(a.id,false,'Baseline proof '+a.id),proofButton(a.id,true,'Scenario proof '+a.id),
          button('Select comparison source '+a.id,guard(()=>select([a.id])),{disabled:!!source.reason,title:source.reason||'Select explicit source object'})])]);
    }),
    el('div',{class:'module-actions'},[
      button('Previous comparisons',guard(()=>{state.offset=Math.max(0,state.offset-40);rerender();}),{disabled:state.offset===0}),
      button('Next comparisons',guard(()=>{state.offset+=40;rerender();}),{disabled:state.offset+40>=matches.length})]));
}
