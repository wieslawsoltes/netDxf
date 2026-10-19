import {attributeId} from './attribute-contracts.mjs';

export function insertionDefinitions(data) {
  const definitions=(data.definitions||[]).map(d=>({...d.appearance,verify:d.verify,preset:d.preset}));
  if(definitions.length>10_000||new Set(definitions.map(d=>attributeId(d.id))).size!==definitions.length)
    throw new Error('Invalid or excessive insertion attribute definitions.');
  return definitions;
}
export function insertionValue(definition,value) {
  if(typeof value!=='string'||value.length>1_000_000||value.includes('\0')||!definition.multiline&&/[\r\n]/.test(value))
    throw new Error(`Invalid value for ${definition.tag}. Single-line values cannot contain paragraph breaks.`);
  return definition.multiline?value.replace(/\r\n|\r|\n/g,'\\P'):value;
}

/** Detached command-line attribute entry. Nothing is inserted until every prompt is complete. */
export class InsertAttributeSession {
  constructor(completed,definitions,expectedKey) {
    this.name='INSERT';this.command=structuredClone(completed.command);this.insertOptions=completed.insertOptions;
    this.expectedKey=expectedKey;this.attributesPrepared=true;this.selecting=false;this.index=0;this.version=0;
    this.definitions=definitions.filter(d=>!d.constant);this.values=new Map(this.definitions.map(d=>[attributeId(d.id),d.value]));
    this.spec={steps:[...this.definitions.filter(d=>!d.preset).map(d=>({definition:d,verify:false})),
      ...this.definitions.filter(d=>d.verify).map(d=>({definition:d,verify:true}))]};
    this.complete=this.spec.steps.length===0;
  }
  get anchor(){return null;}
  get step(){
    const item=this.spec.steps[this.index];if(!item)return {kind:'text',prompt:'Attributes complete'};
    return {kind:'text',prompt:`${item.verify?'Verify ':''}${item.definition.prompt||item.definition.tag} [${item.definition.tag}]`,defaultValue:this.values.get(attributeId(item.definition.id))};
  }
  get prompt(){const s=this.step;return `INSERT — ${s.prompt} <${s.defaultValue??''}>:`;}
  get attributeAssignments(){return this.definitions.map(d=>({definition:{value:attributeId(d.id)},value:this.values.get(attributeId(d.id))}));}
  input(value){
    if(this.complete)throw new Error('Attribute entry is complete.');
    const d=this.spec.steps[this.index].definition;
    // Enter keeps the displayed value; two quotation marks explicitly clear it.
    const text=value===''?this.values.get(attributeId(d.id)):value==='""'?'':value;
    this.values.set(attributeId(d.id),insertionValue(d,text));this.index++;this.version++;this.complete=this.index===this.spec.steps.length;
  }
  pick(){throw new Error('Enter the attribute value on the command line, or press Escape to cancel insertion.');}
}
