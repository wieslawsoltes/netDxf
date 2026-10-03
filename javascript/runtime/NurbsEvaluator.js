// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// The source-ordered evaluator from Entities/Spline.cs, shared by curve models.
import { Vector3 } from '../netDxf/Vector3.js';
import { PeriodicSplineData } from '../netDxf/Entities/PeriodicSplineData.js';
import { PeriodicSplineExactEvaluation } from '../netDxf/Entities/PeriodicSplineExactEvaluation.js';
import { DotNetMath as M, MultiplyDouble as mul, Copy } from './GeometryRuntime.js';
import { ValueList } from './ValueList.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, IndexOutOfRangeException } from './Errors.js';
const bits=new DataView(new ArrayBuffer(8));
const at=(a,i)=>{if(i<0||i>=a.length)throw new IndexOutOfRangeException();return a[i];};
export function CreateKnotVector(count,degree,periodic){
  const n=count+(periodic?2*degree:degree)+1,knots=Array(n).fill(0);
  if(periodic){const f=1/(count-degree);for(let i=0;i<n;i++)knots[i]=mul(i-degree,f);}
  else {let i=0;for(;i<=degree;i++){if(i>=n)throw new IndexOutOfRangeException();knots[i]=0;}for(;i<count;i++)knots[i]=i-degree;for(;i<n;i++)knots[i]=count-degree;}
  return knots;
}
function N(knots,i,p,u){
  if(p<=0)return at(knots,i)<=u&&u<at(knots,i+1)?1:0;
  let left=0,right=0;
  if(!(Math.abs(at(knots,i+p)-at(knots,i))<Number.MIN_VALUE))left=(u-at(knots,i))/(at(knots,i+p)-at(knots,i));
  if(!(Math.abs(at(knots,i+p+1)-at(knots,i+1))<Number.MIN_VALUE))right=(at(knots,i+p+1)-u)/(at(knots,i+p+1)-at(knots,i+1));
  return mul(left,N(knots,i,p-1,u))+mul(right,N(knots,i+1,p-1,u));
}
function mantissa(value){
  bits.setFloat64(0,value);let raw=bits.getBigUint64(0),field=Number((raw>>52n)&2047n),adjustment=0;
  if(field===0){value=mul(value,18014398509481984);bits.setFloat64(0,value);raw=bits.getBigUint64(0);field=Number((raw>>52n)&2047n);adjustment=-54;}
  bits.setBigUint64(0,(raw&0x800fffffffffffffn)|0x3ff0000000000000n);return [bits.getFloat64(0),field-1023+adjustment];
}
function scale(value,exponent){
  if(value===0||exponent===0)return value;const [m,e]=mantissa(value),combined=e+exponent;bits.setFloat64(0,m);const raw=bits.getBigUint64(0);
  if(combined>1023)return value>0?Infinity:-Infinity;
  if(combined<-1075){bits.setBigUint64(0,raw&(1n<<63n));return bits.getFloat64(0);}
  if(combined>=-1022){bits.setBigUint64(0,(raw&0x800fffffffffffffn)|(BigInt(combined+1023)<<52n));return bits.getFloat64(0);}
  return mul(mul(m,M.Pow(2,combined+1074)),Number.MIN_VALUE);
}
function scaledSum(terms,powers){
  // degree <= 10: .NET Array.Sort uses its insertion-sort path, preserving equal keys.
  const pairs=powers.map((p,i)=>[p,terms[i]]).sort((a,b)=>a[0]-b[0]);
  pairs.forEach(([p,t],i)=>{powers[i]=p;terms[i]=t;});
  let sum=0,correction=0,exponent=0;
  for(let i=terms.length-1;i>=0;i--){if(terms[i]===0)continue;if(sum===0&&correction===0)exponent=powers[i];
    const v=scale(terms[i],powers[i]-exponent),next=sum+v;correction+=Math.abs(sum)>=Math.abs(v)?(sum-next)+v:(v-next)+sum;sum=next;
  }return [sum+correction,exponent];
}
function periodicPoint(controls,weights,knots,degree,u){
  let low=degree,high=controls.length;while(low+1<high){const mid=low+Math.trunc((high-low)/2);if(u<knots[mid])high=mid;else low=mid;}
  const first=low-degree,coeff=Array(degree+1).fill(0),exponents=Array(degree+1).fill(0),exact=()=>PeriodicSplineExactEvaluation.Evaluate(controls,weights,knots,degree,u,first);
  for(let i=0;i<=degree;i++){
    const value=N(knots,first+i,degree,u);
    if(!Number.isFinite(value)||value<0||(value>0&&value<2.2250738585072014e-308)||(value===0&&(i<degree||u>knots[low])))return exact();
    if(value===0)continue;const [a,ae]=mantissa(value),[b,be]=mantissa(weights[first+i]);coeff[i]=mul(a,b);exponents[i]=ae+be;
  }
  const terms=coeff.slice(),powers=exponents.slice(),[den,de]=scaledSum(terms,powers);if(den<=0)throw new ArgumentException('Periodic denominator is not representable.');
  let exactPoint=null;
  const coordinate=axis=>{
    if(exactPoint!==null)return exactPoint[axis];let min=Number.MAX_VALUE,max=-Number.MAX_VALUE,largest=-2147483648;
    for(let i=0;i<coeff.length;i++){
      terms[i]=0;powers[i]=0;if(coeff[i]===0)continue;const v=controls[first+i][axis];min=M.Min(min,v);max=M.Max(max,v);if(v===0)continue;
      const [m,e]=mantissa(v);terms[i]=mul(coeff[i],m);powers[i]=exponents[i]+e;largest=Math.max(largest,powers[i]);
    }
    if(min===max)return min;const [num,ne]=scaledSum(terms,powers);
    if(min<0&&max>0){const resultPower=num===0?-2147483648:ne+mantissa(num)[1];if(num===0||largest-resultPower>=4){exactPoint=exact();return exactPoint[axis];}}
    let result=scale(num/den,ne-de);result=M.Max(min,M.Min(max,result));PeriodicSplineData.Finite(result);return result;
  };
  return new Vector3(coordinate('X'),coordinate('Y'),coordinate('Z'));
}
function point(controls,weights,knots,degree,u,periodic){
  if(periodic)return periodicPoint(controls,weights,knots,degree,u);
  let sum=Vector3.Zero,den=0;
  for(let i=0;i<controls.length;i++){const n=N(knots,i,degree,u);den+=mul(n,weights[i]);sum=Vector3.Add(sum,Vector3.Multiply(mul(weights[i],n),controls[i]));}
  return Math.abs(den)<Number.MIN_VALUE?Vector3.Zero:Vector3.Multiply(1/den,sum);
}
export function NurbsEvaluator(controls,weights,knots,degree,closed,periodic,precision){
  if(precision<2)throw new ArgumentOutOfRangeException('precision',precision);
  if(controls==null)throw new ArgumentNullException('controls');const count=controls.length;
  if(count===0)throw new ArgumentException('A spline with control points is required.','controls');
  if(weights==null)weights=Array(count).fill(1);else if(weights.length!==count)throw new ArgumentException('Control and weight counts differ.','weights');
  if(knots==null)knots=CreateKnotVector(count,degree,periodic);
  else if(knots.length!==count+(periodic?2*degree:degree)+1)throw new ArgumentException('Invalid number of knots.');
  if(periodic)PeriodicSplineData.Validate(controls,weights,knots,degree);
  const ctrl=periodic?[...controls.slice(count-degree),...controls]:controls,w=periodic?[...weights.slice(count-degree),...weights]:weights;
  let start,end;if(periodic){start=knots[degree];end=knots[knots.length-degree-1];}
  else {start=knots[0];end=knots[knots.length-1];if(!closed)precision--;}
  const delta=(end-start)/precision;
  if(periodic&&(delta<=0||start+delta<=start||end-delta>=end))throw new ArgumentException('Periodic sampling parameters are not representable.');
  const result=new ValueList();let previous=start;
  for(let i=0;i<precision;i++){
    const u=start+mul(delta,i);if(periodic&&((i>0&&u<=previous)||u>=end))throw new ArgumentException('Periodic sampling parameters are not representable.');
    previous=u;result.Add(point(ctrl,w,knots,degree,u,periodic));
  }
  if(!(closed||periodic))result.Add(Copy(ctrl[ctrl.length-1]));return result;
}
