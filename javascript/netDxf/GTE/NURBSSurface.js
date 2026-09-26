// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see GTE/LICENSE.BSL-1.0.
import { ParametricSurface } from './ParametricSurface.js';
import { Vector3 } from '../Vector3.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { SurfaceStorage, Dimension, SurfaceJet, SurfaceRange } from '../../runtime/SurfaceStorage.js';
export class NURBSSurface extends ParametricSurface {
  #storage;#weights;
  constructor(input0,input1,controls,weights){
    super(0,1,0,1,true);this.#storage=SurfaceStorage(this,input0,input1,controls);this.#weights=FixedArray(Array(this.#storage.points.length).fill(0));
    if(weights!==null){if(weights.length>this.#weights.length)throw new ArgumentException('Destination array is not long enough.','destinationArray');for(let i=0;i<weights.length;i++)this.#weights[i]=weights[i];}this.isConstructed=true;
  }
  BasisFunction(dim){return Dimension(this.#storage.basis,dim);} NumControls(dim){return Dimension(this.#storage.count,dim);}
  get Controls(){return this.#storage.points;} get Weights(){return this.#weights;}
  #index(i0,i1){const {count}=this.#storage;return i0>=0&&i0<count[0]&&i1>=0&&i1<count[1]?i0+count[0]*i1:-1;}
  SetControl(i0,i1,p){const i=this.#index(i0,i1);if(i>=0)this.Controls[i]=p;}
  GetControl(i0,i1){return this.Controls.get_Item(Math.max(0,this.#index(i0,i1)));}
  SetWeight(i0,i1,w){const i=this.#index(i0,i1);if(i>=0)this.#weights[i]=w;}
  GetWeight(i0,i1){return this.#weights.get_Item(Math.max(0,this.#index(i0,i1)));}
  Evaluate(u,v,order,output){
    const jet=SurfaceJet();output.value=jet;if(!this.isConstructed||order>=6)return;
    const range=SurfaceRange(this.#storage.basis,u,v,order),evaluate=(du,dv)=>{const x={value:null},w={value:0};this.Compute(du,dv,...range,x,w);return [x.value,w.value];};
    const [x,w]=evaluate(0,0),inv=1/w,scale=(s,v)=>Vector3.Multiply(s,v),sub=Vector3.Subtract;
    jet[0]=scale(inv,x);
    if(order>=1){const [xu,wu]=evaluate(1,0);jet[1]=scale(inv,sub(xu,scale(wu,jet[0])));
      const [xv,wv]=evaluate(0,1);jet[2]=scale(inv,sub(xv,scale(wv,jet[0])));
      if(order>=2){const [xuu,wuu]=evaluate(2,0);jet[3]=scale(inv,sub(sub(xuu,scale(mul(2,wu),jet[1])),scale(wuu,jet[0])));
        const [xuv,wuv]=evaluate(1,1);jet[4]=scale(inv,sub(sub(sub(xuv,scale(wu,jet[2])),scale(wv,jet[1])),scale(wuv,jet[0])));
        const [xvv,wvv]=evaluate(0,2);jet[5]=scale(inv,sub(sub(xvv,scale(mul(2,wv),jet[2])),scale(wvv,jet[0])));}}
  }
  Compute(du,dv,umin,umax,vmin,vmax,x,w){
    const {count,basis,points}=this.#storage;x.value=Vector3.Zero;w.value=0;
    for(let iv=vmin;iv<=vmax;iv++){const tv=basis[1].GetValue(dv,iv),jv=iv>=count[1]?iv-count[1]:iv;
      for(let iu=umin;iu<=umax;iu++){const tu=basis[0].GetValue(du,iu),ju=iu>=count[0]?iu-count[0]:iu,index=ju+count[0]*jv,t=mul(mul(tu,tv),this.#weights[index]);
        x.value=Vector3.Add(x.value,Vector3.Multiply(t,points[index]));w.value+=t;}}
  }
}
