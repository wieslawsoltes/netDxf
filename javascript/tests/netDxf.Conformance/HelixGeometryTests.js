// Complete detached original authoring and invalid-input cases. Typed wire/ownership cases stay unregistered.
import {Helix,Vector3,Matrix3} from '../../index.js';
import {ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Near,Throws,BooleanName} from './TestHarness.js';
const nearVector=(expected,actual,label)=>{for(const key of ['X','Y','Z'])Near(expected[key],actual[key],label+' '+key);};
export function RegisterHelixGeometryTests(){
  for(let shape=0;shape<6;shape++)for(const right of [false,true])for(let pose=0;pose<3;pose++)Run(`helix/authoring/${shape}/${BooleanName(right)}/${pose}`,()=>HelixAuthoring(shape,right,pose));
  Run('helix/authoring/budgets-and-invalid',HelixAuthoringInvalid);
}
export function AuthoredHelix(shape,right,pose,tolerance=1e-5){
  const matrix=pose===0?Matrix3.Identity:Matrix3.Multiply(Matrix3.RotationY(.45),Matrix3.RotationX(-.3)),translation=pose===2?new Vector3(100,-200,30):Vector3.Zero;
  const initial=shape===4?0:5,final=shape===0?5:shape===5?0:2,pitch=shape===2?0:shape===3?-1.5:1.5;
  return Helix.Create(translation,Vector3.Add(Matrix3.Multiply(matrix,new Vector3(initial,0,0)),translation),Matrix3.Multiply(matrix,new Vector3(0,0,3)),final,2.25,pitch,right,tolerance);
}
export function CubicSample(helix,parameter){
  const segments=helix.ControlPoints.length/4,span=Math.min(segments-1,Math.trunc(parameter*segments)),t=parameter*segments-span,c=1-t,i=span*4,p=helix.ControlPoints;
  return Vector3.Add(Vector3.Add(Vector3.Add(Vector3.Multiply(c*c*c,p[i]),Vector3.Multiply(3*c*c*t,p[i+1])),Vector3.Multiply(3*c*t*t,p[i+2])),Vector3.Multiply(t*t*t,p[i+3]));
}
export function HelixAuthoring(shape,right,pose){
  const h=AuthoredHelix(shape,right,pose),segments=h.ControlPoints.length/4;
  Check(segments>=9&&segments<=65536,'Authoring segment budget');Equal(4*(segments+1),h.Knots.length,'Authored cubic knot count');
  const bound=h.GetApproximationErrorBound(segments);Check(bound>0&&bound<=1e-5,'Selected approximation bound exceeds tolerance');
  for(let i=0;i<h.Knots.length;i++)Near(Math.trunc(i/4)/segments,h.Knots[i],'Hermite span knots');
  for(let i=0;i<=250;i++){
    const t=i/250,expected=h.EvaluateDefinition(t),actual=CubicSample(h,t);
    Check(Vector3.Subtract(expected,actual).Modulus()<=bound+1e-10,'Authored spline violates analytic error bound');
    if(i>0&&i<250){const delta=1e-6,derivative=Vector3.Divide(Vector3.Subtract(h.EvaluateDefinition(t+delta),h.EvaluateDefinition(t-delta)),2*delta);Check(Vector3.Subtract(derivative,h.EvaluateDefinitionDerivative(t)).Modulus()<2e-7,'Analytic derivative differs from independent centered difference');}
  }
  nearVector(h.StartPoint,h.EvaluateDefinition(0),'Analytic start');nearVector(h.EvaluateDefinitionDerivative(0),h.StartTangent,'Stored start tangent');nearVector(h.EvaluateDefinitionDerivative(1),h.EndTangent,'Stored end tangent');
  const axis=Vector3.Normalize(h.AxisVector),offset=Vector3.Subtract(h.EvaluateDefinition(1),h.AxisBasePoint);
  Near(h.Turns*h.TurnHeight,Vector3.DotProduct(axis,offset),'Analytic signed total height');Near(h.Radius,Vector3.Subtract(offset,Vector3.Multiply(axis,Vector3.DotProduct(axis,offset))).Modulus(),'Analytic terminal radius');
  const transformed=h.Clone(),reflection=Matrix3.Multiply(Matrix3.Multiply(Matrix3.Reflection(Vector3.UnitX),Matrix3.RotationZ(.5)),Matrix3.Scale(2)),move=new Vector3(7,8,9);
  transformed.TransformBy(reflection,move);
  for(const t of [0,.123,.875,1]){nearVector(Vector3.Add(Matrix3.Multiply(reflection,h.EvaluateDefinition(t)),move),transformed.EvaluateDefinition(t),'Transformed analytic definition');nearVector(Matrix3.Multiply(reflection,h.EvaluateDefinitionDerivative(t)),transformed.EvaluateDefinitionDerivative(t),'Transformed analytic derivative');}
}
export function HelixAuthoringInvalid(){
  for(const t of [-1,2,NaN,Infinity]){const h=AuthoredHelix(1,true,0);Throws(ArgumentOutOfRangeException,()=>h.EvaluateDefinition(t));Throws(ArgumentOutOfRangeException,()=>h.EvaluateDefinitionDerivative(t));}
  for(const tolerance of [0,-1,NaN,Infinity])Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,tolerance));
  for(const budget of [-1,0,1048577,2147483647])Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,1e-5,budget));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,100,1,true,1e-5,8));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,1e-20,8));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,new Vector3(1,0,1),Vector3.UnitZ,1,1,1));
  Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.Zero,1,1,1));
  Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,Number.MAX_VALUE,1));
  const h2=AuthoredHelix(0,true,0);h2.StartPoint=Vector3.Add(h2.StartPoint,Vector3.UnitZ);Throws(ArgumentException,()=>h2.WithRegeneratedSpline());Throws(ArgumentOutOfRangeException,()=>h2.GetApproximationErrorBound(0));
}
