// Complete detached fit cases from the pinned source; wire and INSERT cases remain unregistered.
import {Spline,Vector3,Matrix3} from '../../index.js';
import {Run,Check,Equal,Near} from './TestHarness.js';
export const SplineTangentMatrices=[Matrix3.Identity,new Matrix3(0,-1,0,1,0,0,0,0,1),new Matrix3(2,0,0,0,3,0,0,0,-4),new Matrix3(1,2,0,0,1,3,4,0,1),new Matrix3(0,0,0,0,0,0,0,0,0)];
export function RegisterSplineTangentTransformTests(){for(let mask=0;mask<4;mask++)Run(`spline/tangent-transform/fit/${mask}`,()=>SplineFitTangentTransform(mask));}
export function NewTangentSpline(mask,fitted=false){const points=[new Vector3(1,2,3),new Vector3(4,6,9),new Vector3(8,5,2)],s=fitted?new Spline(points):new Spline(points,[1,.75,1],2);if(mask&1)s.StartTangent=new Vector3(2,-3,5);if(mask&2)s.EndTangent=new Vector3(-7,11,13);return s;}
export function AssertSplineTangent(expected,actual,name){Equal(expected!==null,actual!==null,name+' presence');if(expected!==null)for(const key of ['X','Y','Z'])Near(expected[key],actual[key],name+' '+key);}
export function SplineFitTangentTransform(mask){
 const s=NewTangentSpline(mask,true);let points=Array.from(s.FitPoints),start=s.StartTangent,end=s.EndTangent;const translation=new Vector3(101,-203,307);
 for(const matrix of SplineTangentMatrices.slice(0,4)){
  s.TransformBy(matrix,translation);start=start!==null?Matrix3.Multiply(matrix,start):null;end=end!==null?Matrix3.Multiply(matrix,end):null;
  points=points.map(p=>Vector3.Add(Matrix3.Multiply(matrix,p),translation));AssertSplineTangent(start,s.StartTangent,'Fit start');AssertSplineTangent(end,s.EndTangent,'Fit end');
  Check(points.length===s.FitPoints.length&&points.every((p,i)=>p.Equals(s.FitPoints[i])),'Fit-point transform changed.');
 }
}
