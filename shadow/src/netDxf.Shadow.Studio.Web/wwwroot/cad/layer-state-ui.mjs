import { el } from './ui.mjs';
import { resourceName } from './resource-contracts.mjs';

const fields = [[1,'On / Off'],[2,'Frozen / Thawed'],[4,'Locked / Unlocked'],[8,'Plot'],[16,'Freeze in new viewports'],
  [64,'Color'],[128,'Linetype'],[256,'Lineweight'],[512,'Transparency']];
const row=(label,input)=>el('label',{class:'form-row'},[el('span',{text:label}),input]);
const input=(label,value='')=>el('input',{'aria-label':label,value});
const checkbox=(label,checked)=>el('input',{'aria-label':label,type:'checkbox',checked});
const idOf=value=>typeof value==='string'?value:value?.value;

/** Shared native/WASM layer-state manager. Each submitted action is one source transaction. */
export function layerStateTools({client,edit,dialog,editable}) {
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
    function choose(state) {
      selected=state;requested=null;list.value=state?idOf(state.id):'';
      name.value=state?.name||'';description.value=state?.description||'';
      rename.disabled=remove.disabled=!state;
      details.textContent=state?`${state.layers.length} saved layers · current layer ${state.currentLayer} · ${state.paperSpace?'paper space':'model space'}`:'Enter a name to save the current layer settings.';
      summary.textContent=state?state.layers.join(', '):'';
      for(const control of controls)control.element.disabled=!state;
      current.disabled=off.disabled=!state;
    }
    function filter() {
      const term=search.value.toLowerCase();
      const visible=states.filter(s=>`${s.name} ${s.description}`.toLowerCase().includes(term));
      list.replaceChildren(...visible.map(s=>el('option',{value:idOf(s.id),text:s.name})));
      choose(visible.find(s=>idOf(s.id)===idOf(selected?.id))||visible[0]||null);
    }
    search.addEventListener('input',filter);list.addEventListener('change',()=>choose(states.find(s=>idOf(s.id)===list.value)||null));
    const left=el('aside',{},[search,list,el('div',{class:'resource-actions'},[button('New',()=>choose(null)),save,rename,remove])]);
    const right=el('div',{class:'resource-fields'},[row('Name',name),row('Description',description),details,
      el('fieldset',{},[el('legend',{text:'Layer properties to restore'}),...controls.map(c=>row(c.label,c.element)),
        el('div',{class:'resource-actions'},[button('Select all',()=>controls.forEach(c=>c.element.checked=true)),button('Clear all',()=>controls.forEach(c=>c.element.checked=false))])]),
      row('Restore current layer',current),row('Turn off layers not in state',off),summary]);
    filter();
    const accepted=await dialog('Layer states manager',[el('div',{class:'resource-manager'},[left,right]),
      el('p',{class:'hint',text:'Restore applies the checked global layer properties. Save current layers replaces the selected state; New captures another state. States persist in DXF. Viewport overrides and LAS transfer are unavailable in this manager.'})],'Restore / Save new');
    if(!accepted)return;
    editable();if(key!==client.key||client.busy)throw new Error('The drawing changed while the layer states manager was open. Open it again.');
    const action=requested||(selected?'restore':'save');
    const state={action,...(selected?{id:{value:idOf(selected.id)}}:{})};
    if(action==='save'||action==='rename')state.name=resourceName(name.value);
    if(action==='save')state.description=description.value;
    if(action==='restore')Object.assign(state,{fields:controls.reduce((mask,c)=>mask|(c.element.checked?c.bit:0),0),restoreCurrentLayer:current.checked,turnOffUnlistedLayers:off.checked});
    await edit('layer-state',{state},{expectedKey:key});
  };
}
