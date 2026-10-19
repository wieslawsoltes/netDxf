import {el} from './ui.mjs';
import {attributeId} from './attribute-contracts.mjs';
import {insertionValue} from './insert-attribute-contracts.mjs';

/** Dialog value collection shared by keyboard, ribbon and repeated placement. Cancel never edits. */
export async function insertionAttributeDialog({definitions,name,dialog,current}) {
  const editable=definitions.filter(d=>!d.constant),values=new Map(editable.map(d=>[attributeId(d.id),d.value]));
  async function collect(items,verify) {
    if(!items.length)return true;
    const controls=new Map(),rows=items.map(d=>{
      const label=`${verify?'Verify attribute':'Insert attribute'} ${d.tag}`;
      const node=el(d.multiline?'textarea':'input',{'aria-label':label});node.value=values.get(attributeId(d.id));node.maxLength=1_000_000;
      controls.set(attributeId(d.id),node);
      return el('label',{class:'form-row'},[el('span',{text:`${d.prompt||d.tag} (${d.tag})${d.invisible?' · invisible':''}`}),node]);
    });
    if(!await dialog(verify?'Verify block attributes':'Insert block attributes',[
      el('p',{text:`Block: ${name}. ${verify?'Confirm or correct the marked values.':'Values apply to this insertion. Constant definitions keep their defaults.'}`}),...rows,
      el('p',{class:'hint',text:'Cancel leaves the drawing unchanged. Multiline values accept MTEXT formatting and paragraph breaks.'})],verify?'Confirm and insert':'Continue'))return false;
    current();for(const d of items)values.set(attributeId(d.id),insertionValue(d,controls.get(attributeId(d.id)).value));return true;
  }
  if(!await collect(editable,false)||!await collect(editable.filter(d=>d.verify),true))return null;
  return editable.map(d=>({definition:{value:attributeId(d.id)},value:values.get(attributeId(d.id))}));
}
