// Complete two detached cases; all original wire and nested INSERT cases remain unregistered.
import {Spline,Vector3} from '../../index.js';
import {InvalidOperationException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Throws} from './TestHarness.js';
export function RegisterSplineReversalTests(){Run('spline/reverse/invalid-knots-do-not-mutate',SplineReverseInvalid);Run('spline/reverse/large-domain-finite',SplineReverseLarge);}
export function NewReversalSpline(degree,form,domain){
 const controls=Array.from({length:7},(_,i)=>new Vector3(i*1.5,(i*i+1)%7,i%3));if(form===1)controls[6]=controls[0];
 const weights=[1,.5,3,1.25,.75,2,1],count=controls.length+degree+1+(form===2?degree:0),knots=Array(count).fill(0);
 if(form===2){for(let i=1;i<count;i++)knots[i]=knots[i-1]+(i%3+1)*.125;}else{for(let i=degree+1;i<controls.length;i++)knots[i]=(i-degree)*(i-degree)*.0625;for(let i=controls.length;i<count;i++)knots[i]=2;}
 const offset=domain===1?-7:domain===2?17:0,scale=domain===2?4:1;const s=new Spline(controls,weights,knots.map(k=>k*scale+offset),degree,form===2);
 Object.assign(s,{StartTangent:new Vector3(2,3,4),EndTangent:new Vector3(-5,6,7),KnotTolerance:1e-8,CtrlPointTolerance:2e-8,FitTolerance:3e-9,IsVisible:false});return s;
}
export function SplineReverseInvalid(){for(const invalid of [NaN,Infinity,-Infinity,-100]){const s=NewReversalSpline(3,0,0);s.Knots[5]=invalid;const controls=s.ControlPoints.Clone(),weights=s.Weights.Clone(),start=s.StartTangent,end=s.EndTangent;
 Throws(InvalidOperationException,()=>s.Reverse());Check(controls.every((v,i)=>v.Equals(s.ControlPoints[i]))&&weights.every((v,i)=>v===s.Weights[i]),'Failed reversal partially mutated geometry');Equal(start,s.StartTangent,'Failed reversal changed start tangent');Equal(end,s.EndTangent,'Failed reversal changed end tangent');}}
export function SplineReverseLarge(){const s=NewReversalSpline(2,0,0);for(let i=0;i<s.Knots.length;i++)s.Knots[i]=1e308+s.Knots[i]*1e307;const a=s.Knots[2],b=s.Knots[s.Knots.length-3];s.Reverse();Check(s.Knots.every(Number.isFinite),'Intermediate a+b overflowed despite finite reflected knots');Equal(a,s.Knots[2],'Large domain start');Equal(b,s.Knots[s.Knots.length-3],'Large domain end');}
