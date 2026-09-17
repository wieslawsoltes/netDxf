// Value/overload adapters shared by original-path entity mirrors.
import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { Matrix3 } from '../netDxf/Matrix3.js';
import { Copy } from './GeometryRuntime.js';
import { ArgumentException } from './Errors.js';
export function EntityVector3(value) {
  if (value instanceof Vector3) return Copy(value);
  if (value instanceof Vector2) return new Vector3(value.X,value.Y,0);
  throw new ArgumentException('Expected a Vector2 or Vector3.');
}
export function EntityVertices3(args, count) {
  if (args.length === 0) return Array.from({length:count},()=>Vector3.Zero);
  if (args.length !== count && !(count === 4 && args.length === 3)) throw new ArgumentException('No matching entity constructor.');
  const kind = args[0]?.constructor;
  if ((kind !== Vector2 && kind !== Vector3) || args.some(v=>v?.constructor!==kind)) throw new ArgumentException('Constructor vector dimensions must match.');
  const result=args.map(EntityVector3); if(result.length<count)result.push(Copy(result.at(-1))); return result;
}
export function TransformedNormal(matrix,normal) {
  const result=Matrix3.Multiply(matrix,normal);return Vector3.Equals(Vector3.Zero,result)?normal:result;
}
