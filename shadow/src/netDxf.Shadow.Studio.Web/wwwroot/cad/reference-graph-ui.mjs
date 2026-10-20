import {el} from './ui.mjs';
import {referenceLimits,referenceIdentity,referenceData,referenceLayout,hitReference,referenceSvg} from './reference-graph-model.mjs';

import {referenceOperations,referenceAnalysisRequest,mergeReferenceAnalysis,referenceAnalysisView,referenceImpactProof} from './reference-analysis-model.mjs';

/** Host-independent source-reference explorer. Diagram positions are not drawing coordinates. */
export function referenceGraphPalette({client,snapshot,report,selectSources,download}) {
  const values={operation:'explore',source:'',target:'',roots:'',direction:'outgoing',depth:2,maximumNodes:100,maximumEdges:500};
  let scene=null,identity='',selected=null,generation=0,lastContainer=null,url='',listOffset=0,selectedComponent=null,proof=null,condensed=false;
  const revoke=()=>{if(url){URL.revokeObjectURL(url);url='';}};
  const render=container=>{
    lastContainer=container;revoke();
    const epoch=++generation,key=client.key,scope=referenceIdentity(client.state);
    if(identity!==scope){scene=null;selected=null;listOffset=0;selectedComponent=null;proof=null;}
    const current=()=>generation===epoch&&container.isConnected&&snapshot()===null&&client.key===key;
    const guard=action=>()=>{if(!current())throw new Error('The drawing changed. Reopen the reference graph.');return action();};
    const button=(text,action,attrs={})=>el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
    const row=(label,input)=>el('label',{class:'property-row'},[el('span',{text:label}),input]);
    const root=el('div',{class:'reference-workspace'}),output=el('div',{'aria-live':'polite'}),detail=el('section',{class:'reference-inspector','aria-live':'polite'}),diagram=el('div',{class:'reference-diagram'}),list=el('div',{class:'reference-list'}),analysisOutput=el('section',{class:'reference-analysis-results'});
    container.replaceChildren(root);
    if(snapshot()!==null){root.append(el('p',{text:'Return to Current drawing to explore source references.'}));return;}
    let sequence=0,groupButtons=[];
    const updateComponentSelection=()=>{for(const [id,button] of groupButtons)button.setAttribute('aria-pressed',id===selectedComponent?'true':'false');};
    const changed=()=>{sequence++;scene=null;selected=null;selectedComponent=null;proof=null;listOffset=0;revoke();output.replaceChildren(el('p',{class:'hint',text:'Scope changed. Explore references to refresh.'}));diagram.replaceChildren();detail.replaceChildren();list.replaceChildren();analysisOutput.replaceChildren();runButton.disabled=false;};
    const roots=el('input',{'aria-label':'Reference root IDs',value:values.roots,placeholder:'Blank = document root',oninput:e=>{values.roots=e.target.value;changed();}});
    const direction=el('select',{'aria-label':'Reference direction'},[el('option',{value:'outgoing',text:'Outgoing dependencies'}),el('option',{value:'incoming',text:'Incoming users'})]);direction.value=values.direction;
    direction.addEventListener('change',()=>{values.direction=direction.value;changed();});
    const number=(name,label,min,max)=>row(label,el('input',{type:'number',min,max,step:1,value:values[name],'aria-label':label,oninput:e=>{values[name]=e.target.value;changed();}}));
    const focusExplore=(id,dir)=>{values.operation='explore';values.roots=id;values.direction=dir;scene=null;selected=null;proof=null;selectedComponent=null;render(container);return render.run();};
    const useRoots=(ids,dir=values.direction)=>{if(values.operation==='path'){if(!ids.length)throw new Error('Choose an explicit path object.');values.source=ids[0];if(ids[1])values.target=ids[1];scene=null;selected=null;render(container);return values.target?render.run():undefined;}values.roots=ids.join(' ');roots.value=values.roots;values.direction=dir;direction.value=dir;changed();return run();};
    const runButton=button(referenceOperations[values.operation],guard(()=>run()));
    const operation=el('select',{'aria-label':'Reference analysis'},Object.entries(referenceOperations).map(([value,text])=>el('option',{value,text})));operation.value=values.operation;
    operation.addEventListener('change',()=>render.setOperation(operation.value));
    const pathInputs={};
    const pathInput=key=>row(`Path ${key}`,pathInputs[key]=el('input',{'aria-label':`Reference path ${key}`,value:values[key],placeholder:'Captured object ID',oninput:e=>{values[key]=e.target.value;changed();}}));
    const setPath=(key,id)=>{values[key]=id;if(pathInputs[key]){pathInputs[key].value=id;changed();}else output.append(el('p',{class:'hint',text:`Path ${key} set to ${id}. Choose Dependency path to run.`}));};
    direction.disabled=values.operation==='impact';if(direction.disabled)direction.value='incoming';
    const rootSearch=el('input',{type:'search','aria-label':'Search reference roots',placeholder:'Scene objects, layers or blocks'});
    const rootChoices=el('select',{'aria-label':'Choose reference root'}),rootHint=el('p',{class:'hint'});
    const updateRoots=()=>{
      const candidates=new Map();
      for(const item of client.data?.items||[])candidates.set(item.id,{id:item.id,label:`${item.type} ${item.id} · ${item.layer||''}`});
      for(const item of client.data?.layers||[]){const id=item.id?.value||item.id;if(typeof id==='string')candidates.set(id,{id,label:`Layer ${item.name} · ${id}`});}
      for(const item of client.data?.blocks||[]){const id=item.id?.value||item.id;if(typeof id==='string')candidates.set(id,{id,label:`Block ${item.name} · ${id}`});}
      const q=rootSearch.value.toLowerCase(),matches=[...candidates.values()].filter(item=>item.label.toLowerCase().includes(q));
      rootChoices.replaceChildren(el('option',{value:'',text:'Document root'}),...matches.slice(0,200).map(item=>el('option',{value:item.id,text:item.label})));
      rootHint.textContent=`${matches.length} discovered roots; first ${Math.min(200,matches.length)} listed. Scene discovery is capped at 5,000 active roots. Enter any captured object ID above to explore it.`;
    };
    rootSearch.addEventListener('input',updateRoots);updateRoots();
    root.append(el('p',{class:'intro',text:'Explore directed source-object references and exact property paths. This diagram is separate from geometric topology, piping connections and drawing coordinates.'}),
      row('Analysis',operation),...(values.operation==='path'?[pathInput('source'),pathInput('target')]:[row('Roots',roots)]),el('div',{class:'toolbar'},[
        button('Use document root',guard(()=>useRoots([])),{disabled:values.operation==='path'}),
        button('Use drawing selection',guard(()=>{const ids=client.data?.selectedEntities||[];if(!ids.length)throw new Error('Select drawing objects first.');return useRoots(ids);})),
        button('Clear reference view',()=>render.clear())]),
      el('details',{},[el('summary',{text:'Find source roots'}),rootSearch,rootChoices,...(values.operation==='path'?[button('Use chosen path start',guard(()=>{if(!rootChoices.value)throw new Error('Choose an explicit object.');setPath('source',rootChoices.value);})),button('Use chosen path end',guard(()=>{if(!rootChoices.value)throw new Error('Choose an explicit object.');setPath('target',rootChoices.value);}))]:[button('Explore chosen root',guard(()=>useRoots(rootChoices.value?[rootChoices.value]:[])))]),rootHint]),
      row('Direction',direction),number('depth','Reference maximum depth',0,16),
      number('maximumNodes','Reference node budget',1,1000),number('maximumEdges','Reference edge budget',1,4000),
      el('div',{class:'toolbar'},[runButton]),output,diagram,analysisOutput,detail,list);
    render.run=()=>{if(!current())throw new Error('The drawing changed. Reopen references.');return run();};
    if(scene)show();

    async function run(more=false,all=false) {
      const request=++sequence,settings={...values};let next=more?scene:null,pages=0;
      if(!more){selected=null;selectedComponent=null;proof=null;}
      runButton.disabled=true;output.replaceChildren(el('p',{text:'Loading source references…'}));
      try{
        do{
          const response=await client.read('graph/query',{body:referenceAnalysisRequest(settings,client.state,next)});
          if(!current()||request!==sequence||!response)return;
          next=mergeReferenceAnalysis(next,referenceData(response,client.state));pages++;
          output.replaceChildren(el('p',{text:`Loading references: ${next.nodes.length} / ${next.nodeCount} objects, ${next.edges.length} / ${next.edgeCount} edges · ${pages} / ${referenceLimits.pages} pages${next.analysis==='components'?` · ${next.results.length} / ${next.resultCount} groups · ${next.links.length} / ${next.linkCount} condensed links`:''}.`}));
        }while(all&&next.partial&&pages<referenceLimits.pages);
        scene=next;identity=scope;listOffset=0;show();
      }catch(error){if(current()&&request===sequence){output.replaceChildren(el('p',{class:'diagnostic',text:error.message}));report(error);}}
      finally{if(current()&&request===sequence)runButton.disabled=false;}
    }
    function show() {
      output.replaceChildren(el('p',{class:'reference-stats',text:`${scene.nodes.length} / ${scene.nodeCount} objects · ${scene.edges.length} / ${scene.edgeCount} references · ${scene.objectsIndexed} indexed objects`}));
      const limits=[scene.nodeLimitReached?'node budget':null,scene.edgeLimitReached?'edge budget':null,scene.depthLimitReached?'depth frontier':null].filter(Boolean);
      if(scene.analysis!=='path')output.append(el('p',{class:limits.length||scene.partial?'graph-partial':'hint',text:limits.length?`Bounded exploration: ${limits.join(', ')} reached. Counts describe this exploration, not every reference in the drawing.`:'Complete reachable source graph within this scope.'}));
      if(scene.analysis==='path')output.append(el('p',{class:scene.status==='incomplete'?'graph-partial':'reference-path-status',text:scene.status==='found'?`Shortest dependency path found: ${scene.edgeCount} reference hops. ${scene.visitedNodes} objects visited; ${scene.examinedEdges} references examined.`:scene.status==='unreachable'?'No dependency path exists in this direction. The bounded search completed.':'Search incomplete: a configured budget was reached. This does not prove the target is unreachable.'}));
      if(scene.analysis==='components')output.append(el('p',{class:'hint',text:'Cyclic groups are informational source-object relationships. A partial scope may split a larger strongly connected group; it cannot establish global acyclicity.'}));
      if(scene.analysis==='impact')output.append(el('p',{class:'hint',text:'Structural impact follows incoming references. Select an object for its shortest proof back to a seed. This does not simulate the effects of editing that object.'}));
      if(scene.partial)output.append(el('p',{class:'graph-partial',text:'Partial pages loaded. Diagram, lists and exports contain loaded items only.'}));
      const actions=[button('Load more references',guard(()=>run(true)),{disabled:!scene.partial}),
        button('Load remaining references (bounded)',guard(()=>run(true,true)),{disabled:!scene.partial})];
      if(download)actions.push(button('Export reference JSON',guard(()=>download('source-references.json',JSON.stringify({identity:client.state,scope:{...values},graph:scene},null,2),'application/json'))),
        button('Export reference SVG',guard(()=>download('source-references.svg',referenceSvg(viewScene(),selected),'image/svg+xml'))));
      output.append(el('div',{class:'toolbar'},actions));
      if(referenceLayout(viewScene()).truncated)output.append(el('p',{class:'hint',text:`Diagram limited to ${referenceLimits.diagramNodes} nodes / ${referenceLimits.diagramEdges} edges; use paged object lists for every loaded object. Arrows retain source-to-target direction.`}));
      output.append(el('p',{class:'reference-legend',text:scene.analysis==='components'?'Colored borders identify groups; orange borders mark cycles. Gold highlights the chosen group.':scene.analysis==='path'?'Gold highlights the ordered dependency path.':scene.analysis==='impact'?'Gold highlights the selected object’s proof back to an impact seed.':'Arrows retain exact source-property direction.'}));
      if(referenceLayout(viewScene()).rankCompressed)output.append(el('p',{class:'hint',text:`Diagram columns are capped at ${referenceLimits.diagramColumns}; deeper ranks share the last column. Tooltips and exported SVG retain actual rank.`}));
      showAnalysis();draw();showList();if(selected)inspect(selected);else if(selectedComponent!=null)inspectComponent(selectedComponent);
    }
    function viewScene(){return referenceAnalysisView(scene,{component:selectedComponent,proof,condensed});}
    function showAnalysis(){
      analysisOutput.replaceChildren();groupButtons=[];
      if(scene.analysis!=='components')return;
      const toggle=el('select',{'aria-label':'Reference diagram view'},[el('option',{value:'objects',text:'Objects and reference links'}),el('option',{value:'condensed',text:'Condensation DAG'})]);toggle.value=condensed?'condensed':'objects';
      toggle.addEventListener('change',()=>{if(!current())return;condensed=toggle.value==='condensed';draw();});
      analysisOutput.append(row('Diagram',toggle),el('h3',{text:`${scene.results.length} / ${scene.resultCount} groups · ${scene.links.length} / ${scene.linkCount} condensation links`}));
      let offset=0;const body=el('div',{class:'reference-components'});analysisOutput.append(body);
      const page=()=>{
        body.replaceChildren();groupButtons=[];
        for(const group of scene.results.slice(offset,offset+25)){const item=button(`Group ${group.id} · ${group.nodeCount} objects · ${group.cyclic?'cyclic':'acyclic'}`,guard(()=>inspectComponent(group.id)),{'aria-pressed':group.id===selectedComponent?'true':'false'});groupButtons.push([group.id,item]);body.append(item);}
        if(scene.results.length>25)body.append(el('div',{class:'toolbar'},[button('Previous groups',guard(()=>{offset=Math.max(0,offset-25);page();}),{disabled:offset===0}),button('Next groups',guard(()=>{offset+=25;page();}),{disabled:offset+25>=scene.results.length})]));
      };page();
    }
    function inspectComponent(id){
      selectedComponent=id;selected=null;proof=null;updateComponentSelection();draw();showList();
      const group=scene.results.find(g=>g.id===id);if(!group)return;
      const members=scene.nodes.filter(n=>n.component===id),links=scene.links.filter(e=>e.source===id||e.target===id);
      detail.replaceChildren(el('h3',{text:`Strongly connected group ${id}`}),el('p',{text:`${members.length} / ${group.nodeCount} member objects loaded · ${group.internalEdgeCount} internal references · ${group.cyclic?'cyclic':'acyclic'}`}));
      if(!scene.explorationComplete)detail.append(el('p',{class:'graph-partial',text:'This group is computed within a partial scope and may belong to a larger component.'}));
      const membersList=el('div',{class:'reference-components'});detail.append(membersList);
      let offset=0;const page=()=>{membersList.replaceChildren(...members.slice(offset,offset+25).map(n=>button(`Inspect ${n.id}`,guard(()=>inspect(n.id)))));if(members.length>25)membersList.append(el('div',{class:'toolbar'},[button('Previous members',guard(()=>{offset=Math.max(0,offset-25);page();}),{disabled:offset===0}),button('Next members',guard(()=>{offset+=25;page();}),{disabled:offset+25>=members.length})]));};page();
      const linkList=el('div',{class:'reference-condensation-links'});detail.append(el('p',{class:'hint',text:`${links.length} loaded incident condensation links. Each count includes parallel property references.`}),linkList);
      let linkOffset=0;const linkPage=()=>{linkList.replaceChildren(...links.slice(linkOffset,linkOffset+25).map(e=>el('p',{text:`Group ${e.source} → Group ${e.target} · ${e.referenceCount} references`})));if(links.length>25)linkList.append(el('div',{class:'toolbar'},[button('Previous group links',guard(()=>{linkOffset=Math.max(0,linkOffset-25);linkPage();}),{disabled:linkOffset===0}),button('Next group links',guard(()=>{linkOffset+=25;linkPage();}),{disabled:linkOffset+25>=links.length})]));};linkPage();
    }
    function draw() {
      revoke();diagram.replaceChildren();
      const visual=viewScene(),layout=referenceLayout(visual,selected);
      url=URL.createObjectURL(new Blob([referenceSvg(visual,selected)],{type:'image/svg+xml'}));
      const image=el('img',{src:url,width:layout.width,height:layout.height,alt:`Directed object references, ${scene.direction}. Use the object list to inspect by keyboard.`});
      image.onclick=event=>{try{if(!current())throw new Error('The drawing changed. Explore again.');const box=image.getBoundingClientRect(),hit=hitReference(layout,(event.clientX-box.left)*layout.width/box.width,(event.clientY-box.top)*layout.height/box.height);if(hit){if(visual.condensed)inspectComponent(hit.component);else inspect(hit.id);}}catch(error){report(error);}};
      diagram.append(image);
    }
    function showList() {
      list.replaceChildren(el('h3',{text:'Loaded objects'}));
      for(const node of scene.nodes.slice(listOffset,listOffset+25))list.append(button(`${node.id} · ${String(node.type||'Object').split('.').at(-1)}${node.name?' · '+node.name:''}`,guard(()=>inspect(node.id)),{'aria-pressed':node.id===selected?'true':'false'}));
      list.append(el('p',{class:'hint',text:`Objects ${scene.nodes.length?listOffset+1:0}–${Math.min(listOffset+25,scene.nodes.length)} of ${scene.nodes.length} loaded.`}),
        el('div',{class:'toolbar'},[button('Previous objects',guard(()=>{listOffset=Math.max(0,listOffset-25);showList();}),{disabled:listOffset===0}),
          button('Next objects',guard(()=>{listOffset+=25;showList();}),{disabled:listOffset+25>=scene.nodes.length})]));
    }
    function inspect(id) {
      const node=scene.nodes.find(item=>item.id===id);if(!node)return;
      selected=id;selectedComponent=null;updateComponentSelection();proof=scene.analysis==='impact'?referenceImpactProof(scene,id):null;draw();showList();detail.replaceChildren(el('h3',{text:`Object ${id}`}),el('p',{text:`${node.type} · ${node.kind}${node.name?' · '+node.name:''}`}),
        el('p',{text:`Depth ${node.depth} · ${node.incoming} incoming / ${node.outgoing} outgoing references in the source index.`}));
      if(node.component!=null)detail.append(el('p',{text:`Strongly connected group ${node.component} · ${node.cyclic?'cyclic':'acyclic'} within this scope.`}));
      if(scene.analysis==='impact'){
        const proofBox=el('section',{class:'reference-proof'},[el('h4',{text:'Why this object is affected'}),el('p',{text:proof.complete?`${proof.edges.length} reference hops to an impact seed.`:proof.reason})]);
        for(const edge of proof.edges)proofBox.append(el('code',{text:`${edge.source} ${edge.path} → ${edge.target}`}));
        detail.append(proofBox);
      }
      if(node.exists===false){detail.append(el('p',{class:'diagnostic',text:'This captured reference points to a missing object.'}));return;}
      detail.append(el('div',{class:'toolbar'},[button('Explore dependencies',guard(()=>{return focusExplore(id,'outgoing');})),button('Explore users',guard(()=>{return focusExplore(id,'incoming');})),button('Use as path start',guard(()=>{setPath('source',id);})),button('Use as path end',guard(()=>{setPath('target',id);}))]));
      const selectable=(client.data?.items||[]).some(item=>item.id===id);
      if(selectSources)detail.append(button('Select source object',guard(()=>selectSources([id],key)),{disabled:!selectable}));
      if(!selectable)detail.append(el('p',{class:'hint',text:'Selection unavailable: this object is not in the discovered active scene roots. It may be a shared definition, resource, inactive-layout object, or beyond the 5,000-root discovery cap.'}));
      const relations=scene.edges.filter(edge=>edge.source===id||edge.target===id);
      detail.append(el('p',{class:'hint',text:`${relations.length} loaded incident references. Paths name the source property; collections use zero-based indices.`}));
      const relationList=el('div',{class:'reference-relations'});detail.append(relationList);
      let offset=0;
      const relationsPage=()=>{
        relationList.replaceChildren();
        for(const edge of relations.slice(offset,offset+25)){
          const card=el('section',{class:'reference-edge'},[el('code',{text:`${edge.source} ${edge.path} → ${edge.target}`})]);
          for(const endpoint of [...new Set([edge.source,edge.target])])card.append(button(`Inspect ${endpoint}`,guard(()=>{if(scene.nodes.some(n=>n.id===endpoint))inspect(endpoint);else return useRoots([endpoint]);})));
          relationList.append(card);
        }
        if(relations.length>25)relationList.append(el('div',{class:'toolbar'},[
          button('Previous references',guard(()=>{offset=Math.max(0,offset-25);relationsPage();}),{disabled:offset===0}),
          button('Next references',guard(()=>{offset+=25;relationsPage();}),{disabled:offset+25>=relations.length})]));
      };relationsPage();
    }
  };
  render.setOperation=operation=>{if(!Object.hasOwn(referenceOperations,operation))throw new Error('Unknown reference analysis.');values.operation=operation;scene=null;selected=null;selectedComponent=null;proof=null;listOffset=0;generation++;revoke();if(lastContainer?.isConnected)render(lastContainer);};
  render.hide=()=>{generation++;revoke();};
  render.clear=()=>{scene=null;selected=null;selectedComponent=null;proof=null;listOffset=0;generation++;revoke();if(lastContainer?.isConnected)render(lastContainer);};
  return render;
}
