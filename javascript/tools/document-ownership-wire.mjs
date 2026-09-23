import { tableStyleLoad, tableStyleCall, tableStyleSnapshot } from './table-style-wire.mjs';
import { managerCall, managerLoad, managerSnapshot } from './section-manager-wire.mjs';
import { createRetainedParent, retainedSnapshot, retainedSet } from './retained-polyline-wire.mjs';
// Input conversion and observation only. Production DxfDocument owns every mutation.
import { wire as modelWire } from './model-wire.mjs';
import { Culture } from '../runtime/GeometryRuntime.js';
import * as api from '../index.js';
import { resolve, typeName } from './model-types.mjs';
import { Copy } from '../runtime/GeometryRuntime.js';
import { HeaderDateTime } from '../runtime/HeaderTime.js';
import { doubleBits, fromBits } from './wire.mjs';
import * as apiErrors from '../runtime/Errors.js';
import { KeyNotFoundException, ArgumentException } from '../runtime/Errors.js';
export function ownershipRef(item){return item==null?null:{type:item.constructor.name,code:item.CodeName,handle:item.Handle,name:item instanceof api.TableObject?item.Name:null,owner:item.Owner?.Handle??null,ownerCode:item.Owner?.CodeName??null};}
const tableNames=['VPorts','Views','ApplicationRegistries','Layers','Linetypes','TextStyles','ShapeStyles','DimensionStyles','MlineStyles','UCSs','Blocks','ImageDefinitions','UnderlayDgnDefinitions','UnderlayDwfDefinitions','UnderlayPdfDefinitions','Groups','Layouts'];
export function ownershipDocument(doc){return {handle:doc.Handle,seed:doc.DrawingVariables.HandleSeed,version:doc.DrawingVariables.AcadVer,name:doc.Name,active:doc.Entities.ActiveLayout,dimensionBlocks:doc.BuildDimensionBlocks,
  thumbnail:btoa(String.fromCharCode(...doc.ThumbnailImage)),classes:doc.Classes.Count,
  registry:Array.from(doc.AddedObjects,p=>({key:p.Key,value:ownershipRef(p.Value)})),
  tables:tableNames.map(name=>({name,table:ownershipRef(doc[name]),items:Array.from(doc[name],ownershipRef)})),
  layouts:Array.from(doc.Layouts,l=>({name:l.Name,block:ownershipRef(l.AssociatedBlock),viewport:ownershipRef(l.Viewport),entities:Array.from(l.AssociatedBlock.Entities,ownershipRef),attributes:Array.from(l.AssociatedBlock.AttributeDefinitions.Values,ownershipRef)}))};}
function layerProperties(p) { return {name:p.Name,flags:p.Flags,linetype:p.LinetypeName,color:modelWire(p.Color),lineweight:p.Lineweight,transparency:modelWire(p.Transparency)}; }
function layerState(state) { return {common:ownershipRef(state),description:state.Description,current:state.CurrentLayer,paper:state.PaperSpace,properties:Array.from(state.Properties,p=>({key:p.Key,value:layerProperties(p.Value)}))}; }
function detailed(value) {
  if(value instanceof api.LayerState)return layerState(value);
  if(value instanceof api.LayerStateProperties)return layerProperties(value);
  if(value instanceof api.DxfSortOrderEntry)return {entity:ownershipRef(value.Entity),handle:value.SortHandle};
  return modelWire(value);
}
function wire(value){
  if(value==null)return null;
  if(value instanceof api.LayerState)return layerState(value);
  if(value instanceof api.LayerStateProperties)return layerProperties(value);
  if(value instanceof api.DxfSortOrderEntry)return {entity:ownershipRef(value.Entity),handle:value.SortHandle};
  if(value instanceof api.DxfDocument)return ownershipDocument(value);
  if(value instanceof api.DxfObject)return ownershipRef(value);
  if(value instanceof api.DxfObjectReference)return {reference:ownershipRef(value.Reference),uses:value.Uses};
  if(value instanceof api.XData)return {registry:ownershipRef(value.ApplicationRegistry),records:value.XDataRecord.Count};
  if(typeof value==='string'||typeof value==='boolean'||typeof value==='number')return value;
  if(typeof value==='bigint')return {long:value.toString()};
  if(value instanceof Map)return Array.from(value,([key,value])=>({key:wire(key),value:wire(value)}));
  if(value[Symbol.iterator])return Array.from(value,wire);if(value.MoveNext)return {iterator:true};return {type:value.constructor.name};
}
export function documentOwnershipCall(input){
  api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;api.MathHelper.Epsilon=1e-12;Culture.Current=input.culture??'';api.Text.DefaultMirrText=false;api.MText.DefaultMirrText=false;
  const values=new Map();
  const ref=id=>{if(!values.has(id))throw new KeyNotFoundException();return values.get(id);};
  function read(v){
    if(v==null||typeof v!=='object')return v;
    if('ref'in v)return ref(v.ref);if('new'in v){const Type=resolve(v.new),args=(v.args??[]).map(read);return Type.CreateOverload&&v.signature?Type.CreateOverload(v.signature.map(typeName).join(','),...args):new Type(...args);}
    if('static'in v)return resolve(v.static)[v.property];if('enum'in v)return v.value;
    if('utf16'in v)return v.utf16.map(c=>String.fromCharCode(c)).join('');
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
      if(target==null&&['get','set','item','call','append-loaded'].includes(step.method))throw new apiErrors.NullReferenceException();
      switch(step.method){
        case 'table-load':result=tableStyleLoad(step,target,read);break;
        case 'table-model':result=tableStyleSnapshot(target);break;
        case 'table-call':result=tableStyleCall(step,target,read,values);break;
        case 'manager-seed-from':target.NumHandles=BigInt('0x'+read(step.handle));break;
        case 'manager-set-owner':target.Owner=read(step.owner);break;
        case 'manager-call':result=managerCall(step,target,read,values);break;
        case 'manager-model':result=managerSnapshot(target);break;
        case 'manager-load':result=managerLoad(step,target,read);break;
        case 'retained-create':result=createRetainedParent(step);break;
        case 'retained-model':result=retainedSnapshot(target);break;
        case 'retained-set':retainedSet(target,step.field,read(step.value));break;
        case 'opaque':result=new api.DxfOpaqueObject(...args);break;
        case 'append-loaded':target.AddLoadedData(...args);break;
        case 'mapping': {
          const mapping=new Map();for(const [key,value] of step.pairs.map(pair=>pair.map(read))){if(key==null)throw new apiErrors.ArgumentNullException('key');if(mapping.has(key))throw new ArgumentException('Duplicate key.');mapping.set(key,value);}result=mapping;break;
        }
        case 'model':result=detailed(target);break;
        case 'las': {const text=target.ToLasString(input.newLine ?? Culture.NewLine);result={saved:true,text,loaded:layerState(api.LayerState.LoadText(text))};break;}
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
      if(step.id)values.set(step.id,result);if(!['model','las','retained-model','manager-model','table-model'].includes(step.method))result=wire(result);
    }catch(e){error=e.name;param=e.ParamName??null;}
    return {result,error,param};
  });
}
