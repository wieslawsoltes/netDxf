import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { Matrix3 } from '../netDxf/Matrix3.js';
import { MathHelper } from '../netDxf/MathHelper.js';
import { CoordinateSystem } from '../netDxf/CoordinateSystem.js';
/** Common change-of-basis operation, preserving the three separate matrix products. */
export function TransformTextAxes(normal, newNormal, rotation, u, v, transformation) {
  const ow = MathHelper.ArbitraryAxis(normal), wo = MathHelper.ArbitraryAxis(newNormal).Transpose();
  const uv = MathHelper.Transform([u, v], rotation, CoordinateSystem.Object, CoordinateSystem.World);
  const transform = point => {
    let result = Matrix3.Multiply(ow, new Vector3(point.X, point.Y, 0));
    result = Matrix3.Multiply(transformation, result);
    result = Matrix3.Multiply(wo, result);
    return new Vector2(result.X, result.Y);
  };
  return { uv, u: transform(uv[0]), v: transform(uv[1]) };
}
