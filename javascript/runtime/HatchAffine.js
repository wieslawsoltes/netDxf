// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {Vector2} from '../netDxf/Vector2.js';
import {Vector3} from '../netDxf/Vector3.js';
import {MathHelper} from '../netDxf/MathHelper.js';
import {DotNetMath as M,MultiplyDouble as mul} from './GeometryRuntime.js';
import {ArgumentException} from './Errors.js';
export function RequireAffineFinite(value){
  if(value instanceof Vector3){RequireAffineFinite(value.X);RequireAffineFinite(value.Y);RequireAffineFinite(value.Z);return;}
  if(value instanceof Vector2){RequireAffineFinite(value.X);RequireAffineFinite(value.Y);return;}
  if(!Number.isFinite(value))throw new ArgumentException('HATCH transforms require finite input and representable results.');
}
export function AffineUnit(value){
  const largest=M.Max(M.Abs(value.X),M.Max(M.Abs(value.Y),M.Abs(value.Z)));
  if(largest===0)throw new ArgumentException('The HATCH plane collapses.');RequireAffineFinite(largest);
  value=Vector3.Divide(value,largest);return Vector3.Divide(value,value.Modulus());
}
export function AffineHypot(x,y){const largest=M.Max(M.Abs(x),M.Abs(y));RequireAffineFinite(largest);if(largest===0)return 0;x/=largest;y/=largest;return mul(largest,M.Sqrt(mul(x,x)+mul(y,y)));}
export const AffineCross=(a,b)=>mul(a.X,b.Y)-mul(a.Y,b.X);
export function AffineAngle(angle){angle%=360;return angle<0?angle+360:angle;}
export function AffinePolarDirection(angle){
  angle=AffineAngle(angle);if(angle===0)return Vector2.UnitX;if(angle===90)return Vector2.UnitY;if(angle===180)return Vector2.Negate(Vector2.UnitX);if(angle===270)return Vector2.Negate(Vector2.UnitY);
  angle=mul(angle,MathHelper.DegToRad);return new Vector2(M.Cos(angle),M.Sin(angle));
}
