import { drawingFileName, inspectDrawing } from './exchange-contracts.mjs';
import { saveDrawingAs } from './exchange-ui.mjs';
import { authoringRequest } from './authoring-contracts.mjs';
import { authoringDialog, isAuthoringAction } from './authoring-ui.mjs';
import { CadClient } from './client.mjs';
import { blockTools } from './block-ui.mjs';
import { CadViewport, bindViewportPageLifetime } from './viewport.mjs';
import { CommandSession, commands, commandName, number, point, distance, polygonArea, format, fit } from './core.mjs';
import { $, el, icon, tool, tabs, ribbon } from './ui.mjs';
import { viewKey } from '../studio-state.mjs';

const client = new CadClient();
const viewport = new CadViewport($('viewport'), $('drawing'), client, {
  pick: p => takePoint(p), select: (p, extend, remove) => select('pick', p, p, extend, remove),
  box: (a,b,crossing,extend,remove) => select(crossing?'crossing':'window',a,b,extend,remove),
  grip: (grip,position) => edit('grip',{entity:grip.entity,grip:grip.id,position}),
  move: (p,pixel) => cursorChanged(p,pixel), error: report,
  context: e => contextMenu(e.clientX,e.clientY), frame: () => { $('loading').hidden=true; if(!client.busy) $('connection').textContent='Local'; },
  camera: camera => { if (viewId) views.set(viewId,{...camera}); }
});
let session = null, tab = 'Home', palette = 'Properties', currentLayer = '0', lastCommand = 'LINE';
let documents = [], viewId = '', stateKey = '', activeIdentity = '', snapshot = null, lastInput = [], historyIndex = 0, toastTimer;
const views = new Map(), layers = new Map();
const blockUi = blockTools({client, edit, dialog, cancel, fit:()=>viewport.fit(), layer:()=>currentLayer});
function persisted() { try { return JSON.parse(localStorage.getItem('netdxf-cad-ui-v1') || '{}'); } catch { return {}; } }
const settings = persisted();
for (const key of ['grid','snap','ortho','polar','osnap','dynamic']) if (typeof settings[key] === 'boolean') viewport.settings[key]=settings[key];
if (Number.isFinite(settings.gridStep) && settings.gridStep>=1e-6 && settings.gridStep<=1e9) viewport.settings.gridStep=settings.gridStep;
if (Number.isInteger(settings.snapModes) && settings.snapModes>=0 && settings.snapModes<=511) viewport.settings.snapModes=settings.snapModes;
if (settings.left) $('workspace').classList.add('left');
if (settings.hidden || innerWidth<700) $('workspace').classList.add('hide-palette');
if (Number.isFinite(settings.width)) document.documentElement.style.setProperty('--palette-width',`${Math.max(210,Math.min(550,settings.width))}px`);
function persist() { try { localStorage.setItem('netdxf-cad-ui-v1',JSON.stringify({...viewport.settings,left:$('workspace').classList.contains('left'),hidden:$('workspace').classList.contains('hide-palette'),width:$('palette').getBoundingClientRect().width||290})); } catch { /* Private browsing can disable storage. */ } }
function log(message, kind='') { const node=el('div',{text:message,class:kind}); $('history').append(node); while($('history').children.length>300) $('history').firstChild.remove(); $('history').scrollTop=$('history').scrollHeight; }
function report(error) {
  if (error?.name==='AbortError') return;
  const message=error?.message || String(error); log(message,'error'); $('toast').textContent=message; $('toast').hidden=false;
  clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').hidden=true,7000);
  if (error?.status===409) { cancel(false); client.refresh().catch(()=>{}); }
}
function run(task) { return Promise.resolve().then(task).catch(report); }
function editable() { if(snapshot!==null) throw new Error('This is a read-only retained snapshot. Return to Current drawing before editing.'); if(!client.state) throw new Error('Load a drawing first.'); }
async function edit(path,payload={},options={}) { editable(); const result=await client.mutate(path,payload,options); await documentTabs(); return result; }
function setSession(value) { if(value!==null)client.beginInteraction(); viewport.cancelInput(); session=value; viewport.command=value; viewport.panMode=false; viewport.snapHit=null; updatePrompt(); updateTools(); viewport.invalidate(); }
function cancel(clearSelection=true) { setSession(null); $('command-input').value=''; $('suggestions').hidden=true; $('dynamic-input').hidden=true; if(clearSelection && client.state && snapshot===null && !client.busy) run(()=>edit('select',{mode:'clear'})); }
async function start(name) {
  editable(); if(client.busy) throw new Error('Wait for the current operation to complete.');
  setSession(new CommandSession(name,currentLayer,client.data.selectedEntities.length)); lastCommand=session.name;
  log(`${session.name} — ${commands[session.name].label}`,'input');
  if(session.spec.selection && !session.selecting) { session.acceptSelection(client.data.selectedEntities.length); if(session.complete) await complete(); }
  $('command-input').focus({preventScroll:true});
}
function updatePrompt() { $('prompt').textContent=session?.prompt || 'Command:'; $('command-input').placeholder=session ? (session.selecting?'Select in the viewport, then Enter':session.step.kind==='point'?'X,Y · @X,Y · @distance<angle':'Enter value'):'Type a command'; }
async function takePoint(p) { if(!session) return; editable(); session.pick(p); updatePrompt(); viewport.invalidate(); if(session.complete) await complete(); }
async function complete() {
  const completed=session; if(!completed?.complete) return;
  if(completed.spec.measure) {
    const p=completed.command.points;
    if(completed.name==='DIST') log(`Distance = ${format(distance(p[0],p[1]))}; ΔX = ${format(p[1].x-p[0].x)}; ΔY = ${format(p[1].y-p[0].y)}; angle = ${format(Math.atan2(p[1].y-p[0].y,p[1].x-p[0].x)*180/Math.PI)}°`,'success');
    else log(`Area = ${format(polygonArea(p))}; perimeter = ${format(p.reduce((sum,v,i)=>sum+distance(v,p[(i+1)%p.length]),0))}`,'success');
    setSession(null); return;
  }
  const command=structuredClone(completed.command);
  if(command.name==='INSERT') { const block=client.data.blocks.find(b=>b.name.toLowerCase()===command.block.toLowerCase()); if(!block) { setSession(null); throw new Error(`Unknown block: ${command.block}`); } command.reference={value:block.id}; delete command.block; }
  try { const authored=authoringRequest(command,client.data); await edit(authored?.path || 'command',authored?.body || {command},authored?{expectedKey:client.key}:{}); log(`${completed.name} completed — revision ${client.state.revision}.`,'success'); }
  finally { if(session===completed) setSession(null); }
}
async function submit(text) {
  const value=text.trim(); $('suggestions').hidden=true;
  if(value) { lastInput.push(value); if(lastInput.length>100) lastInput.shift(); historyIndex=lastInput.length; log(`› ${value}`,'input'); }
  if(session) {
    if(session.selecting) { if(value) throw new Error('Select objects in the viewport, then press Enter.'); session.acceptSelection(client.data.selectedEntities.length); }
    else session.input(value,viewport.resolved || viewport.cursor);
    updatePrompt(); viewport.invalidate(); if(session.complete) await complete(); return;
  }
  if(!value) return start(lastCommand);
  const match=value.match(/^(\S+)(?:\s+([\s\S]*))?$/), name=match[1].toUpperCase(), rest=match[2]||'';
  const aliases={BEDIT:'block-edit',BE:'block-edit',BSAVE:'block-save',BCLOSE:'block-close',U:'undo',UNDO:'undo',REDO:'redo',QNEW:'new',NEW:'new',OPEN:'open',QSAVE:'save',SAVE:'save-as',SAVEAS:'save-as',PROPERTIES:'properties',PR:'properties',LA:'layers',LAYER:'layers',LIST:'explorer',REGEN:'regen',RE:'regen',PAN:'pan',P:'pan',OPTIONS:'settings',OSNAP:'settings',HELP:'help',SNAPSHOT:'snapshot'};
  if(name==='Z'||name==='ZOOM') { if(!rest||['E','EXTENTS','A','ALL'].includes(rest.toUpperCase())) viewport.fit(); else if(rest.toUpperCase()==='IN') await action('zoom-in'); else if(rest.toUpperCase()==='OUT') await action('zoom-out'); else throw new Error('ZOOM accepts Extents, In or Out.'); return; }
  if(aliases[name]==='block-edit' && rest.trim()) {
    const text=rest.trim(), quoted=text.length>1 && ['"',"'"].includes(text[0]) && text.at(-1)===text[0];
    const blockName=quoted?text.slice(1,-1):text;
    const block=client.data.blocks.find(b=>b.name.toLowerCase()===blockName.toLowerCase());
    if(!block) throw new Error(`Unknown block: ${blockName}`);
    return blockUi.begin(block.id);
  }
  if(aliases[name]) return action(aliases[name]);
  await start(name);
  if(rest && session) {
    const tokens=[...rest.matchAll(/"([^"]*)"|'([^']*)'|(\S+)/g)].map(m=>m[1]??m[2]??m[3]);
    for(let i=0;i<tokens.length;i++) {
      if(!session) throw new Error('Extra input after command completion.');
      if(session.selecting) throw new Error('Preselect source objects before an inline modification command.');
      const isText=session.step.kind==='text'&&session.index===session.spec.steps.length-1;
      const token=isText?tokens.slice(i).join(' '):tokens[i]; session.input(token,viewport.resolved||viewport.cursor); if(session.complete) await complete(); if(isText) break;
    }
    updatePrompt(); viewport.invalidate();
  }
}
async function select(mode,first=point(),second=point(),extend=false,remove=false) {
  editable(); await edit('select',{mode,first,second,tolerance:8/viewport.camera.scale,extend,remove}); updatePrompt();
}
function updateTools() {
  document.querySelectorAll('[data-command]').forEach(b=>{b.classList.toggle('active',b.dataset.command===session?.name);b.disabled=!!client.busy||snapshot!==null||!client.state;});
  for(const [name,enabled] of [['undo',client.state?.undoCount],['redo',client.state?.redoCount]]) document.querySelectorAll(`[data-action="${name}"]`).forEach(b=>b.disabled=!enabled||!!client.busy||snapshot!==null);
}
function drawRibbon() { $('ribbon').replaceChildren(...ribbon(tab,client.data,currentLayer,value=>{currentLayer=value;layers.set(client.id,value);})); $('ribbon').setAttribute('aria-label',`${tab} tools`); updateTools(); }
function showPalette(name) { palette=name; $('workspace').classList.remove('hide-palette'); persist(); renderPalette(); }
function inputRow(label,input) { return el('label',{class:'form-row'},[el('span',{text:label}),input]); }
async function dialog(title,content,submitLabel='OK') {
  if($('dialog').open) throw new Error('Finish the open dialog first.');
  $('dialog-title').textContent=title; $('dialog-body').replaceChildren(...content); $('dialog-submit').textContent=submitLabel;
  $('dialog').returnValue='cancel'; $('dialog').showModal();
  return new Promise(resolve=>$('dialog').addEventListener('close',()=>resolve($('dialog').returnValue==='ok'),{once:true}));
}
async function ask(title,label,initial='',type='text') {
  const input=el('input',{type,value:initial,required:true,'aria-label':label});
  return await dialog(title,[inputRow(label,input)])?input.value:null;
}
function button(text,callback,attrs={}) { return el('button',{type:'button',text,...attrs,onclick:()=>run(callback)}); }
function row(label,child) { return el('div',{class:'property-row'},[el('span',{text:label,title:label}),child]); }
function read(text) { return el('span',{class:'read-only',text:String(text??'')}); }
function toolbar(...children) { return el('div',{class:'toolbar'},children); }
function renderPalette() {
  $('palette-title').textContent=palette.toUpperCase();
  $('palette-tabs').replaceChildren(...['Properties','Layers','Blocks','Explorer','Workspaces','Diagnostics'].map(name=>button(name,()=>showPalette(name),{role:'tab','aria-selected':name===palette})));
  const content=$('palette-content'), data=client.data; content.replaceChildren(); if(!data) return;
  const state=data.state;
  if(palette==='Properties') {
    content.append(el('p',{class:'intro',text:`${data.selectedEntities.length} selected ${data.selectedEntities.length===1?'object':'objects'}`}));
    if(data.properties) {
      content.append(el('h3',{text:data.properties.type}));
      const selected=data.items.find(i=>i.id===data.properties.id);
      const layerControl=el('select',{'aria-label':'Object layer',disabled:snapshot!==null,onchange:e=>run(()=>edit('assign-layer',{layer:e.target.value}))},data.layers.map(l=>el('option',{value:l.id.value,text:l.name})));
      layerControl.value=data.layers.find(l=>l.name===selected?.layer)?.id.value||'';
      content.append(row('Layer',layerControl));
      const metadata=el('details',{},[el('summary',{text:'Source metadata'}),row('Source identity',read(data.properties.id))]);
      for(const p of data.properties.properties) {
        if(!p.writable) { metadata.append(row(p.name,read(typeof p.value==='object'?JSON.stringify(p.value):p.value))); continue; }
        const structure=p.kind==='Structure';
        const apply=value=>run(async()=>{await edit('property',{id:data.properties.id,property:p.name,value});log(`${p.name} updated.`,'success');});
        if(structure && p.value && ['X','Y'].every(key=>Object.hasOwn(p.value,key))) {
          content.append(el('h3',{text:p.name}));
          for(const axis of ['X','Y','Z'].filter(key=>Object.hasOwn(p.value,key))) {
            const input=el('input',{value:p.value[axis],'aria-label':`${p.name} ${axis}`,title:'Coordinate: press Enter to apply',disabled:snapshot!==null});
            input.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();run(()=>apply({[axis]:String(number(input.value))}));}});
            content.append(row(axis,input));
          }
          continue;
        }
        if(!structure && ['True','False'].includes(String(p.value))) {
          const input=el('select',{'aria-label':p.name,disabled:snapshot!==null,onchange:e=>apply(e.target.value)},['True','False'].map(value=>el('option',{value,text:value==='True'?'Yes':'No'})));
          input.value=p.value;content.append(row(p.name,input));continue;
        }
        const input=el('input',{value:structure?JSON.stringify(p.value):String(p.value??''),'aria-label':p.name,title:`${p.name}: press Enter to apply`,disabled:snapshot!==null});
        input.addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();run(()=>apply(structure?JSON.parse(input.value):input.value));}});
        content.append(row(p.name,input));
      }
      content.append(metadata);
    } else {
      content.append(el('h3',{text:'Drawing'}),row('Layout',read(state.layout)),row('Source entities',read(state.entities)),row('Visible objects',read(data.itemCount)),row('Shadow objects',read(state.objects)),row('Revision',read(state.revision)),row('Undo / redo',read(`${state.undoCount} / ${state.redoCount}`)),row('Current layer',read(currentLayer)),row('Workspace',read(state.workspaceName)),el('p',{text:'Select one object to edit its native properties. Drag blue grips to reshape it. Multiple selections support the Modify tools and layer assignment.'}));
    }
    if(data.gripDiagnostic) content.append(el('p',{text:data.gripDiagnostic}));
  } else if(palette==='Layers') {
    content.append(toolbar(button('＋ New',()=>action('layer-new')),button('Assign current',()=>action('assign-layer'))));
    for(const layer of data.layers) {
      const card=el('section',{class:`layer-card${currentLayer===layer.name?' current':''}`});
      const mutate=changes=>edit('layer',{layer:{id:layer.id,...changes}});
      const color=el('input',{type:'color',value:layer.color,'aria-label':`${layer.name} color`,onchange:e=>run(()=>mutate({color:e.target.value}))});
      const name=el('input',{type:'text',value:layer.name,'aria-label':`${layer.name} layer name`});
      name.addEventListener('change',()=>run(()=>mutate({name:name.value})));
      card.append(el('div',{class:'layer-header'},[color,name,button('✓',()=>{currentLayer=layer.name;layers.set(client.id,currentLayer);drawRibbon();renderPalette();},{title:'Set current layer'})]));
      card.append(el('div',{class:'layer-flags'},[['visible','On'],['frozen','Freeze'],['locked','Lock'],['plot','Plot']].map(([key,label])=>el('label',{},[el('input',{type:'checkbox',checked:layer[key],'aria-label':`${layer.name} ${label}`,onchange:e=>run(()=>mutate({[key]:e.target.checked}))}),label]))));
      const line=el('select',{'aria-label':`${layer.name} linetype`,onchange:e=>run(()=>mutate({linetype:e.target.value}))},data.linetypes.filter(n=>!['bylayer','byblock'].includes(n.toLowerCase())).map(n=>el('option',{value:n,text:n}))); line.value=layer.linetype;
      const weight=el('select',{'aria-label':`${layer.name} lineweight`,onchange:e=>run(()=>mutate({lineweight:e.target.value}))},['Default','W0','W5','W9','W13','W15','W18','W20','W25','W30','W35','W40','W50','W53','W60','W70','W80','W90','W100','W106','W120','W140','W158','W200','W211'].map(w=>el('option',{value:w,text:w==='Default'?w:`${Number(w.slice(1))/100} mm`})));
      weight.value=layer.lineweight; card.append(el('div',{class:'layer-extra'},[line,weight])); content.append(card);
    }
    content.append(el('p',{class:'hint',text:'Layer changes and assignments are undoable source transactions. Plot flags are stored in DXF; render exports do not apply CTB/STB plot styles.'}));
  } else if(palette==='Blocks') {
    content.append(toolbar(button('Create block',()=>action('block-create')),button('Redefine',()=>action('block-redefine')),button('Rename',()=>action('block-rename'))));
    content.append(toolbar(button('Edit definition…',()=>blockUi.begin())));
    if(data.blockEditor) content.append(el('h3',{text:`Editing: ${data.blockEditor.name}`}), toolbar(button('Save block',()=>blockUi.finish(true)),button('Close / discard',()=>blockUi.finish(false))));
    content.append(el('p',{class:'intro',text:'Insert an existing native block definition. The source definition remains shared; each reference is independently selectable.'}));
    for(const block of data.blocks) content.append(el('section',{class:'layer-card'},[
      el('button',{class:'block-card',onclick:()=>run(async()=>{await start('INSERT');session.input(block.name);updatePrompt();})},[icon('INSERT'),el('b',{text:block.name}),el('small',{text:`${block.entities} definition entities`})]),
      toolbar(button('Edit',()=>blockUi.begin(block.id),{'aria-label':`Edit block ${block.name}`}),button('Rename',()=>blockUi.rename(block.id),{'aria-label':`Rename block ${block.name}`}))]));
    if(!data.blocks.length) content.append(el('p',{text:'No named block definitions in this drawing. Open a DXF containing blocks, or create a sample drawing.'}));
  } else if(palette==='Explorer') {
    const filter=el('input',{type:'search',placeholder:'Filter type, layer or handle','aria-label':'Filter drawing objects'}), list=el('div',{class:'object-list'});
    const update=()=>{const q=filter.value.toLowerCase(), items=data.items.filter(i=>`${i.id} ${i.type} ${i.layer}`.toLowerCase().includes(q));list.replaceChildren(...items.slice(0,500).map(item=>el('button',{class:data.selectedEntities.includes(item.id)?'selected':'',title:`${item.type} on ${item.layer}`,onclick:e=>run(()=>edit('select',{mode:'entity',entity:item.id,extend:e.ctrlKey||e.metaKey,remove:e.shiftKey}))},[el('span',{text:item.type}),el('small',{text:item.id})])));if(items.length>500)list.append(el('p',{text:`Showing 500 of ${items.length} matches. Refine the filter.`}));};
    content.append(toolbar(button('Select all',()=>select('all')),button('Clear',()=>select('clear'))),filter,el('p',{class:'hint',text:`${data.itemCount} visible source objects; explorer enumerates up to 5,000. Selection in the drawing remains geometric.`}),list);filter.addEventListener('input',update);update();
  } else if(palette==='Workspaces') {
    content.append(toolbar(button('Fork branch',()=>action('fork')),button('Retain snapshot',()=>action('snapshot'))),el('h3',{text:'Isolated workspaces'}));
    content.append(button('Live document',()=>switchWorkspace('live'),{class:'block-card'}));
    for(const branch of state.branches) content.append(el('div',{class:'layer-card'},[button(`${branch.name} · r${branch.revision}`,()=>switchWorkspace(branch.id)),button('Review merge',()=>merge(branch))]));
    content.append(el('h3',{text:'Retained snapshots'}),button('Current drawing',()=>setSnapshot(null),{class:'block-card'}));
    for(const revision of state.snapshots) content.append(button(`Revision ${revision} — read only`,()=>setSnapshot(revision),{class:'block-card'}));
    content.append(el('p',{class:'hint',text:'Branches have independent history and selection. Review and merge branch edits into the live document before saving DXF. Snapshots pin source and font resources.'}));
  } else {
    content.append(el('p',{class:'intro',text:`${state.diagnostics.length} reported rendering diagnostics. These are not hidden by the CAD workspace.`}),toolbar(button('Load SHX / SHP',()=>action('font')),button('Advanced diagnostics',()=>action('legacy'))));
    for(const d of state.diagnostics) content.append(el('div',{class:'diagnostic'},[el('b',{text:d.code}),el('span',{text:`${d.message}${d.id?` (${d.id})`:''}`})]));
    if(!state.diagnostics.length) content.append(el('p',{text:'No diagnostics for the current scene.'}));
  }
  if(snapshot!==null) content.querySelectorAll('input,select').forEach(n=>n.disabled=true);
}
async function documentTabs() {
  const receipt=client.id; const result=await client.documents(); if(receipt!==client.id) return; documents=result;
  $('documents').replaceChildren(...documents.map(doc=>el('div',{class:`document-tab${doc.id===client.id?' active':''}`},[
    button(`${doc.dirty?'● ':''}${doc.name}`,()=>activate(doc.id),{class:'name',role:'tab','aria-selected':doc.id===client.id}),
    button('×',()=>closeDocument(doc),{class:'close',title:`Close ${doc.name}`})])));
  const doc=documents.find(d=>d.id===client.id); $('title').textContent=`${doc?.name||'Drawing'}${doc?.dirty?' *':''} — netDxf Shadow Studio`;
}
async function activate(id) { if(id===client.id && client.state) return; cancel(false); snapshot=null;viewport.snapshot=null;viewId='';await client.activate(id);await documentTabs(); }
async function newDocument(demo=false) { const created=await client.create(`Drawing${documents.length+1}.dxf`,demo);await activate(created.id);log(demo?'Sample drawing opened.':'New drawing opened.'); }
async function closeDocument(doc) {
  if(doc.id!==client.id) await activate(doc.id);
  let discard=false;
  if(doc.dirty) { const answer=await dialog('Unsaved drawing',[el('p',{text:`Discard changes to “${doc.name}”? Save DXF first to keep your edits. This also discards isolated branches.`})],'Discard'); if(!answer)return;discard=true; }
  await client.mutate('close',{discard}); const all=await client.documents(); cancel(false);
  if(all.length) await activate(all[0].id); else await newDocument();
}
async function switchWorkspace(id) { cancel(false);setSnapshot(null);await edit('workspace',{id}); }
function setSnapshot(value) { snapshot=value;viewport.snapshot=value;cancel(false);$('view-notice').hidden=value===null;$('view-notice').textContent=`Retained revision ${value} — read only`;viewport.requestFrame();renderPalette();updateTools(); }
async function merge(branch) {
  const state=client.state, request={documentId:state.documentId,branchId:branch.id,targetRevision:state.sourceRevision,incomingRevision:branch.revision};
  let preview=await client.mutate('merge/preview',request);
  if(!preview.canApply) {
    const policy=el('select',{'aria-label':'Conflict policy'},['Unresolved','Target','Incoming'].map(v=>el('option',{value:v,text:v==='Target'?'Keep live values':v==='Incoming'?'Keep branch values':'Do not resolve automatically'})));
    if(!await dialog('Merge conflicts',[el('p',{text:`${preview.conflictCount} conflicts. Review conflicting fields before choosing a resolution.`}),el('pre',{class:'hint',text:JSON.stringify(preview.conflicts,null,2)}),inputRow('Conflict policy',policy)],'Recompute'))return;
    preview=await client.mutate('merge/preview',{...request,choice:policy.value});
    if(!preview.canApply)throw new Error('The merge still has unresolved conflicts. No source changes were made.');
  }
  if(await dialog('Apply reviewed merge',[el('p',{text:`Merge “${branch.name}”: ${preview.added} added, ${preview.removed} removed, ${preview.changed} changed shadow objects. This is one undoable live-document transaction.`})],'Merge')) {
    await edit('merge/apply',{documentId:preview.documentId,planId:preview.planId,targetRevision:preview.targetRevision,incomingRevision:preview.incomingRevision});log('Branch merged into the live document.','success');
  }
}
function download(blob,name) { const url=URL.createObjectURL(blob), a=el('a',{href:url,download:name});document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),10000); }
async function save() { editable();const blob=await client.mutate('save');download(blob,drawingFileName(documents.find(d=>d.id===client.id)?.name||'Drawing','dxf'));await documentTabs();log('DXF download created. Keep the downloaded file to retain this session.','success'); }
async function exportImage(backend) {
  const query=new URLSearchParams({width:viewport.canvas.width,height:viewport.canvas.height,cx:viewport.camera.x,cy:viewport.camera.y,scale:viewport.camera.scale*viewport.dpr,backend,white:'true',revision:client.state.revision});
  if(snapshot!==null)query.set('snapshot',snapshot);
  const blob=await client.read(`render?${query}`,{blob:true});if(blob)download(blob,`drawing.${backend==='skia'?'png':'svg'}`);
}
async function draftingSettings() {
  const grid=el('input',{type:'text',value:viewport.settings.gridStep,'aria-label':'Grid spacing'}),flags=[];
  const content=[inputRow('Grid spacing (drawing units)',grid),el('h3',{text:'Running object snaps'})];
  for(const [bit,name] of [[1,'Endpoint'],[2,'Midpoint'],[4,'Center'],[8,'Quadrant'],[16,'Intersection'],[32,'Perpendicular'],[64,'Nearest'],[128,'Insertion'],[256,'Tangent']]) {
    const input=el('input',{type:'checkbox',checked:!!(viewport.settings.snapModes&bit),'aria-label':name});flags.push([bit,input]);content.push(inputRow(name,input));
  }
  content.push(el('p',{class:'hint',text:'Ortho constrains to 90°. Polar uses 15° increments. Explicit typed coordinates take precedence. Native object snap can override grid and angular constraints. Numerical nearest-curve solutions are marked ≈.'}));
  if(await dialog('Drafting settings',content)) {const n=number(grid.value);if(n<1e-6||n>1e9)throw new Error('Grid spacing must be between 0.000001 and 1,000,000,000.');viewport.settings.gridStep=n;viewport.settings.snapModes=flags.reduce((sum,[bit,input])=>sum+(input.checked?bit:0),0);persist();viewport.invalidate();}
}
async function action(name) {
  if(isAuthoringAction(name)){cancel(false);return authoringDialog(name,{client,editable,edit,dialog,showPalette,currentLayer});}
  if(['properties','layers','blocks','explorer','workspaces','diagnostics'].includes(name))return showPalette(name[0].toUpperCase()+name.slice(1));
  switch(name) {
    case 'block-edit':editable();return blockUi.begin();
    case 'block-save':editable();return blockUi.finish(true);
    case 'block-close':editable();return blockUi.finish(false);
    case 'new':return newDocument(); case 'demo':return newDocument(true); case 'open':$('open-file').click();return;case 'save':return save();case 'save-as':editable();return saveDrawingAs({client,dialog,inputRow,download,documentName:documents.find(d=>d.id===client.id)?.name||'Drawing',refreshDocuments:documentTabs,log,editable});case 'font':$('font-file').click();return;
    case 'undo':case 'redo':cancel(false);return edit(`history/${name}`);
    case 'fit':viewport.fit();return;case 'regen':await client.refresh();viewport.requestFrame(true);return;
    case 'zoom-in':viewport.camera.scale=Math.min(1e8,viewport.camera.scale*1.5);viewport.changed();return;
    case 'zoom-out':viewport.camera.scale=Math.max(1e-8,viewport.camera.scale/1.5);viewport.changed();return;
    case 'pan':{const next=!viewport.panMode;cancel(false);viewport.panMode=next;log(next?'Pan: drag in the drawing; Escape to exit.':'Pan mode ended.');return;}
    case 'palette':$('workspace').classList.toggle('hide-palette');persist();return;
    case 'dock':$('workspace').classList.toggle('left');persist();return;
    case 'clean':$('studio').classList.toggle('clean');return;
    case 'settings':return draftingSettings();
    case 'layer-new':{const value=await ask('New layer','Layer name','Layer1');if(value!==null){await edit('layer',{layer:{name:value}});currentLayer=value;layers.set(client.id,value);drawRibbon();showPalette('Layers');}return;}
    case 'assign-layer':{const layer=client.data.layers.find(l=>l.name===currentLayer);if(!layer)throw new Error('Choose an existing layer.');await edit('assign-layer',{layer:layer.id.value});return;}
    case 'snapshot':await edit('retain');showPalette('Workspaces');return;
    case 'snapshots':showPalette('Workspaces');return;
    case 'fork':{const name=await ask('Fork immutable workspace','Branch name','Design alternative');if(name)await edit('fork',{name});showPalette('Workspaces');return;}
    case 'annotation':{const s=client.state,mode=el('select',{'aria-label':'Annotation mode'},['Drawing','Disabled','Enabled'].map(v=>el('option',{value:v,text:v})));mode.value=s.annotationMode;
      const paper=el('input',{value:s.annotationPaperUnits,'aria-label':'Paper units'}),drawing=el('input',{value:s.annotationDrawingUnits,'aria-label':'Drawing units'});
      if(await dialog('Model annotation context',[inputRow('Mode',mode),inputRow('Paper units',paper),inputRow('Drawing units',drawing),el('p',{class:'hint',text:'Presentation-only context; the source drawing header is unchanged.'})]))await edit('annotation-view',{mode:mode.value,paperUnits:number(paper.value),drawingUnits:number(drawing.value)});return;}
    case 'linetype':{const selection=el('select',{},[el('option',{value:'Periodic',text:'Periodic pattern'}),el('option',{value:'A',text:'Fit endpoint dashes'})]);selection.value=client.state.linetypeAlignment;if(await dialog('Linetype pattern alignment',[inputRow('Alignment',selection)]))await edit('linetype-view',{alignment:selection.value});return;}
    case 'graph':if(viewport.graph){viewport.graph=null;viewport.invalidate();}else{const key=viewKey(client.state),graph=await client.read('graph');if(graph){viewport.graph=graph;viewport.graphKey=key;viewport.invalidate();log(`Topology: ${graph.components} components, ${graph.cycleRank} independent cycles, ${graph.deadEnds} dead ends.`);}}return;
    case 'export-png':return exportImage('skia');case 'export-svg':return exportImage('svg');
    case 'legacy':window.open('diagnostics.html','_blank','noopener');return;
    case 'reset-ui':for(const [key,value] of Object.entries({grid:true,snap:false,ortho:false,polar:false,osnap:true,dynamic:true,gridStep:10,snapModes:23}))viewport.settings[key]=value;$('workspace').className='workspace';$('studio').className='';document.documentElement.style.removeProperty('--palette-width');document.documentElement.style.removeProperty('--command-height');persist();modes();viewport.changed();return;
    case 'help':await dialog('Shadow CAD command reference',[el('p',{class:'hint',text:'Native netDxf editing on immutable shadow transactions. Enter ends point chains; C closes; U removes the last draft point; Escape cancels. Coordinates: X,Y; @dx,dy; @distance<angle. A bare distance follows the cursor. All edits are undoable.'}),el('table',{class:'help-table'},Object.entries(commands).map(([name,s])=>el('tr',{},[el('td',{text:`${name} (${s.aliases.join(', ')})`}),el('td',{text:s.label})]))),el('p',{class:'hint',text:'TRIM/EXTEND: select target LINE first, then a LINE/CIRCLE boundary, Enter, then pick an endpoint. FILLET/CHAMFER: two intersecting XY lines. EXPLODE: 2D polylines. SPLINE: cubic control points. These commands preserve native source geometry and refuse unsupported combinations. BLOCK creates shared definitions; BLOCKREDEFINE replaces one from selected sources; BLOCKRENAME retains references. HATCH/HATCHEDIT accept built-in name, scale, angle and island style. HATCHPOLY makes a solid polygon. Hatch settings import bounded custom PAT files. DWG import uses signature detection; SAVEAS offers host-declared DWG profiles with strict conversion checks. 3D solids, general curve trimming, AutoLISP and CTB/STB plotting are not implemented.'}),el('p',{class:'hint',text:'Ctrl+N/O/S: new/open/save · Ctrl+Z/Y: undo/redo · Ctrl+1: palettes · Ctrl+0: clean screen · F2: history · F3: object snap · F7: grid · F8: ortho · F9: grid snap · F10: polar · F12: dynamic input. Middle drag pans; wheel zooms about the pointer; right click opens command actions. Two-finger touch pans/zooms.'})],'Close');return;
    default:throw new Error(`Unknown workspace action: ${name}`);
  }
}
function modes() {
  $('drafting-modes').replaceChildren(...[['snap','SNAP','F9'],['grid','GRID','F7'],['ortho','ORTHO','F8'],['polar','POLAR','F10'],['osnap','OSNAP','F3'],['dynamic','DYN','F12']].map(([key,label,shortcut])=>button(label,()=>toggle(key),{'aria-pressed':viewport.settings[key],title:`${label} (${shortcut})`})));
}
function toggle(key) {viewport.settings[key]=!viewport.settings[key];if(key==='ortho'&&viewport.settings.ortho)viewport.settings.polar=false;if(key==='polar'&&viewport.settings.polar)viewport.settings.ortho=false;persist();modes();viewport.invalidate();}
function cursorChanged(p,pixel) {
  $('coordinates').textContent=`${format(p.x)}, ${format(p.y)}, ${format(p.z)}`;
  const node=$('dynamic-input');node.hidden=!viewport.settings.dynamic||!session||session.selecting||!pixel;
  if(!node.hidden){node.textContent=`${session.step.prompt}: ${format(p.x)}, ${format(p.y)}`;node.style.left=`${Math.max(4,Math.min(viewport.size.w-280,pixel.x+20))}px`;node.style.top=`${Math.min(viewport.size.h-28,pixel.y+25)}px`;}
}
function contextMenu(x,y) {
  const menu=$('context-menu');menu.replaceChildren(button(session?'Enter / finish':`Repeat ${lastCommand}`,()=>submit('')),button('Cancel',()=>cancel()),el('hr'),button('Undo',()=>action('undo')),button('Zoom extents',()=>action('fit')),button('Properties',()=>action('properties')),button('Select all',()=>select('all')));
  menu.style.left=`${Math.min(x,innerWidth-205)}px`;menu.style.top=`${Math.min(y,innerHeight-220)}px`;menu.hidden=false;
}
$('quick-access').replaceChildren(...[['new','New (Ctrl+N)'],['open','Open (Ctrl+O)'],['save','Save (Ctrl+S)'],['undo','Undo (Ctrl+Z)'],['redo','Redo (Ctrl+Y)']].map(([name,label])=>{const b=tool(name,label,true);b.querySelector('span').remove();return b;}));
$('ribbon-tabs').replaceChildren(...tabs.map(name=>button(name,()=>{tab=name;drawRibbon();$('ribbon-tabs').querySelectorAll('button').forEach(b=>b.setAttribute('aria-selected',String(b.textContent===name)));},{role:'tab','aria-selected':name===tab})));
$('file-menu').addEventListener('click',()=>{const menu=$('context-menu');menu.replaceChildren(...[['new','New drawing'],['demo','New sample drawing'],['open','Open DXF / DWG…'],['save','Save DXF'],['save-as','Save drawing as…'],['export-png','Export PNG'],['export-svg','Export SVG'],['legacy','Advanced diagnostics']].map(([name,label])=>button(label,()=>action(name))));menu.style.left='3px';menu.style.top='32px';menu.hidden=false;});
document.addEventListener('click',e=>{const command=e.target.closest('[data-command]'),target=e.target.closest('[data-action]');if(command)run(()=>start(command.dataset.command));else if(target)run(()=>action(target.dataset.action));if(!e.target.closest('#file-menu'))$('context-menu').hidden=true;});
$('command-form').addEventListener('submit',e=>{e.preventDefault();const text=$('command-input').value;$('command-input').value='';run(()=>submit(text));});
$('command-input').addEventListener('input',()=>{
  const value=$('command-input').value.trim().toUpperCase(),matches=!session&&value&&!value.includes(' ')?Object.keys(commands).filter(k=>k.startsWith(value)||commands[k].aliases.some(a=>a.startsWith(value))).slice(0,9):[];
  $('suggestions').replaceChildren(...matches.map(name=>el('button',{type:'button',onclick:()=>{ $('command-input').value='';$('suggestions').hidden=true;run(()=>start(name));}},[el('b',{text:name}),el('small',{text:commands[name].label})])));$('suggestions').hidden=!matches.length;
});
$('backend').addEventListener('change',e=>{viewport.backend=e.target.value;viewport.requestFrame();});
$('open-file').addEventListener('change',e=>run(async()=>{
  const file=e.target.files[0];e.target.value='';if(!file)return;const format=await inspectDrawing(file);
  log('Opening drawing…');
  let opened;
  try { opened=await client.open(file); }
  catch(error) { await documentTabs().catch(()=>{});throw error; }
  if(client.id===opened.id)viewport.fit();await documentTabs();log(`Opened ${format.toUpperCase()} drawing ${file.name}.`,'success');
}));
$('font-file').addEventListener('change',e=>run(async()=>{const file=e.target.files[0];e.target.value='';if(!file)return;editable();if(file.size>4*1024*1024)throw new Error('Font upload limit is 4 MiB.');await client.mutate('font',file,{binary:true,query:{name:file.name}});log(`Loaded ${file.name}.`,'success');}));
document.addEventListener('keydown',e=>{
  if($('dialog').open)return;
  const input=e.target.closest('input,select,textarea'),control=e.ctrlKey||e.metaKey;
  if(e.key==='Escape'){e.preventDefault();cancel();viewport.panMode=false;$('context-menu').hidden=true;return;}
  if(control&&['s','o','n','z','y','0','1'].includes(e.key.toLowerCase())){e.preventDefault();run(()=>action({s:'save',o:'open',n:'new',z:e.shiftKey?'redo':'undo',y:'redo','0':'clean','1':'palette'}[e.key.toLowerCase()]));return;}
  const toggles={F3:'osnap',F7:'grid',F8:'ortho',F9:'snap',F10:'polar',F12:'dynamic'};if(toggles[e.key]){e.preventDefault();toggle(toggles[e.key]);return;}
  if(e.key==='F1'){e.preventDefault();run(()=>action('help'));return;}if(e.key==='F2'){e.preventDefault();$('studio').classList.toggle('history-expanded');return;}
  if(input){
    if(e.target===$('command-input')){
      if(e.key==='ArrowUp'||e.key==='ArrowDown'){e.preventDefault();historyIndex=Math.max(0,Math.min(lastInput.length,historyIndex+(e.key==='ArrowUp'?-1:1)));e.target.value=lastInput[historyIndex]||'';}
      if(e.key==='Tab'&&!$('suggestions').hidden){e.preventDefault();e.target.value=$('suggestions').querySelector('b')?.textContent||e.target.value;}
    }return;
  }
  if(e.key===' '){e.preventDefault();viewport.space=true;return;}
  if(control&&e.key.toLowerCase()==='a'){e.preventDefault();run(()=>select('all'));return;}
  if(e.key==='Delete'){e.preventDefault();run(()=>start('ERASE'));return;}
  if(e.key==='Enter'){e.preventDefault();run(()=>submit(''));return;}
  if(e.key.length===1&&!control&&!e.altKey){e.preventDefault();$('command-input').focus();$('command-input').value+=e.key;$('command-input').dispatchEvent(new Event('input'));}
});
document.addEventListener('keyup',e=>{if(e.key===' ')viewport.space=false;});window.addEventListener('blur',()=>{viewport.space=false;});
function splitter(id,vertical){const node=$(id);node.addEventListener('pointerdown',e=>{e.preventDefault();node.setPointerCapture(e.pointerId);const start={x:e.clientX,y:e.clientY,width:$('palette').getBoundingClientRect().width,height:$('command-line').getBoundingClientRect().height};
  const move=m=>{if(vertical){const sign=$('workspace').classList.contains('left')?1:-1;document.documentElement.style.setProperty('--palette-width',`${Math.max(210,Math.min(innerWidth*.55,start.width+(m.clientX-start.x)*sign))}px`);}else document.documentElement.style.setProperty('--command-height',`${Math.max(32,Math.min(innerHeight*.55,start.height+start.y-m.clientY))}px`);};
  const up=()=>{node.removeEventListener('pointermove',move);persist();};node.addEventListener('pointermove',move);node.addEventListener('pointerup',up,{once:true});node.addEventListener('pointercancel',up,{once:true});});
  node.addEventListener('keydown',e=>{const step=e.key==='ArrowLeft'||e.key==='ArrowDown'?-20:e.key==='ArrowRight'||e.key==='ArrowUp'?20:0;if(!step)return;e.preventDefault();const size=vertical?$('palette').getBoundingClientRect().width:$('command-line').getBoundingClientRect().height;document.documentElement.style.setProperty(vertical?'--palette-width':'--command-height',`${Math.max(vertical?210:32,Math.min(vertical?550:innerHeight*.55,size+step))}px`);persist();});}
splitter('palette-splitter',true);splitter('command-splitter',false);
client.addEventListener('reset',()=>{viewport.clearFrame();viewport.graph=null;$('loading').hidden=false;viewport.invalidate();});
client.addEventListener('state',()=>{
  const s=client.state; $('studio').dataset.document=client.id;
  const identity=JSON.stringify([client.id,s.serverId,s.documentId,s.workspaceId,s.viewVersion]);
  if(activeIdentity && identity!==activeIdentity){cancel(false);snapshot=null;viewport.snapshot=null;$('view-notice').hidden=true;}
  activeIdentity=identity;
  const nextView=`${client.id}:${s.documentId}:${s.workspaceId}:${s.layout}`;
  if(nextView!==viewId){const saved=views.get(nextView);viewId=nextView;viewport.camera=saved||fit(s.bounds,viewport.size);}
  currentLayer=layers.get(client.id)||currentLayer;if(!client.data.layers.some(l=>l.name===currentLayer))currentLayer='0';
  $('layouts').replaceChildren(...s.layouts.map(name=>button(name,async()=>{cancel(false);setSnapshot(null);await edit('layout',{name});},{role:'tab','aria-selected':name===s.layout})));
  $('revision').textContent=`r${s.revision}`;
  if(snapshot===null){$('view-notice').hidden=!client.data.blockEditor;$('view-notice').textContent=client.data.blockEditor?`Block Editor: ${client.data.blockEditor.name} — BSAVE to commit, BCLOSE to discard`:'';}
  if(client.data.blockEditor)$('layouts').querySelectorAll('button').forEach(b=>b.disabled=true);$('selection-count').textContent=`${client.data.selectedEntities.length} selected · ${client.data.itemCount} visible`;
  if(stateKey!==viewKey(s)){stateKey=viewKey(s);drawRibbon();renderPalette();viewport.setData();}
  $('loading').hidden=true;updatePrompt();updateTools();
});
client.addEventListener('busy',()=>{$('connection').textContent=client.busy?'Working…':'Local';$('connection').classList.toggle('busy',!!client.busy);updateTools();if(!client.busy)viewport.requestFrame();});
window.addEventListener('beforeunload',e=>{if(documents.some(d=>d.dirty)){e.preventDefault();e.returnValue='';}});
bindViewportPageLifetime(viewport);
async function pollDocuments() {
  try {
    if(!document.hidden&&!client.busy&&!session&&!viewport.inputActive&&!$('dialog').open&&!$('palette-content').contains(document.activeElement))
      await run(async()=>{await client.refresh();await documentTabs();});
  } finally { setTimeout(pollDocuments,2500); }
}
setTimeout(pollDocuments,2500);
log('netDxf Shadow CAD Studio — native DXF · immutable transactions · local workspace');
log('Type HELP for commands. Middle-drag pans; wheel zooms; Enter finishes; Escape cancels.');
drawRibbon();modes();renderPalette();
run(async()=>{documents=await client.documents();if(documents.length)await activate(documents[0].id);else await newDocument();});
