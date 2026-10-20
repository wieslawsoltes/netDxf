import { el } from './ui.mjs';
import { lineweights, boundedNumber } from './resource-contracts.mjs';
const row=(label,input)=>el('label',{class:'form-row'},[el('span',{text:label}),input]);
const input=(label,value='',type='text')=>el('input',{'aria-label':label,value,type});
const select=(label,items,value)=>{const node=el('select',{'aria-label':label},items.map(v=>el('option',{value:v,text:v})));node.value=value;return node;};
const flags=[['visible',1,'On',true],['frozen',2,'Frozen',false],['locked',4,'Locked',false],['plot',8,'Plot',false],['frozenInNewViewports',16,'Freeze in new viewports',false]];

export function savedLayerValues(entry) {
  return {...Object.fromEntries(flags.map(([name,bit,,invert])=>[name,!!(entry.flags&bit)!==invert])),
    color:entry.color,colorIndex:String(entry.colorIndex),colorMode:entry.trueColor?'RGB':'Indexed',
    linetype:entry.linetype,lineweight:entry.lineweight,transparency:String(entry.transparency)};
}
/** Only changed fields are submitted, preserving unedited imported flags and packed alpha. */
export function savedLayerPatch(entry,values) {
  const before=savedLayerValues(entry),patch={name:entry.name};
  for(const [name] of flags)if(values[name]!==before[name])patch[name]=!!values[name];
  if(values.colorMode!==before.colorMode||values.colorMode==='RGB'&&values.color.toLowerCase()!==before.color.toLowerCase()||values.colorMode==='Indexed'&&values.colorIndex!==before.colorIndex){
    if(values.colorMode==='RGB'){if(!/^#[0-9a-f]{6}$/i.test(values.color))throw new Error('Saved RGB colors use #RRGGBB.');patch.color=values.color;}
    else {const value=boundedNumber(values.colorIndex,1,255,'Indexed color');if(!Number.isInteger(value))throw new Error('Indexed color uses an integer.');patch.colorIndex=value;}
  }
  if(values.transparency!==before.transparency){const value=boundedNumber(values.transparency,0,90,'Transparency');if(!Number.isInteger(value))throw new Error('Transparency uses whole percentages.');patch.transparency=value;}
  if(values.linetype!==before.linetype)patch.linetype=values.linetype;
  if(values.lineweight!==before.lineweight){if(!lineweights.includes(values.lineweight))throw new Error('Choose a supported saved lineweight.');patch.lineweight=values.lineweight;}
  return Object.keys(patch).length===1?null:patch;
}
export async function editSavedLayers({client,edit,dialog,editable,key,state,resources}) {
  const receipt=await client.read(`layer-state/details?id=${encodeURIComponent(typeof state.id==='string'?state.id:state.id.value)}`);
  if(!receipt||client.key!==key)return;
  const entries=receipt.entries||[],removed=new Set(),added=new Set(),drafts=new Map();
  const list=el('select',{'aria-label':'Saved layer entries',size:12}),search=input('Find saved layers','','search'),form=el('div',{class:'resource-fields'});
  const available=select('Drawing layer to add',(resources.layers||[]).map(l=>l.name),'');
  const pendingAdditions=select('Pending saved additions',[],'');
  const current=select('Saved current layer',(resources.layers||[]).map(l=>l.name),receipt.state.currentLayer);
  const status=el('p',{class:'hint'});let selected=null,readValues=null;
  const keep=()=>{if(selected&&readValues)drafts.set(selected.name,readValues());};
  function choose(entry){
    keep();selected=entry;readValues=null;form.replaceChildren();if(!entry)return;
    list.value=entry.name;const values=drafts.get(entry.name)||savedLayerValues(entry),controls={};
    for(const [name,,label] of flags){controls[name]=el('input',{'aria-label':`Saved ${label}`,type:'checkbox',checked:values[name]});form.append(row(label,controls[name]));}
    controls.colorMode=select('Saved color mode',['Indexed','RGB'],values.colorMode);controls.color=input('Saved RGB color',values.color);
    controls.colorIndex=input('Saved indexed color',values.colorIndex,'number');
    controls.linetype=select('Saved linetype',[...new Set([values.linetype,...(resources.linetypes||[]).map(l=>l.status.name)])],values.linetype);
    controls.lineweight=select('Saved lineweight',[...new Set([values.lineweight,...lineweights])],values.lineweight);
    controls.transparency=input('Saved transparency',values.transparency,'number');controls.transparency.min=0;controls.transparency.max=90;
    const colorRow=row('RGB color',controls.color),indexRow=row('Indexed color',controls.colorIndex);
    const mode=()=>{colorRow.hidden=controls.colorMode.value!=='RGB';indexRow.hidden=controls.colorMode.value!=='Indexed';};
    controls.colorMode.addEventListener('change',mode);mode();
    form.append(row('Color type',controls.colorMode),colorRow,indexRow,row('Linetype',controls.linetype),row('Lineweight',controls.lineweight),row('Transparency',controls.transparency));
    readValues=()=>Object.fromEntries(Object.entries(controls).map(([name,node])=>[name,flags.some(f=>f[0]===name)?node.checked:node.value]));
  }
  function render(){
    const candidates=entries.filter(e=>!removed.has(e.name)&&e.name.toLowerCase().includes(search.value.toLowerCase()));
    list.replaceChildren(...candidates.map(e=>el('option',{value:e.name,text:e.name})));
    const next=candidates.find(e=>e.name===selected?.name)||candidates[0];choose(next||null);
    available.replaceChildren(...(resources.layers||[]).filter(l=>!added.has(l.name)&&(!entries.some(e=>e.name===l.name)||removed.has(l.name))).map(l=>el('option',{value:l.name,text:l.name})));
    available.value=available.children[0]?.value||'';add.disabled=!available.value;remove.disabled=!selected;
    pendingAdditions.replaceChildren(...[...added].map(name=>el('option',{value:name,text:name})));
    pendingAdditions.value=pendingAdditions.children[0]?.value||'';removePending.disabled=!added.size;
    status.textContent=`${entries.length-removed.size} retained · ${added.size} to add · ${removed.size} to remove. ${[...added].join(', ')}`;
  }
  const add=el('button',{type:'button',text:'Add drawing layer',onclick:()=>{const name=available.value;if(!name)return;if(removed.has(name))removed.delete(name);else added.add(name);render();}});
  const remove=el('button',{type:'button',text:'Remove saved layer',onclick:()=>{if(!selected)return;keep();removed.add(selected.name);selected=null;readValues=null;render();}});
  const removePending=el('button',{type:'button',text:'Remove pending addition',onclick:()=>{added.delete(pendingAdditions.value);render();}});
  list.addEventListener('change',()=>choose(entries.find(e=>e.name===list.value)||null));search.addEventListener('input',render);
  render();
  const accepted=await dialog('Edit saved layer properties',[row('Saved current layer',current),el('div',{class:'resource-manager'},[
    el('aside',{},[search,list,remove,available,add,pendingAdditions,removePending,status]),form]),el('p',{class:'hint',text:'These edits change only the saved state. Added entries capture current drawing layer properties; save and reopen to edit those entries. Restore applies the state to the drawing.'})],'Save saved properties');
  if(!accepted)return;editable();if(client.key!==key||client.busy)throw new Error('The drawing changed while editing saved properties. Reopen the manager.');
  keep();const changes=entries.filter(e=>!removed.has(e.name)&&drafts.has(e.name)).map(e=>savedLayerPatch(e,drafts.get(e.name))).filter(Boolean);
  if(!changes.length&&!added.size&&!removed.size&&current.value===receipt.state.currentLayer)return;
  const request={action:'edit',id:state.id,entries:changes,addLayers:[...added],removeLayers:[...removed],
    ...(current.value!==receipt.state.currentLayer?{currentLayer:current.value}:{})};
  await edit('layer-state',{state:request},{expectedKey:key});
}
