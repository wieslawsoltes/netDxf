import {attributeChanges,attributeId} from './attribute-contracts.mjs';

export function definitionChanges(original,input) {
  const result={definition:{value:attributeId(original.id)}};
  const appearance=attributeChanges({...original,constant:false},input);
  if(appearance)result.appearance=appearance;
  for(const key of ['tag','prompt','verify','preset']) {
    if(!(key in input))continue;
    let value=key==='verify'||key==='preset'?!!input[key]:String(input[key]);
    if(value===original[key])continue;
    if(key==='tag') {
      if(!value||value.length>255||/[\s!\x00-\x1f\x7f]/u.test(value))throw new Error('Tags require 1–255 characters without whitespace, control characters or exclamation marks.');
      value=value.toUpperCase();
    }
    if(key==='prompt'&&(value.length>1_000_000||/[\x00\r\n]/.test(value)))throw new Error('Choose a bounded single-line prompt.');
    if(value!==original[key])result[key]=value;
  }
  return Object.keys(result).length>1?result:null;
}

export function definitionBatch(original,current,drafts,removed) {
  const originals=new Map(original.map(a=>[attributeId(a.id),a]));
  const edits=current.map(a=>drafts.has(attributeId(a.id))?definitionChanges(originals.get(attributeId(a.id)),drafts.get(attributeId(a.id))):null).filter(Boolean);
  const tags=current.map(a=>edits.find(e=>attributeId(e.definition)===attributeId(a.id))?.tag??a.tag);
  if(new Set(tags.map(t=>t.toUpperCase())).size!==tags.length)throw new Error('Attribute definition tags must be unique.');
  const order=current.map(a=>attributeId(a.id)),expected=original.filter(a=>!removed.has(attributeId(a.id))).map(a=>attributeId(a.id));
  const changes={edits,remove:[...removed].map(value=>({value}))};
  if(order.some((id,i)=>id!==expected[i]))changes.order=order.map(value=>({value}));
  return edits.length||removed.size||changes.order?changes:null;
}
