import { el } from './ui.mjs';
import { parsePoint } from './core.mjs';

/** DOM adapter for reusable native block plans; source identity is retained across dialogs. */
export function blockTools({ client, edit, dialog, cancel, fit, layer }) {
  const row = (label, input) => el('label', {class:'form-row'}, [el('span',{text:label}), input]);
  const text = (label, value = '') => el('input', {'aria-label':label, value, required:true});
  const receipt = () => {
    const key = client.key;
    return () => { if (client.key !== key || client.busy) throw new Error('The drawing changed while the block dialog was open. Repeat the operation.'); };
  };
  return {
    async create() {
      if (!client.data?.selectedEntities.length) throw new Error('Select source objects before creating a block, or use the BLOCK command.');
      const current = receipt(), name = text('Block name'), origin = text('Base point','0,0');
      const disposition = el('select', {'aria-label':'Source objects'}, ['Convert','Retain','Remove'].map(value=>el('option',{value,text:value})));
      if (!await dialog('Block definition', [row('Name', name), row('Base point (X,Y,Z)', origin), row('Source objects', disposition),
        el('p',{class:'hint',text:'Convert replaces the selection with one INSERT. Retain keeps the originals. Remove keeps only the reusable definition. One undo reverses the entire operation.'})], 'Create block')) return;
      current(); cancel(false);
      await edit('block/create', {name:name.value, basePoint:parsePoint(origin.value), disposition:disposition.value, layer:layer()});
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
      current(); await edit('block/rename',{block,name:name.value});
    }
  };
}
