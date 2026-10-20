import { el } from './ui.mjs';
import { resourceName } from './resource-contracts.mjs';
import { editSavedLayers } from './layer-state-editor.mjs';
import { importLayerState } from './layer-state-transfer-ui.mjs';

const fields = [[1,'On / Off'],[2,'Frozen / Thawed'],[4,'Locked / Unlocked'],[8,'Plot'],[16,'Freeze in new viewports'],
  [64,'Color'],[128,'Linetype'],[256,'Lineweight'],[512,'Transparency']];
const row=(label,input)=>el('label',{class:'form-row'},[el('span',{text:label}),input]);
const input=(label,value='')=>el('input',{'aria-label':label,value});
const checkbox=(label,checked)=>el('input',{'aria-label':label,type:'checkbox',checked});
const idOf=value=>typeof value==='string'?value:value?.value;

/** Shared native/WASM layer-state manager. Each submitted action is one source transaction. */
export function layerStateTools({client,edit,dialog,editable,download}) {
  return async function open() {
    editable();if(client.busy)throw new Error('Finish the current edit before opening layer states.');
    const key=client.key,data=await client.read('resources');if(!data||key!==client.key)return;
    const states=data.layerStates||[],list=el('select',{'aria-label':'Saved layer states',size:12});
    const search=input('Find layer states'),name=input('Layer state name'),description=input('Layer state description');
    search.type='search';name.maxLength=255;description.maxLength=4096;
    const details=el('p',{class:'hint'}),summary=el('p',{class:'hint'});
    const current=checkbox('Restore current layer',true),off=checkbox('Turn off unlisted layers',false);
    const controls=fields.map(([bit,label])=>({bit,label,element:checkbox(`Restore ${label}`,true)}));
    let selected=null,requested=null;
    const close=action=>{requested=action;document.getElementById('dialog').close('ok');};
    const button=(text,action)=>el('button',{type:'button',text,onclick:action});
    const save=button('Save current layers',()=>close('save')),rename=button('Rename',()=>close('rename')),remove=button('Delete',()=>close('delete'));
    const saved=button('Edit saved properties',()=>close('edit')),options=button('Save restore mask',()=>close('options'));
    const exportLas=button('Export LAS',()=>close('export'));
    function choose(state) {
      selected=state;requested=null;list.value=state?idOf(state.id):'';
      name.value=state?.name||'';description.value=state?.description||'';
      rename.disabled=remove.disabled=!state;
      saved.disabled=options.disabled=exportLas.disabled=!state;
      details.textContent=state?`${state.layers.length} saved layers · current layer ${state.currentLayer} · ${state.paperSpace?'paper space':'model space'}`:'Enter a name to save the current layer settings.';
      summary.textContent=state?state.layers.join(', '):'';
      for(const control of controls){control.element.disabled=!state;control.element.checked=((state?.fields??991)&control.bit)!==0;}
      current.disabled=off.disabled=!state;
    }
    function filter() {
      const term=search.value.toLowerCase();
      const visible=states.filter(s=>`${s.name} ${s.description}`.toLowerCase().includes(term));
      list.replaceChildren(...visible.map(s=>el('option',{value:idOf(s.id),text:s.name})));
      choose(visible.find(s=>idOf(s.id)===idOf(selected?.id))||visible[0]||null);
    }
    search.addEventListener('input',filter);list.addEventListener('change',()=>choose(states.find(s=>idOf(s.id)===list.value)||null));
    const left=el('aside',{},[search,list,el('div',{class:'resource-actions'},[button('New',()=>choose(null)),save,rename,remove,saved,button('Import LAS',()=>close('import')),exportLas])]);
    const right=el('div',{class:'resource-fields'},[row('Name',name),row('Description',description),details,
      el('fieldset',{},[el('legend',{text:'Layer properties to restore'}),...controls.map(c=>row(c.label,c.element)),
        el('div',{class:'resource-actions'},[button('Select all',()=>controls.forEach(c=>c.element.checked=true)),button('Clear all',()=>controls.forEach(c=>c.element.checked=false)),options])]),
      row('Restore current layer',current),row('Turn off layers not in state',off),summary]);
    filter();
    const accepted=await dialog('Layer states manager',[el('div',{class:'resource-manager'},[left,right]),
      el('p',{class:'hint',text:'Restore applies the checked global properties. Save restore mask persists those choices in DXF/LAS; current-layer and unlisted-layer choices apply only to this restore. Edit saved properties changes the state without changing drawing layers. Viewport overrides and plot-style payloads are unsupported.'})],'Restore / Save new');
    if(!accepted)return;
    editable();if(key!==client.key||client.busy)throw new Error('The drawing changed while the layer states manager was open. Open it again.');
    const action=requested||(selected?'restore':'save');
    if(action==='edit')return editSavedLayers({client,edit,dialog,editable,key,state:selected,resources:data});
    if(action==='import')return importLayerState({client,edit,dialog,editable,key,resources:data});
    if(action==='export'){
      const result=await client.read(`layer-state/export?id=${encodeURIComponent(idOf(selected.id))}`);if(!result||client.key!==key)return;
      if(typeof download!=='function')throw new Error('This host does not provide LAS downloads.');
      return download(result.fileName,result.text);
    }
    const state={action,...(selected?{id:{value:idOf(selected.id)}}:{})};
    if(action==='save'||action==='rename')state.name=resourceName(name.value);
    if(action==='save')state.description=description.value;
    if(action==='restore')Object.assign(state,{fields:controls.reduce((mask,c)=>mask|(c.element.checked?c.bit:0),0),restoreCurrentLayer:current.checked,turnOffUnlistedLayers:off.checked});
    if(action==='options')state.fields=controls.reduce((mask,c)=>mask|(c.element.checked?c.bit:0),0);
    await edit('layer-state',{state},{expectedKey:key});
  };
}
