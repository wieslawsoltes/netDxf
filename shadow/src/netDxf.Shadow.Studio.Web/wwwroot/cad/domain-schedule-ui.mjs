import {el} from './ui.mjs';
import {scheduleRequest,mergeSchedule,scheduleStreams,scheduleOffsets,scheduleSelection,scheduleCsv,scheduleField} from './domain-schedule-model.mjs';
import {connectivityIdentity} from './domain-connectivity-model.mjs';

/** Searchable source schedules. Read, inspect and export are independent of explicit drawing selection/edit actions. */
export function domainScheduleTools({client,selectEntities,editRecord,report,download,snapshot=()=>null}) {
  let scene=null,identity='',module='',generation=0;
  const values={query:'',class:'',groupBy:'class',groupValue:null,sortBy:'tag',descending:false,maximumObjects:10000};
  const hide=()=>{generation++;};
  function render(container,id) {
    hide();const epoch=generation,key=client.key,scope=connectivityIdentity(client);
    if(identity!==scope||module!==id)scene=null;
    if(module!==id){values.query='';values.class='';values.groupBy='class';values.groupValue=null;}
    identity=scope;module=id;
    const current=()=>epoch===generation&&container.isConnected&&client.key===key&&connectivityIdentity(client)===scope&&snapshot()===null&&client.data?.modules?.some(m=>m.id===id&&m.enabled);
    const check=()=>{if(!current())throw new Error('The drawing or module changed. Reopen Data manager and refresh.');};
    const button=(text,action,attrs={})=>el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(()=>{check();return action();}).catch(report)});
    const row=(text,control)=>el('label',{class:'form-row'},[el('span',{text}),control]);
    const root=el('section',{class:'domain-schedule'}),status=el('p',{'aria-live':'polite',class:'module-caption'}),output=el('div'),inspector=el('section',{class:'domain-inspector'});
    container.replaceChildren(root);
    if(snapshot()!==null){scene=null;root.append(el('p',{text:'Return to Current drawing to read discipline schedules.'}));return;}
    let sequence=0,offset=0,groupOffset=0;
    function changed(){sequence++;scene=null;offset=0;groupOffset=0;output.replaceChildren();inspector.replaceChildren();run.disabled=false;more.disabled=true;status.textContent='Query changed. Refresh the schedule.';}
    const text=(name,label,max)=>el('input',{value:values[name]??'',maxlength:max,'aria-label':label,oninput:event=>{values[name]=event.target.value;if(name==='groupBy')values.groupValue=null;changed();}});
    const search=text('query','Search discipline schedule',256),group=text('groupBy','Schedule group field',128);
    const run=button('Refresh data',()=>load(false,false)),more=button('Load remaining schedule',()=>load(true,true),{disabled:!scene?.partial});
    const sort=el('select',{'aria-label':'Schedule sort field'},['tag','class','circuit','spec','service','nominalSize','route','entity'].map(value=>el('option',{value,text:value})));sort.value=values.sortBy;
    sort.addEventListener('change',()=>{values.sortBy=sort.value;changed();});
    const descending=el('input',{type:'checkbox',checked:values.descending,'aria-label':'Descending schedule order',onchange:event=>{values.descending=event.target.checked;changed();}});
    root.append(el('p',{class:'intro',text:'Search tagged layout roots, including hidden layers. Group by class, tag, circuit, spec, service, route or another metadata field. Counts describe objects and route IDs; lengths include only straight pipe, wire and signal source segments in drawing units.'}),
      row('Search',search),row('Class',text('class','Schedule class filter',128)),row('Group by',group),
      el('details',{},[el('summary',{text:'Schedule options'}),row('Sort',sort),row('Descending',descending),
        row('Scan limit',el('input',{type:'number',min:1,max:100000,value:values.maximumObjects,'aria-label':'Schedule object limit',oninput:event=>{values.maximumObjects=event.target.value;changed();}}))]),
      el('div',{class:'module-actions'},[run,more]),status,output,inspector);
    search.addEventListener('keydown',event=>{if(event.key==='Enter'&&!event.isComposing){event.preventDefault();void load(false,false).catch(report);}});
    async function load(continuation,bulk) {
      check();const request=++sequence,valid=()=>current()&&sequence===request;run.disabled=true;more.disabled=true;
      try {
        let accumulated=continuation?scene:null;
        const query=continuation?scene?.query:scheduleRequest(id,values,client.state);
        if(!query)throw new Error('Refresh the schedule first.');
        for(let page=0;page<(bulk?40:1);page++) {
          const body={...query};
          if(accumulated){body.scheduleKey=accumulated.scheduleKey;for(const name of scheduleStreams)body[scheduleOffsets[name]]=accumulated[name].length;}
          const data=await client.read('modules/schedule',{body});if(!valid()||!data)return;
          accumulated=mergeSchedule(accumulated,data);accumulated.query=query;
          if(!accumulated.partial)break;
        }
        if(!valid())return;scene=accumulated;offset=Math.min(offset,Math.max(0,Math.ceil(scene.rows.length/50)-1)*50);show();
        status.textContent=scene.partial?'More retained pages are available. Bulk loading reads at most 40 pages per activation.':'All retained schedule pages loaded.';
      } catch(error) {if(valid())throw error;}
      finally {if(valid()){run.disabled=false;more.disabled=!scene?.partial;}}
    }
    function select(rows,extend=false) {
      const selected=scheduleSelection(rows,client.data?.items||[]);if(selected.reason)throw new Error(selected.reason);
      return selectEntities(selected.entities,null,extend,key);
    }
    function inspect(item) {
      inspector.replaceChildren(el('h3',{text:item.tag||item.class}),el('code',{text:typeof item.entity==='string'?item.entity:item.entity.value}));
      const properties=el('dl');
      for(const [label,value]of [['Class',item.class],['Key',item.key],['Symbol',`${item.symbolId||'—'} ${item.symbolVersion||''}`],...Object.entries(item.properties||{})])
        properties.append(el('dt',{text:label}),el('dd',{text:value}));
      inspector.append(properties,el('details',{},[el('summary',{text:`${item.ports.length} ports · ${item.references.length} references`}),
        el('pre',{text:JSON.stringify({ports:item.ports,references:item.references},null,2)})]));
      const selected=scheduleSelection([item],client.data?.items||[]);
      inspector.append(el('div',{class:'module-actions'},[
        button('Select schedule object',()=>select([item]),{disabled:!!selected.reason,title:selected.reason||''}),
        button('Add schedule object to selection',()=>select([item],true),{disabled:!!selected.reason}),
        button('Edit schedule object data',()=>editRecord(id,item),{disabled:!editRecord})]));
      if(selected.reason)inspector.append(el('small',{text:selected.reason}));
    }
    function exportFile(kind) {
      const text=kind==='json'?JSON.stringify(scene,null,2):scheduleCsv(scene,kind);
      download(new Blob([text],{type:kind==='json'?'application/json':'text/csv'}),`${id}-schedule-${kind}.${kind==='json'?'json':'csv'}`);
    }
    function show() {
      if(!scene)return;
      output.replaceChildren(el('p',{class:'module-caption',text:`${scene.rows.length}/${scene.totals.rows} matching objects · ${scene.groups.length}/${scene.totals.groups} groups · ${scene.recordsScanned} records scanned · ${scene.complete?'complete':'partial'} data coverage`}),
        el('p',{class:'hint',text:`Layout ${scene.layout}; drawing units ${scene.drawingUnits}. ${scene.scanComplete?'Root scan complete.':'Root scan limited.'} ${scene.invalidRecords} unreadable metadata roots. Group summaries cover the retained query even while row pages are loading.`}),
        ...(scene.limitations||[]).map(text=>el('p',{class:'hint',text})),
        el('div',{class:'module-actions'},[['json','Export schedule JSON'],['rows','Export schedule rows CSV'],['groups','Export schedule groups CSV']].map(([kind,label])=>button(label,()=>exportFile(kind),{disabled:typeof download!=='function'}))));
      if(scene.options.groupValue!=null)output.append(button(`Clear group filter: ${scene.options.groupValue||'(empty)'}`,()=>{values.groupValue=null;changed();return load(false,false);}));
      const groups=el('details',{},[el('summary',{text:`Groups by ${scene.options.groupBy}`})]);
      for(const g of scene.groups.slice(groupOffset,groupOffset+50))groups.append(el('div',{class:'module-actions'},[
        button(`${g.value||'(empty)'} · ${g.objects} objects`,()=>{values.groupValue=g.value;changed();return load(false,false);}),
        el('small',{text:`${g.routes} routes · ${g.measuredSegments} measured segments · ${Number(g.segmentLength).toPrecision(6)} ${scene.drawingUnits}`})]));
      groups.append(button('Previous groups',()=>{groupOffset=Math.max(0,groupOffset-50);show();},{disabled:groupOffset===0}),
        button('Next groups',()=>{groupOffset+=50;show();},{disabled:groupOffset+50>=scene.groups.length}));
      output.append(groups);
      const shown=scene.rows.slice(offset,offset+50),table=el('table',{class:'schedule-table'}),body=el('tbody');
      table.append(el('caption',{text:`Objects ${scene.rows.length?offset+1:0}–${Math.min(offset+50,scene.rows.length)} of ${scene.totals.rows}`}),
        el('thead',{},[el('tr',{},['Tag','Class','Group','Length','Inspect'].map(text=>el('th',{scope:'col',text})))]),body);
      for(const item of shown)body.append(el('tr',{},[el('td',{text:item.tag}),el('td',{text:item.class}),el('td',{text:scheduleField(item,scene.options.groupBy)}),
        el('td',{text:item.segmentLength==null?'—':Number(item.segmentLength).toPrecision(6)}),el('td',{},[button('Inspect',()=>inspect(item),{'aria-label':`Inspect schedule object ${item.tag||item.key}`})])]));
      output.append(el('div',{class:'schedule-scroll',tabindex:0,'aria-label':'Scrollable discipline schedule'},[table]),
        el('div',{class:'module-actions'},[button('Previous records',()=>{offset=Math.max(0,offset-50);show();},{disabled:offset===0}),
          button('Next records',()=>{offset+=50;show();},{disabled:offset+50>=scene.rows.length}),
          button('Select displayed schedule objects',()=>select(shown),{disabled:!!scheduleSelection(shown,client.data?.items||[]).reason})]));
      if(scene.issues.length)output.append(el('details',{},[el('summary',{text:`Unreadable metadata (${scene.invalidRecords}; ${scene.issues.length} shown)`}),
        ...scene.issues.map(item=>el('p',{text:`${typeof item.entity==='string'?item.entity:item.entity.value}: ${item.message}`}))]));
    }
    if(scene)show();
  }
  return {render,hide};
}
