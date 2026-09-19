// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see GTE/LICENSE.BSL-1.0.
import { ParametricSurface } from './ParametricSurface.js';
import { Vector3 } from '../Vector3.js';
import { MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { SurfaceStorage, Dimension, SurfaceJet, SurfaceRange } from '../../runtime/SurfaceStorage.js';
export class BSplineSurface extends ParametricSurface {
  #storage;
  constructor(input0,input1,controls){super(0,1,0,1,true);this.#storage=SurfaceStorage(this,input0,input1,controls);this.isConstructed=true;}
  BasisFunction(dim){return Dimension(this.#storage.basis,dim);}
  NumControls(dim){return Dimension(this.#storage.count,dim);}
  Controls(){return this.#storage.points;}
  SetControl(i0,i1,point){const {count,points}=this.#storage;if(i0>=0&&i0<count[0]&&i1>=0&&i1<count[1])points[i0+count[0]*i1]=point;}
  GetControl(i0,i1){const {count,points}=this.#storage;return points.get_Item(i0>=0&&i0<count[0]&&i1>=0&&i1<count[1]?i0+count[0]*i1:0);}
  Evaluate(u,v,order,output){
    const jet=SurfaceJet();output.value=jet;if(!this.isConstructed||order>=6)return;
    const range=SurfaceRange(this.#storage.basis,u,v,order);
    jet[0]=this.#compute(0,0,...range);
    if(order>=1){jet[1]=this.#compute(1,0,...range);jet[2]=this.#compute(0,1,...range);
      if(order>=2){jet[3]=this.#compute(2,0,...range);jet[4]=this.#compute(1,1,...range);jet[5]=this.#compute(0,2,...range);}}
  }
  #compute(du,dv,umin,umax,vmin,vmax){
    const {count,basis,points}=this.#storage;let result=Vector3.Zero;
    for(let iv=vmin;iv<=vmax;iv++){const tv=basis[1].GetValue(dv,iv),jv=iv>=count[1]?iv-count[1]:iv;
      for(let iu=umin;iu<=umax;iu++){const tu=basis[0].GetValue(du,iu),ju=iu>=count[0]?iu-count[0]:iu;
        result=Vector3.Add(result,Vector3.Multiply(mul(tu,tv),points[ju+count[0]*jv]));}}
    return result;
  }
}
