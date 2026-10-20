import {el} from './ui.mjs';
import {connectivityIdentity,connectivityRequest,mergeConnectivity,connectivityStreams,connectivityOffsets,connectivityCaps,connectivityProof,connectivitySelection,connectivitySelectionIndex,connectivitySvg,connectivityCsv} from './domain-connectivity-model.mjs';
import {domainLoopProof,domainProofIndex,domainStructureCsv,domainCondensationSvg} from './domain-structure-model.mjs';
import {isolationExclusions,isolationCsv,isolationStreams} from './domain-isolation-model.mjs';
import {renderIsolationReport} from './domain-isolation-ui.mjs';

/** Reusable discipline connectivity panel. No source mutation occurs without an explicit Select action. */
export function domainConnectivityTools({client,selectEntities,report,download,snapshot=()=>null}) {
  let scene=null,identity='',module='',generation=0,url='',selected=null,component=null,listOffset=0,issueOffset=0,filter='',proof=null;
  let strongComponent=null,critical=false,diagramMode='ports',structureOffset=0,criticalOffset=0;
  const isolationState={offset:0,filter:'all'};
  const values={operation:'network',source:'',target:'',direction:'both',maximumVisits:10000,maximumDepth:256,maximumObjects:10000,tolerance:.01,maximumLoops:20,maximumProofSteps:4000,maximumSearchWork:100000,maximumExaminedEdges:200000,excludedConnections:'',excludedPorts:''};
  const revoke=()=>{if(url){URL.revokeObjectURL(url);url='';}};
  const hide=()=>{generation++;revoke();};
  function render(container,id) {
    hide();const epoch=generation,key=client.key,scope=connectivityIdentity(client);
    if(identity!==scope||module!==id){scene=null;selected=null;proof=null;component=null;strongComponent=null;critical=false;diagramMode='ports';listOffset=0;issueOffset=0;structureOffset=0;criticalOffset=0;values.source='';values.target='';values.excludedConnections='';values.excludedPorts='';isolationState.offset=0;}
    module=id;identity=scope;
    const current=()=>generation===epoch&&container.isConnected&&snapshot()===null&&client.key===key&&connectivityIdentity(client)===scope&&client.data?.modules?.some(m=>m.id===id&&m.enabled);
    let sequence=0,running=false;
    const check=()=>{if(!current())throw new Error('The drawing or module changed. Reopen Connectivity and refresh.');};
    const guard=action=>()=>{check();return action();};
    const button=(text,action,attrs={})=>el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
    const row=(name,control)=>el('label',{class:'form-row'},[el('span',{text:name}),control]);
    const root=el('section',{class:'domain-connectivity'}),status=el('p',{'aria-live':'polite',class:'module-caption'}),output=el('div'),inspector=el('section',{class:'domain-inspector','aria-live':'polite'}),diagram=el('div',{class:'domain-diagram',tabindex:0,'aria-label':'Scrollable connectivity diagram'}),list=el('div'),issues=el('div'),structure=el('section',{class:'domain-structure'}),isolation=el('section',{class:'domain-isolation'});
    container.replaceChildren(root);
    if(snapshot()!==null){scene=null;root.append(el('p',{text:'Return to Current drawing to analyze discipline connectivity. Retained drawing snapshots are not analyzed by this panel.'}));return;}
    const changed=()=>{sequence++;running=false;runButton.disabled=false;moreButton.disabled=!scene?.partial;status.textContent='Query changed. Run analysis to use these values; the displayed receipt retains its original scope.';};
    const operation=el('select',{'aria-label':'Connectivity operation'},[['network','Network and components'],['path','Minimum-link path'],['trace','Trace declared passages'],['structure','Directed groups, loops and cut links'],['isolation','Isolation and alternative routes']].map(([value,text])=>el('option',{value,text})));operation.value=values.operation;
    operation.addEventListener('change',()=>{values.operation=operation.value;direction.disabled=operation.value==='structure';assumptions.hidden=operation.value!=='isolation';changed();});
    const direction=el('select',{'aria-label':'Connectivity direction'},[['both','Either direction'],['forward','Follow passage direction'],['reverse','Against passage direction']].map(([value,text])=>el('option',{value,text})));direction.value=values.direction;
    direction.addEventListener('change',()=>{values.direction=direction.value;changed();});
    direction.disabled=values.operation==='structure';
    const source=el('input',{'aria-label':'Connectivity source port',value:values.source,maxlength:2048,placeholder:'Choose a loaded port below'}),target=el('input',{'aria-label':'Connectivity target port',value:values.target,maxlength:2048,placeholder:'Choose a loaded port below'});
    for(const [name,input]of [['source',source],['target',target]])input.addEventListener('input',()=>{values[name]=input.value;changed();});
    const number=(name,label,min,max,step=1)=>row(label,el('input',{type:'number',min,max,step,value:values[name],'aria-label':label,oninput:e=>{values[name]=e.target.value;changed();}}));
    const excludedConnections=el('textarea',{'aria-label':'Excluded connection IDs',rows:3,maxlength:128000,value:values.excludedConnections,placeholder:'Exact declared IDs, one per line'}),
      excludedPorts=el('textarea',{'aria-label':'Excluded port IDs',rows:3,maxlength:128000,value:values.excludedPorts,placeholder:'Exact declared IDs, one per line'});
    for(const [name,input]of [['excludedConnections',excludedConnections],['excludedPorts',excludedPorts]])input.addEventListener('input',()=>{values[name]=input.value;changed();});
    const assumptions=el('section',{class:'domain-isolation-assumptions',hidden:values.operation!=='isolation'},[
      el('p',{class:'hint',text:'Explicit graph removal assumptions only. Pick a loaded port/connection below or enter exact IDs, one per line. The source must remain retained; target is optional. Maximum 2000 exclusions / 128000 ID characters.'}),
      row('Excluded connections',excludedConnections),row('Excluded ports',excludedPorts),number('maximumExaminedEdges','Isolation edge examination limit',1,400000),
      button('Clear exclusion assumptions',guard(()=>{values.excludedConnections='';values.excludedPorts='';excludedConnections.value='';excludedPorts.value='';changed();}))]);
    function addExclusion(kind,value){
      const field=kind==='port'?'excludedPorts':'excludedConnections',input=kind==='port'?excludedPorts:excludedConnections;
      const next={...values,[field]:[values[field],value].filter(Boolean).join('\n')},normalized=isolationExclusions(next.excludedConnections,next.excludedPorts);
      values[field]=normalized[field].join('\n');input.value=values[field];values.operation='isolation';operation.value='isolation';direction.disabled=false;assumptions.hidden=false;changed();
    }
    const runButton=button('Run connectivity analysis',guard(()=>load(false,false))),moreButton=button('Load remaining connectivity',guard(()=>load(true,true)),{disabled:!scene?.partial});
    root.append(el('p',{class:'intro',text:'Explore declared ports and connections in this discipline and active layout. Lines that cross do not connect. Dashed diagram links are declared internal passages; no flow, pressure, switching or electrical simulation is performed.'}),
      row('Analysis',operation),row('Direction',direction),row('Source port',source),row('Target port',target),assumptions,
      el('details',{},[el('summary',{text:'Scan and search bounds'}),number('maximumObjects','Connectivity object limit',1,100000),number('maximumVisits','Connectivity visit limit',1,50000),number('maximumDepth','Connectivity depth limit',0,1000),number('tolerance','Connectivity gap tolerance',.000001,1e6,'any'),number('maximumLoops','Loop witness limit',1,100),number('maximumProofSteps','Loop proof step limit',2,10000),number('maximumSearchWork','Loop search work limit',1,1000000),el('p',{class:'hint',text:'Projection cap: 20000 ports / 40000 links / 500 findings. Bulk loading stops after 40 pages and can be continued. Missing declarations remain visible. Structure always follows declared direction; at most one loop witness per strong group is sought.'})]),
      el('div',{class:'module-actions'},[runButton,moreButton,button('Clear connectivity',()=>{sequence++;running=false;scene=null;proof=null;selected=null;component=null;strongComponent=null;critical=false;diagramMode='ports';revoke();output.replaceChildren();diagram.replaceChildren();structure.replaceChildren();isolation.replaceChildren();inspector.replaceChildren();list.replaceChildren();issues.replaceChildren();status.textContent='Connectivity cleared.';runButton.disabled=false;moreButton.disabled=true;})]),status,output,diagram,structure,isolation,inspector,list,issues);
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
          if(accumulated){body.graphKey=accumulated.graphKey;for(const name of connectivityStreams)body[connectivityOffsets[name]]=accumulated[name].length;}
          status.textContent=`Loading connectivity page ${pages+1}…`;
          const data=await client.read('modules/connectivity',{body});if(!valid()||!data)return;
          const merged={...mergeConnectivity(accumulated,data),query};
          if(!continuation&&pages===0){proof=null;selected=null;component=null;strongComponent=null;critical=false;diagramMode='ports';structureOffset=0;criticalOffset=0;isolationState.offset=0;inspector.replaceChildren();}
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
      const name=`${id}-connectivity`,structural=connectivityStreams.slice(5,11).includes(kind),scenario=isolationStreams.includes(kind),svg=kind==='svg'||kind==='condensationSvg';
      const data=kind==='json'?JSON.stringify(scene,null,2):svg?(kind==='condensationSvg'?domainCondensationSvg(scene,strongComponent):connectivitySvg(scene,{component,selected,proof,strongComponent,critical})):scenario?isolationCsv(scene,kind):structural?domainStructureCsv(scene,kind):connectivityCsv(scene,kind);
      const mime=kind==='json'?'application/json':svg?'image/svg+xml':'text/csv';
      download(new Blob([data],{type:mime}),`${name}${['nodes','edges','issues'].includes(kind)||structural||scenario||kind==='condensationSvg'?'-'+kind:''}.${mime==='text/csv'?'csv':svg?'svg':kind}`);
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
      showDiagram();showStructure();showIsolation();showList();showIssues();if(selected)inspect(selected);moreButton.disabled=running||!scene.partial;
    }
    function showIsolation(){
      renderIsolationReport({scene,container:isolation,inspector,button,guard,showProof:evidence=>{proof=evidence;component=null;strongComponent=null;diagramMode='ports';showDiagram();},select,sourceRows:client.data?.items||[],exportFile,state:isolationState,inspectPort:node=>{inspect(node);showDiagram();}});
    }
    function showDiagram() {
      revoke();diagram.replaceChildren();if(!scene?.nodes.length)return;
      url=URL.createObjectURL(new Blob([diagramMode==='condensation'&&scene.structure?domainCondensationSvg(scene,strongComponent):connectivitySvg(scene,{component,selected,proof,strongComponent,critical})],{type:'image/svg+xml'}));
      diagram.append(el('img',{src:url,alt:diagramMode==='condensation'?'Directed condensation of strong groups':'Declared connectivity schematic, colored by component',class:'domain-graph-image'}),el('p',{class:'hint',text:diagramMode==='condensation'?'Condensation: at most 120 loaded groups / 240 links, bounded eight-column layout. Later ranks share the last column. Use group and connection lists to inspect source ports.':'Schematic view: at most 180 loaded ports / 360 links. Positions are arranged for inspection, not drawing coordinates. Solid = external connection; dashed = internal passage; arrows = declared direction. Orange = weak cut links when enabled. Use the port list to inspect and highlight.'}));
    }
    function showStructure() {
      structure.replaceChildren();if(!scene?.structure)return;
      const coverage=scene.structure,membership=new Map();
      for(const port of scene.portStructures){if(!membership.has(port.strongComponent))membership.set(port.strongComponent,[]);membership.get(port.strongComponent).push(port.id);}
      const members=group=>membership.get(group)||[];
      const sourceRows=client.data?.items||[],sourceIndex=connectivitySelectionIndex(scene,sourceRows),sourceState=ids=>connectivitySelection(scene,ids,sourceRows,sourceIndex);
      const groupSelect=group=>{const ids=members(group.id),state=sourceState(ids);return {ids,reason:ids.length!==group.portCount?'Load all group membership pages before selecting its sources.':state.reason};};
      const chooseGroup=group=>{strongComponent=group;component=null;proof=null;showDiagram();showStructure();showList();};
      structure.append(el('h3',{text:'Directed structure'}),el('p',{class:'domain-result',text:`${scene.strongComponents.length}/${scene.totals.strongComponents} strong groups · ${scene.condensation.length}/${scene.totals.condensation} condensation links · ${scene.criticalConnections.length}/${scene.totals.criticalConnections} weak cut links · ${scene.loops.length}/${scene.totals.loops} loop witnesses`}),
        el('p',{text:`Scope ${coverage.scopeComplete?'complete':'incomplete'} · loop search ${coverage.loopSearchComplete?'complete':'limited'} · ${coverage.searchWork}/${coverage.options.maximumSearchWork} work · ${scene.loopSteps.length}/${scene.totals.loopSteps} proof steps loaded. A group means mutual directed reachability. One witness per group is sought; this does not enumerate all loops.`}),
        el('p',{class:'hint',text:'Cut links and articulation ports ignore direction. Removing one separates the retained graph; this is not simulated failure or operational criticality. Missing declarations or scan limits may create apparent cuts and split groups. Loop absence applies only to the retained graph. Reversing the same bidirectional connection is not a loop.'}),
        ...(coverage.loopLimitReached||coverage.proofLimitReached||coverage.searchLimitReached?[el('p',{class:'hint',text:`Evidence limits reached:${coverage.loopLimitReached?' witness count':''}${coverage.proofLimitReached?' proof steps':''}${coverage.searchLimitReached?' search work':''}. Increase the corresponding bound and run again; loading pages does not repeat the search.`})]:[]),
        el('div',{class:'module-actions'},[
          button('Show port structure',guard(()=>{diagramMode='ports';showDiagram();showStructure();}),{'aria-pressed':diagramMode==='ports'}),
          button('Show condensation',guard(()=>{diagramMode='condensation';showDiagram();showStructure();}),{'aria-pressed':diagramMode==='condensation'}),
          button('Highlight weak cut links',guard(()=>{critical=!critical;diagramMode='ports';proof=null;showDiagram();showStructure();}),{'aria-pressed':critical}),
          button('Show all strong groups',guard(()=>chooseGroup(null)),{'aria-pressed':strongComponent==null})]),
        el('details',{},[el('summary',{text:'Structure exports'}),...[
          ['strongComponents','Export strong groups CSV'],['portStructures','Export structural ports CSV'],['condensation','Export condensation CSV'],
          ['criticalConnections','Export weak cut links CSV'],['loops','Export loops CSV'],['loopSteps','Export loop proof CSV'],['condensationSvg','Export condensation SVG']
        ].map(([kind,text])=>button(text,guard(()=>exportFile(kind)),{disabled:typeof download!=='function'}))]));
      structureOffset=Math.min(structureOffset,Math.max(0,Math.ceil(scene.strongComponents.length/40)-1)*40);
      structure.append(el('h4',{text:'Strong groups'}),el('p',{class:'hint',text:`${structureOffset+1}–${Math.min(structureOffset+40,scene.strongComponents.length)} of ${scene.strongComponents.length} loaded groups`}),
        ...scene.strongComponents.slice(structureOffset,structureOffset+40).map(group=>{
          const source=groupSelect(group);
          return el('article',{class:'domain-connection'},[
            button(`Strong group ${group.id} · ${group.portCount} ports · loop ${group.loopStatus}`,guard(()=>chooseGroup(group.id)),{'aria-pressed':strongComponent===group.id}),
            button(`Select strong group ${group.id} sources`,guard(()=>select(source.ids)),{disabled:!!source.reason,title:source.reason||'Select explicit source objects in this strong group'})
          ]);
        }),el('div',{class:'module-actions'},[
          button('Previous strong groups',guard(()=>{structureOffset=Math.max(0,structureOffset-40);showStructure();}),{disabled:structureOffset===0}),
          button('Next strong groups',guard(()=>{structureOffset+=40;showStructure();}),{disabled:structureOffset+40>=scene.strongComponents.length})]));
      const transitions=scene.condensation.filter(e=>strongComponent==null||e.source===strongComponent||e.target===strongComponent);
      structure.append(el('details',{},[el('summary',{text:`Condensation links · ${transitions.length} loaded${strongComponent==null?'':' incident to selected group'}`}),
        el('p',{class:'hint',text:'First 100 matching loaded links. Choose a group to inspect its incident transitions; export retains all loaded links.'}),
        ...transitions.slice(0,100).map(edge=>el('div',{class:'domain-connection'},[el('span',{text:`Group ${edge.source} → ${edge.target} · ${edge.connectionCount} declared connections`}),
          button(`Inspect condensation ${edge.id}`,guard(()=>{
            const from=new Set(members(edge.source)),to=new Set(members(edge.target));
            const links=scene.edges.filter(e=>e.direction==='reverse'?from.has(e.target)&&to.has(e.source):from.has(e.source)&&to.has(e.target));
            inspector.replaceChildren(el('h3',{text:`Condensation ${edge.id}`}),el('p',{text:`${links.length}/${edge.connectionCount} supporting declared connections loaded. Follow each endpoint to its source.`}),
              ...links.slice(0,100).map(e=>el('div',{class:'domain-connection'},[el('code',{text:e.id}),button('Inspect source '+e.source,guard(()=>inspect(e.source)),{disabled:!scene.nodes.some(n=>n.id===e.source)}),button('Inspect target '+e.target,guard(()=>inspect(e.target)),{disabled:!scene.nodes.some(n=>n.id===e.target)})])));
          }))]))]));
      const proofIndex=domainProofIndex(scene);
      structure.append(el('h4',{text:'Loop witnesses'}),...scene.loops.map(loop=>{
        const evidence=domainLoopProof(scene,loop.id,proofIndex),selection=sourceState(evidence.nodes);
        return el('article',{class:'domain-connection'},[
          el('span',{text:`Loop ${loop.id} · group ${loop.strongComponent} · ${loop.stepCount} distinct connections${evidence.complete?'':' · load proof/source pages'}`}),
          button(`Inspect loop ${loop.id}`,guard(()=>{proof=evidence;strongComponent=loop.strongComponent;component=null;diagramMode='ports';showDiagram();inspector.replaceChildren(el('h3',{text:`Loop ${loop.id} proof`}),el('p',{text:`Closed directed proof: ${evidence.edges.length} distinct connections. First ${Math.min(100,evidence.edges.length)} steps shown; exports retain every loaded step. Structural evidence only.`}),...evidence.edges.slice(0,100).map((edge,i)=>el('div',{class:'domain-connection'},[el('code',{text:`${i+1}. ${evidence.nodes[i]} → ${evidence.nodes[i+1]}`}),el('code',{text:edge}),button('Inspect loop port '+i,guard(()=>inspect(evidence.nodes[i])))])));showStructure();}),{disabled:!evidence.complete}),
          button(`Select loop ${loop.id} sources`,guard(()=>select(evidence.nodes)),{disabled:!evidence.complete||!!selection.reason,title:selection.reason||'Select explicit objects supporting this loop'})
        ]);
      }));
      criticalOffset=Math.min(criticalOffset,Math.max(0,Math.ceil(scene.criticalConnections.length/40)-1)*40);
      structure.append(el('h4',{text:'Weak cut links and articulation ports'}),
        ...scene.criticalConnections.slice(criticalOffset,criticalOffset+40).map(edge=>button('Inspect weak cut link '+edge.id,guard(()=>{
          critical=true;strongComponent=null;component=null;diagramMode='ports';proof={nodes:[edge.source,edge.target],edges:[edge.id],complete:true};showDiagram();
          inspector.replaceChildren(el('h3',{text:'Weak cut link'}),el('code',{text:edge.id}),el('p',{text:'Removing this declared connection increases weak components in the retained graph. Incomplete scope may hide alternate routes.'}),
            ...[edge.source,edge.target].map(id=>button('Inspect cut endpoint '+id,guard(()=>inspect(id)),{disabled:!scene.nodes.some(n=>n.id===id)})),
            button('Select cut link sources',guard(()=>select([edge.source,edge.target])),{disabled:!!sourceState([edge.source,edge.target]).reason}));
        }))),el('div',{class:'module-actions'},[
          button('Previous weak cut links',guard(()=>{criticalOffset=Math.max(0,criticalOffset-40);showStructure();}),{disabled:criticalOffset===0}),
          button('Next weak cut links',guard(()=>{criticalOffset+=40;showStructure();}),{disabled:criticalOffset+40>=scene.criticalConnections.length})]),
        el('p',{class:'hint',text:`${scene.portStructures.filter(n=>n.articulationPoint).length} articulation ports in loaded membership. Inspect a port below for its classification; structural ports CSV retains every loaded classification.`}));
    }
    function choose(name,node) {values[name]=node.id;(name==='source'?source:target).value=node.id;changed();}
    function select(ids) {
      check();const sources=connectivitySelection(scene,ids,client.data?.items||[]);if(sources.reason)throw new Error(sources.reason);
      return selectEntities(sources.entities,null,false,key);
    }
    function inspect(nodeId) {
      if(!scene)return;selected=nodeId;const node=scene.nodes.find(n=>n.id===nodeId);if(!node)return;
      const connections=scene.edges.filter(e=>e.source===nodeId||e.target===nodeId),sourceState=connectivitySelection(scene,[nodeId],client.data?.items||[]),visit=scene.visits.find(v=>v.id===nodeId);
      const structural=scene.portStructures.find(p=>p.id===nodeId);
      inspector.replaceChildren(el('h3',{text:`${node.tag||node.class} · ${node.port}`}),el('p',{text:`${node.kind} · component ${node.component} · ${node.degree} links · ${node.open?'declared open':'not declared open'}${structural?' · strong group '+structural.strongComponent+(structural.articulationPoint?' · weak articulation port':''):''}`}),el('code',{text:node.id}),
        el('p',{text:`Source: ${node.entity?.value||node.entity} · position ${node.position.x}, ${node.position.y}, ${node.position.z}${node.nominalSize?' · nominal size '+node.nominalSize:''}`}),
        el('div',{class:'module-actions'},[button('Use as source port',guard(()=>choose('source',node))),button('Use as target port',guard(()=>choose('target',node))),button('Exclude this port',guard(()=>addExclusion('port',nodeId))),button('Select port source',guard(()=>select([nodeId])),{disabled:!!sourceState.reason,title:sourceState.reason||'Select this source object'}),
          button('Trace from this port',guard(()=>{choose('source',node);values.operation='trace';operation.value='trace';direction.disabled=false;assumptions.hidden=true;return load(false,false);})),
          button('Show predecessor proof',guard(()=>{proof=connectivityProof(scene,nodeId);showDiagram();inspector.append(el('p',{text:`Proof: ${proof.edges.length} links${proof.complete?'':' · load more proof pages'}`}),button('Select proof sources',guard(()=>select(proof.nodes)),{disabled:!proof.complete||!!connectivitySelection(scene,proof.nodes,client.data?.items||[]).reason}));}),{disabled:!visit})]),
        ...(sourceState.reason?[el('p',{class:'hint',text:sourceState.reason})]:[]),el('p',{text:`${connections.length}/${node.degree} incident links loaded.`}),
        ...connections.slice(0,100).map(edge=>{const other=edge.source===nodeId?edge.target:edge.source;return el('div',{class:'domain-connection'},[el('span',{text:`${edge.kind} · ${edge.direction} · ${edge.reciprocal?'reciprocal/declaration':'one-sided'} · span ${Number(edge.span).toPrecision(5)}`}),el('code',{text:edge.id}),button(other,guard(()=>{inspect(other);showDiagram();}),{disabled:!scene.nodes.some(n=>n.id===other)}),button('Exclude connection '+edge.id,guard(()=>addExclusion('connection',edge.id)))]);}));
      showList();
    }
    function showList() {
      const search=el('input',{type:'search',value:filter,'aria-label':'Search connectivity ports',placeholder:'Tag, key, port or kind'});
      search.addEventListener('input',()=>{filter=search.value;listOffset=0;showRows();});
      const rows=el('div');list.replaceChildren(el('h3',{text:'Ports and source objects'}),search,rows);
      function showRows(){const q=filter.toLowerCase(),groupIds=strongComponent==null?null:new Set(scene.portStructures.filter(n=>n.strongComponent===strongComponent).map(n=>n.id)),matches=scene.nodes.filter(n=>(component==null||n.component===component)&&(!groupIds||groupIds.has(n.id))&&`${n.tag} ${n.key} ${n.port} ${n.kind}`.toLowerCase().includes(q));
        listOffset=Math.min(listOffset,Math.max(0,Math.ceil(matches.length/40)-1)*40);
        rows.replaceChildren(el('p',{text:`${matches.length} loaded ports match · ${listOffset+1}–${Math.min(listOffset+40,matches.length)}`}),...matches.slice(listOffset,listOffset+40).map(n=>button(`${n.tag||n.class} · ${n.port}`,guard(()=>{inspect(n.id);showDiagram();}),{'aria-pressed':n.id===selected})),el('div',{class:'module-actions'},[button('Previous ports',guard(()=>{listOffset=Math.max(0,listOffset-40);showRows();}),{disabled:listOffset===0}),button('Next ports',guard(()=>{listOffset+=40;showRows();}),{disabled:listOffset+40>=matches.length})]));}
      showRows();
    }
    function showIssues(){issueOffset=Math.min(issueOffset,Math.max(0,Math.ceil(scene.issues.length/40)-1)*40);issues.replaceChildren(el('h3',{text:'Connectivity findings'}),...scene.issues.slice(issueOffset,issueOffset+40).map(issue=>el('article',{class:`module-card ${issue.severity==='error'?'error':''}`},[el('strong',{text:issue.code}),el('p',{text:issue.message}),button('Inspect finding port',guard(()=>{inspect(issue.nodes[0]);showDiagram();}),{disabled:!issue.nodes?.length||!scene.nodes.some(n=>n.id===issue.nodes[0])})])),el('div',{class:'module-actions'},[button('Previous findings',guard(()=>{issueOffset=Math.max(0,issueOffset-40);showIssues();}),{disabled:issueOffset===0}),button('Next findings',guard(()=>{issueOffset+=40;showIssues();}),{disabled:issueOffset+40>=scene.issues.length})]));}
    if(scene)show();
  }
  function setOperation(operation) {
    if(!['network','path','trace','structure','isolation'].includes(operation))throw new Error('Unknown connectivity operation.');
    hide();values.operation=operation;scene=null;proof=null;strongComponent=null;critical=false;diagramMode='ports';
  }
  return {render,hide,destroy:hide,setOperation};
}
