// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see GTE/LICENSE.BSL-1.0.
import { Copy, CopyValue, MultiplyDouble as mul, RemainderDouble } from '../../runtime/GeometryRuntime.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ArgumentException, IndexOutOfRangeException, NullReferenceException, OverflowException } from '../../runtime/Errors.js';
const array = (n, value = () => 0) => {
  if (n < 0) throw new OverflowException();
  return FixedArray(Array.from({length:n}, value));
};
export class UniqueKnot {
  constructor(t=0, multiplicity=0) { this.T=t; this.Multiplicity=multiplicity; }
  [CopyValue]() { return new UniqueKnot(this.T,this.Multiplicity); }
}
export class BasisFunctionInput {
  constructor(numControls,degree) {
    this.NumControls=0; this.Degree=0; this.Uniform=false; this.Periodic=false; this.NumUniqueKnots=0; this.UniqueKnots=null;
    if (arguments.length===0) return;
    this.NumControls=numControls; this.Degree=degree; this.Uniform=true;
    this.NumUniqueKnots=numControls-degree+1;
    this.UniqueKnots=array(this.NumUniqueKnots,()=>new UniqueKnot());
    this.UniqueKnots[0]=new UniqueKnot(0,degree+1);
    for(let i=1;i<=this.NumUniqueKnots-2;i++) this.UniqueKnots[i]=new UniqueKnot(i/(this.NumUniqueKnots-1.0),1);
    this.UniqueKnots[this.UniqueKnots.length-1]=new UniqueKnot(1,degree+1);
  }
  [CopyValue]() { const copy=new BasisFunctionInput(); Object.assign(copy,this); return copy; }
}
/** Source-ordered local-support basis evaluation, with derivatives through order three.
 * Knots and unique knots are mutable arrays; cached domain and lookup keys intentionally
 * retain their source behavior after direct array edits. Debug.Assert has no browser UI.
 */
export class BasisFunction {
  #count; #degree; #min; #max; #length; #open; #uniform; #periodic; #unique; #knots; #keys; #jet;
  constructor(input) { this.Create(input); }
  Create(input) {
    this.#count=input.Periodic?input.NumControls+input.Degree:input.NumControls;
    this.#degree=input.Degree; this.#min=0; this.#max=0; this.#length=0; this.#open=false;
    this.#uniform=input.Uniform; this.#periodic=input.Periodic; this.#jet=new Array(4);
    if(input.UniqueKnots===null) throw new NullReferenceException();
    this.#unique=FixedArray(input.UniqueKnots,Copy);
    // Iterations retained for source exception ordering on malformed arrays.
    let u=this.#unique[0].T;
    for(let i=1;i<input.NumUniqueKnots-1;i++) u=this.#unique[i].T;
    const first=this.#unique[0].Multiplicity,last=this.#unique[this.#unique.length-1].Multiplicity;
    for(let i=1;i<=input.NumUniqueKnots-2;i++) void this.#unique[i].Multiplicity;
    this.#open=first===last&&first===this.#degree+1;
    this.#knots=array(this.#count+this.#degree+1); this.#keys=array(input.NumUniqueKnots,()=>null);
    let sum=0,j=0;
    for(let i=0;i<input.NumUniqueKnots;i++) {
      const t=this.#unique[i].T, mult=this.#unique[i].Multiplicity;
      for(let k=0;k<mult;k++,j++) this.#knots[j]=t;
      this.#keys[i]={value:t,index:sum-1}; sum+=mult;
    }
    this.#min=this.#knots[this.#degree]; this.#max=this.#knots[this.#count]; this.#length=this.#max-this.#min;
    for(let d=0;d<4;d++) this.#jet[d]=array(this.#degree+1,()=>array(this.#count+this.#degree));
  }
  get NumControls(){return this.#count;} get Degree(){return this.#degree;}
  get NumUniqueKnots(){return this.#unique.length;} get NumKnots(){return this.#knots.length;}
  get MinDomain(){return this.#min;} get MaxDomain(){return this.#max;}
  get IsOpen(){return this.#open;} get IsUniform(){return this.#uniform;} get IsPeriodic(){return this.#periodic;}
  get UniqueKnots(){return this.#unique;} get Knots(){return this.#knots;}
  #index(t) {
    if(this.#periodic) { let r=RemainderDouble(t-this.#min,this.#length); if(r<0)r+=this.#length; t=this.#min+r; }
    if(t<=this.#min)return [this.#degree,this.#min];
    if(t>=this.#max)return [this.#count-1,this.#max];
    for(const key of this.#keys) if(t<key.value)return [key.index,t];
    const error=new Error('Unexpected condition.'); error.name='Exception'; throw error;
  }
  Evaluate(t,order,minIndex,maxIndex) {
    const [i,parameter]=this.#index(t); t=parameter;
    const J=this.#jet,K=this.#knots,highest=Math.min(order,3);
    J[0][0][i]=1;
    for(let d=1;d<=highest;d++)J[d][0][i]=0;
    let n0=t-K[i],n1=K[i+1]-t;
    for(let j=1;j<=this.#degree;j++) {
      const d0=K[i+j]-K[i],d1=K[i+1]-K[i-j+1],inv0=d0>0?1/d0:0,inv1=d1>0?1/d1:0;
      J[0][j][i]=mul(mul(n0,J[0][j-1][i]),inv0);
      J[0][j][i-j]=mul(mul(n1,J[0][j-1][i-j+1]),inv1);
      for(let d=1;d<=highest;d++) {
        const a=mul(n0,J[d][j-1][i])+(d===1?J[0][j-1][i]:mul(d,J[d-1][j-1][i]));
        const b=mul(n1,J[d][j-1][i-j+1])-(d===1?J[0][j-1][i-j+1]:mul(d,J[d-1][j-1][i-j+1]));
        J[d][j][i]=mul(a,inv0); J[d][j][i-j]=mul(b,inv1);
      }
    }
    for(let j=2;j<=this.#degree;j++)for(let k=i-j+1;k<i;k++) {
      n0=t-K[k]; n1=K[k+j+1]-t;
      const d0=K[k+j]-K[k],d1=K[k+j+1]-K[k+1],inv0=d0>0?1/d0:0,inv1=d1>0?1/d1:0;
      J[0][j][k]=mul(mul(n0,J[0][j-1][k]),inv0)+mul(mul(n1,J[0][j-1][k+1]),inv1);
      for(let d=1;d<=highest;d++) {
        const a=mul(n0,J[d][j-1][k])+(d===1?J[0][j-1][k]:mul(d,J[d-1][j-1][k]));
        const b=mul(n1,J[d][j-1][k+1])-(d===1?J[0][j-1][k+1]:mul(d,J[d-1][j-1][k+1]));
        J[d][j][k]=mul(a,inv0)+mul(b,inv1);
      }
    }
    minIndex.value=i-this.#degree; maxIndex.value=i;
  }
  GetValue(order,i) {
    if(order<4) { if(i>=0&&i<this.#count+this.#degree) {
      if(order<0)throw new IndexOutOfRangeException(); return this.#jet[order][this.#degree][i];
    } throw new ArgumentException('Invalid index.','i'); }
    throw new ArgumentException('Invalid order.','order');
  }
}
