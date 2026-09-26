// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {HatchPattern} from './HatchPattern.js';
import {HatchPatternLineDefinition} from './HatchPatternLineDefinition.js';
import {HatchFillType} from './HatchFillType.js';
import {HatchType} from './HatchType.js';
import {Vector2 as V} from '../Vector2.js';
import {Vector3} from '../Vector3.js';
import {Matrix3} from '../Matrix3.js';
import {MathHelper} from '../MathHelper.js';
import {DotNetMath as M,MultiplyDouble as mul} from '../../runtime/GeometryRuntime.js';
import {RequireAffineFinite as finite,AffineHypot as hypot,AffinePolarDirection as polar} from '../../runtime/HatchAffine.js';
import {ArgumentException,NotSupportedException} from '../../runtime/Errors.js';
export const HasExplicitAffinePattern=h=>h.Pattern.Fill===HatchFillType.PatternFill&&(h.Pattern.Type===HatchType.Predefined||h.Pattern.Type===HatchType.Custom);
function near(expected,actual){finite(expected);finite(actual);const scale=M.Max(1,M.Max(M.Abs(expected.X),M.Abs(expected.Y)));if(M.Abs(expected.X-actual.X)>mul(1e-10,scale)||M.Abs(expected.Y-actual.Y)>mul(1e-10,scale))throw new ArgumentException('Transformed pattern geometry cannot be represented within the source tolerance.');}
const rotate=(v,angle)=>{const d=polar(angle);return new V(mul(d.X,v.X)-mul(d.Y,v.Y),mul(d.Y,v.X)+mul(d.X,v.Y));};
export function TransformAffinePattern(h,transformation,translation,map,scale,angle,identity){
  const original=h.Pattern;
  if(!identity&&original.constructor!==HatchPattern)throw new NotSupportedException('Unknown HATCH pattern subclass.');
  if(!identity&&original.LineDefinitions.Count===0)throw new NotSupportedException('Explicit line definitions are required.');
  if(original.LineDefinitions.Count>32767)throw new ArgumentException('Pattern line count cannot be stored.');
  finite(original.Origin);const origin=Vector3.Add(Matrix3.Multiply(transformation,new Vector3(original.Origin.X,original.Origin.Y,0)),translation);finite(origin);
  const termX=mul(transformation.M31,original.Origin.X),termY=mul(transformation.M32,original.Origin.Y);finite(termX);finite(termY);
  const roundoff=mul(8,2.2204460492503131e-16),tolerance=mul(roundoff,M.Abs(termX))+mul(roundoff,M.Abs(termY))+mul(roundoff,M.Abs(translation.Z));
  if(M.Abs(origin.Z)>tolerance)throw new NotSupportedException('The WCS pattern origin cannot be stored as Point2d.');
  const result=Object.assign(new HatchPattern(original.Name,original.Description),{Style:original.Style,Fill:original.Fill,Type:original.Type,IsDouble:original.IsDouble,Origin:new V(origin.X,origin.Y),Angle:angle,Scale:scale});
  for(const line of original.LineDefinitions){
    if(line==null)throw new ArgumentException('Pattern lines must be supplied.');finite(line.Angle);finite(line.Origin);finite(line.Delta);
    if(line.DashPattern.Count>32767)throw new ArgumentException('Dash count cannot be stored.');
    const oldAngle=original.Angle+line.Angle,oldBase=V.Multiply(original.Scale,rotate(line.Origin,original.Angle)),oldOffset=V.Multiply(original.Scale,rotate(line.Delta,oldAngle));
    const direction=map(polar(oldAngle),true),stretch=hypot(direction.X,direction.Y);finite(stretch);if(stretch===0)throw new ArgumentException('A pattern direction collapses.');
    const newAngle=mul(M.Atan2(direction.Y,direction.X),MathHelper.RadToDeg),newBase=map(oldBase,false),newOffset=map(oldOffset,true);
    const copy=Object.assign(new HatchPatternLineDefinition(),{Angle:newAngle-angle,Origin:V.Divide(rotate(newBase,-angle),scale),Delta:V.Divide(rotate(newOffset,-newAngle),scale)});
    finite(copy.Angle);finite(copy.Origin);finite(copy.Delta);if(line.Delta.Y!==0&&copy.Delta.Y===0)throw new ArgumentException('Pattern spacing cannot be represented.');
    const encodedAngle=result.Angle+copy.Angle;near(V.Divide(direction,stretch),polar(encodedAngle));near(newBase,V.Multiply(result.Scale,rotate(copy.Origin,result.Angle)));near(newOffset,V.Multiply(result.Scale,rotate(copy.Delta,encodedAngle)));
    for(const dash of line.DashPattern){
      finite(dash);const expected=mul(mul(original.Scale,dash),stretch);finite(expected);const value=dash===0?dash:expected/result.Scale;finite(value);const encoded=mul(value,result.Scale);finite(encoded);
      if(dash!==0&&(expected===0||value===0||encoded===0))throw new ArgumentException('A dash would become a dot.');
      if(M.Abs(expected-encoded)>mul(1e-10,M.Max(1,M.Abs(expected))))throw new ArgumentException('Dash length cannot be represented.');copy.DashPattern.Add(value);
    }result.LineDefinitions.Add(copy);
  }return result;
}
