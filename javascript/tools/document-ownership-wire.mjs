// Input conversion and observation only. Production DxfDocument owns every mutation.
import * as api from '../index.js';
import { resolve, typeName } from './model-types.mjs';
import { Copy } from '../runtime/GeometryRuntime.js';
import { HeaderDateTime } from '../runtime/HeaderTime.js';
import { doubleBits, fromBits } from './wire.mjs';
import { KeyNotFoundException, ArgumentException } from '../runtime/Errors.js';
export function ownershipRef(item){return item==null?null:{type:item.constructor.name,code:item.CodeName,handle:item.Handle,name:item instanceof api.TableObject?item.Name:null,owner:item.Owner?.Handle??null,ownerCode:item.Owner?.CodeName??null};}
const tableNames=['VPorts','Views','ApplicationRegistries','Layers','Linetypes','TextStyles','ShapeStyles','DimensionStyles','MlineStyles','UCSs','Blocks','ImageDefinitions','UnderlayDgnDefinitions','UnderlayDwfDefinitions','UnderlayPdfDefinitions','Groups','Layouts'];
export function ownershipDocument(doc){return {handle:doc.Handle,seed:doc.DrawingVariables.HandleSeed,version:doc.DrawingVariables.AcadVer,name:doc.Name,active:doc.Entities.ActiveLayout,dimensionBlocks:doc.BuildDimensionBlocks,
  thumbnail:btoa(String.fromCharCode(...doc.ThumbnailImage)),classes:doc.Classes.Count,
  registry:Array.from(doc.AddedObjects,p=>({key:p.Key,value:ownershipRef(p.Value)})),
  tables:tableNames.map(name=>({name,table:ownershipRef(doc[name]),items:Array.from(doc[name],ownershipRef)})),
  layouts:Array.from(doc.Layouts,l=>({name:l.Name,block:ownershipRef(l.AssociatedBlock),viewport:ownershipRef(l.Viewport),entities:Array.from(l.AssociatedBlock.Entities,ownershipRef),attributes:Array.from(l.AssociatedBlock.AttributeDefinitions.Values,ownershipRef)}))};}
function wire(value){
  if(value==null)return null;if(value instanceof api.DxfDocument)return ownershipDocument(value);
  if(value instanceof api.DxfObject)return ownershipRef(value);
  if(value instanceof api.DxfObjectReference)return {reference:ownershipRef(value.Reference),uses:value.Uses};
  if(value instanceof api.XData)return {registry:ownershipRef(value.ApplicationRegistry),records:value.XDataRecord.Count};
  if(typeof value==='string'||typeof value==='boolean'||typeof value==='number')return value;
  if(typeof value==='bigint')return {long:value.toString()};
  if(value[Symbol.iterator])return Array.from(value,wire);if(value.MoveNext)return {iterator:true};return {type:value.constructor.name};
}
export function documentOwnershipCall(input){
  const values=new Map();
  const ref=id=>{if(!values.has(id))throw new KeyNotFoundException();return values.get(id);};
  function read(v){
    if(v==null||typeof v!=='object')return v;
    if('ref'in v)return ref(v.ref);if('new'in v){const Type=resolve(v.new),args=(v.args??[]).map(read);return Type.CreateOverload&&v.signature?Type.CreateOverload(v.signature.map(typeName).join(','),...args):new Type(...args);}
    if('static'in v)return resolve(v.static)[v.property];if('enum'in v)return v.value;
    if('int'in v)return v.int;if('short'in v)return v.short;if('byte'in v)return v.byte;
    if('double'in v)return fromBits(v.double);if('long'in v)return BigInt(v.long);
    if('copy'in v)return Copy(read(v.copy));if('array'in v)return v.array==='Byte'?Uint8Array.from(v.values.map(read)):v.values.map(read);
    if('datetime'in v)return new HeaderDateTime(BigInt(v.datetime.ticks),v.datetime.kind??0);
    throw new Error('Unknown ownership input descriptor.');
  }
  return input.steps.map(step=>{
    let result=null,error=null,param=null;
    try{
      const target=step.target?ref(step.target):null,args=(step.args??[]).map(read);
      switch(step.method){
        case 'new':result=read(step.value);break;
        case 'get':result=target[step.member];break;
        case 'set':{
          let scope=target,descriptor;while(scope&&!descriptor){descriptor=Object.getOwnPropertyDescriptor(scope,step.member);scope=Object.getPrototypeOf(scope);}
          if(descriptor?.get&&!descriptor.set)throw new ArgumentException('Property set method not found.');target[step.member]=read(step.value);break;
        }
        case 'item':result=target.get_Item(...args);break;
        case 'call':{
          const many=step.signature?.[0]?.startsWith('IEnumerable<');
          result=target instanceof api.DrawingEntities&&['Add','Remove'].includes(step.member)?target[step.member](args[0],many??false):target[step.member](...args);break;
        }
        case 'snapshot':result=target;break;
        case 'same':result=args[0]===args[1];break;
        default:throw new Error('Unknown ownership operation.');
      }
      if(step.id)values.set(step.id,result);result=wire(result);
    }catch(e){error=e.name;param=e.ParamName??null;}
    return {result,error,param};
  });
}
