import {el} from './ui.mjs';

/** ATTSYNC prepares a reviewable proposal; only the existing review Apply action mutates source. */
export async function synchronizeAttributes({client,edit,dialog,editable,showPalette}) {
  editable();if(client.busy)throw new Error('Finish the current edit before synchronizing attributes.');
  const key=client.key,blocks=(client.data?.blocks||[]).filter(b=>!b.isExternal);
  if(!blocks.length)throw new Error('Create or import a local block definition first.');
  const selected=client.data?.selectedEntities||[];
  const selectedBlock=(client.data?.items||[]).find(item=>selected.includes(item.id)&&item.block)?.block;
  const search=el('input',{type:'search',placeholder:'Find block by name','aria-label':'Find block to synchronize'});
  const list=el('select',{'aria-label':'Block to synchronize',size:Math.min(8,Math.max(2,blocks.length))});
  function filter(){const value=list.value||selectedBlock;const visible=blocks.filter(b=>b.name.toLowerCase().includes(search.value.toLowerCase()));list.replaceChildren(...visible.map(b=>el('option',{value:b.id,text:b.name})));if(visible.some(b=>b.id===value))list.value=value;}
  search.addEventListener('input',filter);filter();
  const convert=el('input',{type:'checkbox',checked:false,'aria-label':'Allow attribute text mode conversion'});
  const accepted=await dialog('Synchronize block attributes (ATTSYNC)',[
    el('p',{text:'Update every reference of this block, including other layouts and nested blocks. Retained attributes keep their assigned values; formatting and properties reset to the definition.'}),search,list,
    el('p',{class:'attribute-warning',text:'Attributes removed from the definition are removed from references. INSERT extended data (XData) is deleted. Review the affected values and before/after drawing before applying.'}),
    el('label',{class:'form-row'},[el('span',{text:'Allow text mode conversion'}),convert]),
    el('p',{class:'hint',text:'Enable only when changing between single-line and multiline definitions. Multiline-to-single-line conversion uses visible text, removes inline formatting and replaces paragraph breaks with spaces. The previous fallback is replaced; Undo restores it.'})
  ],'Create synchronization preview');
  if(!accepted)return;
  editable();if(key!==client.key||client.busy)throw new Error('The drawing or selection changed. Reopen ATTSYNC.');
  const block=blocks.find(b=>b.id===list.value);if(!block)throw new Error('Choose a block definition.');
  const result=await edit('edit-preview',{description:`ATTSYNC ${block.name}`,operations:[{kind:'attribute-sync',attributeSync:{block:{value:block.id},convertTextModes:convert.checked}}]},{expectedKey:key});
  if(result?.previewId)showPalette('Edit previews');
}
