// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see GTE/LICENSE.BSL-1.0.
import { Vector3 } from '../Vector3.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export class ParametricSurface {
  constructor(umin,umax,vmin,vmax,isRectangular) {
    if(new.target===ParametricSurface)throw new NotSupportedException('ParametricSurface is abstract.');
    this.uMin=umin;this.uMax=umax;this.vMin=vmin;this.vMax=vmax;this.isRectangular=isRectangular;this.isConstructed=false;
  }
  static get SUP_ORDER(){return 6;}
  get IsConstructed(){return this.isConstructed;} get UMin(){return this.uMin;} get UMax(){return this.uMax;}
  get VMin(){return this.vMin;} get VMax(){return this.vMax;} get IsIsRectangular(){return this.isRectangular;}
  GetPosition(u,v){const result={value:null};this.Evaluate(u,v,0,result);return result.value.get_Item(0);}
  GetUTangent(u,v){const result={value:null};this.Evaluate(u,v,1,result);return Vector3.Normalize(result.value[1]);}
  GetVTangent(u,v){const result={value:null};this.Evaluate(u,v,1,result);return Vector3.Normalize(result.value[2]);}
}
