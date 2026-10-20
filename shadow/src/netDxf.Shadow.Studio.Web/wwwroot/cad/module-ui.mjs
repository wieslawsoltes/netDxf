import { el, group, tool } from './ui.mjs';
import { moduleRequest, moduleSession } from './module-contracts.mjs';
import { domainConnectivityTools } from './domain-connectivity-ui.mjs';

export function moduleTools({client,edit,dialog,report,editable,place,showPalette,changed,viewport,download,snapshot=()=>null}) {
  if (!document.querySelector('link[data-cad-modules]')) document.head.append(el('link',{rel:'stylesheet',href:new URL('./modules.css',import.meta.url).href,'data-cad-modules':''}));
  let descriptors = [], loaded = '', pending = null, chosen = '', section = 'symbols', epoch = 0, result = null, query = '', offset = 0, dataOffset = 0, analysisTolerance = '.01';
  const applied = new Set();
  const connectivity = domainConnectivityTools({client,selectEntities,report,download,snapshot});
  const configurationKey = () => JSON.stringify([client.id,client.state?.documentId,client.data?.moduleConfigurationVersion]);
  const button = (text,action,attrs={}) => el('button',{type:'button',text,...attrs,onclick:()=>Promise.resolve().then(action).catch(report)});
  const row = (label,control) => el('label',{class:'form-row'},[el('span',{text:label}),control]);
  function enabled(id) { return client.data?.modules?.find(m=>m.id===id)?.enabled === true; }
  function preference() { try { const value=JSON.parse(localStorage.getItem('netdxf-cad-modules-v1')||'{}'); return value && typeof value==='object' && !Array.isArray(value)?value:{}; } catch { return {}; } }
  async function sync() {
    if(!client.state || client.busy)return;
    const key=configurationKey(); if(loaded===key)return; if(pending)return pending;
    pending=(async()=>{
      const response=await client.read('modules'); if(!response || key!==configurationKey())return;
      descriptors=response.modules || []; loaded=key;
      const scope=JSON.stringify([client.id,client.state.documentId]);
      if(!applied.has(scope)) {
        applied.add(scope); const saved=preference();
        for(const state of descriptors) {
          if(scope!==JSON.stringify([client.id,client.state?.documentId]))return;
          if(typeof saved[state.descriptor.id]==='boolean' && state.enabled!==saved[state.descriptor.id])
            await edit('modules/toggle',{module:state.descriptor.id,enabled:saved[state.descriptor.id]},{expectedKey:client.key});
        }
      }
      changed();
    })().finally(()=>{pending=null;if(client.state && key!==configurationKey())void sync().catch(report);});
    return pending;
  }
  async function toggle(id,value) {
    editable(); await edit('modules/toggle',{module:id,enabled:value});
    try {localStorage.setItem('netdxf-cad-modules-v1',JSON.stringify({...preference(),[id]:value}));}catch{}
    loaded='';result=null;await sync();
  }
  function open(id,mode='symbols') { if(mode==='structure')connectivity.setOperation('structure');chosen=id;section=mode==='structure'?'connectivity':mode;result=null;offset=0;dataOffset=0;showPalette('Modules'); }
  function ribbon(id) {
    const state=descriptors.find(m=>m.descriptor.id===id);
    const commands=state?.descriptor.commands || [];
    return [group('Author',commands.map(c=>tool(`module-command:${id}:${c.id}`,c.name,true,c.requiresSymbol))),
      group('Discipline',[tool(`module-open:${id}:symbols`,'Symbol library',true,true),tool(`module-open:${id}:data`,'Data manager',true),tool(`module-open:${id}:validation`,'Validate',true),tool(`module-open:${id}:connectivity`,'Connectivity',true),tool(`module-open:${id}:structure`,'Network structure',true),tool('modules','Modules',true)])];
  }
  async function action(name) {
    if(name==='modules'){chosen='';showPalette('Modules');return true;}
    if(name.startsWith('module-open:')){const[,id,mode]=name.split(':');open(id,mode);return true;}
    if(name.startsWith('module-command:')){const[,id,command]=name.split(':');await launch(id,command);return true;}
    return false;
  }
  async function launch(id,commandId,symbol=null) {
    editable(); await sync();
    const descriptor=descriptors.find(m=>m.descriptor.id===id)?.descriptor;
    const command=descriptor?.commands?.find(c=>c.id===commandId);
    if(!enabled(id) || !command)throw new Error('This module command is unavailable. Enable the module first.');
    if(command.requiresSymbol && !symbol){open(id,'symbols');return;}
    const key=client.key, selection=[...(client.data.selectedEntities||[])];
    if(command.requiresSelection&&!selection.length)throw new Error('Select source objects before running this command.');
    const fields=new Map(), content=[el('p',{class:'hint',text:command.description})];
    if(symbol)content.push(el('p',{text:`${symbol.name} · ${symbol.version} · ${symbol.units}`}));
    for(const field of [...(command.fields||[]),...(symbol?.attributes||[])]) {
      if([...fields.keys()].some(id=>id.toLowerCase()===field.id.toLowerCase()))continue;
      const input=field.choices?.length?el('select',{'aria-label':field.name},field.choices.map(value=>el('option',{value,text:value}))):el('input',{type:field.type==='number'?'number':'text',step:'any','aria-label':field.name,required:field.required,maxlength:8192});
      input.value=field.default||'';fields.set(field.id,input);content.push(row(field.name+(field.required?' *':''),input));
      if(field.description)content.push(el('small',{class:'module-caption',text:field.description}));
    }
    if(command.maximumPoints>0)content.push(el('p',{class:'hint',text:`Choose ${command.minimumPoints===command.maximumPoints?command.minimumPoints:`${command.minimumPoints}–${command.maximumPoints}`} points in the drawing after accepting. Object snaps and drafting modes apply; Escape cancels.`}));
    if(!await dialog(command.name,content,command.maximumPoints>0?'Pick points':'Apply'))return;
    if(key!==client.key)throw new Error('The drawing changed while the command dialog was open. Reopen it.');
    const request=moduleRequest(descriptor,command,Object.fromEntries([...fields].map(([id,input])=>[id,input.value])),symbol,selection);
    if(command.maximumPoints>0)place(moduleSession(descriptor,command,request,key));
    else await edit('modules/command',request,{expectedKey:key});
  }
  async function selectEntities(ids,position,extend=false,expectedKey=client.key) {
    editable(); const unique=[...new Set(ids.map(id=>typeof id==='string'?id:id.value))];
    if(unique.length>500)throw new Error('Choose a result with at most 500 objects.');
    await edit('select',{mode:'entities',entities:unique.map(value=>({value})),extend},{expectedKey});
    if(position){viewport.camera.x=position.x;viewport.camera.y=position.y;viewport.changed();}
  }
  function render(container) {
    connectivity.hide();
    const generation=++epoch;
    const current=()=>generation===epoch && container.isConnected;
    container.replaceChildren(el('p',{class:'intro',text:'Optional discipline toolsets. Turning a module off preserves its drawing objects and data.'}));
    if(!loaded){container.append(el('p',{text:'Loading module catalog…'}));void sync().catch(report);return;}
    container.append(el('div',{class:'module-tabs'},[button('Modules',()=>{chosen='';changed();},{'aria-pressed':!chosen}),...descriptors.filter(m=>enabled(m.descriptor.id)).map(m=>button(m.descriptor.name,()=>open(m.descriptor.id),{'aria-pressed':chosen===m.descriptor.id}))]));
    if(!chosen||!enabled(chosen)) {
      for(const state of descriptors){const m=state.descriptor;const input=el('input',{type:'checkbox',checked:enabled(m.id),'aria-label':`Enable ${m.name}`,onchange:e=>toggle(m.id,e.target.checked).catch(report)});
        container.append(el('section',{class:'module-card'},[el('label',{class:'module-switch'},[input,el('span',{text:m.name})]),el('p',{text:m.description}),el('small',{text:`Version ${m.version} · ${m.commands.length} commands`}),el('p',{class:'module-caption',text:(m.standards||[]).join(' · ')})]));}
      return;
    }
    const descriptor=descriptors.find(m=>m.descriptor.id===chosen).descriptor;
    container.append(el('div',{class:'module-tabs'},[['symbols','Symbols'],['data','Data manager'],['validation','Validation'],['connectivity','Connectivity']].map(([mode,title])=>button(title,()=>{section=mode;result=null;changed();},{'aria-pressed':section===mode}))));
    const output=el('div',{'aria-live':'polite'}); const key=client.key,id=chosen,mode=section;
    const cacheKey=JSON.stringify([configurationKey(),client.state?.workspaceId,client.state?.viewVersion,client.state?.layout,client.state?.revision]);
    let requestGeneration=0;
    const valid=request=>current()&&client.key===key&&id===chosen&&mode===section&&(request===undefined||request===requestGeneration);
    if(section==='connectivity'){container.append(output);connectivity.render(output,id);return;}
    if(section==='symbols'){
      const search=el('input',{value:query,placeholder:'Search symbols, tags or standards','aria-label':'Search symbol library',maxlength:256});
      const run=async(start=0)=>{const request=++requestGeneration;query=search.value;offset=start;output.replaceChildren(el('p',{text:'Searching…'}));try{const data=await client.read('modules/symbols',{body:{module:id,query,offset:start,limit:30}});if(valid(request)&&data){result={key:cacheKey,id,mode,data,query,offset:start};showSymbols(data,start);}}catch(error){if(valid(request))throw error;}};
      container.append(el('div',{class:'module-search'},[search,button('Search',()=>run(0))]),output);
      search.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();void run(0).catch(report);}});
      function showSymbols(data,pageOffset){output.replaceChildren(el('p',{class:'module-caption',text:`${data.total} matches · ${data.items.length} on this page`}));
        for(const symbol of data.items){const insert=descriptor.commands.find(c=>c.requiresSymbol);const props=el('dl');
          for(const [label,value] of [['Units',symbol.units],['Base point',`${symbol.basePoint.x}, ${symbol.basePoint.y}, ${symbol.basePoint.z}`],['Ports',(symbol.ports||[]).map(p=>`${p.id} (${p.kind})`).join(', ')],['Attributes',(symbol.attributes||[]).map(a=>a.name).join(', ')],['References',(symbol.standards||[]).join(' · ')]])props.append(el('dt',{text:label}),el('dd',{text:value||'—'}));
          output.append(el('article',{class:'module-card'},[el('h3',{text:symbol.name}),el('small',{text:`${symbol.category} · ${symbol.version}`}),el('p',{text:symbol.description}),el('details',{},[el('summary',{text:'Definition and standards'}),props]),...(insert?[button('Insert',()=>launch(id,insert.id,symbol))]:[])]));}
        output.append(el('div',{class:'module-actions'},[button('Previous',()=>run(Math.max(0,pageOffset-30)),{disabled:pageOffset===0}),button('Next',()=>run(pageOffset+30),{disabled:pageOffset+data.items.length>=data.total})]));}
      if(result?.key===cacheKey&&result.id===id&&result.mode===mode&&result.query===query&&result.offset===offset)showSymbols(result.data,result.offset);else void run(offset).catch(report);
    } else {
      const tolerance=el('input',{type:'number',step:'any',min:0.000001,value:analysisTolerance,'aria-label':'Connection tolerance'});
      const run=async()=>{const request=++requestGeneration;analysisTolerance=tolerance.value;output.replaceChildren(el('p',{text:'Analyzing discipline data…'}));try{const data=await client.read('modules/analyze',{body:{revision:client.state.revision,module:id,options:{connectionTolerance:Number(analysisTolerance),maximumObjects:10000,maximumIssues:500}}});if(valid(request)&&data){result={key:cacheKey,id,mode,data,tolerance:analysisTolerance};dataOffset=Math.min(dataOffset,Math.max(0,Math.ceil(data.records.length/50)-1)*50);showAnalysis(data);}}catch(error){if(valid(request))throw error;}};
      container.append(row('Connection tolerance',tolerance),el('div',{class:'module-actions'},[button(section==='data'?'Refresh data':'Run validation',run)]),output);
      function showAnalysis(data){output.replaceChildren(el('p',{class:'module-caption',text:`${data.records.length} records · ${data.issues.length} findings · ${data.complete?'complete':'partial'} scan` }));if(data.limitation)output.append(el('p',{text:data.limitation}));
        if(mode==='data'){
          for(const item of data.records.slice(dataOffset,dataOffset+50)){
            const props=el('dl');
            for(const [name,value]of Object.entries(item.properties||{}))props.append(el('dt',{text:name}),el('dd',{text:value}));
            props.append(el('dt',{text:'Source ID'}),el('dd',{text:typeof item.entity==='string'?item.entity:item.entity.value}));
            for(const port of item.ports||[])props.append(el('dt',{text:`Port ${port.id}`}),el('dd',{text:`${port.kind} · ${port.position.x}, ${port.position.y}, ${port.position.z}${port.nominalSize?' · '+port.nominalSize:''}`}));
            output.append(el('article',{class:'module-card'},[el('h3',{text:item.tag||item.class}),el('small',{text:`${item.class} · ${typeof item.entity==='string'?item.entity:item.entity.value}`}),
              el('details',{},[el('summary',{text:'Properties and connections'}),props]),
              el('div',{class:'module-actions'},[button('Select object',()=>selectEntities([item.entity],item.ports?.[0]?.position,false,key)),button('Add to selection',()=>selectEntities([item.entity],item.ports?.[0]?.position,true,key))])]));
          }
          output.append(el('div',{class:'module-actions'},[
            button('Previous records',()=>{dataOffset=Math.max(0,dataOffset-50);showAnalysis(data);},{disabled:dataOffset===0}),
            button('Next records',()=>{dataOffset+=50;showAnalysis(data);},{disabled:dataOffset+50>=data.records.length})]));
        }
        else{if(!data.issues.length)output.append(el('p',{text:data.complete?'No issues found by the enabled rules.':'No findings in the scanned subset.'}));for(const issue of data.issues)output.append(el('article',{class:`module-card module-issue ${issue.severity==='error'?'error':''}`},[el('strong',{text:issue.code}),el('p',{text:issue.message}),button('Select and locate',()=>selectEntities(issue.entities,issue.position,false,key),{disabled:!issue.entities?.length})]));}}
      if(result?.key===cacheKey&&result.id===id&&result.mode===mode&&result.tolerance===analysisTolerance)showAnalysis(result.data);
    }
  }
  return {sync,ribbon,action,render,open,launch,hide:()=>connectivity.hide(),catalog:()=>descriptors};
}
