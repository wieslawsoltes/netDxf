// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Matrix3 } from '../netDxf/Matrix3.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { MathHelper } from '../netDxf/MathHelper.js';
import { TransformedNormal } from './EntityGeometry.js';
/** Conversion appearance differs from Clone: visibility, XData and proxy payload are not copied. */
export function CopyCurveAppearance(source,target) {
  target.Layer=source.Layer.Clone();target.Linetype=source.Linetype.Clone();target.Color=source.Color.Clone();
  target.Lineweight=source.Lineweight;target.Transparency=source.Transparency.Clone();target.LinetypeScale=source.LinetypeScale;target.Normal=source.Normal;
  return target;
}
export function CurveTransformFrame(source,matrix,translation){
  const center=Vector3.Add(Matrix3.Multiply(matrix,source.Center),translation),normal=TransformedNormal(matrix,source.Normal);
  const ow=MathHelper.ArbitraryAxis(source.Normal),wo=MathHelper.ArbitraryAxis(normal).Transpose();
  // Keep three separate products: precomposing matrices changes last-bit results.
  const direction=p=>Matrix3.Multiply(wo,Matrix3.Multiply(matrix,Matrix3.Multiply(ow,p)));
  return {center,normal,direction};
}
