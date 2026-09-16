import * as Types from '../geometry.js';
import { CopyValue, Culture } from '../runtime/GeometryRuntime.js';
import { fromBits, doubleBits } from './wire.mjs';
// The manifest selects exact C# overloads for tests; public name dispatch is tested separately.
export function createGeometryCaller(manifest) {
  const files = new Map(manifest.files.map(file => [file.source.split('/').at(-1).slice(0, -3), file]));
  function decode(value) {
    if (value == null || typeof value !== 'object') return value;
    if ('d' in value) return fromBits(value.d);
    if ('culture' in value) return value.culture;
    if (Array.isArray(value)) return value.map(decode);
    if ('type' in value) {
      const type = Types[value.type.split('.').at(-1)];
      const constructor = files.get(value.type.split('.').at(-1))?.members.find(m=>m.kind==='constructor'&&m.signature===value.signature);
      const args = value.args.map((v,i) => v!=null && constructor?.parameters[i]?.type==='byte[]' ? Uint8Array.from(v) : decode(v));
      if (value.factory) return type[value.factory](...args);
      const instance = Object.hasOwn(type, 'CreateOverload') ? type.CreateOverload(value.signature, ...args) : new type(...args);
      for (const [key, v] of Object.entries(value.properties ?? {})) instance[key] = decode(v);
      if (value.normalize) instance.Normalize();
      return instance;
    }
    throw new Error('Unmapped geometry input.');
  }
  const isIntegerType = t => /^(byte|short|int|long|int\?|short\?)$/.test(t ?? '') || (t?.startsWith('netDxf.') && !files.has(t.split('.').at(-1)));
  function encode(value, hint = null) {
    if (value == null) return null;
    if (typeof value === 'string' || typeof value === 'boolean') return value;
    if (typeof value === 'number') return hint === 'integer' ? value : { d: doubleBits(value) };
    if (Array.isArray(value) || ArrayBuffer.isView(value)) return Array.from(value, item => encode(item, hint));
    const name = value.constructor.name;
    if (name === 'Color') return { type: name, properties: Object.fromEntries(['A', 'B', 'G', 'R'].map(k => [k, value[k]])) };
    const properties = {};
    if (name === 'Tuple') {
      for (const key of Object.keys(value).sort()) properties[key] = encode(value[key]);
      return { type: `Tuple\`${Object.keys(value).length}`, properties };
    }
    const fields = new Set();
    for (let t = Object.getPrototypeOf(value); t && t !== Object.prototype; t = Object.getPrototypeOf(t))
      for (const [key, descriptor] of Object.entries(Object.getOwnPropertyDescriptors(t)))
        if (descriptor.get && key !== 'HasValueEdit') fields.add(key);
    for (const key of [...fields].sort()) {
      const propType = files.get(name)?.members.find(m => m.kind === 'property' && m.name === key)?.returnType;
      const integer = propType?.includes('byte[]') || isIntegerType(propType) || (!propType && ['Degree', 'Type'].includes(key));
      properties[key] = encode(value[key], integer ? 'integer' : null);
    }
    return { type: name, properties };
  }
  const integralReturns = new Set(['Sign', 'PointInSegment', 'GetHashCode', 'RgbToAci', 'ToTrueColor', 'ToAlphaValue']);
  const splitSignature = signature => {
    let depth = 0, start = 0, result = [];
    for (let i = 0; i < signature.length; i++) {
      if (signature[i] === '<') depth++;
      if (signature[i] === '>') depth--;
      if (signature[i] === ',' && depth === 0) { result.push(signature.slice(start, i)); start = i + 1; }
    }
    if (signature.length) result.push(signature.slice(start));
    return result;
  };
  return function geometry(input) {
    const originalEpsilon = Types.MathHelper.Epsilon, originalCulture = Culture.Current;
    try {
      Culture.Current = input.culture ?? '';
      if ('epsilon' in input) Types.MathHelper.Epsilon = input.epsilon;
      return input.requests.map(request => {
        try {
          const name = request.type.split('.').at(-1), type = Types[name];
          const instance = request.instance ? decode(request.instance) : null;
          let result, args = [], parameterTypes = [], returnHint = null;
          if (request.action === 'construct') result = decode(request.value);
          else if (request.action === 'get' || request.action === 'set') {
            const property = files.get(name)?.members.find(m => m.kind === 'property' && m.name === request.member);
            returnHint = isIntegerType(property?.returnType) || property?.returnType.includes('byte[]') ? 'integer' : null;
            const target = instance ?? type, indexes = request.indexes?.map(decode) ?? [];
            if (request.action === 'set') {
              if (request.member === 'Item') target.set_Item(...indexes, decode(request.value));
              else target[request.member] = decode(request.value);
            }
            result = request.member === 'Item' ? target.get_Item(...indexes) : target[request.member];
          } else if (request.action === 'call') {
            const method = files.get(name).members.find(m => m.name === request.member && m.signature === request.signature && m.isStatic === request.static);
            if (!method) throw new Error(`Missing manifest method ${name}.${request.member}(${request.signature})`);
            returnHint = isIntegerType(method.returnType) ? 'integer' : null;
            parameterTypes = splitSignature(request.signature);
            args = request.args.map((v, i) => parameterTypes[i]?.startsWith('out ') || parameterTypes[i]?.startsWith('ref ') ? { value: decode(v) } : decode(v));
            result = (request.static ? type : instance)[method.implementation](...args);
          } else throw new Error('Unknown geometry action.');
          return { ok: true, result: encode(result, returnHint), instance: encode(instance),
            arguments: args.map((value, i) => {
              let t = parameterTypes[i] ?? '';
              if (/^(out|ref) /.test(t)) { value = value.value; t = t.replace(/^(out|ref) /, ''); }
              if (t === 'System.IFormatProvider' && value != null) return { culture: value };
              return encode(value, isIntegerType(t.replace(/\[\]$/, '')) ? 'integer' : null);
            }) };
        } catch (error) { return { ok: false, error: error.name, paramName: error.ParamName ?? null }; }
      });
    } finally { Types.MathHelper.Epsilon = originalEpsilon; Culture.Current = originalCulture; }
  };
}
