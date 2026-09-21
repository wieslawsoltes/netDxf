import { BoxedScalar } from '../runtime/BoxedScalar.js';
// Shared Node/browser operation interpreter; production implementations provide all behavior.
import * as api from '../index.js';
import { shapeInput } from './styles-wire.mjs';
import { ArgumentException, ArgumentNullException, NullReferenceException, InvalidOperationException, KeyNotFoundException, IndexOutOfRangeException } from '../runtime/Errors.js';
import { Copy, Culture } from '../runtime/GeometryRuntime.js';

import { fromBits, bytesToBase64 } from './wire.mjs';

import { resolve, typeName } from './model-types.mjs';
import { wire } from './model-wire.mjs';

export function jsGeometry(input) {
  api.BlockRecord.DefaultUnits=0;api.Insert.DefaultInsUnits=0;
  const values = new Map(); api.MathHelper.Epsilon = 1e-12; Culture.Current = ''; api.Text.DefaultMirrText = false; api.MText.DefaultMirrText = false;
  const native = input.nativeManifest;
  const observers=new Map(), observations=[];
  function read(value) {
    if (value == null || typeof value !== 'object') return value;
    if ('char' in value) return new api.BoxedChar(String.fromCharCode(value.char));
    if ('datetime' in value) return new api.HeaderDateTime(BigInt(value.datetime.ticks),value.datetime.kind??0);
    if ('timespan' in value) return new api.HeaderTimeSpan(BigInt(value.timespan));
    if ('box' in value) {
      const descriptor=value.box;
      if ('char' in descriptor) return read(descriptor);
      if ('enum' in descriptor) return new api.HeaderEnum(descriptor.enum.split('.').at(-1),resolve(descriptor.enum),descriptor.value);
      const kind=['int','short','byte','double','long'].find(key=>key in descriptor);
      if(!kind)throw new Error('Unsupported boxed header descriptor.');
      return new BoxedScalar({int:'Int32',short:'Int16',byte:'Byte',double:'Double',long:'Int64'}[kind],read(descriptor));
    }
    if ('resolver' in value) {
      const map=new Map();
      for(const [key,item] of value.resolver) {
        const resolved=read(key);
        if(resolved===null)throw new ArgumentNullException('key');
        if(map.has(resolved))throw new ArgumentException('An item with the same key has already been added.');
        map.set(resolved,read(item));
      }
      return item=>{if(item===null)throw new ArgumentNullException('key');if(!map.has(item))throw new KeyNotFoundException();return map.get(item);};
    }
    if ('copy' in value) return Copy(read(value.copy));
    if ('utf16' in value) return value.utf16.map(n=>String.fromCharCode(n)).join('');
    if ('ref' in value) { if(!values.has(value.ref)) throw new KeyNotFoundException('Missing scenario reference: '+value.ref); return values.get(value.ref); }
    if ('double' in value) return fromBits(value.double);
    if ('long' in value) return BigInt(value.long);
    if ('int' in value) return value.int;
    if ('short' in value) return value.short;
    if ('byte' in value) return value.byte;
    if ('enum' in value) return value.value;
    if ('out' in value) return {value:0};
    if ('culture' in value) {
      if (value.culture !== '') throw new Error('This geometry verifier currently uses only the explicit invariant provider.');
      return null;
    }
    if ('array' in value) return value.array === 'Byte' ? Uint8Array.from(value.values.map(read)) : value.array === 'Object' ? value.values.map(item => {
      const boxed = item && typeof item === 'object' && ['int','short','byte','double'].find(key=>key in item);
      return boxed ? new BoxedScalar({int:'Int32',short:'Int16',byte:'Byte',double:'Double'}[boxed],read(item)) : read(item);
    }) : value.values.map(read);
    if ('new' in value) return construct(resolve(value.new),(value.args ?? []).map(read),value.signature);
    if ('static' in value) return resolve(value.static)[value.property];
    throw new Error('Unknown value descriptor.');
  }
  function invoke(type, target, step, args) {
    const signature=step.signature?.map(typeName).join(',');
    const file=native?.files.find(f=>f.source.split('/').at(-1)===type.name+'.cs');
    const method=file?.members.find(m=>m.name===step.member&&m.signature===signature&&m.isStatic===!target);
    if (file && signature!==undefined && !method) throw new Error('Unmapped exact member '+type.name+'.'+step.member+'('+signature+')');
    return (target??type)[method?.implementation??step.member](...args.map(Copy));
  }
  function construct(type,args,signature) {
    if (type.CreateOverload && signature) return type.CreateOverload(signature.map(typeName).join(','),...args.map(Copy));
    return new type(...args);
  }
  return input.steps.map(step => {
    try {
      if (step.target && !values.has(step.target)) throw new KeyNotFoundException('Missing scenario target: ' + step.target);
      const target = step.target ? values.get(step.target) : null;
      const type = step.type ? resolve(step.type) : target?.constructor;
      const args = (step.args ?? []).map(read);
      let result;
      switch (step.kind) {
        case 'lin-names': result = api.Linetype.NamesFromText(step.text); break;
        case 'lin-load': result = api.Linetype.LoadText(step.text,step.patternName); break;
        case 'lin-save': result = target.ToLinString(step.newLine ?? '\n'); break;
        case 'shape-names': case 'shape-query': result=shapeInput(step);break;
        case 'reference-equals': result=args[0]===args[1];break;
        case 'observe': {
          const handler=(sender,e)=>{
            const properties={};for(const key of ['OldValue','NewValue','Item','Cancel'])if(key in e)properties[key]=wire(e[key]);
            observations.push({observer:step.observer,member:step.member,values:properties});
            if('replace' in step){if(!('NewValue' in e))throw new NullReferenceException();e.NewValue=read(step.replace);}
            if('cancel' in step){if(!('Cancel' in e))throw new NullReferenceException();e.Cancel=step.cancel;}
            if(step.throw)throw new InvalidOperationException('Observer failure');
          };
          target[step.member].Add(handler);observers.set(step.observer,{target,event:step.member,handler});break;
        }
        case 'unobserve': {const item=observers.get(step.observer);item.target[item.event].Remove(item.handler);observers.delete(step.observer);break;}
        case 'events': return {ok:true,value:structuredClone(observations)};
        case 'pat-names': result = api.HatchPattern.NamesFromText(step.text); break;
        case 'pat-load': result = api.HatchPattern.LoadText(step.text, step.patternName); break;
        case 'pat-save': result = target.ToPatString(step.newLine ?? '\n'); break;
        case 'map-add': target.set(args[0],args[1]);break;
        case 'value': result = read(step.value); break;
        case 'new': result = construct(type,args,step.signature); break;
        case 'get': result = (target ?? type)[step.member]; if(target instanceof api.DimensionStyle && step.member==='DecimalSeparator') result=new api.BoxedChar(result); break;
        case 'set': { const value=read(step.value); (target ?? type)[step.member] = value instanceof api.BoxedChar && !(target instanceof api.HeaderVariable) ? value.Value : value; break; }
        case 'index':
          if ((Array.isArray(target) || target instanceof Uint8Array) && typeof target.get_Item !== 'function') {
            if (!Number.isInteger(args[0]) || args[0] < 0 || args[0] >= target.length) throw new IndexOutOfRangeException();
            result = Copy(target[args[0]]);
          } else result = target.get_Item(...args);
          break;
        case 'set-index':
          if ((Array.isArray(target) || target instanceof Uint8Array) && typeof target.set_Item !== 'function') {
            if (!Number.isInteger(args[0]) || args[0] < 0 || args[0] >= target.length) throw new IndexOutOfRangeException();
            target[args[0]] = Copy(read(step.value));
          } else target.set_Item(...args, read(step.value));
          break;
        case 'snapshot': result = target; break;
        case 'call': {
          if(type===api.EntityCollection&&step.member==='Remove'&&step.signature)
            result=target.Remove(args[0],step.signature[0].startsWith('IEnumerable<'));
          else if(type===api.DxfClassCollection&&['Contains','Remove'].includes(step.member)&&step.signature?.[0]==='DxfClass')
            result=target[step.member](args[0],'DxfClass');
          else if (type === api.UnitHelper && step.member === 'ConversionFactor' && step.signature)
            result = api.UnitHelper.ConversionFactor(...args, step.signature[0].replace(/^.*\./,''), step.signature[1].replace(/^.*\./,''));
          else result = invoke(type,target,step,args);
          if (step.signature?.some(s => s.endsWith('&')))
            return {ok:true,value:{result:wire(result),outputs:args.filter((_,i)=>step.signature[i].endsWith('&')).map(a=>wire(a.value))}};
          break;
        }
        case 'emit': {
          const T = (code,value) => new api.DxfTag(code,value);
          const doc = api.DxfRawDocument.Create([T(0,'SECTION'),T(2,'HEADER'),T(9,'$ACADVER'),T(1,'AC1032'),T(0,'ENDSEC'),
            T(0,'SECTION'),T(2,'ENTITIES'),T(0,'POINT'),T(10,target.X),T(20,target.Y),T(30,target.Z),T(0,'ENDSEC'),T(0,'EOF')]);
          return {ok:true,value:{text:bytesToBase64(doc.ToBytes(false)),binary:bytesToBase64(doc.ToBytes(true))}};
        }
        default: throw new Error('Unknown step: '+step.kind);
      }
      if (step.id) values.set(step.id,result);
      return {ok:true,value:wire(result)};
    } catch (error) { return {ok:false,error:error.name,param:error.ParamName ?? null}; }
  });
}
