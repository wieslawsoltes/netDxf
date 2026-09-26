import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { Matrix3 } from '../netDxf/Matrix3.js';
import { Matrix4 } from '../netDxf/Matrix4.js';
import { Copy } from './GeometryRuntime.js';
import { ArgumentException } from './Errors.js';

/** JavaScript overload dispatch; all geometry is provided by the mirrored core. */
export function EntityTransform(args) {
  if (args.length === 1 && args[0] instanceof Matrix4) {
    const m = args[0];
    return [new Matrix3(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33), new Vector3(m.M14, m.M24, m.M34)];
  }
  if (args.length === 2 && args[0] instanceof Matrix3 && args[1] instanceof Vector3) return args;
  throw new ArgumentException('TransformBy requires Matrix4 or Matrix3 and Vector3.');
}
export function EntityVector3(value) {
  if (value instanceof Vector3) return Copy(value);
  if (value instanceof Vector2) return new Vector3(value.X, value.Y, 0);
  throw new ArgumentException('Expected Vector2 or Vector3.');
}
export function EntityVectorGroup(args, count, dimension = 3) {
  const type = args[0] instanceof Vector2 ? Vector2 : Vector3;
  if (args.length !== count || !args.every(v => v instanceof type) || (dimension === 2 && type !== Vector2))
    throw new ArgumentException('No matching entity constructor.');
  return args.map(dimension === 2 ? Copy : EntityVector3);
}
export function TransformedNormal(transformation, normal) {
  const result = Matrix3.Multiply(transformation, normal);
  return Vector3.Equals(Vector3.Zero, result) ? normal : result;
}
export const TransformPoint = (matrix, point, translation) => Vector3.Add(Matrix3.Multiply(matrix, point), translation);
/** Public IReadOnlyList surface over the entity's independent live reactor collection. */
export function ReadOnlyReferenceList(list) {
  return Object.freeze({
    get Count() { return list.Count; }, get length() { return list.Count; },
    get_Item(index) { return list.get_Item(index); },
    GetEnumerator() { return list.GetEnumerator(); },
    [Symbol.iterator]() { return list[Symbol.iterator](); }
  });
}
