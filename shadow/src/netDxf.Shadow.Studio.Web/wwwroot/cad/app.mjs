import { RibbonCustomization } from './ribbon-customization.mjs';
import { createDialogLifecycle } from './dialog-lifecycle.mjs';
import { renderCustomizedRibbonGroups, ribbonCustomizationTools, refreshCustomRibbonCommands } from './ribbon-customization-ui.mjs';
import { layerManagerTools } from './layer-manager-ui.mjs';
import { resourceTools } from './resource-ui.mjs';
import { moduleTools } from './module-ui.mjs';
import { analysisTools } from './analysis-ui.mjs';
import { drawingFileName, inspectDrawing } from './exchange-contracts.mjs';
import { saveDrawingAs } from './exchange-ui.mjs';
import { authoringRequest } from './authoring-contracts.mjs';
import { authoringDialog, isAuthoringAction } from './authoring-ui.mjs';
import { RibbonLayout, chromePreferences, ribbonModes, reconcileRibbonContext } from './ribbon-layout.mjs';
import { bindRovingToolbar } from './ribbon-keyboard.mjs';
import { PaletteDock } from './palette-docking.mjs';
import { workspaceProfileTools } from './workspace-profile-ui.mjs';
import { workspaceProfileLayout } from './workspace-profiles.mjs';
import { layerStateTools } from './layer-state-ui.mjs';
import { attributeTools } from './attribute-ui.mjs';
import { synchronizeAttributes } from './attribute-sync-ui.mjs';
import { definitionTools } from './attribute-definition-ui.mjs';
import { graphPalette } from './graph-ui.mjs';
import { referenceGraphPalette } from './reference-graph-ui.mjs';
import { buildCommandCatalog } from './command-catalog.mjs';
import { createCommandAccess } from './command-access.mjs';
import { CadClient } from './client.mjs';
import { blockTools } from './block-ui.mjs';
import { blockLibraryTools } from './block-library-ui.mjs';
import { createInsertSession, resolveInsertCommand } from './block-contracts.mjs';
import { insertionDefinitions, InsertAttributeSession } from './insert-attribute-contracts.mjs';
import { insertionAttributeDialog } from './insert-attribute-ui.mjs';
import { CadViewport, bindViewportPageLifetime } from './viewport.mjs';
import { CommandSession, commands, commandName, number, point, distance, polygonArea, format, fit } from './core.mjs';
import { $, el, icon, ribbon, ribbonEntries as describeRibbonEntries } from './ui.mjs';
import { viewKey } from '../studio-state.mjs';
import { mountAgentWorkspace } from '../agents/workspace.mjs';
import { installStudioControl } from '../agents/studio-control.mjs';

const client = new CadClient();
const showDialog = createDialogLifecycle($('dialog'));
const viewport = new CadViewport($('viewport'), $('drawing'), client, {
  pick: p => takePoint(p), select: (p, extend, remove) => select('pick', p, p, extend, remove),
  selectCandidate: (candidate,p,extend,remove,expectedKey) => selectCandidate(candidate,p,extend,remove,expectedKey),
  box: (a,b,crossing,extend,remove) => select(crossing?'crossing':'window',a,b,extend,remove),
  window: (a,b) => stretchWindow(a,b),
  grip: (grip,position) => edit('grip',{entity:grip.entity,grip:grip.id,position}),
  move: (p,pixel) => cursorChanged(p,pixel), error: report, message: text=>log(text),
  context: e => contextMenu(e.clientX,e.clientY), frame: () => { $('loading').hidden=true; if(!client.busy) $('connection').textContent='Local'; },
  camera: camera => { if (viewId) views.set(viewId,{...camera}); }
});
let session = null, tab = 'Home', palette = 'Properties', currentLayer = '0', lastCommand = 'LINE';
let documents = [], viewId = '', stateKey = '', activeIdentity = '', snapshot = null, lastInput = [], historyIndex = 0, toastTimer;
let agentWorkspace = null;
let commandAccess = null;
let ribbonContext = {};
const renderGraph = graphPalette({client, viewport, report, snapshot:()=>snapshot,
  beginPick:()=>cancel(false), download:(name,text,type)=>download(new Blob([text],{type}),name),
  selectSources:(sources,expectedKey)=>edit('select',{mode:'entities',entities:[...new Set(sources.map(source=>source.rootEntity).filter(Boolean))].map(value=>({value}))},{expectedKey})});
const renderReferences=referenceGraphPalette({client,report,snapshot:()=>snapshot,
  download:(name,text,type)=>download(new Blob([text],{type}),name),
  selectSources:(ids,expectedKey)=>edit('select',{mode:'entities',entities:ids.map(value=>({value}))},{expectedKey})});
const views = new Map(), layers = new Map();
let profileStorage;try{profileStorage=localStorage;}catch{/* Storage may be unavailable in embedded hosts. */}
const ribbonCustomization = new RibbonCustomization({storage:profileStorage,report,changed:()=>{drawRibbonTabs();drawRibbon();}});
const ribbonLayout = new RibbonLayout($('ribbon'),{customization:ribbonCustomization,report});
let chrome = chromePreferences();
const ribbonModeLabels={full:'Full ribbon',buttons:'Panel buttons',titles:'Panel titles',tabs:'Tabs only'};
const blockUi = blockTools({client, edit, dialog, cancel, fit:()=>viewport.fit(), layer:()=>currentLayer, placeInsert:options=>{editable();setSession(createInsertSession(options,currentLayer));lastCommand='INSERT';ensureCommandLine();$('command-input').focus({preventScroll:true});}});
const blockLibraryUi = blockLibraryTools({client,edit,dialog,showPalette,download});
const resourceUi = resourceTools({client, edit, dialog, run, editable,layerFilters:()=>layerManager.open()});
const layerManager=layerManagerTools({client,edit,dialog,editable,editLayer:(id,create)=>resourceUi.layers(id,create),report});
const ribbonEditor=ribbonCustomizationTools({store:ribbonCustomization,entries:()=>describeRibbonEntries(client.data),
  panels:name=>rawRibbonGroups(name).map(node=>({id:node.dataset.ribbonPanel||node.dataset.panel,name:node.dataset.panel})),
  catalog:commandCatalog,dialog,download:(name,text,type)=>download(new Blob([text],{type}),name),report});
const analysisPalettes=Object.fromEntries(['Health','Edit previews','Workflows'].map(name=>[name,analysisTools({client,edit,viewport,report,editable})]));
const moduleUi = moduleTools({client,edit,dialog,report,editable,viewport,showPalette,download,snapshot:()=>snapshot,
  place:value=>{cancel(false);setSession(value);ensureCommandLine();$('command-input').focus({preventScroll:true});},
  changed:()=>{drawRibbonTabs();drawRibbon();renderPalette();commandAccess?.refresh();}});
function persisted() { try { const value=JSON.parse(localStorage.getItem('netdxf-cad-ui-v1') || '{}');return value&&typeof value==='object'&&!Array.isArray(value)?value:{}; } catch { return {}; } }
const settings = persisted();
const insertionSettings={attreq:settings.attreq===0?0:1,attdia:settings.attdia===0?0:1};
chrome = chromePreferences(settings);
for (const key of ['grid','snap','ortho','polar','osnap','dynamic','tracking','selectionCycling']) if (typeof settings[key] === 'boolean') viewport.settings[key]=settings[key];
if (Number.isFinite(settings.gridStep) && settings.gridStep>=1e-6 && settings.gridStep<=1e9) viewport.settings.gridStep=settings.gridStep;
if (Number.isFinite(settings.polarAngle) && settings.polarAngle>0 && settings.polarAngle<=90) viewport.settings.polarAngle=settings.polarAngle;
if (Number.isFinite(settings.polarTolerance) && settings.polarTolerance>=0 && settings.polarTolerance<=15) viewport.settings.polarTolerance=settings.polarTolerance;
if (Number.isInteger(settings.snapModes) && settings.snapModes>=0 && settings.snapModes<=511) viewport.settings.snapModes=settings.snapModes;
const paletteNames=['Properties','Layers','Blocks','Explorer','Workspaces','Graph','References','Modules','Health','Edit previews','Workflows','Diagnostics'];
const docking=new PaletteDock({workspace:$('workspace'),legacyPalette:$('palette'),legacySplitter:$('palette-splitter'),
  palettes:paletteNames.map(id=>({id,title:id})),legacySettings:settings,render:renderPaletteContent,
  onActiveChange:id=>{palette=id;},onLayoutChange:()=>viewport.resize(),keepOpen:id=>id==='Graph'&&!!viewport.graphPick,onHide:id=>{analysisPalettes[id]?.destroy();if(id==='Modules')moduleUi.hide?.();if(id==='Graph')renderGraph.hide();if(id==='References')renderReferences.hide();},report});
const uiWorkspaces=workspaceProfileTools({paletteIds:paletteNames,storage:profileStorage,dialog,report,
  download:(name,text,type)=>download(new Blob([text],{type}),name),
  capture:()=>({chrome,tab,commandHeight:parseFloat(document.documentElement.style.getPropertyValue('--command-height'))||90,palettes:docking.snapshot(),quickAccess:commandAccess?.snapshot(),ribbonCustomization:ribbonCustomization.snapshot()}),
  restore:value=>{
    const layout=workspaceProfileLayout(value,paletteNames);
    ribbonCustomization.restore(layout.ribbonCustomization,{notify:false,strict:false});
    ribbonLayout.hide();chrome=layout.chrome;
    document.documentElement.style.setProperty('--command-height',`${Math.min(innerHeight*.55,layout.commandHeight)}px`);
    tab=ribbonEntries().some(entry=>entry.key===layout.tab&&!entry.disabled)?layout.tab:'Home';
    docking.restore(layout.palettes);commandAccess?.restore(layout.quickAccess);applyChrome();drawRibbonTabs();drawRibbon();persist();viewport.resize();
  }});
if(Number.isFinite(settings.commandHeight))document.documentElement.style.setProperty('--command-height',`${Math.max(32,Math.min(innerHeight*.55,settings.commandHeight))}px`);
applyChrome();
function persist() { try { const {temporaryOrtho,...drafting}=viewport.settings;localStorage.setItem('netdxf-cad-ui-v1',JSON.stringify({...drafting,...chrome,...insertionSettings,commandHeight:parseFloat(document.documentElement.style.getPropertyValue('--command-height'))||90})); } catch { /* Private browsing can disable storage. */ } }
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
function setSession(value) { if(value!==null)client.beginInteraction(); viewport.insertionPreview.clear(); viewport.cancelInput(); session=value; viewport.command=value; viewport.panMode=false; viewport.snapHit=null; updatePrompt(); updateTools(); viewport.invalidate(); }
function cancel(clearSelection=true) { setSession(null); $('command-input').value=''; $('suggestions').hidden=true; $('dynamic-input').hidden=true; if(clearSelection && client.state && snapshot===null && !client.busy) run(()=>edit('select',{mode:'clear'})); }
async function start(name) {
  editable(); if(client.busy) throw new Error('Wait for the current operation to complete.');
  setSession(new CommandSession(name,currentLayer,client.data.selectedEntities.length));
  session.forceCommandAttributes=name.toUpperCase()==='-INSERT';
  lastCommand=session.forceCommandAttributes?'-INSERT':session.name;
  log(`${session.name} — ${commands[session.name].label}`,'input');
  if(session.spec.selection && !session.spec.crossingSelection && !session.selecting) { session.acceptSelection(client.data.selectedEntities.length); if(session.complete) await complete(); }
  ensureCommandLine();$('command-input').focus({preventScroll:true});
}
function updatePrompt() { $('prompt').textContent=session?.prompt || 'Command:'; $('command-input').placeholder=session ? (session.selecting?'Select in the viewport, then Enter':['point','window'].includes(session.step.kind)?'X,Y · @X,Y · @distance<angle':'Enter value'):'Type a command'; }
async function acquireCrossing(target) {
  const [first,second]=target.command.points;
  await edit('select',{mode:'crossing',first,second,tolerance:8/viewport.camera.scale,extend:false},{expectedKey:client.key});
  if(session!==target)return;
  updatePrompt();viewport.invalidate();
  if(!client.data.selectedEntities.length)throw new Error('The crossing window contains no source objects. Enter C to choose another window.');
}
async function stretchWindow(first,second) { if(!session?.spec.crossingSelection)return;editable();const target=session;target.captureWindow(first,second);await acquireCrossing(target); }
async function takePoint(p) { if(!session) return; editable();const target=session,window=target.step.kind==='window';target.pick(p);if(window&&target.selecting)await acquireCrossing(target);if(session!==target)return;updatePrompt();viewport.invalidate();if(target.complete)await complete(); }
async function takeInput(value) {const target=session,window=target.step.kind==='window';target.input(value,viewport.resolved||viewport.cursor);if(window&&target.selecting)await acquireCrossing(target);}

async function complete() {
  const completed=session; if(!completed?.complete||completed.processing) return;
  if(completed.definitionRequest){
    completed.processing=true;
    try{const definition=structuredClone(completed.definitionRequest);definition.changes.add[0].position=structuredClone(completed.command.points[0]);
      await edit('attribute-definitions',{definition},{expectedKey:completed.expectedKey});log(`ATTDEF completed — revision ${client.state.revision}.`,'success');}
    finally{if(session===completed)setSession(null);}return;
  }
  if(completed.moduleRequest){
    try{const request=structuredClone(completed.moduleRequest);request.command.points=structuredClone(completed.command.points);
      await edit('modules/command',request,{expectedKey:completed.expectedKey});log(`${completed.name} completed — revision ${client.state.revision}.`,'success');}
    finally{if(session===completed)setSession(null);}return;
  }
  if(completed.spec.measure) {
    const p=completed.command.points;
    if(completed.name==='DIST') log(`Distance = ${format(distance(p[0],p[1]))}; ΔX = ${format(p[1].x-p[0].x)}; ΔY = ${format(p[1].y-p[0].y)}; angle = ${format(Math.atan2(p[1].y-p[0].y,p[1].x-p[0].x)*180/Math.PI)}°`,'success');
    else log(`Area = ${format(polygonArea(p))}; perimeter = ${format(p.reduce((sum,v,i)=>sum+distance(v,p[(i+1)%p.length]),0))}`,'success');
    setSession(null); return;
  }
  let command=structuredClone(completed.command);const key=completed.expectedKey||client.key;
  let placed=false;completed.processing=true;
  const current=()=>{if(session!==completed||client.key!==key||client.busy)throw new Error('The drawing or insertion changed. Repeat INSERT.');};
  try {
    current();
    if(command.name==='INSERT') {
      command=resolveInsertCommand(command,client.data.blocks);
      const block=client.data.blocks.find(b=>b.id===command.reference.value);
      let attributes=completed.attributeAssignments||[];
      if(!completed.attributesPrepared&&insertionSettings.attreq===1&&block.attributes) {
        const data=await client.read(`attribute-definitions?revision=${client.state.revision}&block=${encodeURIComponent(block.id)}`);
        current();if(!data)return;
        const definitions=insertionDefinitions(data);
        if(completed.forceCommandAttributes||insertionSettings.attdia===0) {
          const prompts=new InsertAttributeSession(completed,definitions,key);
          if(!prompts.complete){setSession(prompts);ensureCommandLine();$('command-input').focus({preventScroll:true});return;}
          attributes=prompts.attributeAssignments;
        } else {
          attributes=await insertionAttributeDialog({definitions,name:block.name,dialog,current});
          if(attributes===null)return;
        }
      }
      current();await edit('insert',{insert:{command,attributes}},{expectedKey:key});
    } else {
      const authored=authoringRequest(command,client.data);
      await edit(authored?.path||'command',authored?.body||{command},{expectedKey:key});
    }
    log(`${completed.name} completed — revision ${client.state.revision}.`,'success');placed=true;
  } finally {
    completed.processing=false;
    if(session===completed)setSession(placed&&completed.insertOptions?.repeat?createInsertSession(completed.insertOptions,currentLayer):null);
  }
}
async function submit(text) {
  const value=text.trim(); $('suggestions').hidden=true;
  if(value) { lastInput.push(value); if(lastInput.length>100) lastInput.shift(); historyIndex=lastInput.length; log(`› ${value}`,'input'); }
  if(session) {
    const target=session;
    const overrides={END:1,ENDPOINT:1,MID:2,MIDPOINT:2,CEN:4,CENTER:4,QUA:8,INT:16,PER:32,NEA:64,INS:128,TAN:256,NONE:0};
    if(!session.selecting && ['point','number'].includes(session.step.kind) && Object.hasOwn(overrides,value.toUpperCase())) {
      viewport.snapOverride=overrides[value.toUpperCase()];viewport.snapGeneration++;viewport.snapAbort?.abort();viewport.snapHit=null;
      log(`${value.toUpperCase()} — applies to the next picked point.`);return;
    }
    if(session.selecting) {
      if(session.spec.crossingSelection&&['C','CROSSING'].includes(value.toUpperCase())){session.resetWindow();updatePrompt();viewport.invalidate();return;}
      if(value)throw new Error('Select objects in the viewport, then press Enter.');
      session.acceptSelection(client.data.selectedEntities.length);
    } else await takeInput(session.attributesPrepared?text:value);
    if(session!==target)return;
    updatePrompt(); viewport.invalidate(); if(target.complete) await complete(); return;
  }
  if(!value) return start(lastCommand);
  const match=value.match(/^(\S+)(?:\s+([\s\S]*))?$/), name=match[1].toUpperCase(), rest=match[2]||'';
  if(name==='ATTREQ'||name==='ATTDIA') {
    const key=name.toLowerCase(),input=rest.trim()||await ask(name,name==='ATTREQ'?'Prompt for insertion attribute values (0 or 1)':'Attribute dialog (1) or command prompts (0)',String(insertionSettings[key]));
    if(input===null)return;if(!/^[01]$/.test(input))throw new Error(`${name} accepts 0 or 1.`);
    insertionSettings[key]=Number(input);persist();log(`${name} = ${input}`);return;
  }
  const aliases={CUI:'ribbon-customize',LAYISO:'layer-isolate',LAYUNISO:'layer-unisolate',WORKSPACE:'ui-workspaces',WSSAVE:'ui-workspaces',REFGRAPH:'reference-graph',BATTMAN:'attribute-definitions',ATTDEF:'attribute-create',ATTSYNC:'attribute-sync',EATTEDIT:'attributes',ATTEDIT:'attributes',ATE:'attributes',BEDIT:'block-edit',BE:'block-edit',BSAVE:'block-save',BCLOSE:'block-close',U:'undo',UNDO:'undo',REDO:'redo',QNEW:'new',NEW:'new',OPEN:'open',QSAVE:'save',SAVE:'save-as',SAVEAS:'save-as',PROPERTIES:'properties',PR:'properties',LA:'layers',LAYER:'layers',LAYERSTATE:'layer-states',LAS:'layer-states',LIST:'explorer',REGEN:'regen',RE:'regen',PAN:'pan',P:'pan',OPTIONS:'settings',OSNAP:'settings',HELP:'help',SNAPSHOT:'snapshot',GRAPH:'graph-analysis',TOPOLOGY:'graph-analysis'};
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
      const target=session,token=isText?tokens.slice(i).join(' '):tokens[i]; await takeInput(token); if(session!==target)return;if(target.complete) await complete(); if(isText) break;
    }
    updatePrompt(); viewport.invalidate();
  }
}
async function select(mode,first=point(),second=point(),extend=false,remove=false) {
  editable();const target=session;
  if(target?.spec.crossingSelection&&target.selecting&&!remove){
    if(mode==='crossing'||mode==='window')return stretchWindow(first,second);
    throw new Error('STRETCH uses one crossing window. Shift removes objects; C replaces the window.');
  }
  const picked=target?.selecting&&target.spec.pickSelection&&mode==='pick'&&!remove;
  await edit('select',{mode,first,second,tolerance:8/viewport.camera.scale,extend:picked?false:extend,remove});
  if(picked&&session===target&&client.data.selectedEntities.length===1)target.acceptPickedSelection(1,first);
  updatePrompt();viewport.invalidate();
}
async function selectCandidate(candidate,first,extend,remove,expectedKey) {
  editable();const target=session;
  if(target?.spec.crossingSelection&&target.selecting&&!remove)
    throw new Error('STRETCH uses one crossing window. Shift removes objects; C replaces the window.');
  const picked=target?.selecting&&target.spec.pickSelection&&!remove;
  await edit('select',{mode:'entity',entity:candidate.entity,first,tolerance:8/viewport.camera.scale,extend:picked?false:extend,remove},{expectedKey});
  if(picked&&session===target&&client.data.selectedEntities.length===1)target.acceptPickedSelection(1,first);
  updatePrompt();viewport.invalidate();
}
function updateTools() {
  document.querySelectorAll('[data-command]').forEach(b=>{b.classList.toggle('active',b.dataset.command===session?.name);b.disabled=!!client.busy||snapshot!==null||!client.state;});
  for(const [name,enabled] of [['undo',client.state?.undoCount],['redo',client.state?.redoCount]]) document.querySelectorAll(`[data-action="${name}"]`).forEach(b=>b.disabled=!enabled||!!client.busy||snapshot!==null);
  commandAccess?.refresh();
  refreshCustomRibbonCommands(document,{catalog:commandCatalog,state:commandState});
}
async function setCurrentLayer(name) { const layer=client.data?.layers.find(l=>l.name===name);if(!layer)throw new Error('Choose an existing layer.');await edit('resource',{resource:{kind:'layer',action:'current',id:layer.id}}); }
function ribbonEntries(){return ribbonCustomization.tabs(describeRibbonEntries(client.data));}
function rawRibbonGroups(name){return name.startsWith('user:')?[]:name.startsWith('module:')?moduleUi.ribbon(name.slice(7)):ribbon(name,client.data,currentLayer,value=>run(()=>setCurrentLayer(value)));}
function customizedRibbonGroups(name){return renderCustomizedRibbonGroups(ribbonCustomization,name,rawRibbonGroups(name),
  {catalog:commandCatalog,state:commandState,execute:entry=>{ribbonLayout.hide();return executeCatalogCommand(entry);},report});}
function drawRibbonTabs(){
  const entries=ribbonEntries();ribbonContext=reconcileRibbonContext(tab,ribbonContext,entries);tab=ribbonContext.tab;
  $('ribbon-tabs').replaceChildren(...entries.map(({key,name,disabled,title,contextual})=>button(name,()=>selectRibbon(key),{id:`ribbon-tab-${key.replaceAll(' ','-')}`,role:'tab','data-tab':key,'data-contextual':contextual||false,disabled:!!disabled,title:title||name,'aria-controls':'ribbon','aria-selected':key===tab,tabindex:key===tab?0:-1})));
}
function drawRibbon() { ribbonLayout.set(customizedRibbonGroups(tab),{tab,entries:ribbonCustomization.tabs(describeRibbonEntries(client.data),{includeHidden:true}),resolveGroups:customizedRibbonGroups}); $('ribbon').setAttribute('aria-label',`${ribbonEntries().find(e=>e.key===tab)?.name||tab} tools`); $('ribbon').setAttribute('aria-labelledby',`ribbon-tab-${tab.replaceAll(' ','-')}`); updateTools(); }
function selectRibbon(name, focus=false) {
  if(!ribbonEntries().some(entry=>entry.key===name&&!entry.disabled))return;
  tab=name; drawRibbon();
  $('ribbon-tabs').querySelectorAll('[role=tab]').forEach(button=>{const active=button.dataset.tab===name;button.setAttribute('aria-selected',String(active));button.tabIndex=active?0:-1;if(active){button.scrollIntoView({block:'nearest',inline:'nearest'});if(focus)button.focus({preventScroll:true});}});
  ribbonLayout.reveal();
}
function applyChrome() {
  ribbonLayout.setMode(chrome.ribbonMode);
  $('studio').classList.toggle('command-line-hidden',!chrome.commandLineVisible);
  $('studio').classList.toggle('command-history-hidden',!chrome.commandHistoryVisible);
  $('ribbon-toggle').title=`Ribbon: ${ribbonModeLabels[chrome.ribbonMode]}. Click to cycle display; double-click a tab to toggle tabs only.`;
  $('ribbon-toggle').textContent=chrome.ribbonMode==='tabs'?'⌄':'⌃';
  $('command-line-toggle').setAttribute('aria-pressed',String(chrome.commandLineVisible));
  $('command-history-toggle').setAttribute('aria-expanded',String(chrome.commandHistoryVisible));
  $('command-history-toggle').textContent=chrome.commandHistoryVisible?'⌄':'⌃';
  ribbonLayout.close();ribbonLayout.schedule();
}
function ensureCommandLine() { if(!chrome.commandLineVisible){chrome.commandLineVisible=true;applyChrome();persist();} }

function showPalette(name) { docking.show(name); }
function inputRow(label,input) { return el('label',{class:'form-row'},[el('span',{text:label}),input]); }
async function dialog(title,content,submitLabel='OK') {
  return await showDialog(()=>{
    $('dialog-title').textContent=title; $('dialog-body').replaceChildren(...content); $('dialog-submit').textContent=submitLabel;
  })==='ok';
}
async function ask(title,label,initial='',type='text') {
  const input=el('input',{type,value:initial,required:true,'aria-label':label});
  return await dialog(title,[inputRow(label,input)])?input.value:null;
}
function button(text,callback,attrs={}) { return el('button',{type:'button',text,...attrs,onclick:()=>run(callback)}); }
function row(label,child) { return el('div',{class:'property-row'},[el('span',{text:label,title:label}),child]); }
function read(text) { return el('span',{class:'read-only',text:String(text??'')}); }
function toolbar(...children) { return el('div',{class:'toolbar'},children); }
function renderPalette() { docking.refresh(); }
function renderPaletteContent(palette,content) {
  const data=client.data; content.replaceChildren(); if(!data) return;
  const state=data.state;
  if(palette==='Modules'){moduleUi.render(content);return;}
  if(palette==='Health'){analysisPalettes.Health.health(content);return;}
  if(palette==='Edit previews'){analysisPalettes['Edit previews'].previews(content);return;}
  if(palette==='Workflows'){analysisPalettes.Workflows.workflows(content);return;}
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
    content.append(toolbar(button('Manager…',()=>resourceUi.layers()),button('＋ New',()=>action('layer-new')),button('Assign current',()=>action('assign-layer'))));
    for(const layer of data.layers) {
      const card=el('section',{class:`layer-card${currentLayer===layer.name?' current':''}`});
      const mutate=changes=>edit('layer',{layer:{id:layer.id,...changes}});
      const color=el('input',{type:'color',value:layer.color,'aria-label':`${layer.name} color`,onchange:e=>run(()=>mutate({color:e.target.value}))});
      const name=el('input',{type:'text',value:layer.name,'aria-label':`${layer.name} layer name`});
      name.addEventListener('change',()=>run(()=>mutate({name:name.value})));
      card.append(el('div',{class:'layer-header'},[color,name,button('✓',()=>setCurrentLayer(layer.name),{title:'Set current layer'}),button('…',()=>resourceUi.layers(layer.id.value),{title:'All layer properties'})]));
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
      el('button',{class:'block-card',onclick:()=>run(()=>blockUi.insert(block.id))},[icon('INSERT'),el('b',{text:block.name}),el('small',{text:`${block.entities} definition entities`})]),
      toolbar(button('Edit',()=>blockUi.begin(block.id),{'aria-label':`Edit block ${block.name}`}),button('Rename',()=>blockUi.rename(block.id),{'aria-label':`Rename block ${block.name}`}))]));
    if(!data.blocks.length) content.append(el('p',{text:'No named block definitions in this drawing. Open a DXF containing blocks, or create a sample drawing.'}));
  } else if(palette==='Explorer') {
    const filter=el('input',{type:'search',placeholder:'Filter type, layer or handle','aria-label':'Filter drawing objects'}), list=el('div',{class:'object-list'});
    const update=()=>{const q=filter.value.toLowerCase(), items=data.items.filter(i=>`${i.id} ${i.type} ${i.layer}`.toLowerCase().includes(q));list.replaceChildren(...items.slice(0,500).map(item=>el('button',{class:data.selectedEntities.includes(item.id)?'selected':'',title:`${item.type} on ${item.layer}`,onclick:e=>run(()=>edit('select',{mode:'entity',entity:item.id,extend:e.ctrlKey||e.metaKey,remove:e.shiftKey}))},[el('span',{text:item.type}),el('small',{text:item.id})])));if(items.length>500)list.append(el('p',{text:`Showing 500 of ${items.length} matches. Refine the filter.`}));};
    content.append(toolbar(button('Select all',()=>select('all')),button('Clear',()=>select('clear'))),filter,el('p',{class:'hint',text:`${data.itemCount} visible source objects; explorer enumerates up to 5,000. Selection in the drawing remains geometric.`}),list);filter.addEventListener('input',update);update();
  } else if(palette==='Graph') {
    renderGraph(content);
  } else if(palette==='References') {
    renderReferences(content);
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
  const polar=el('input',{type:'text',value:viewport.settings.polarAngle,'aria-label':'Polar angle increment'});
  const aperture=el('input',{type:'text',value:viewport.settings.polarTolerance,'aria-label':'Polar acquisition angle'});
  const content=[inputRow('Grid spacing (drawing units)',grid),inputRow('Polar angle increment (degrees)',polar),inputRow('Polar acquisition angle (degrees)',aperture),el('h3',{text:'Running object snaps'})];
  for(const [bit,name] of [[1,'Endpoint'],[2,'Midpoint'],[4,'Center'],[8,'Quadrant'],[16,'Intersection'],[32,'Perpendicular'],[64,'Nearest'],[128,'Insertion'],[256,'Tangent']]) {
    const input=el('input',{type:'checkbox',checked:!!(viewport.settings.snapModes&bit),'aria-label':name});flags.push([bit,input]);content.push(inputRow(name,input));
  }
  content.push(el('p',{class:'hint',text:'Ortho locks X or Y; hold Shift in the viewport to temporarily reverse Ortho. Polar acquires nearby angle increments. F11 tracks horizontal/vertical alignment from up to four recent object snaps. END/MID/CEN/INT/PER/NEA/INS/TAN/NONE override the next picked point. Explicit typed coordinates take precedence.'}));
  if(await dialog('Drafting settings',content)) {const n=number(grid.value);if(n<1e-6||n>1e9)throw new Error('Grid spacing must be between 0.000001 and 1,000,000,000.');const angle=number(polar.value),tolerance=number(aperture.value);if(angle<=0||angle>90||tolerance<0||tolerance>15)throw new Error('Polar increment must be >0 and ≤90 degrees; acquisition angle 0–15 degrees.');viewport.settings.gridStep=n;viewport.settings.polarAngle=angle;viewport.settings.polarTolerance=tolerance;viewport.settings.snapModes=flags.reduce((sum,[bit,input])=>sum+(input.checked?bit:0),0);persist();viewport.invalidate();}
}
async function action(name) {
  if(name.startsWith('graph-query:')){showPalette('Graph');renderGraph.setOperation(name.slice(12));return;}
  if(name.startsWith('reference-query:')){showPalette('References');renderReferences.setOperation(name.slice(16));return;}
  if(name==='modules'||name.startsWith('module-open:')||name.startsWith('module-command:'))return moduleUi.action(name);
  if(isAuthoringAction(name)){cancel(false);return authoringDialog(name,{client,editable,edit,dialog,showPalette,currentLayer});}
  if(['properties','layers','blocks','explorer','workspaces','diagnostics'].includes(name))return showPalette(name[0].toUpperCase()+name.slice(1));
  switch(name) {
    case 'agents':agentWorkspace?.show();return;
    case 'ui-workspaces':return uiWorkspaces();
    case 'reference-graph':showPalette('References');return;
    case 'health':showPalette('Health');return;
    case 'edit-previews':showPalette('Edit previews');return;
    case 'workflows':showPalette('Workflows');return;
    case 'block-insert':editable();return blockUi.insert();
    case 'block-library-import':editable();return blockLibraryUi.importLibrary();
    case 'block-library-export':editable();return blockLibraryUi.exportLibrary();
    case 'block-edit':editable();return blockUi.begin();
    case 'block-save':editable();return blockUi.finish(true);
    case 'block-close':editable();return blockUi.finish(false);
    case 'new':return newDocument(); case 'demo':return newDocument(true); case 'open':$('open-file').click();return;case 'save':return save();case 'save-as':editable();return saveDrawingAs({client,dialog,inputRow,download,documentName:documents.find(d=>d.id===client.id)?.name||'Drawing',refreshDocuments:documentTabs,log,editable});case 'font':$('font-file').click();return;
    case 'undo':case 'redo':cancel(false);return edit(`history/${name}`);
    case 'fit':viewport.fit();return;case 'regen':await client.refresh();viewport.requestFrame(true);return;
    case 'zoom-in':viewport.camera.scale=Math.min(1e8,viewport.camera.scale*1.5);viewport.changed();return;
    case 'zoom-out':viewport.camera.scale=Math.max(1e-8,viewport.camera.scale/1.5);viewport.changed();return;
    case 'pan':{const next=!viewport.panMode;cancel(false);viewport.panMode=next;log(next?'Pan: drag in the drawing; Escape to exit.':'Pan mode ended.');return;}
    case 'palette':docking.toggle();return;
    case 'palette-picker':return workspaceMenu(document.querySelector('.document-bar [data-action="palette-picker"]'),paletteNames.map(id=>[id,()=>showPalette(id)]));
    case 'dock':docking.dockSide();return;
    case 'palette-lock':docking.setLocked(!docking.snapshot().locked);return;
    case 'palette-reset':docking.reset();return;
    case 'command-search':commandAccess.search();return;
    case 'quick-access-customize':commandAccess.customize();return;
    case 'ribbon-customize':return ribbonEditor.customize();
    case 'clean':ribbonLayout.hide();$('studio').classList.toggle('clean');return;
    case 'ribbon-minimize':chrome.ribbonMode=chrome.ribbonMode==='tabs'?'full':'tabs';applyChrome();persist();return;
    case 'ribbon-cycle':chrome.ribbonMode=ribbonModes[(ribbonModes.indexOf(chrome.ribbonMode)+1)%ribbonModes.length];applyChrome();persist();return;
    case 'ribbon-display':return workspaceMenu($('ribbon-display'),ribbonModes.map(mode=>[ribbonModeLabels[mode],()=>{chrome.ribbonMode=mode;applyChrome();persist();},mode===chrome.ribbonMode]));
    case 'command-line':chrome.commandLineVisible=!chrome.commandLineVisible;applyChrome();persist();if(chrome.commandLineVisible)$('command-input').focus({preventScroll:true});else $('viewport').focus({preventScroll:true});return;
    case 'command-history':chrome.commandHistoryVisible=!chrome.commandLineVisible||!chrome.commandHistoryVisible;chrome.commandLineVisible=true;applyChrome();persist();return;
    case 'settings':return draftingSettings();
    case 'selection-cycling':toggle('selectionCycling');return;
    case 'layer-new':return resourceUi.layers(null,true);
    case 'attributes':return attributeTools({client,edit,dialog,editable})();
    case 'attribute-definitions':return definitionTools({client,edit,dialog,editable}).manage();
    case 'attribute-create':return definitionTools({client,edit,dialog,editable,beginPlacement:value=>{setSession(value);ensureCommandLine();$('command-input').focus({preventScroll:true});}}).create();
    case 'attribute-sync':return synchronizeAttributes({client,edit,dialog,editable,showPalette});
    case 'layer-states':return layerStateTools({client,edit,dialog,editable,download:(name,text)=>download(new Blob([text],{type:'text/plain'}),name)})();
    case 'layer-manager':return resourceUi.layers();
    case 'layer-filters':return layerManager.open();
    case 'layer-isolate':return layerManager.isolateSelection();
    case 'layer-unisolate':return layerManager.restore();
    case 'linetypes':return resourceUi.linetypes();
    case 'text-styles':return resourceUi.textStyles();
    case 'assign-layer':{const layer=client.data.layers.find(l=>l.name===currentLayer);if(!layer)throw new Error('Choose an existing layer.');await edit('assign-layer',{layer:layer.id.value});return;}
    case 'snapshot':await edit('retain');showPalette('Workspaces');return;
    case 'snapshots':showPalette('Workspaces');return;
    case 'fork':{const name=await ask('Fork immutable workspace','Branch name','Design alternative');if(name)await edit('fork',{name});showPalette('Workspaces');return;}
    case 'annotation':{const s=client.state,mode=el('select',{'aria-label':'Annotation mode'},['Drawing','Disabled','Enabled'].map(v=>el('option',{value:v,text:v})));mode.value=s.annotationMode;
      const paper=el('input',{value:s.annotationPaperUnits,'aria-label':'Paper units'}),drawing=el('input',{value:s.annotationDrawingUnits,'aria-label':'Drawing units'});
      if(await dialog('Model annotation context',[inputRow('Mode',mode),inputRow('Paper units',paper),inputRow('Drawing units',drawing),el('p',{class:'hint',text:'Presentation-only context; the source drawing header is unchanged.'})]))await edit('annotation-view',{mode:mode.value,paperUnits:number(paper.value),drawingUnits:number(drawing.value)});return;}
    case 'linetype':{const selection=el('select',{},[el('option',{value:'Periodic',text:'Periodic pattern'}),el('option',{value:'A',text:'Fit endpoint dashes'})]);selection.value=client.state.linetypeAlignment;if(await dialog('Linetype pattern alignment',[inputRow('Alignment',selection)]))await edit('linetype-view',{alignment:selection.value});return;}
    case 'graph-analysis':showPalette('Graph');return;
    case 'graph-load':showPalette('Graph');return renderGraph.loadTopology();
    case 'graph-fit':viewport.fitGraph();return;
    case 'graph-clear':renderGraph.clearOverlay();return;
    case 'graph':if(viewport.graph)renderGraph.clearOverlay();else{showPalette('Graph');await renderGraph.loadTopology();}return;
    case 'export-png':return exportImage('skia');case 'export-svg':return exportImage('svg');
    case 'legacy':window.open('diagnostics.html','_blank','noopener');return;
    case 'reset-ui':viewport.cancelSelectionCycle();for(const [key,value] of Object.entries({grid:true,snap:false,ortho:false,polar:false,osnap:true,dynamic:true,tracking:false,selectionCycling:false,polarAngle:15,polarTolerance:5,gridStep:10,snapModes:23}))viewport.settings[key]=value;$('studio').className='';docking.reset();chrome=chromePreferences();applyChrome();document.documentElement.style.removeProperty('--command-height');persist();modes();viewport.changed();return;
    case 'help':await dialog('Shadow CAD command reference',[el('p',{class:'hint',text:'Native netDxf editing on immutable shadow transactions. Enter ends point chains; C closes; U removes the last draft point; Escape cancels. Coordinates: X,Y; @dx,dy; @distance<angle. A bare distance follows the cursor. All edits are undoable.'}),el('table',{class:'help-table'},Object.entries(commands).map(([name,s])=>el('tr',{},[el('td',{text:`${name} (${s.aliases.join(', ')})`}),el('td',{text:s.label})]))),el('p',{class:'hint',text:'STRETCH: draw one XY crossing window (drag or two corners), Enter, then base and second point or Displacement. Shift removes objects; C replaces the crossing window. BREAK: pick one object as the first break point, use F to replace it, or @ for a gap-free split. BREAKATPOINT asks for one interior point after selecting the object. Supported curves are listed in the drafting guide; unsupported geometry is refused atomically. TRIM/EXTEND: select target LINE first, then a LINE/CIRCLE boundary, Enter, then pick an endpoint. FILLET/CHAMFER: two intersecting XY lines. EXPLODE: 2D polylines and ordinary unattributed INSERTs, one level at a time. Array inserts, attached data, disabled exploding, and unrepresentable affine geometry are refused. SPLINE: cubic control points. These commands preserve native source geometry and refuse unsupported combinations. BLOCK creates shared definitions; BLOCKREDEFINE replaces one from selected sources; BLOCKRENAME retains references. HATCH/HATCHEDIT accept built-in name, scale, angle and island style. HATCHPOLY makes a solid polygon. Hatch settings import bounded custom PAT files. DWG import uses signature detection; SAVEAS offers host-declared DWG profiles with strict conversion checks. 3D solids, general curve trimming, AutoLISP and CTB/STB plotting are not implemented.'}),el('p',{class:'hint',text:'Ctrl+N/O/S: new/open/save · Ctrl+Z/Y: undo/redo · Ctrl+1: palettes · Ctrl+0: clean screen · Ctrl+9: command line · F2: history · F3: object snap · F7: grid · F8: ortho · F9: grid snap · F10: polar · F11: tracking · F12: dynamic input. CYCLE toggles overlap selection; Shift+Space in the viewport opens its chooser temporarily. Middle drag pans; wheel zooms about the pointer; right click opens command actions. Two-finger touch pans/zooms.'})],'Close');return;
    default:throw new Error(`Unknown workspace action: ${name}`);
  }
}
function modes() {
  $('drafting-modes').replaceChildren(...[['snap','SNAP','F9'],['grid','GRID','F7'],['ortho','ORTHO','F8'],['polar','POLAR','F10'],['osnap','OSNAP','F3'],['tracking','OTRACK','F11'],['dynamic','DYN','F12'],['selectionCycling','CYCLE','Shift+Space']].map(([key,label,shortcut])=>button(label,()=>toggle(key),{'aria-pressed':viewport.settings[key],title:`${label} (${shortcut})`})));
}
function toggle(key) {viewport.settings[key]=!viewport.settings[key];if(key==='selectionCycling')viewport.cancelSelectionCycle();if(key==='ortho'&&viewport.settings.ortho)viewport.settings.polar=false;if(key==='polar'&&viewport.settings.polar)viewport.settings.ortho=false;persist();modes();viewport.invalidate();}
function cursorChanged(p,pixel) {
  $('coordinates').textContent=`${format(p.x)}, ${format(p.y)}, ${format(p.z)}`;
  const node=$('dynamic-input');node.hidden=!viewport.settings.dynamic||!session||session.selecting||!pixel;
  if(!node.hidden){node.textContent=`${session.step.prompt}: ${format(p.x)}, ${format(p.y)}`;node.style.left=`${Math.max(4,Math.min(viewport.size.w-280,pixel.x+20))}px`;node.style.top=`${Math.min(viewport.size.h-28,pixel.y+25)}px`;}
}
function contextMenu(x,y) {
  hideContextMenu();
  const execute=callback=>()=>{hideContextMenu(true);return callback();};
  const menu=$('context-menu');menu.replaceChildren(button(session?'Enter / finish':`Repeat ${lastCommand}`,execute(()=>submit(''))),button('Cancel',execute(()=>cancel())),el('hr'),button('Undo',execute(()=>action('undo'))),button('Zoom extents',execute(()=>action('fit'))),button('Properties',execute(()=>action('properties'))),button('Select all',execute(()=>select('all'))));
  menu.style.left=`${Math.min(x,innerWidth-205)}px`;menu.style.top=`${Math.min(y,innerHeight-220)}px`;menu.hidden=false;
  menu.setAttribute('role','menu');
  for(const item of menu.querySelectorAll('button'))item.setAttribute('role','menuitem');
  menuKeyboard=bindRovingToolbar(menu,{orientation:'vertical',onEscape:()=>hideContextMenu(true),onTab:()=>hideContextMenu(true)});
  menuKeyboard.focus();
}
let menuKeyboard=null,menuAnchor=null;
function hideContextMenu(restoreFocus=false) {
  const menu=$('context-menu'),anchor=menuAnchor;
  menuKeyboard?.dispose();menuKeyboard=null;menuAnchor=null;menu.hidden=true;
  anchor?.setAttribute('aria-expanded','false');
  if(restoreFocus)(anchor?.isConnected?anchor:$('viewport')).focus({preventScroll:true});
}
function workspaceMenu(anchor, entries) {
  hideContextMenu();
  const menu=$('context-menu'), bounds=anchor.getBoundingClientRect();
  menuAnchor=anchor;
  anchor.setAttribute('aria-haspopup','menu');anchor.setAttribute('aria-controls',menu.id);anchor.setAttribute('aria-expanded','true');
  menu.setAttribute('role','menu');
  menu.replaceChildren(...entries.map(([label,callback,checked])=>button(label,()=>{hideContextMenu(true);return callback();},checked===undefined?{role:'menuitem','aria-label':label}:{role:'menuitemradio','aria-label':label,'aria-checked':checked})));
  menu.hidden=false;menu.style.left=`${Math.max(4,Math.min(bounds.left,innerWidth-menu.offsetWidth-4))}px`;
  menu.style.top=`${Math.max(4,Math.min(bounds.bottom+2,innerHeight-menu.offsetHeight-4))}px`;
  menuKeyboard=bindRovingToolbar(menu,{orientation:'vertical',onEscape:()=>hideContextMenu(true),onTab:()=>hideContextMenu(true)});
  menuKeyboard.focus();
}
function commandCatalog(){return buildCommandCatalog({modules:moduleUi.catalog()});}
function commandState(){return {hasDocument:!!client.state,busy:!!client.busy,readOnly:snapshot!==null,commandActive:!!session,
    blockEditing:!!client.data?.blockEditor,selectionCount:client.data?.selectedEntities?.length||0,
    blockCount:client.data?.blocks?.length||0,undoCount:client.state?.undoCount||0,redoCount:client.state?.redoCount||0,
    modules:client.data?.modules||[]};}
function executeCatalogCommand(entry){return entry.kind==='command'?start(entry.target):entry.kind==='input'?submit(entry.target):action(entry.target);}
commandAccess=createCommandAccess({toolbar:$('quick-access'),catalog:commandCatalog,state:commandState,execute:executeCatalogCommand,report});
drawRibbonTabs();
$('ribbon-tabs').setAttribute('aria-orientation','horizontal');
$('ribbon-tabs').addEventListener('keydown',event=>{
  if(event.defaultPrevented||event.isComposing||event.ctrlKey||event.metaKey||event.altKey||event.shiftKey||!event.target.closest('[role=tab]'))return;
  const entries=ribbonEntries().filter(entry=>!entry.disabled),key=event.key,index=entries.findIndex(e=>e.key===tab);let next;
  if(!entries.length)return;
  if(key==='ArrowDown'){event.preventDefault();event.stopPropagation();ribbonLayout.reveal(true);return;}
  if(key==='ArrowRight')next=(index+1)%entries.length;else if(key==='ArrowLeft')next=(index+entries.length-1)%entries.length;
  else if(key==='Home')next=0;else if(key==='End')next=entries.length-1;else return;
  event.preventDefault();event.stopPropagation();selectRibbon(entries[next].key,true);
});
$('ribbon-tabs').addEventListener('dblclick',event=>{if(event.target.closest('[role=tab]'))run(()=>action('ribbon-minimize'));});
$('file-menu').addEventListener('click',()=>workspaceMenu($('file-menu'),[['new','New drawing'],['demo','New sample drawing'],['open','Open DXF / DWG…'],['save','Save DXF'],['save-as','Save drawing as…'],['export-png','Export PNG'],['export-svg','Export SVG'],['legacy','Advanced diagnostics']].map(([name,label])=>[label,()=>action(name)])));
document.addEventListener('click',e=>{const command=e.target.closest('[data-command]'),target=e.target.closest('[data-action]');if(command)run(()=>start(command.dataset.command));else if(target)run(()=>action(target.dataset.action));if(!menuAnchor?.contains(e.target))hideContextMenu();});
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
  if(e.defaultPrevented)return;
  if(e.target.closest('.agent-workspace'))return;
  if(e.target.closest('dialog[open]')&&!e.target.closest('.command-access-dialog'))return;
  if(commandAccess.handleShortcut(e))return;
  if(e.target.closest('.command-access-dialog'))return;
  if((e.ctrlKey||e.metaKey)&&e.shiftKey&&e.key.toLowerCase()==='a')return;
  if($('dialog').open)return;
  const input=e.target.closest('input,select,textarea'),control=e.ctrlKey||e.metaKey;
  if(e.key==='Escape'){e.preventDefault();if(!$('context-menu').hidden){hideContextMenu(true);return;}if(viewport.cancelSelectionCycle()||viewport.cancelGraphPick())return;cancel();viewport.panMode=false;return;}
  if(control&&['s','o','n','z','y','0','1','9'].includes(e.key.toLowerCase())){e.preventDefault();run(()=>action({s:'save',o:'open',n:'new',z:e.shiftKey?'redo':'undo',y:'redo','0':'clean','1':'palette','9':'command-line'}[e.key.toLowerCase()]));return;}
  const toggles={F3:'osnap',F7:'grid',F8:'ortho',F9:'snap',F10:'polar',F11:'tracking',F12:'dynamic'};if(toggles[e.key]){e.preventDefault();toggle(toggles[e.key]);return;}
  if(e.key==='F1'){e.preventDefault();run(()=>action('help'));return;}if(e.key==='F2'){e.preventDefault();run(()=>action('command-history'));return;}
  if(input){
    if(e.target===$('command-input')){
      if(e.key==='ArrowUp'||e.key==='ArrowDown'){e.preventDefault();historyIndex=Math.max(0,Math.min(lastInput.length,historyIndex+(e.key==='ArrowUp'?-1:1)));e.target.value=lastInput[historyIndex]||'';}
      if(e.key==='Tab'&&!$('suggestions').hidden){e.preventDefault();e.target.value=$('suggestions').querySelector('b')?.textContent||e.target.value;}
    }return;
  }
  if(e.target.closest('button,a,[role=separator],[data-palette]'))return;
  if(e.key===' '){e.preventDefault();viewport.space=true;return;}
  if(control&&e.key.toLowerCase()==='a'){e.preventDefault();run(()=>select('all'));return;}
  if(e.key==='Delete'){e.preventDefault();run(()=>start('ERASE'));return;}
  if(e.key==='Enter'){e.preventDefault();run(()=>submit(''));return;}
  if(e.key.length===1&&!control&&!e.altKey){e.preventDefault();ensureCommandLine();$('command-input').focus();$('command-input').value+=e.key;$('command-input').dispatchEvent(new Event('input'));}
});
document.addEventListener('keyup',e=>{if(e.key===' ')viewport.space=false;});window.addEventListener('blur',()=>{viewport.space=false;});
function commandSplitter(){
  const node=$('command-splitter');
  const resize=value=>document.documentElement.style.setProperty('--command-height',`${Math.max(32,Math.min(innerHeight*.55,value))}px`);
  node.addEventListener('pointerdown',event=>{
    event.preventDefault();node.setPointerCapture(event.pointerId);
    const y=event.clientY,height=$('command-line').getBoundingClientRect().height;
    const move=current=>resize(height+y-current.clientY);
    const end=()=>{node.removeEventListener('pointermove',move);node.removeEventListener('pointerup',end);node.removeEventListener('pointercancel',end);persist();};
    node.addEventListener('pointermove',move);node.addEventListener('pointerup',end);node.addEventListener('pointercancel',end);
  });
  node.addEventListener('keydown',event=>{const step=event.key==='ArrowDown'?-20:event.key==='ArrowUp'?20:0;if(!step)return;event.preventDefault();resize($('command-line').getBoundingClientRect().height+step);persist();});
}
commandSplitter();
client.addEventListener('reset',()=>{viewport.clearFrame();viewport.graph=null;$('loading').hidden=false;viewport.invalidate();});
client.addEventListener('state',()=>{
  const s=client.state; $('studio').dataset.document=client.id;
  drawRibbonTabs();void moduleUi.sync().catch(report);
  agentWorkspace?.documentChanged();
  const identity=JSON.stringify([client.id,s.serverId,s.documentId,s.workspaceId,s.viewVersion]);
  if(activeIdentity && identity!==activeIdentity){cancel(false);snapshot=null;viewport.snapshot=null;$('view-notice').hidden=true;}
  activeIdentity=identity;
  const nextView=`${client.id}:${s.documentId}:${s.workspaceId}:${s.layout}`;
  if(nextView!==viewId){const saved=views.get(nextView);viewId=nextView;viewport.camera=saved||fit(s.bounds,viewport.size);}
  currentLayer=client.data.currentLayer||layers.get(client.id)||currentLayer;if(!client.data.layers.some(l=>l.name===currentLayer))currentLayer='0';layers.set(client.id,currentLayer);
  $('layouts').replaceChildren(...s.layouts.map(name=>button(name,async()=>{cancel(false);setSnapshot(null);await edit('layout',{name});},{role:'tab','aria-selected':name===s.layout})));
  $('revision').textContent=`r${s.revision}`;
  if(snapshot===null){$('view-notice').hidden=!client.data.blockEditor;$('view-notice').textContent=client.data.blockEditor?`Block Editor: ${client.data.blockEditor.name} — BSAVE to commit, BCLOSE to discard`:'';}
  if(client.data.blockEditor)$('layouts').querySelectorAll('button').forEach(b=>b.disabled=true);$('selection-count').textContent=`${client.data.selectedEntities.length} selected · ${client.data.itemCount} visible`;
  if(stateKey!==viewKey(s)){stateKey=viewKey(s);drawRibbon();renderPalette();viewport.setData();}
  $('loading').hidden=true;updatePrompt();updateTools();
});
client.addEventListener('busy',()=>{$('connection').textContent=client.busy?'Working…':'Local';$('connection').classList.toggle('busy',!!client.busy);updateTools();if(!client.busy){viewport.requestFrame();void moduleUi.sync().catch(report);}});
window.addEventListener('beforeunload',e=>{if(documents.some(d=>d.dirty)){e.preventDefault();e.returnValue='';}});
bindViewportPageLifetime(viewport);
async function pollDocuments() {
  try {
    if(!document.hidden&&!client.busy&&!session&&!viewport.inputActive&&!document.querySelector('dialog[open]')&&!docking.containsFocus()&&!ribbonLayout.containsFocus())
      await run(async()=>{await client.refresh();await documentTabs();});
  } finally { setTimeout(pollDocuments,2500); }
}
setTimeout(pollDocuments,2500);
log('netDxf Shadow CAD Studio — native DXF · immutable transactions · local workspace');
log('Type HELP for commands. Middle-drag pans; wheel zooms; Enter finishes; Escape cancels.');
drawRibbon();modes();renderPalette();
const studioAgentUi = installStudioControl({
  client, viewport, documents:()=>documents, currentLayer:()=>currentLayer, ribbon:()=>tab, palette:()=>palette,
  retainedSnapshot:()=>snapshot,
  command:()=>session?{name:session.name,prompt:session.prompt,selecting:session.selecting,complete:session.complete}:null,
  showRibbon:name=>selectRibbon(name),ribbonEntries,
  setLayer:name=>setCurrentLayer(name),
  submit, cancel, showPalette, persist, modes, activate, newDocument, edit, setSnapshot, switchWorkspace,
  action, documentTabs, report
});
agentWorkspace = mountAgentWorkspace({
  workspace:$('workspace'),documentBar:document.querySelector('.document-bar'),getDocument:()=>client.id,
  getDocumentName:()=>documents.find(d=>d.id===client.id)?.name,
  drawingReferences:{
    capture:()=>client.read('references/capture',{body:{documentId:client.state.documentId,workspaceId:client.state.workspaceId,viewVersion:client.state.viewVersion,revision:client.state.revision}}),
    validate:(references,refresh=false)=>client.read('references/validate',{body:{references,refresh}})
  },
  drawingPreview:(previewId,after)=>client.read(`edit-preview/render?${new URLSearchParams({previewId,after:String(after),width:640,height:400,backend:'svg'})}`,{blob:true}),
  openDrawingPreview:()=>showPalette('Edit previews'),
  inspectDrawing:()=>studioAgentUi.snapshot(),refreshDrawing:async()=>{await client.refresh();await documentTabs();},report
});
run(async()=>{documents=await client.documents();if(documents.length)await activate(documents[0].id);else await newDocument();});
