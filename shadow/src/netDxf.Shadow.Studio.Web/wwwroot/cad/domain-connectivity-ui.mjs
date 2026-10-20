import {el} from './ui.mjs';
import {connectivityIdentity,connectivityRequest,mergeConnectivity,connectivityStreams,connectivityCaps,connectivityProof,connectivitySelection,connectivitySvg,connectivityCsv} from './domain-connectivity-model.mjs';

/** Reusable discipline connectivity panel. No source mutation occurs without an explicit Select action. */
export function domainConnectivityTools({client,selectEntities,report,download,snapshot=()=>null}) {
  let scene=null,identity='',module='',generation=0,url='',selected=null,component=null,listOffset=0,issueOffset=0,filter='',proof=null;
  const values={operation:'network',source:'',target:'',direction:'both',maximumVisits:10000,maximumDepth:256,maximumObjects:10000,tolerance:.01};
  const revoke=()=>{if(url){URL.revokeObjectURL(url);url='';}};
  const hide=()=>{generation++;revoke();};
  function render(container,id) {
    hide();const epoch=generation,key=client.key,scope=connectivityIdentity(client);
    if(identity!==scope||module!==id){scene=null;selected=null;proof=null;component=null;listOffset=0;issueOffset=0;values.source='';values.target='';}
    module=id;identity=scope;
    const current=()=>generation===epoch&&container.isConnected&&snapshot()===null&&client.key===key&&connectivityIdentity(client)===scope&&client.data?.modules?.some(m=>m.id===id&&m.enabled);
    let sequence=0,running=false;
    const check=()=>{if(!current())throw new Error('The drawing or module changed. Reopen Connectivity and refresh.');};
    const guard=action=>()=>{check();return action();};
    const button=(text,action,attrs={})=>el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
    const row=(name,control)=>el('label',{class:'form-row'},[el('span',{text:name}),control]);
    const root=el('section',{class:'domain-connectivity'}),status=el('p',{'aria-live':'polite',class:'module-caption'}),output=el('div'),inspector=el('section',{class:'domain-inspector','aria-live':'polite'}),diagram=el('div',{class:'domain-diagram'}),list=el('div'),issues=el('div');
    container.replaceChildren(root);
    if(snapshot()!==null){scene=null;root.append(el('p',{text:'Return to Current drawing to analyze discipline connectivity. Retained drawing snapshots are not analyzed by this panel.'}));return;}
    const changed=()=>{sequence++;running=false;runButton.disabled=false;moreButton.disabled=!scene?.partial;status.textContent='Query changed. Run analysis to use these values; the displayed receipt retains its original scope.';};
    const operation=el('select',{'aria-label':'Connectivity operation'},[['network','Network and components'],['path','Minimum-link path'],['trace','Trace declared passages']].map(([value,text])=>el('option',{value,text})));operation.value=values.operation;
    operation.addEventListener('change',()=>{values.operation=operation.value;changed();});
    const direction=el('select',{'aria-label':'Connectivity direction'},[['both','Either direction'],['forward','Follow passage direction'],['reverse','Against passage direction']].map(([value,text])=>el('option',{value,text})));direction.value=values.direction;
    direction.addEventListener('change',()=>{values.direction=direction.value;changed();});
    const source=el('input',{'aria-label':'Connectivity source port',value:values.source,maxlength:2048,placeholder:'Choose a loaded port below'}),target=el('input',{'aria-label':'Connectivity target port',value:values.target,maxlength:2048,placeholder:'Choose a loaded port below'});
    for(const [name,input]of [['source',source],['target',target]])input.addEventListener('input',()=>{values[name]=input.value;changed();});
    const number=(name,label,min,max,step=1)=>row(label,el('input',{type:'number',min,max,step,value:values[name],'aria-label':label,oninput:e=>{values[name]=e.target.value;changed();}}));
    const runButton=button('Run connectivity analysis',guard(()=>load(false,false))),moreButton=button('Load remaining connectivity',guard(()=>load(true,true)),{disabled:!scene?.partial});
    root.append(el('p',{class:'intro',text:'Explore declared ports and connections in this discipline and active layout. Lines that cross do not connect. Dashed diagram links are declared internal passages; no flow, pressure, switching or electrical simulation is performed.'}),
      row('Analysis',operation),row('Direction',direction),row('Source port',source),row('Target port',target),
      el('details',{},[el('summary',{text:'Scan and search bounds'}),number('maximumObjects','Connectivity object limit',1,100000),number('maximumVisits','Connectivity visit limit',1,50000),number('maximumDepth','Connectivity depth limit',0,1000),number('tolerance','Connectivity gap tolerance',.000001,1e6,'any'),el('p',{class:'hint',text:'Projection cap: 20000 ports / 40000 links / 500 findings. Bulk loading stops after 40 pages and can be continued. Missing declarations remain visible.'})]),
      el('div',{class:'module-actions'},[runButton,moreButton,button('Clear connectivity',()=>{sequence++;running=false;scene=null;proof=null;selected=null;component=null;revoke();output.replaceChildren();diagram.replaceChildren();inspector.replaceChildren();list.replaceChildren();issues.replaceChildren();status.textContent='Connectivity cleared.';runButton.disabled=false;moreButton.disabled=true;})]),status,output,diagram,inspector,list,issues);
    async function load(continuation,bulk) {
      check();const requestId=++sequence;running=true;runButton.disabled=true;moreButton.disabled=true;
      const valid=()=>current()&&requestId===sequence;
      try {
        let accumulated=continuation?scene:null;
        if(continuation&&!accumulated)throw new Error('Run a connectivity analysis first.');
        // Continuations use the original validated request, even if unsubmitted input controls have changed.
        const query=continuation?accumulated.query:connectivityRequest(id,values,client.state);
        let pages=0;
        do {
          if(!valid())return;
          const body={...query};
          if(accumulated){body.graphKey=accumulated.graphKey;for(const name of connectivityStreams)body[`${name==='issues'?'issue':name==='components'?'component':name==='visits'?'visit':name.slice(0,-1)}Offset`]=accumulated[name].length;}
          status.textContent=`Loading connectivity page ${pages+1}…`;
          const data=await client.read('modules/connectivity',{body});if(!valid()||!data)return;
          const merged={...mergeConnectivity(accumulated,data),query};
          if(!continuation&&pages===0){proof=null;selected=null;component=null;inspector.replaceChildren();}
          accumulated=merged;scene=accumulated;pages++;
          if(selected&&!scene.nodes.some(n=>n.id===selected)){selected=null;proof=null;inspector.replaceChildren();}
          show();status.textContent=`Loaded ${scene.nodes.length}/${scene.totals.nodes} ports and ${scene.edges.length}/${scene.totals.edges} links (${pages} pages this run).`;
        } while(bulk&&scene.partial&&pages<connectivityCaps.pages);
        if(valid())status.textContent=scene.partial?'Partial pages loaded. Load remaining connectivity to continue (40 pages per action).':'All receipt pages loaded. Scan and declaration coverage are reported separately below.';
      } catch(error){if(valid())throw error;}
      finally{if(valid()){running=false;runButton.disabled=false;moreButton.disabled=!scene?.partial;}}
    }
    function exportFile(kind) {
      check();if(!scene)return;
      const name=`${id}-connectivity`,data=kind==='json'?JSON.stringify(scene,null,2):kind==='svg'?connectivitySvg(scene,{component,selected,proof}):connectivityCsv(scene,kind);
      const mime=kind==='json'?'application/json':kind==='svg'?'image/svg+xml':'text/csv';
      download(new Blob([data],{type:mime}),`${name}${['nodes','edges','issues'].includes(kind)?'-'+kind:''}.${mime==='text/csv'?'csv':kind}`);
    }
    function show() {
      if(!scene)return;
      output.replaceChildren(el('p',{class:'module-caption',text:`${scene.recordCount} discipline objects · ${scene.nodes.length}/${scene.totals.nodes} ports · ${scene.edges.length}/${scene.totals.edges} links · ${scene.components.length}/${scene.totals.components} weak components · ${scene.issues.length}/${scene.totals.issues} findings`}),
        el('p',{text:`Scan ${scene.complete?'complete':'partial'} · declarations ${scene.declarationsComplete?'complete':'incomplete'} · findings ${scene.issuesComplete?'complete':'limited'} · pages ${scene.partial?'partial':'complete'}. Components belong to the scanned declared network.`}),
        ...(scene.limitations||[]).map(text=>el('p',{class:'hint',text})),
        ...(scene.walk?[el('p',{class:'domain-result',text:`${scene.operation==='path'?'Path':'Trace'}: ${scene.walk.status}${scene.walk.pathLength!=null?' · '+scene.walk.pathLength+' links':''} · ${scene.visits.length}/${scene.totals.visits} proof ports loaded · ${scene.walk.complete?'complete declared scope':'bounded or incomplete scope'}.`})]:[]),
        el('div',{class:'module-actions'},[['json','Export connectivity JSON'],['svg','Export connectivity SVG'],['nodes','Export ports CSV'],['edges','Export connections CSV'],['issues','Export findings CSV']].map(([kind,title])=>button(title,guard(()=>exportFile(kind)),{disabled:typeof download!=='function'}))));
      const choices=el('select',{'aria-label':'Connectivity component'},[el('option',{value:'',text:'All loaded components'}),...scene.components.slice(0,500).map(c=>el('option',{value:c.id,text:`Component ${c.id} · ${c.nodeCount} ports · ${c.entityCount} objects · ${c.openPortCount} open`}))]);choices.value=component??'';
      choices.addEventListener('change',()=>{check();component=choices.value===''?null:Number(choices.value);listOffset=0;showDiagram();showList();});
      output.append(row('Component',choices),el('small',{text:'First 500 loaded components listed; each port inspector identifies its component. Export includes all loaded component records.'}));
      showDiagram();showList();showIssues();if(selected)inspect(selected);moreButton.disabled=running||!scene.partial;
    }
    function showDiagram() {
      revoke();diagram.replaceChildren();if(!scene?.nodes.length)return;
      url=URL.createObjectURL(new Blob([connectivitySvg(scene,{component,selected,proof})],{type:'image/svg+xml'}));
      diagram.append(el('img',{src:url,alt:'Declared connectivity schematic, colored by component',class:'domain-graph-image'}),el('p',{class:'hint',text:'Schematic view: at most 180 loaded ports / 360 links. Positions are arranged for inspection, not drawing coordinates. Solid = external connection; dashed = internal passage; arrows = declared direction. Use the port list to inspect and highlight.'}));
    }
    function choose(name,node) {values[name]=node.id;(name==='source'?source:target).value=node.id;changed();}
    function select(ids) {
      check();const sources=connectivitySelection(scene,ids,client.data?.items||[]);if(sources.reason)throw new Error(sources.reason);
      return selectEntities(sources.entities,null,false,key);
    }
    function inspect(nodeId) {
      if(!scene)return;selected=nodeId;const node=scene.nodes.find(n=>n.id===nodeId);if(!node)return;
      const connections=scene.edges.filter(e=>e.source===nodeId||e.target===nodeId),sourceState=connectivitySelection(scene,[nodeId],client.data?.items||[]),visit=scene.visits.find(v=>v.id===nodeId);
      inspector.replaceChildren(el('h3',{text:`${node.tag||node.class} · ${node.port}`}),el('p',{text:`${node.kind} · component ${node.component} · ${node.degree} links · ${node.open?'declared open':'not declared open'}`}),el('code',{text:node.id}),
        el('p',{text:`Source: ${node.entity?.value||node.entity} · position ${node.position.x}, ${node.position.y}, ${node.position.z}${node.nominalSize?' · nominal size '+node.nominalSize:''}`}),
        el('div',{class:'module-actions'},[button('Use as source port',guard(()=>choose('source',node))),button('Use as target port',guard(()=>choose('target',node))),button('Select port source',guard(()=>select([nodeId])),{disabled:!!sourceState.reason,title:sourceState.reason||'Select this source object'}),
          button('Trace from this port',guard(()=>{choose('source',node);values.operation='trace';operation.value='trace';return load(false,false);})),
          button('Show predecessor proof',guard(()=>{proof=connectivityProof(scene,nodeId);showDiagram();inspector.append(el('p',{text:`Proof: ${proof.edges.length} links${proof.complete?'':' · load more proof pages'}`}),button('Select proof sources',guard(()=>select(proof.nodes)),{disabled:!proof.complete||!!connectivitySelection(scene,proof.nodes,client.data?.items||[]).reason}));}),{disabled:!visit})]),
        ...(sourceState.reason?[el('p',{class:'hint',text:sourceState.reason})]:[]),el('p',{text:`${connections.length}/${node.degree} incident links loaded.`}),
        ...connections.slice(0,100).map(edge=>{const other=edge.source===nodeId?edge.target:edge.source;return el('div',{class:'domain-connection'},[el('span',{text:`${edge.kind} · ${edge.direction} · ${edge.reciprocal?'reciprocal/declaration':'one-sided'} · span ${Number(edge.span).toPrecision(5)}`}),button(other,guard(()=>{inspect(other);showDiagram();}),{disabled:!scene.nodes.some(n=>n.id===other)})]);}));
      showList();
    }
    function showList() {
      const search=el('input',{type:'search',value:filter,'aria-label':'Search connectivity ports',placeholder:'Tag, key, port or kind'});
      search.addEventListener('input',()=>{filter=search.value;listOffset=0;showRows();});
      const rows=el('div');list.replaceChildren(el('h3',{text:'Ports and source objects'}),search,rows);
      function showRows(){const q=filter.toLowerCase(),matches=scene.nodes.filter(n=>(component==null||n.component===component)&&`${n.tag} ${n.key} ${n.port} ${n.kind}`.toLowerCase().includes(q));
        listOffset=Math.min(listOffset,Math.max(0,Math.ceil(matches.length/40)-1)*40);
        rows.replaceChildren(el('p',{text:`${matches.length} loaded ports match · ${listOffset+1}–${Math.min(listOffset+40,matches.length)}`}),...matches.slice(listOffset,listOffset+40).map(n=>button(`${n.tag||n.class} · ${n.port}`,guard(()=>{inspect(n.id);showDiagram();}),{'aria-pressed':n.id===selected})),el('div',{class:'module-actions'},[button('Previous ports',guard(()=>{listOffset=Math.max(0,listOffset-40);showRows();}),{disabled:listOffset===0}),button('Next ports',guard(()=>{listOffset+=40;showRows();}),{disabled:listOffset+40>=matches.length})]));}
      showRows();
    }
    function showIssues(){issueOffset=Math.min(issueOffset,Math.max(0,Math.ceil(scene.issues.length/40)-1)*40);issues.replaceChildren(el('h3',{text:'Connectivity findings'}),...scene.issues.slice(issueOffset,issueOffset+40).map(issue=>el('article',{class:`module-card ${issue.severity==='error'?'error':''}`},[el('strong',{text:issue.code}),el('p',{text:issue.message}),button('Inspect finding port',guard(()=>{inspect(issue.nodes[0]);showDiagram();}),{disabled:!issue.nodes?.length||!scene.nodes.some(n=>n.id===issue.nodes[0])})])),el('div',{class:'module-actions'},[button('Previous findings',guard(()=>{issueOffset=Math.max(0,issueOffset-40);showIssues();}),{disabled:issueOffset===0}),button('Next findings',guard(()=>{issueOffset+=40;showIssues();}),{disabled:issueOffset+40>=scene.issues.length})]));}
    if(scene)show();
  }
  return {render,hide,destroy:hide};
}
