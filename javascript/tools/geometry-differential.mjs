import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { OracleClient } from './OracleClient.mjs';
import { javascriptRoot, baseline, configuration } from './dotnet.mjs';
import { runtimeFingerprint, verificationFingerprint } from './evidence.mjs';
import { doubleBits } from './wire.mjs';
import { createGeometryCaller } from './geometry-wire.mjs';
const D = d => ({ d: doubleBits(d) });
const V = (n, values, normalize = false) => ({ type: `netDxf.Vector${n}`, signature: Array(n).fill('double').join(','), args: values.map(D), normalize });
const M = (n, values) => ({ type: `netDxf.Matrix${n}`, signature: Array(n * n).fill('double').join(','), args: values.map(D) });
const points = n => Array.from({ length: n }, (_, i) => V(3, [i, i * i, i - 2]));
const instance = (name, variant = 0) => {
  if (/^Vector\d$/.test(name)) {
    const n = Number(name.at(-1));
    return V(n, Array.from({ length: n }, (_, i) => variant === 1 ? 0 : variant === 2 ? (i === 0 ? 1 : 0) : i + 1), variant === 2);
  }
  if (/^Matrix\d$/.test(name)) {
    const n = Number(name.at(-1));
    return M(n, Array.from({ length: n * n }, (_, i) => variant === 1 ? 0 : i % (n + 1) === 0 ? (variant === 2 ? 1 : 3) : (variant === 2 ? 0 : i % 3)));
  }
  if (/^BezierCurve(Cubic|Quadratic)$/.test(name)) return { type: `netDxf.${name}`, signature: 'System.Collections.Generic.IEnumerable<netDxf.Vector3>', args: [points(name.endsWith('Cubic') ? 4 : 3)] };
  if (name === 'BoundingRectangle') return { type: 'netDxf.BoundingRectangle', signature: 'netDxf.Vector2,netDxf.Vector2', args: [V(2, [-2, -3]), V(2, [4, 5])] };
  if (name === 'ClippingBoundary') return { type: 'netDxf.ClippingBoundary', signature: 'netDxf.Vector2,netDxf.Vector2', args: [V(2, [-2, -3]), V(2, [4, 5])] };
  if (name === 'Transparency') return { type: 'netDxf.Transparency', signature: 'short', args: [25] };
  if (name === 'AciColor') return { type: 'netDxf.AciColor', signature: 'byte,byte,byte', args: [121, 67, 200] };
  if (name === 'DxfClass') return { type: 'netDxf.DxfClass', signature: 'string,string,string', args: ['CLASS','CppClass','Application'] };
  if (name === 'UnitStyleFormat') return { type:'netDxf.Units.UnitStyleFormat',signature:'',args:[] };
  if (name === 'HeaderVariable') return { type:'netDxf.Header.HeaderVariable',signature:'string,short,object',args:['$CUSTOM',1,'value'] };
  if (name === 'MeshEdge') return { type:'netDxf.Entities.MeshEdge',signature:'int,int,double',args:[0,1,D(2.5)] };
  if (['ToleranceEntry','ToleranceValue','DatumReferenceValue','HatchPatternLineDefinition','MTextBackgroundFill','MTextParagraphOptions','MTextFormattingOptions'].includes(name)) return { type:'netDxf.Entities.'+name,signature:'',args:[] };
  throw new Error('Unmapped instance '+name);
};
function samples(type, name, variant) {
  if (type.startsWith('out ')) return null;
  if (type === 'double' || type === 'float') {
    const values = /^(threshold|t|saturation|lightness|hue)$/.test(name) ? [0, 0.5, 1, -1, 2, NaN, 0.25, 0.125] : [0, 0.5, 1, -1, 2, NaN, 1e-20, 123.125];
    return D(values[variant % values.length]);
  }
  if (['byte', 'short', 'int'].includes(type)) {
    const values = name === 'precision' ? [2, 3, 16, -1, 1, 5, 8, 32] : name === 'numDigits' ? [0, 1, 3, -1, 16, 15, 2, 5] : type === 'byte' ? [0, 1, 127, 255, 51, 88, 13, 200] : [0, 1, 3, -1, 16, 25, 90, 100];
    return values[variant % values.length];
  }
  if (type === 'bool') return variant % 2 === 0;
  if (type === 'string') return [null,'',' ','a\nb','$CUSTOM','東京','value','value'][variant%8];
  if (type === 'int?' || type === 'short?') return variant%3 ? variant : null;
  if (type === 'double?') return variant%3 ? samples('double',name,variant) : null;
  if (/^netDxf\.(?:Entities|Units)\./.test(type) && !['UnitStyleFormat','MeshEdge','ToleranceEntry','ToleranceValue','DatumReferenceValue','HatchPatternLineDefinition','MTextBackgroundFill','MTextParagraphOptions','MTextFormattingOptions'].includes(type.split('.').at(-1))) return variant%8;
  if (type === 'object') return variant % 2 ? null : 'unrelated';
  if (type === 'System.IFormatProvider') return variant % 3 ? { culture: variant % 2 ? 'pl-PL' : 'en-US' } : null;
  if (type === 'System.Drawing.Color') return { type, factory: 'FromArgb', args: [121, 67, 200] };
  if (type === 'netDxf.CoordinateSystem') return variant % 3;
  if (/^netDxf.Vector[234]$/.test(type)) {
    const n = Number(type.at(-1));
    if (variant === 5) return V(n, Array(n).fill(NaN));
    if (variant === 6) return V(n, Array(n).fill(-0));
    return instance(type.slice(7), variant % 3);
  }
  if (/^netDxf.Matrix[234]$/.test(type)) return instance(type.slice(7), variant % 3);
  if (type.startsWith('netDxf.')) return instance(type.split('.').at(-1));
  if (type === 'double[]') return variant === 3 ? null : Array.from({ length: variant % 4 + 2 }, (_, i) => D(i + 0.5));
  if (type === 'byte[]') return variant === 3 ? null : [50, 150, 250];
  const generic = /^System.Collections.Generic.IEnumerable<(.+)>$/.exec(type);
  if (generic) {
    if (variant === 3) return null;
    const count = variant === 1 ? 0 : variant === 2 ? 1 : 4;
    return Array.from({ length: count }, (_, i) => samples(generic[1], name, i));
  }
  throw new Error(`Missing sample ${type} ${name}`);
}
export function geometryCorpus(manifest) {
  const requests = [];
  for (const file of manifest.files) {
    const name = file.source.split('/').at(-1).slice(0, -3), full = file.source.slice(0,-3).split('/').join('.');
    for (const member of file.members) {
      if (member.accessibility !== 'Public' || member.isAbstract) continue;
      if (member.kind === 'field' && member.constant) { requests.push({id:`${name}/constant/${member.name}`,type:full,action:'get',member:member.name}); continue; }
      if (['Ordinary', 'UserDefinedOperator', 'Conversion'].includes(member.kind)) {
        for (let variant = 0; variant < 8; variant++) {
          const parameters = member.parameters;
          const args = parameters.map((p, i) => samples((p.refKind === 'Out' ? 'out ' : '') + p.type, p.name, /threshold|numDigits|precision|^t$/.test(p.name) ? variant : variant + i));
          requests.push({ id: `${name}/${member.name}/${member.signature}/${variant}`, type: full, action: 'call',
            static: member.isStatic, member: member.name, signature: member.signature, args,
            ...(!member.isStatic ? { instance: instance(name, variant % 3) } : {}) });
        }
      } else if (member.kind === 'property') {
        if (name === 'BezierCurve') continue;
        requests.push({ id: `${name}/get/${member.name}`, type: full, action: 'get', member: member.name, ...(!member.isStatic ? { instance: instance(name) } : {}) });
        if (member.canSet) for(let variant=0;variant<8;variant++) requests.push({id:`${name}/set/${member.name}/${variant}`,type:full,action:'set',member:member.name,value:samples(member.returnType,member.name,variant),...(!member.isStatic?{instance:instance(name)}:{})});
      } else if (member.kind === 'constructor' && name !== 'BezierCurve') {
        for (let variant = 0; variant < 8; variant++) {
          const args = member.parameters.map((p, i) => samples(p.type, p.name, variant + i));
          requests.push({ id: `${name}/constructor/${member.signature}/${variant}`, type: full, action: 'construct', value: { type: full, signature: member.signature, args } });
        }
      }
    }
    if (/^(Vector|Matrix)\d$/.test(name)) {
      const n = Number(name.at(-1));
      for (let row = -1; row <= n; row++) for (const column of name.startsWith('Matrix') ? [-1, 0, n - 1, n] : [0]) {
        const indexes = name.startsWith('Matrix') ? [row, column] : [row];
        for (const action of ['get', 'set']) requests.push({ id: `${name}/index/${action}/${indexes}`, type: full, instance: instance(name), action, member: 'Item', indexes, value: D(42.25) });
      }
    }
  }
  return requests;
}
export async function geometryDifferential() {
  const manifest = JSON.parse(fs.readFileSync(path.join(javascriptRoot, 'native-port-manifest.json')));
  const corpus = geometryCorpus(manifest), js = createGeometryCaller(manifest), oracle = new OracleClient();
  const output = path.join(javascriptRoot, 'artifacts/geometry-differential', configuration);
  fs.mkdirSync(output, { recursive: true });
  const proof = { runtimeFingerprint: runtimeFingerprint(), verificationFingerprint: verificationFingerprint() };
  const failures = [], results = [];
  let completed = false, fatal = null;
  try {
    for (let at = 0; at < corpus.length; at += 256) {
      const requests = corpus.slice(at, at + 256), input = { op: 'geometry', requests };
      const expected = await oracle.request(input);
      if (!expected.ok) throw new Error('Oracle geometry batch failed: ' + expected.error);
      const actual = js(input);
      for (let i = 0; i < requests.length; i++) {
        const same = isDeepStrictEqual(expected.value[i], actual[i]);
        results.push({ id: requests[i].id, passed: same });
        if (!same) failures.push({ request: requests[i], expected: expected.value[i], actual: actual[i] });
      }
    }
    completed = true;
  } catch (error) { fatal = error.stack; throw error; }
  finally {
    try { await oracle.close(); } catch (error) { fatal ??= error.stack; completed = false; }
    if (proof.runtimeFingerprint !== runtimeFingerprint() || proof.verificationFingerprint !== verificationFingerprint()) { completed = false; fatal = 'Code changed during geometry verification.'; }
    fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify({ ...proof, sourceRef: baseline.ref, completed, fatal, configuration,
      comparison: 'Exact public snapshots, exception types/parameter names and IEEE-754 bits. No numeric tolerance.',
      stats: { comparisons: results.length, failures: failures.length }, results, failures }, null, 2) + '\n');
  }
  console.log(`Geometry: ${results.length} comparisons; ${failures.length} mismatches.`);
  for (const f of failures.slice(0, 18)) console.error(f.request.id, JSON.stringify(f.expected).slice(0, 160), JSON.stringify(f.actual).slice(0, 160));
  if (!completed || fatal || failures.length) throw new Error('Geometry differential failed.');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await geometryDifferential();
