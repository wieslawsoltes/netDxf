import { Copy } from './GeometryRuntime.js';
import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from './Errors.js';
export function FiniteView(value, parameter = 'value') {
  const entries = value instanceof Vector3 ? [value.X,value.Y,value.Z] : value instanceof Vector2 ? [value.X,value.Y] : [value];
  for (const n of entries) if (!Number.isFinite(n)) throw new ArgumentOutOfRangeException(parameter, n);
  return Copy(value);
}
export function PositiveView(value, parameter = 'value') {
  FiniteView(value,parameter); if (value <= 0) throw new ArgumentOutOfRangeException(parameter,value); return value;
}
export function NonzeroView(value, parameter = 'value') {
  FiniteView(value,parameter);
  if (value.X === 0 && value.Y === 0 && value.Z === 0) throw new ArgumentException('The vector must not be zero.','value');
  return Copy(value);
}
export const ViewRange = (low,high) => value => RequireInteger(value,low,high);
/** Internal descriptor helper. Each instance has fresh copied defaults and guarded setters. */
export function InstallViewFields(Type, fields) {
  const records = new WeakMap();
  const state = item => {
    if (!records.has(item)) records.set(item,Object.fromEntries(Object.entries(fields).map(([name,[initial]])=>[name,typeof initial === 'function' ? initial() : Copy(initial)])));
    return records.get(item);
  };
  for (const [name,[initial,validate]] of Object.entries(fields)) Object.defineProperty(Type.prototype,name,{
    get() { return Copy(state(this)[name]); }, set(value) { const checked = validate ? validate(value) : Copy(value); state(this)[name] = checked; }
  });
}
