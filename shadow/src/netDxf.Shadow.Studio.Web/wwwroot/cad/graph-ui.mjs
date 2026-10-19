import { el } from './ui.mjs';
import { format } from './core.mjs';
import { graphOperations, graphRequest, graphData, graphOverlay, graphViewKey } from './graph-contracts.mjs';

export function graphPalette({client, viewport, report, snapshot}) {
  const values = {endpointTolerance:.01,curveTolerance:.05,splitIntersections:true,respectElevation:true,limit:50,source:0,target:1,depth:8};
  let operation = 'summary', saved = null, savedKey = '', savedOperation = '', savedOffset = 0, generation = 0;
  const row = (label, input) => el('label',{class:'property-row'},[el('span',{text:label}),input]);
  return function render(container) {
    const epoch = ++generation, inputs = new Map(); let requestSequence = 0;
    const invalidate = () => {
      requestSequence++; saved=null; savedKey=''; runButton.disabled=false;
      output.replaceChildren(el('p',{class:'hint',text:'Inputs changed. Run analysis to update the results.'}));
      if(viewport.graph?.highlight){viewport.graph=null;viewport.invalidate();}
    };
    const button = (text, action, attrs = {}) => el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
    const field = (name, label, options = {}) => {
      const input = el('input',{type:'number',step:'any',value:values[name]??'', 'aria-label':label,...options});
      input.addEventListener('input',()=>{values[name] = input.value;invalidate();}); inputs.set(name,input); return row(label,input);
    };
    const check = (name,label) => { const input=el('input',{type:'checkbox',checked:values[name],onchange:()=>{values[name]=input.checked;invalidate();}}); return row(label,input); };
    const select = el('select',{'aria-label':'Graph analysis operation'},Object.entries(graphOperations).map(([value,text])=>el('option',{value,text})));
    select.value = operation;
    select.addEventListener('change',()=>{operation=select.value;saved=null;render(container);});
    const output = el('div',{'aria-live':'polite'});
    const runButton = button('Run analysis',()=>query(0));
    container.replaceChildren(el('p',{class:'intro',text:'Analyze the active workspace’s semantic curves. Results use drawing units and the tolerances below.'}),row('Analysis',select));
    if (snapshot() !== null) { container.append(el('p',{text:'Return to Current drawing to analyze topology. The retained view is not the active graph.'})); return; }
    if (['path','traverse'].includes(operation)) container.append(field('source','Start node',{min:0,step:1}));
    if (operation==='path') container.append(field('target','End node',{min:0,step:1}));
    if (operation==='cycle') container.append(field('cycle','Cycle ID',{min:0,step:1}));
    if (operation==='traverse') container.append(field('depth','Maximum hops',{min:0,max:128,step:1}));
    if (operation==='nearest') container.append(...['x','y','z'].map(axis=>field(axis,axis.toUpperCase())),field('maximumDistance','Maximum distance (optional)',{min:0}));
    if (['nodes','edges'].includes(operation)) container.append(field('component','Component (optional)',{min:0,step:1}),field('node','Node ID (optional)',{min:0,step:1}));
    if (operation==='nodes') container.append(field('minimumDegree','Minimum degree',{min:0,step:1}),field('maximumDegree','Maximum degree',{min:0,step:1}));
    if (operation==='edges') {
      container.append(field('edge','Edge ID (optional)',{min:0,step:1}));
      for(const [name,label] of [['entity','Source entity'],['occurrence','Exact occurrence']]){
        const input=el('input',{value:values[name]??'','aria-label':`${label} filter`,oninput:e=>{values[name]=e.target.value;invalidate();}});
        container.append(row(label,input));
      }
    }
    container.append(el('details',{},[el('summary',{text:'Topology tolerances and budgets'}),field('endpointTolerance','Endpoint tolerance',{min:0}),field('curveTolerance','Curve tolerance',{min:0}),
      check('splitIntersections','Split intersections'),check('respectElevation','Respect elevation'),field('maximumSegments','Maximum segments',{min:1,max:250000,step:1,placeholder:'100000'}),
      field('maximumCandidatePairs','Candidate pair budget',{min:1,max:20000000,step:1,placeholder:'2000000'}),
      el('p',{class:'hint',text:'Curves are sampled; tighter tolerances increase work. Hidden layers, annotation strokes and presentation-only geometry are excluded. Exceeding a budget reports an error.'})]));
    if (!['summary','nearest'].includes(operation)) container.append(field('limit','Page / visit limit',{min:1,max:200,step:1}));
    container.append(el('div',{class:'toolbar'},[runButton,button('Clear overlay',()=>{viewport.graph=null;viewport.invalidate();})]),output);
    if (saved && savedKey===client.key && savedOperation===operation) show(saved,savedOffset);
    async function query(offset) {
      const sequence=++requestSequence;
      runButton.disabled=true; output.replaceChildren(el('p',{text:'Analyzing topology…'}));
      const key=client.key, currentOperation=operation;
      try {
        const body=graphRequest(currentOperation,values,client.state,offset), response=await client.read('graph/query',{body});
        if (sequence!==requestSequence || epoch!==generation || key!==client.key || !container.isConnected || !response) return;
        const data=graphData(response,client.state); if(!data)return;
        saved=data; savedKey=key; savedOperation=currentOperation; savedOffset=offset; show(data,offset);
      } catch(error) { if(sequence===requestSequence&&epoch===generation&&key===client.key){ output.replaceChildren(el('p',{class:'diagnostic',text:error.message}));report(error); } }
      finally { if(sequence===requestSequence)runButton.disabled=false; }
    }
    function overlay(data) { viewport.graph=graphOverlay(data);viewport.graphKey=graphViewKey(client.state);viewport.invalidate(); }
    function focus(node) { viewport.camera.x=node.x;viewport.camera.y=node.y;viewport.changed(); }
    function show(data, offset) {
      const resultKey=client.key;
      const useResult=action=>()=>{
        if(resultKey!==client.key || epoch!==generation || snapshot()!==null)
          throw new Error('The drawing changed. Run analysis again before using this result.');
        return action();
      };
      output.replaceChildren();
      for (const [key,label] of Object.entries({nodeCount:'Nodes',edgeCount:'Edges',componentCount:'Components',cycleRank:'Independent cycles',bridgeCount:'Bridges',articulationPointCount:'Articulation points',deadEndCount:'Dead ends',junctionCount:'Junctions',isolatedNodeCount:'Isolated nodes',totalLength:'Total length',diagnosticCount:'Diagnostics'}))
        if(data[key]!==undefined)output.append(row(label,el('span',{text:format(data[key])})));
      if(operation==='summary')return;
      if(operation==='path')output.append(el('p',{text:data.reachable?`Path length ${format(data.length)} · ${data.edgeCount} edges`:'No path: the nodes belong to disconnected components.'}));
      if(operation==='cycle')output.append(el('p',{text:`Cycle ${data.cycle} · length ${format(data.length)} · ${data.edgeCount} edges · closed path`}));
      if(operation==='cycles')output.append(el('p',{class:'hint',text:'An independent fundamental cycle basis. Each closing edge defines a loop; inspect a cycle to follow its ordered geometry.'}));
      if(operation==='spanning_forest')output.append(el('p',{text:`Minimum forest length ${format(data.length)} · ${data.componentCount} components · ${data.edgeCount} edges`}));
      if(operation==='traverse')output.append(el('p',{text:`${data.nodes.length} visited nodes${data.nodeLimitReached?' · node limit reached':''}${data.depthLimitReached?' · hop limit reached':''}`}));
      if(operation==='nearest')output.append(el('p',{text:data.found?`Node ${data.node.id} · distance ${format(data.distance)}`:'No node within the requested distance.'}));
      const page=data.steps || data, items=page.items || data.nodes || (data.node?[data.node]:[]);
      if(!['components','diagnostics','cycles'].includes(operation))output.append(button('Highlight this result',useResult(()=>overlay(data))));
      if(page.total!==undefined)output.append(el('p',{class:'hint',text:`${page.total} matches · showing ${items.length?offset+1:0}–${offset+items.length}`}));
      for(const item of items) {
        const node=item.node || item, edge=item.viaEdge || (item.a!==undefined?item:null), card=el('section',{class:'layer-card'});
        if(Number.isFinite(node.x)&&Number.isFinite(node.y)) {
          card.append(el('b',{text:`Node ${node.id} · degree ${node.degree??'—'}`}),el('p',{text:`${format(node.x)}, ${format(node.y)}, ${format(node.z||0)}${item.depth!==undefined?` · hop ${item.depth}`:''}`}));
          card.append(el('div',{class:'toolbar'},[button('Focus',useResult(()=>focus(node))),button('Start',useResult(()=>{values.source=node.id;inputs.get('source')&&(inputs.get('source').value=node.id);invalidate();})),button('End',useResult(()=>{values.target=node.id;inputs.get('target')&&(inputs.get('target').value=node.id);invalidate();}))]));
        } else if(operation==='components') card.append(el('b',{text:`Component ${item.id}`}),el('p',{text:`${item.nodeCount} nodes · ${item.edgeCount} edges · ${item.cycleRank} cycles · length ${format(item.totalLength)}`}),button('Inspect nodes',useResult(()=>{values.component=item.id;operation='nodes';saved=null;render(container);})));
        else if(operation==='cycles')card.append(el('b',{text:`Cycle ${item.id}`}),el('p',{text:`${item.edgeCount} edges · closing edge ${item.closingEdge}`}),button('Inspect cycle',useResult(()=>{values.cycle=item.id;operation='cycle';saved=null;render(container);})));
        else if(operation==='diagnostics')card.append(el('b',{text:item.code}),el('p',{text:`${item.severity}: ${item.message}`}));
        if(edge)card.append(el('b',{text:`Edge ${edge.id}: ${edge.a} → ${edge.b}`}),el('p',{text:`Length ${format(edge.length)} · ${edge.entity}`}),el('small',{text:edge.occurrence}));
        output.append(card);
      }
      if(page.total!==undefined)output.append(el('div',{class:'toolbar'},[button('Previous',useResult(()=>query(Math.max(0,offset-Number(values.limit)))),{disabled:offset===0}),button('Next',useResult(()=>query(page.nextOffset)),{disabled:page.nextOffset===null})]));
    }
  };
}
