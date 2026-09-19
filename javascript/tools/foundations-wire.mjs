import { coordinateWire } from './coordinate-wire.mjs';
import { databaseModelWire } from './database-model-wire.mjs';
import { BoxedScalar } from '../runtime/BoxedScalar.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
// Shared Node/browser operation interpreter; production implementations provide all behavior.
import * as api from '../index.js';
import { utf16Wire } from './mtext-wire.mjs';
import { entityWire } from './entities-wire.mjs';
import { styleWire, shapeInput } from './styles-wire.mjs';
import { InvalidOperationException, KeyNotFoundException } from '../runtime/Errors.js';
import { Copy, Culture } from '../runtime/GeometryRuntime.js';

import { doubleBits, fromBits, bytesToBase64 } from './wire.mjs';

const resolve = name => name.startsWith('List<') ? ReferenceList : api[name.replace(/^netDxf\./, '').replace(/^(Units|Collections|Entities|Tables|Objects|IO)\./, '')];
function wire(value) {
  if (value == null) return null;
  if (typeof value === 'number') return { double: doubleBits(value) };
  if (typeof value === 'bigint') return { long: value.toString() };
  if (typeof value === 'string') return utf16Wire(value);
  if (typeof value === 'boolean') return value;
  const type = value.constructor.name;
  if (/^Vector[234]$/.test(type)) return { type, values: [...'XYZW'.slice(0, Number(type.at(-1)))].map(key => doubleBits(value[key])), normalized: value.IsNormalized };
  if (/^Matrix[234]$/.test(type)) {
    const n = Number(type.at(-1)), values = [];
    for (let r=1;r<=n;r++) for (let c=1;c<=n;c++) values.push(doubleBits(value[`M${r}${c}`]));
    return { type, values, identity: Copy(value).IsIdentity };
  }
  if (type === 'Tuple') return Object.keys(value).map(key=>wire(value[key]));
  if (value instanceof api.BezierCurve) return {type,degree:value.Degree,points:value.ControlPoints.map(wire)};
  if (value instanceof api.BoundingRectangle) return {type,min:wire(value.Min),max:wire(value.Max),center:wire(value.Center),radius:wire(value.Radius),width:wire(value.Width),height:wire(value.Height)};
  if (value instanceof api.ClippingBoundary) return {type,kind:value.Type,vertices:value.Vertexes.map(wire)};
  if (value instanceof api.AciColor) return {type:'AciColor',r:value.R,g:value.G,b:value.B,index:value.Index,trueColor:value.UseTrueColor,byLayer:value.IsByLayer,byBlock:value.IsByBlock};
  if (value instanceof api.HatchPatternLineDefinition) return {type,angle:wire(value.Angle),origin:wire(value.Origin),delta:wire(value.Delta),dashes:Array.from(value.DashPattern,wire)};
  if (value instanceof api.HatchPattern) {
    const pattern={type,name:value.Name,description:value.Description,style:value.Style,fill:value.Fill,kind:value.Type,isDouble:value.IsDouble,
      origin:wire(value.Origin),angle:wire(value.Angle),scale:wire(value.Scale),lines:Array.from(value.LineDefinitions,wire)};
    if (!(value instanceof api.HatchGradientPattern)) return {pattern};
    return {pattern,gradientType:value.GradientType,color1:wire(value.Color1),color2:wire(value.Color2),single:value.SingleColor,
      tint:wire(value.Tint),shift:wire(value.Shift),centered:value.Centered,aci1:wire(value.Color1AciIndex),aci2:wire(value.Color2AciIndex),
      auto1:value.IsColor1AciIndexAutomatic,auto2:value.IsColor2AciIndexAutomatic};
  }
  if (value instanceof api.XDataRecord) return {type:'XDataRecord',code:value.Code,value:wire(value.Value)};
  if (value instanceof api.DxfClass) return {type:'DxfClass',name:value.Name,cpp:value.CppClassName,application:value.ApplicationName,flags:value.ProxyFlags,count:value.InstanceCount,wasProxy:value.WasProxy,entity:value.IsEntity};
  if (value instanceof api.Color) return {type:'Color',argb:value.ToArgb(),name:value.Name,known:value.IsKnownColor,named: value.IsNamedColor,empty:value.IsEmpty};
  if (value instanceof api.Transparency) return {type,value:value.Value,stored:value.StoredAlphaValue,byLayer:value.IsByLayer,byBlock:value.IsByBlock};
  const model=databaseModelWire(value,wire); if(model!==undefined)return model;
  const coordinate=coordinateWire(value,wire); if(coordinate!==undefined)return coordinate;
  const entity=entityWire(value,wire); if(entity!==undefined)return entity;
  const style=styleWire(value,wire); if(style!==undefined)return style;
  if(value instanceof Map)return Array.from(value,([key,item])=>[wire(key),wire(item)]);
  if(value instanceof api.DxfTag)return {code:value.Code,value:wire(value.Value)};
  if (typeof value[Symbol.iterator] === 'function') return Array.from(value, wire);
  throw new Error('Unmapped geometry result: ' + type);
}

export function jsGeometry(input) {
  const values = new Map(); api.MathHelper.Epsilon = 1e-12; Culture.Current = ''; api.Text.DefaultMirrText = false; api.MText.DefaultMirrText = false;
  const native = input.nativeManifest;
  const observers=new Map(), observations=[];
  function read(value) {
    if (value == null || typeof value !== 'object') return value;
    if ('resolver' in value) { const map=new Map(value.resolver.map(([a,b])=>[read(a),read(b)])); return item=>{if(!map.has(item))throw new KeyNotFoundException();return map.get(item);}; }
    if ('copy' in value) return Copy(read(value.copy));
    if ('utf16' in value) return value.utf16.map(n=>String.fromCharCode(n)).join('');
    if ('ref' in value) { if(!values.has(value.ref)) throw new Error('Missing scenario reference: '+value.ref); return values.get(value.ref); }
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
  function typeName(name) {
    if (name.endsWith('&')) return 'out ' + typeName(name.slice(0,-1));
    if (name.endsWith('[]')) return typeName(name.slice(0,-2)) + '[]';
    if (name.startsWith('IEnumerable<')) return 'System.Collections.Generic.IEnumerable<' + typeName(name.slice(12,-1)) + '>';
    const simple={Double:'double',Int32:'int',Int16:'short',Byte:'byte',Boolean:'bool',String:'string',Object:'object',IFormatProvider:'System.IFormatProvider',Color:'System.Drawing.Color'};
    if (simple[name]) return simple[name];
    if(name.startsWith('netDxf.')) return name;
    return 'netDxf.'+name;
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
            if('replace' in step)e.NewValue=read(step.replace);
            if('cancel' in step)e.Cancel=step.cancel;
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
        case 'new': result = construct(type,args,step.signature); break;
        case 'get': result = (target ?? type)[step.member]; break;
        case 'set': (target ?? type)[step.member] = read(step.value); break;
        case 'index': result = target.get_Item(...args); break;
        case 'set-index': target.set_Item(...args, read(step.value)); break;
        case 'snapshot': result = target; break;
        case 'call': {
          if(type===api.DxfClassCollection&&['Contains','Remove'].includes(step.member)&&step.signature?.[0]==='DxfClass')
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
