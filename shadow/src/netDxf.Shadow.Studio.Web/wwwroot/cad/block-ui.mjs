import { el } from './ui.mjs';
import { number, parsePoint } from './core.mjs';
import { insertOptions } from './block-contracts.mjs';

/** DOM adapter for reusable native block plans; source identity is retained across dialogs. */
export function blockTools({ client, edit, dialog, cancel, fit, placeInsert }) {
  let lastInsert = null;
  const row = (label, input) => el('label', {class:'form-row'}, [el('span',{text:label}), input]);
  const text = (label, value = '') => el('input', {'aria-label':label, value, required:true});
  const receipt = () => {
    const key = client.key;
    return () => { if (client.key !== key || client.busy) throw new Error('The drawing changed while the block dialog was open. Repeat the operation.'); };
  };
  return {
    async insert(blockId) {
      if (client.busy) throw new Error('Finish the current edit before inserting a block.');
      const current = receipt();
      const blocks = (client.data?.blocks || []).filter(b => !b.isExternal && b.id !== client.data?.blockEditor?.id);
      if (!blocks.length) throw new Error('Create or import a reusable local block definition first.');
      const search = text('Search block definitions'); search.required = false; search.type = 'search'; search.placeholder = 'Search definitions';
      const select = el('select', {'aria-label':'Block to insert',size:Math.min(6,Math.max(2,blocks.length))});
      const details = el('p',{class:'hint'});
      const previous = lastInsert && blocks.some(b=>b.id===lastInsert.id) ? lastInsert : null;
      const initial = blocks.find(b=>b.id===blockId) || blocks.find(b=>b.id===previous?.id) || blocks[0];
      let constrain = () => {};
      function filter() {
        const selected = select.value || initial.id;
        const visible = blocks.filter(b=>`${b.name} ${b.description || ''}`.toLowerCase().includes(search.value.toLowerCase()));
        select.replaceChildren(...visible.map(b=>el('option',{value:b.id,text:b.name})));
        if (visible.some(b=>b.id===selected)) select.value=selected;
        update();
      }
      function update() {
        const selected = blocks.find(b=>b.id===select.value);
        const origin=selected?.origin || {x:0,y:0,z:0};
        details.textContent=selected ? `${selected.entities} definition objects · ${selected.units || 'Unitless'} · base ${origin.x}, ${origin.y}, ${origin.z}. ${selected.description || ''}${selected.scaleUniformly ? ' Uniform scaling required.' : ''}${selected.allowExploding===false ? ' Definition disallows exploding.' : ''}${selected.attributes ? ` ${selected.attributes} attributes keep their default values; edit them through Properties after placement.` : ''}` : 'No matching definitions.';
        constrain(selected);
      }
      search.addEventListener('input',filter); select.addEventListener('change',update); filter();
      const scaleX=text('Block X scale',String(previous?.scale.x ?? 1)), scaleY=text('Block Y scale',String(previous?.scale.y ?? 1)), scaleZ=text('Block Z scale',String(previous?.scale.z ?? 1));
      const uniform=el('input',{type:'checkbox','aria-label':'Uniform block scale'}); uniform.checked=!previous || previous.scale.x===previous.scale.y && previous.scale.x===previous.scale.z;
      const sync=()=>{ scaleY.disabled=uniform.checked;scaleZ.disabled=uniform.checked;if(uniform.checked){scaleY.value=scaleX.value;scaleZ.value=scaleX.value;} };
      constrain=selected=>{uniform.disabled=!!selected?.scaleUniformly;if(uniform.disabled)uniform.checked=true;sync();};
      uniform.addEventListener('change',sync);scaleX.addEventListener('input',sync);sync();
      update();
      const rotation=text('Block rotation in degrees',String(previous?.rotation ?? 0));
      const alternate=el('input',{type:'checkbox','aria-label':'Use alternate definition base point'}); alternate.checked=!!previous?.basePoint;
      const base=text('Alternate definition base point',previous?.basePoint ? `${previous.basePoint.x},${previous.basePoint.y},${previous.basePoint.z}` : '0,0,0');base.disabled=!alternate.checked;
      alternate.addEventListener('change',()=>base.disabled=!alternate.checked);
      const repeat=el('input',{type:'checkbox','aria-label':'Repeat block placement'});repeat.checked=previous?.repeat ?? false;
      if (!await dialog('Insert block', [row('Find definition',search),row('Block',select),details,row('Uniform scale',uniform),row('X scale',scaleX),row('Y scale',scaleY),row('Z scale',scaleZ),
        row('Rotation (degrees)',rotation),row('Alternate base point',alternate),row('Point in definition coordinates',base),row('Repeat placement',repeat),
        el('p',{class:'hint',text:'Pick the insertion point in the drawing after choosing Place. Negative scale mirrors an axis. An alternate base point anchors that point without changing the shared definition. Each placement is one undo step; Escape ends repeated placement.'})], 'Place in drawing')) return;
      current();
      const selected=blocks.find(b=>b.id===select.value);
      const options=insertOptions(selected,{scale:{x:number(scaleX.value),y:number(uniform.checked?scaleX.value:scaleY.value),z:number(uniform.checked?scaleX.value:scaleZ.value)},
        rotation:number(rotation.value),basePoint:alternate.checked?parsePoint(base.value):null,repeat:repeat.checked});
      if (typeof placeInsert !== 'function') throw new Error('The host does not support configured block placement.');
      lastInsert=options;cancel(false);await placeInsert(options);
    },
    async begin(block) {
      if (client.data?.blockEditor) throw new Error('Save or discard the current block editor before opening another definition.');
      const current = receipt();
      if (!block) {
        const choices = client.data?.blocks || [];
        if (!choices.length) throw new Error('Create or import a named block first.');
        const select = el('select', {'aria-label':'Block definition'}, choices.map(b=>el('option',{value:b.id,text:b.name})));
        if (!await dialog('Edit block definition', [row('Definition',select), el('p',{class:'hint',text:'Edits are isolated until Save block. Every reference will update when the definition is saved.'})], 'Open editor')) return;
        block = select.value;
      }
      current(); cancel(false); await edit('block/begin',{block}); fit();
    },
    async finish(save) {
      const block = client.data?.blockEditor;
      if (!block) throw new Error('No block definition is open.');
      const current = receipt();
      if (!save && block.changed && !await dialog('Discard block edits', [el('p',{text:`Discard changes to “${block.name}”? Live drawing objects will not be changed.`})], 'Discard changes')) return;
      current(); cancel(false);
      await edit('block/finish', {save, discard:!save, targetRevision:client.state.sourceRevision}); fit();
    },
    async rename(block) {
      const current = receipt(); const source = client.data.blocks.find(b=>b.id===block);
      if (!source) throw new Error('The definition is no longer available.');
      const name = text('New block name',source.name);
      if (!await dialog('Rename block', [row('Name',name)],'Rename')) return;
      current(); await edit('block/rename',{block:{value:block},name:name.value});
    }
  };
}
