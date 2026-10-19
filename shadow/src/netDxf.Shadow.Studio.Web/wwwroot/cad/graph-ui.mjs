import { el } from './ui.mjs';
import { format, fit } from './core.mjs';
import { graphOperations, graphRequest, graphProjectionRequest, graphData, graphOverlay } from './graph-contracts.mjs';
import { graphLimits, graphModes, graphIdentity, mergeGraphProjection, graphResultItems, graphPresentation, graphLegend, graphBounds, graphSourceLinks, graphSvg } from './graph-visualization.mjs';

/** Reusable graph workspace; topology reads never mutate or automatically select drawing objects. */
export function graphPalette({client, viewport, report, snapshot, selectSources, beginPick, download}) {
  const values={endpointTolerance:.01,curveTolerance:.05,splitIntersections:true,respectElevation:true,limit:50,source:0,target:1,depth:8};
  const visual={mode:'components',showNodes:true,showEdges:true,labels:'none',dimDrawing:false};
  let operation='summary',saved=null,savedIdentity='',savedOperation='',savedOffset=0,generation=0,projection=null,projectionIdentity='',buildKey='',buildIdentity='',result=null,selected=null,overlayShown=true,lastContainer=null,loadCurrent=null;
  let thumbnailUrl='';
  const revokeThumbnail=()=>{if(thumbnailUrl){URL.revokeObjectURL(thumbnailUrl);thumbnailUrl='';}};
  const row=(label,input)=>el('label',{class:'property-row'},[el('span',{text:label}),input]);
  const render=function(container) {
    revokeThumbnail();
    lastContainer=container;
    const epoch=++generation,renderedKey=client.key,inputs=new Map();let requestSequence=0,projectionSequence=0;
    viewport.cancelGraphPick?.();
    if(projectionIdentity!==graphIdentity(client.state)){projection=null;result=null;selected=null;}
    if(savedIdentity!==graphIdentity(client.state)){saved=null;result=null;}
    if(buildIdentity!==graphIdentity(client.state))buildKey='';
    const current=()=>epoch===generation&&container.isConnected&&snapshot()===null;
    const guard=action=>()=>{if(!current()||renderedKey!==client.key)throw new Error('The drawing changed. Open the graph workspace again.');return action();};
    const button=(text,action,attrs={})=>el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
    const apply=()=>{
      if(!overlayShown||!projection&&!result){viewport.graph=null;viewport.invalidate();return;}
      const scene=graphPresentation(projection,result,{...visual,selected});
      if(viewport.setGraph)viewport.setGraph(scene);else{viewport.graph=scene;viewport.graphKey=graphIdentity(client.state);viewport.invalidate();}
    };
    const invalidate=(topology=false)=>{
      requestSequence++;saved=null;result=null;runButton.disabled=false;viewport.cancelGraphPick?.();
      projectionSequence++;loadButton.disabled=false;
      if(topology){projection=null;projectionIdentity='';buildKey='';selected=null;}
      output.replaceChildren(el('p',{class:'hint',text:'Inputs changed. Run analysis to update the results.'}));apply();showVisual();
    };
    const field=(name,label,options={},topology=false)=>{
      const input=el('input',{type:'number',step:'any',value:values[name]??'','aria-label':label,...options});
      input.addEventListener('input',()=>{values[name]=input.value;invalidate(topology);});inputs.set(name,input);return row(label,input);
    };
    const check=(name,label)=>row(label,el('input',{type:'checkbox','aria-label':label,checked:values[name],onchange:e=>{values[name]=e.target.checked;invalidate(true);}}));
    const textField=(name,label,topology=false)=>row(label,el('input',{value:values[name]??'','aria-label':label,oninput:e=>{values[name]=e.target.value;invalidate(topology);}}));
    const output=el('div',{class:'graph-results','aria-live':'polite'}),overview=el('div',{class:'graph-overview'}),legend=el('div',{class:'graph-legend'}),inspector=el('div',{class:'graph-inspector','aria-live':'polite'}),pickNotice=el('p',{class:'hint','aria-live':'polite'});
    const runButton=button('Run analysis',()=>query(0)),loadButton=button('Load topology',()=>loadTopology(false));
    const select=el('select',{'aria-label':'Graph analysis operation'},Object.entries(graphOperations).map(([value,text])=>el('option',{value,text})));select.value=operation;
    select.addEventListener('change',()=>{operation=select.value;saved=null;result=null;render(container);});
    const mode=el('select',{'aria-label':'Graph coloring'},Object.entries(graphModes).map(([value,text])=>el('option',{value,text})));mode.value=visual.mode;
    mode.addEventListener('change',()=>{visual.mode=mode.value;overlayShown=true;apply();showVisual();});
    const labels=el('select',{'aria-label':'Graph labels'},Object.entries({none:'No labels',nodes:'Node IDs',edges:'Edge IDs',all:'Node and edge IDs'}).map(([value,text])=>el('option',{value,text})));labels.value=visual.labels;
    labels.addEventListener('change',()=>{visual.labels=labels.value;apply();showVisual();});
    const visibility=(key,label)=>row(label,el('input',{type:'checkbox','aria-label':label,checked:visual[key],onchange:e=>{visual[key]=e.target.checked;apply();showVisual();}}));
    const fitScene=scene=>{if(viewport.fitGraph)viewport.fitGraph(scene);else{const bounds=graphBounds(scene);if(bounds&&viewport.size){viewport.camera=fit(bounds,viewport.size);viewport.changed();}}};
    const pick=kind=>guard(async()=>{
      if(!projection&&!result)await loadTopology(false);
      if(!current())return;
      overlayShown=true;apply();if(!viewport.graph)return;
      beginPick?.();
      if(kind!=='inspect'){visual.showNodes=true;apply();}
      const key=client.key;
      viewport.beginGraphPick?.({kind:kind==='inspect'?'any':'node',onCancel:()=>{pickNotice.textContent='Graph picking cancelled.';},onPick:hit=>{
        if(!current()||key!==client.key)throw new Error('The drawing changed during graph picking. Reload topology.');
        pickNotice.textContent='';
        if(kind==='inspect')inspect(hit);else{values[kind]=hit.node.id;inputs.get(kind)&&(inputs.get(kind).value=hit.node.id);invalidate();pickNotice.textContent=`${kind==='source'?'Start':'End'} node: ${hit.node.id}.`;}
      }});
      pickNotice.textContent=kind==='inspect'?'Click a visible node or edge in the drawing. Escape cancels.':`Click a visible ${kind==='source'?'start':'end'} node in the drawing. Escape cancels.`;
    });
    const workspace=el('div',{class:'graph-workspace'},[el('p',{class:'intro',text:'Explore sampled topology in drawing coordinates. Colors, picks and exports describe the loaded projection; partial coverage is always shown.'})]);
    container.replaceChildren(workspace);
    if(snapshot()!==null){workspace.append(el('p',{text:'Return to Current drawing to analyze topology. The retained view is not the active graph.'}));return;}
    workspace.append(el('div',{class:'toolbar graph-primary'},[loadButton,button('Fit graph',guard(()=>fitScene(viewport.graph))),button('Inspect in drawing',pick('inspect')),button('Show overlay',guard(()=>{overlayShown=true;apply();})),button('Clear overlay',()=>render.clearOverlay())]),overview,
      el('details',{class:'graph-display',open:true},[el('summary',{text:'Visualization'}),row('Color by',mode),row('Labels',labels),el('div',{class:'graph-visibility'},[visibility('showNodes','Nodes'),visibility('showEdges','Edges'),visibility('dimDrawing','Dim drawing')]),legend,
        el('p',{class:'hint',text:`Labels are limited to ${graphLimits.labels} visible items. Overlapping elevations share an XY view; inspect Z to distinguish them.`})]),pickNotice,inspector,
      el('details',{},[el('summary',{text:'Projection filters'}),field('projectionComponent','Projection component',{min:0,step:1},true),textField('projectionEntity','Projection source entity',true),textField('projectionRootEntity','Projection root entity',true),textField('projectionOccurrence','Projection occurrence',true)]),
      row('Analysis',select));
    if(['path','traverse'].includes(operation))workspace.append(field('source','Start node',{min:0,step:1}),button('Pick start node',pick('source')));
    if(operation==='path')workspace.append(field('target','End node',{min:0,step:1}),button('Pick end node',pick('target')));
    if(operation==='cycle')workspace.append(field('cycle','Cycle ID',{min:0,step:1}));
    if(operation==='traverse')workspace.append(field('depth','Maximum hops',{min:0,max:128,step:1}));
    if(operation==='nearest')workspace.append(...['x','y','z'].map(axis=>field(axis,axis.toUpperCase())),field('maximumDistance','Maximum distance (optional)',{min:0}));
    if(['nodes','edges'].includes(operation))workspace.append(field('component','Component (optional)',{min:0,step:1}),field('node','Node ID (optional)',{min:0,step:1}));
    if(operation==='nodes')workspace.append(field('minimumDegree','Minimum degree',{min:0,step:1}),field('maximumDegree','Maximum degree',{min:0,step:1}));
    if(operation==='edges')workspace.append(field('edge','Edge ID (optional)',{min:0,step:1}),textField('entity','Source entity filter'),textField('rootEntity','Root entity filter'),textField('occurrence','Exact occurrence filter'));
    workspace.append(el('details',{},[el('summary',{text:'Topology tolerances and budgets'}),field('endpointTolerance','Endpoint tolerance',{min:0},true),field('curveTolerance','Curve tolerance',{min:0},true),
      check('splitIntersections','Split intersections'),check('respectElevation','Respect elevation'),field('maximumSegments','Maximum segments',{min:1,max:250000,step:1,placeholder:'100000'},true),
      field('maximumCandidatePairs','Candidate pair budget',{min:1,max:20000000,step:1,placeholder:'2000000'},true),
      el('p',{class:'hint',text:'Curves are sampled; tighter tolerances increase work. Hidden layers, annotation strokes and presentation-only geometry are excluded. Exceeding a build budget reports an error.'})]));
    if(!['summary','nearest'].includes(operation))workspace.append(field('limit','Page / visit limit',{min:1,max:200,step:1}));
    workspace.append(el('div',{class:'toolbar'},[runButton]),output);
    showVisual();
    if(saved&&savedIdentity===graphIdentity(client.state)&&savedOperation===operation)show(saved,savedOffset);
    if(projection||result)apply();
    loadCurrent=()=>loadTopology(false);

    function projectionValues(){return {...values,graphKey:undefined,component:values.projectionComponent,entity:values.projectionEntity,rootEntity:values.projectionRootEntity,occurrence:values.projectionOccurrence};}
    function hasMore(scene){return scene&&((scene.nextNode!==null&&scene.nodes.length<graphLimits.nodes)||(scene.nextEdge!==null&&scene.edges.length<graphLimits.edges));}
    async function loadTopology(more,remaining=false) {
      const sequence=++projectionSequence,key=client.key,identity=graphIdentity(client.state),settings=projectionValues();let previous=more?projection:null,pages=0;
      requestSequence++;runButton.disabled=true;loadButton.disabled=true;overview.replaceChildren(el('p',{text:'Loading graph projection…'}));
      try{
        do{
          const offsets=previous?{nodeOffset:previous.nodes.length>=graphLimits.nodes?previous.nodeTotal:previous.nextNode??previous.nodeTotal,edgeOffset:previous.edges.length>=graphLimits.edges?previous.edgeTotal:previous.nextEdge??previous.edgeTotal}:{};
          const body=graphProjectionRequest({...settings,graphKey:previous?.graphKey},client.state,offsets),response=await client.read('graph/query',{body});
          if(sequence!==projectionSequence||!current()||key!==client.key||!response)return;
          const data=graphData(response,client.state);projection=mergeGraphProjection(previous,data);projectionIdentity=identity;buildKey=data.graphKey;buildIdentity=identity;overlayShown=true;previous=projection;pages++;
          if(remaining)overview.replaceChildren(el('p',{class:'hint',text:`Loading remaining topology: ${pages} / ${graphLimits.projectionPages} pages · ${projection.nodes.length} / ${projection.nodeTotal} nodes · ${projection.edges.length} / ${projection.edgeTotal} edges. Clear overlay cancels.`}));
        }while(remaining&&hasMore(projection)&&pages<graphLimits.projectionPages);
        if(remaining&&hasMore(projection)&&pages===graphLimits.projectionPages)projection={...projection,pageBudgetReached:true};
        if(!more){result=null;selected=null;saved=null;output.replaceChildren(el('p',{class:'hint',text:'Topology loaded. Choose an analysis or inspect an item.'}));}
        apply();showVisual();
      }catch(error){if(sequence===projectionSequence&&current()&&key===client.key){overview.replaceChildren(el('p',{class:'diagnostic',text:error.message}));report(error);}}
      finally{if(sequence===projectionSequence){loadButton.disabled=false;runButton.disabled=false;}}
    }
    function showVisual() {
      revokeThumbnail();
      overview.replaceChildren();legend.replaceChildren();
      if(!projection){overview.append(el('p',{class:'hint',text:'Load topology to color components, inspect features and pick graph endpoints.'}));return;}
      const key=client.key,identity=projectionIdentity;
      const use=action=>()=>{if(!current()||identity!==graphIdentity(client.state)||key!==client.key)throw new Error('The drawing changed. Reload topology before using this view.');return action();};
      overview.append(el('div',{class:'graph-stats'},[el('strong',{text:`${projection.nodes.length} / ${projection.nodeTotal} nodes`}),el('strong',{text:`${projection.edges.length} / ${projection.edgeTotal} edges`}),el('span',{text:`Graph total: ${projection.componentCount??'—'} components · ${projection.cycleRank??'—'} independent cycles`})]));
      overview.append(el('p',{class:projection.partial?'graph-partial':'hint',text:projection.partial?`Partial topology. Only loaded nodes and edges can be inspected or exported.${projection.capped?' Workspace display budget reached. Narrow the projection filters.':''}${projection.pageBudgetReached?` Stopped after ${graphLimits.projectionPages} continuation pages. Load remaining again to continue, or narrow the filters.`:''}`:'Complete filtered topology loaded.'}));
      const overviewScene=graphPresentation(projection,result,visual);
      thumbnailUrl=URL.createObjectURL(new Blob([graphSvg(overviewScene,{title:'Loaded graph overview',maximumEdges:1200,maximumNodes:600})],{type:'image/svg+xml'}));
      overview.append(el('img',{class:'graph-thumbnail',src:thumbnailUrl,alt:'Loaded graph overview. Use Fit graph to explore in the drawing.'}));
      if(overviewScene.edges.length>1200||overviewScene.nodes.length>600)overview.append(el('small',{class:'hint',text:'Overview thumbnail limited to 1,200 edges / 600 nodes; main viewport uses all loaded items.'}));
      const more=hasMore(projection);
      const actions=[button('Load more topology',use(()=>loadTopology(true)),{disabled:!more}),button('Load remaining topology (bounded)',use(()=>loadTopology(true,true)),{disabled:!more})];
      if(download)actions.push(button('Export graph JSON',use(()=>download('graph-topology.json',JSON.stringify({identity:client.state,projection,result,visual},null,2),'application/json'))),button('Export graph SVG',use(()=>download('graph-topology.svg',graphSvg(graphPresentation(projection,result,visual)),'image/svg+xml'))));
      overview.append(el('div',{class:'toolbar'},actions));
      for(const entry of graphLegend(graphPresentation(projection,result,visual)))legend.append(el('span',{class:'graph-legend-item'},[el('i',{class:`graph-color-${entry.color.slice(1)}`,'aria-hidden':'true'}),el('span',{text:entry.label})]));
      if(visual.mode==='components'&&projection.componentCount>12)legend.append(el('small',{text:'Colors repeat after 12 components; IDs distinguish them. First 24 loaded components shown.'}));
    }
    function inspect(hit) {
      const key=client.key;
      const use=action=>()=>{if(!current()||key!==client.key)throw new Error('The drawing changed. Inspect the current topology again.');return action();};
      selected={kind:hit.kind,id:hit.id};apply();inspector.replaceChildren();
      const item=hit.node||hit.edge;
      inspector.append(el('h3',{text:`${hit.kind==='node'?'Node':'Edge'} ${item.id}`}),button('Clear inspection',()=>{selected=null;inspector.replaceChildren();apply();}));
      if(hit.kind==='node'){
        inspector.append(el('p',{text:`${format(item.x)}, ${format(item.y)}, ${format(item.z||0)} · degree ${item.degree??'—'} · component ${item.component??'—'}`}),el('div',{class:'toolbar'},[
          button('Use as start',use(()=>{values.source=item.id;operation='path';saved=null;render(container);})),button('Use as end',use(()=>{values.target=item.id;operation='path';saved=null;render(container);})),button('Incident edges',use(()=>{values.node=item.id;operation='edges';saved=null;render(container);}))]));
      }else{
        inspector.append(el('p',{text:`Node ${item.a} → ${item.b} · length ${format(item.length)} · component ${item.component??'—'}${item.bridge?' · bridge':''}`}),el('p',{text:`Source ${item.entity} · root ${item.rootEntity||item.entity}`}),el('small',{text:item.occurrence||''}));
        if(selectSources)inspector.append(button('Select source in drawing',sourceAction({edges:[item]})));
        inspector.append(button('All edges from occurrence',use(()=>{values.occurrence=item.occurrence;values.entity=item.entity;operation='edges';saved=null;render(container);})));
      }
    }
    function sourceAction(scene) {
      const key=client.key,identity=graphIdentity(client.state);
      return async()=>{if(!current()||key!==client.key||identity!==graphIdentity(client.state))throw new Error('The drawing changed. Run analysis again before selecting sources.');const sources=graphSourceLinks(scene);if(!sources.length)throw new Error('This result has no source-linked edges. Inspect incident edges for a node.');await selectSources(sources,key);};
    }
    async function query(offset,all=false) {
      const sequence=++requestSequence,key=client.key,currentOperation=operation;
      runButton.disabled=true;output.replaceChildren(el('p',{text:all?'Loading bounded result pages…':'Analyzing topology…'}));
      try{
        const args={...values,graphKey:projection?.graphKey||buildKey||undefined},body=graphRequest(currentOperation,args,client.state,offset);
        let response=await client.read('graph/query',{body});
        if(sequence!==requestSequence||!current()||key!==client.key||!response)return;
        let data=graphData(response,client.state);
        if(all){
          const accumulated=[...graphResultItems(data)],seen=new Set([offset]);let page=data.steps||data,pages=1;
          while(page.nextOffset!==null&&page.nextOffset!==undefined&&accumulated.length<graphLimits.resultItems&&pages<graphLimits.resultPages){
            if(!Number.isSafeInteger(page.nextOffset)||page.nextOffset<0||seen.has(page.nextOffset))throw new Error('The result repeated an invalid page cursor.');seen.add(page.nextOffset);
            body.arguments.offset=page.nextOffset;body.arguments.graphKey=data.graphKey||args.graphKey;
            response=await client.read('graph/query',{body});if(sequence!==requestSequence||!current()||key!==client.key||!response)return;
            const next=graphData(response,client.state);if(data.graphKey&&next.graphKey!==data.graphKey)throw new Error('The topology changed between result pages. Run analysis again.');
            page=next.steps||next;const incoming=graphResultItems(next),accepted=incoming.slice(0,graphLimits.resultItems-accumulated.length);accumulated.push(...accepted);pages++;
            if(accepted.length<incoming.length)page={...page,nextOffset:offset+accumulated.length};
          }
          const merged={...(data.steps||data),items:accumulated,nextOffset:page.nextOffset};data=data.steps?{...data,steps:merged}:{...data,...merged};data={...data,loadedTogether:true};
        }
        saved=data;savedIdentity=graphIdentity(client.state);savedOperation=currentOperation;savedOffset=offset;buildKey=data.graphKey||buildKey;buildIdentity=savedIdentity;show(data,offset);
      }catch(error){if(sequence===requestSequence&&current()&&key===client.key){output.replaceChildren(el('p',{class:'diagnostic',text:error.message}));report(error);}}
      finally{if(sequence===requestSequence)runButton.disabled=false;}
    }
    function overlay(data){result=graphOverlay(data);visual.mode='result';mode.value='result';overlayShown=true;apply();showVisual();}
    function focus(node){viewport.camera.x=node.x;viewport.camera.y=node.y;viewport.changed();}
    function show(data,offset) {
      const resultKey=client.key;
      const useResult=action=>()=>{if(resultKey!==client.key||!current())throw new Error('The drawing changed. Run analysis again before using this result.');return action();};
      output.replaceChildren();
      for(const [key,label] of Object.entries({nodeCount:'Nodes',edgeCount:'Edges',componentCount:'Components',cycleRank:'Independent cycles',bridgeCount:'Bridges',articulationPointCount:'Articulation points',deadEndCount:'Dead ends',junctionCount:'Junctions',isolatedNodeCount:'Isolated nodes',totalLength:'Total length',diagnosticCount:'Diagnostics'}))if(data[key]!==undefined)output.append(row(label,el('span',{text:format(data[key])})));
      if(download)output.append(button('Export result JSON',useResult(()=>download(`graph-${operation}.json`,JSON.stringify({identity:client.state,operation,arguments:graphRequest(operation,values,client.state,offset).arguments,data},null,2),'application/json'))));
      if(operation==='summary')return;
      if(operation==='path')output.append(el('p',{text:data.reachable?`Path length ${format(data.length)} · ${data.edgeCount} edges`:'No path: the nodes belong to disconnected components.'}));
      if(operation==='cycle')output.append(el('p',{text:`Cycle ${data.cycle} · length ${format(data.length)} · ${data.edgeCount} edges · closed path`}));
      if(operation==='cycles')output.append(el('p',{class:'hint',text:'An independent fundamental cycle basis. Each closing edge defines a loop; inspect a cycle to follow its ordered geometry.'}));
      if(operation==='spanning_forest')output.append(el('p',{text:`Minimum forest length ${format(data.length)} · ${data.componentCount} components · ${data.edgeCount} edges`}));
      if(operation==='traverse')output.append(el('p',{text:`${data.nodes.length} visited nodes${data.nodeLimitReached?' · node limit reached':''}${data.depthLimitReached?' · hop limit reached':''}`}));
      if(operation==='nearest')output.append(el('p',{text:data.found?`Node ${data.node.id} · distance ${format(data.distance)}`:'No node within the requested distance.'}));
      const page=data.steps||data,items=graphResultItems(data),scene=graphOverlay(data);
      if(overlayShown&&projection&&(scene.nodes.length||scene.edges.length))overlay(data);
      if(!['components','diagnostics','cycles'].includes(operation)){
        const actions=[button('Highlight this result',useResult(()=>overlay(data))),button('Fit result',useResult(()=>{overlay(data);fitScene(scene);}))];
        if(selectSources&&scene.edges.length)actions.push(button('Select result sources',sourceAction(scene)));
        output.append(el('div',{class:'toolbar'},actions));
      }
      if(page.total!==undefined){output.append(el('p',{class:'hint',text:`${page.total} matches · showing ${items.length?offset+1:0}–${offset+items.length}`}));if(items.length<page.total)output.append(el('p',{class:'graph-partial',text:`Partial result. Highlights and exports include these ${items.length} loaded items only.`}));}
      if(page.nextOffset!==null&&page.nextOffset!==undefined&&operation!=='traverse')output.append(button('Load full result (bounded)',useResult(()=>query(0,true))));
      if(data.loadedTogether)output.append(el('p',{class:'hint',text:`Loaded across pages; limited to ${graphLimits.resultItems} items / ${graphLimits.resultPages} pages.`}));
      if(items.length>200)output.append(el('p',{class:'hint',text:'The first 200 item cards are shown below. Highlight, fit, source selection and export use all loaded items; use ordinary pages for individual inspection.'}));
      for(const item of items.slice(0,200)){
        const node=item.node||item,edge=item.viaEdge||(item.a!==undefined?item:null),card=el('section',{class:'layer-card graph-result-card'});
        if(Number.isFinite(node.x)&&Number.isFinite(node.y)){
          card.append(el('b',{text:`Node ${node.id} · degree ${node.degree??'—'}`}),el('p',{text:`${format(node.x)}, ${format(node.y)}, ${format(node.z||0)}${item.depth!==undefined?` · hop ${item.depth}`:''}`}));
          card.append(el('div',{class:'toolbar'},[button('Focus',useResult(()=>focus(node))),button('Inspect node',useResult(()=>inspect({kind:'node',id:node.id,node}))),button('Start',useResult(()=>{values.source=node.id;inputs.get('source')&&(inputs.get('source').value=node.id);invalidate();})),button('End',useResult(()=>{values.target=node.id;inputs.get('target')&&(inputs.get('target').value=node.id);invalidate();}))]));
        }else if(operation==='components')card.append(el('b',{text:`Component ${item.id}`}),el('p',{text:`${item.nodeCount} nodes · ${item.edgeCount} edges · ${item.cycleRank} cycles · length ${format(item.totalLength)}`}),button('Inspect nodes',useResult(()=>{values.component=item.id;operation='nodes';saved=null;render(container);})),button('Visualize component',useResult(()=>{values.projectionComponent=item.id;projection=null;result=null;render(container);return loadCurrent();})));
        else if(operation==='cycles')card.append(el('b',{text:`Cycle ${item.id}`}),el('p',{text:`${item.edgeCount} edges · closing edge ${item.closingEdge}`}),button('Inspect cycle',useResult(()=>{values.cycle=item.id;operation='cycle';saved=null;render(container);})));
        else if(operation==='diagnostics')card.append(el('b',{text:item.code}),el('p',{text:`${item.severity}: ${item.message}`}));
        if(edge)card.append(el('b',{text:`Edge ${edge.id}: ${edge.a} → ${edge.b}`}),el('p',{text:`Length ${format(edge.length)} · ${edge.entity}`}),el('small',{text:edge.occurrence}),button('Inspect edge',useResult(()=>inspect({kind:'edge',id:edge.id,edge}))));
        output.append(card);
      }
      if(page.total!==undefined)output.append(el('div',{class:'toolbar'},[button('Previous',useResult(()=>query(Math.max(0,offset-Number(values.limit)))),{disabled:offset===0}),button('Next',useResult(()=>query(page.nextOffset)),{disabled:page.nextOffset===null||page.nextOffset===undefined})]));
    }
  };
  render.setOperation=value=>{if(!Object.hasOwn(graphOperations,value))throw new Error('Unknown graph operation.');operation=value;saved=null;result=null;if(lastContainer?.isConnected)render(lastContainer);};
  render.loadTopology=()=>loadCurrent?.();
  render.hide=()=>{generation++;loadCurrent=null;revokeThumbnail();viewport.cancelGraphPick?.();};
  render.clearOverlay=()=>{generation++;overlayShown=false;viewport.cancelGraphPick?.();viewport.graph=null;viewport.invalidate();if(lastContainer?.isConnected)render(lastContainer);};
  return render;
}
