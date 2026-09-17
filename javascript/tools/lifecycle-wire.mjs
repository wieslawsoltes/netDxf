// Node/browser scenario interpreter; every operation invokes the production typed classes.
import { ApplicationRegistry, XData, XDataRecord, XDataCode, XDataDictionary, DxfObject, DxfTag, DxfRawDocument } from '../index.js';
import { NotSupportedException } from '../runtime/Errors.js';
import { bytesToBase64, doubleBits } from './wire.mjs';
class Carrier extends DxfObject {}
export function lifecycleCall(input) {
  return input.scenarios.map(scenario => {
    const refs = new Map(), events = []; let mode = '', collisionTarget = null, iterator;
    const ref = key => { if (!refs.has(key)) throw new Error('Unmapped protocol reference '+key); return refs.get(key); };
    const dict = key => ref(key) instanceof DxfObject ? ref(key).XData : ref(key);
    function registry(name) {
      const r = new ApplicationRegistry(name);
      r.NameChanged.Add((sender,e) => {
        events.push('name:'+sender.Name+':'+e.OldValue+':'+e.NewValue);
        if (mode === 'change-event') e.NewValue = 'IGNORED';
        if (mode === 'throw-name') throw new NotSupportedException('observer');
        if (mode === 'collision') dict(collisionTarget).Add(new XData(new ApplicationRegistry(e.NewValue)));
        if (mode === 'detach') dict(collisionTarget).Remove(sender.Name);
      }); return r;
    }
    function watch(id, value) {
      const add = value instanceof DxfObject ? value.XDataAddAppReg : value instanceof XDataDictionary ? value.AddAppReg : null;
      const remove = value instanceof DxfObject ? value.XDataRemoveAppReg : value instanceof XDataDictionary ? value.RemoveAppReg : null;
      add?.Add((sender,e) => { events.push(id+':add:'+e.Item.Name); if (mode === 'throw-add') throw new NotSupportedException('observer'); });
      remove?.Add((sender,e) => { events.push(id+':remove:'+e.Item.Name); if (mode === 'throw-remove') throw new NotSupportedException('observer'); });
    }
    function snapshot() {
      const ids = new Map(), pending = [], nodes = [];
      const id = value => { if (value == null) return null; if (!ids.has(value)) { ids.set(value, ids.size); pending.push(value); } return ids.get(value); };
      const names = Array.from(refs, ([key,value])=>[key,id(value)]);
      for (let at=0;at<pending.length;at++) {
        const value = pending[at];
        if (value instanceof XDataDictionary) nodes.push({type:'dictionary',entries:Array.from(value,p=>[p.Key,id(p.Value)])});
        else if (value instanceof XData) nodes.push({type:'data',registry:id(value.ApplicationRegistry),container:id(value.Container),records:Array.from(value.XDataRecord,r=>id(r))});
        else if (value instanceof XDataRecord) nodes.push({type:'record',code:value.Code,value:value.Value instanceof Uint8Array?{bytes:id(value.Value)}:typeof value.Value==='number' && value.Code!==XDataCode.Int16 && value.Code!==XDataCode.Int32?{bits:doubleBits(value.Value)}:value.Value});
        else if (value instanceof Uint8Array) nodes.push({type:'bytes',values:Array.from(value)});
        else if (value instanceof DxfObject) nodes.push({type:value instanceof ApplicationRegistry?'registry':'carrier',code:value.CodeName,handle:value.Handle,owner:id(value.Owner),extension:id(value.ExtensionDictionary),reactors:Array.from(value.PersistentReactors,o=>id(o)),name:value instanceof ApplicationRegistry?value.Name:null,reserved:value instanceof ApplicationRegistry?value.IsReserved:null,xdata:id(value.XData)});
        else throw new Error('Unmapped snapshot type.');
      }
      return {names,nodes};
    }
    return scenario.operations.map(op=>{
      events.length=0;mode=op.mode??'';collisionTarget=op.collisionTarget??null;
      let result=null,error=null,param=null;
      const store = value => { refs.set(op.id,value); watch(op.id,value); };
      try {switch(op.method) {
        case 'Registry':store(registry(op.name));break;
        case 'Carrier':store(new Carrier(op.name));break;
        case 'Dictionary':store(new XDataDictionary(op.capacity??0));break;
        case 'Data':store(new XData(ref(op.registry)));break;
        case 'Record':ref(op.target).XDataRecord.Add(new XDataRecord(op.code,op.code===XDataCode.BinaryData?Uint8Array.from(op.value):op.value));break;
        case 'Add':dict(op.target).Add(ref(op.data));break;
        case 'AddKey':dict(op.target).Add(op.key,ref(op.data));break;
        case 'Set':dict(op.target).set_Item(op.key,ref(op.data));break;
        case 'Remove':result=dict(op.target).Remove(op.key);break;
        case 'RemovePair':result=dict(op.target).RemovePair({Key:op.key,Value:ref(op.data)});break;
        case 'ContainsPair':result=dict(op.target).Contains({Key:op.key,Value:ref(op.data)});break;
        case 'Clear':dict(op.target).Clear();break;
        case 'Rename':ref(op.target).Name=op.name;break;
        case 'Clone':store(Object.hasOwn(op,'name')?ref(op.target).Clone(op.name):ref(op.target).Clone());break;
        case 'Get':store(dict(op.target).get_Item(op.key));break;
        case 'Has':result=dict(op.target).ContainsAppId(op.key);break;
        case 'Try':result=dict(op.target).TryGetValue(op.key,{});break;
        case 'NullData':refs.set(op.id,null);break;
        case 'Mutate':ref(op.target).XDataRecord.get_Item(op.index).Value[op.at]=op.value;break;
        case 'RecordsClear':ref(op.target).XDataRecord.Clear();break;
        case 'Canonicalize':dict(op.target).CanonicalizeApplicationRegistry(op.key,ref(op.registry));break;
        case 'ReplaceBinding':dict(op.target).ReplaceForBinding(op.key,ref(op.data));break;
        case 'Enumerate':iterator=dict(op.target).GetEnumerator();result={key:iterator.Current.Key,empty:iterator.Current.Value==null};break;
        case 'Next':result={moved:iterator.MoveNext(),key:iterator.Current.Key,empty:iterator.Current.Value==null};break;
        case 'Reset':iterator.Reset();result={key:iterator.Current.Key,empty:iterator.Current.Value==null};break;
        case 'Compare':result=ref(op.target).CompareTo(ref(op.other));break;
        case 'Equal':result=ref(op.target).Equals(ref(op.other));break;
        case 'References':result={has:ref(op.target).HasReferences(),isNull:ref(op.target).GetReferences()===null};break;
        case 'AssignHandle':result=ref(op.target).AssignHandle(BigInt(op.value)).toString();break;
        case 'Emit':{
          const data=ref(op.target), tags=[[0,'SECTION'],[2,'HEADER'],[9,'$ACADVER'],[1,'AC1032'],[0,'ENDSEC'],[0,'SECTION'],[2,'ENTITIES'],[0,'POINT'],[1001,data.ApplicationRegistry.Name]].map(([c,v])=>new DxfTag(c,v));
          for(const r of data.XDataRecord)tags.push(new DxfTag(r.Code,r.Value));tags.push(new DxfTag(0,'ENDSEC'),new DxfTag(0,'EOF'));const doc=DxfRawDocument.Create(tags);result={text:bytesToBase64(doc.ToBytes(false)),binary:bytesToBase64(doc.ToBytes(true))};break;
        }
        default:throw new Error('Unknown operation '+op.method);
      }} catch(e){error=e.name;param=e.ParamName??null;}
      return {result,error,param,events:events.slice(),graph:snapshot()};
    });
  });
}
