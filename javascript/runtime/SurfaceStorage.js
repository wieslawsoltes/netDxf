// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Based on Geometric Tools, David Eberly, Copyright (c) 1998-2022.
// Geometric Tools portions: Boost Software License 1.0; see GTE/LICENSE.BSL-1.0.
import { BasisFunction } from '../netDxf/GTE/BasisFunction.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { FixedArray } from './FixedArray.js';
import { ArgumentException, IndexOutOfRangeException, OverflowException } from './Errors.js';
export function SurfaceStorage(surface,input0,input1,controls) {
  const basis=[new BasisFunction(input0),new BasisFunction(input1)],count=[input0.NumControls,input1.NumControls];
  surface.uMin=basis[0].MinDomain;surface.uMax=basis[0].MaxDomain;surface.vMin=basis[1].MinDomain;surface.vMax=basis[1].MaxDomain;
  const size=Math.imul(count[0],count[1]);if(size<0)throw new OverflowException();
  const points=FixedArray(Array.from({length:size},()=>new Vector3()));
  if(controls!==null) {if(controls.length>size)throw new ArgumentException('Destination array is not long enough.','destinationArray');for(let i=0;i<controls.length;i++)points[i]=controls[i];}
  return {basis,count,points};
}
export function Dimension(values,dim){if(!Number.isInteger(dim)||dim<0||dim>=values.length)throw new IndexOutOfRangeException();return values[dim];}
export function SurfaceJet(){return FixedArray(Array.from({length:6},()=>new Vector3()));}
export function SurfaceRange(basis,u,v,order){const loU={value:0},hiU={value:0},loV={value:0},hiV={value:0};basis[0].Evaluate(u,order,loU,hiU);basis[1].Evaluate(v,order,loV,hiV);return [loU.value,hiU.value,loV.value,hiV.value];}
