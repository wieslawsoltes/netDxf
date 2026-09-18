// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Matrix3 } from '../Matrix3.js';
import { Vector3 } from '../Vector3.js';
import { MathHelper } from '../MathHelper.js';
import { MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const widths=new WeakMap();
export function ValidatePolylineWidth(value,name){if(!Number.isFinite(value)||value<0)throw new ArgumentOutOfRangeException(name,value);}
export function InstallPolyline2DFidelity(Type){
  Object.defineProperty(Type.prototype,'ConstantWidth',{get(){return widths.get(this)??null;},set(value){
    if(this.HasStoredRecords&&value!==null)throw new NotSupportedException('ConstantWidth requires LWPOLYLINE storage.');
    if(value!==null)ValidatePolylineWidth(value,'value');widths.set(this,value);
  }});
  Type.ValidateWidth=ValidatePolylineWidth;
  Type.prototype.GetEffectiveStartWidth=function(i){const v=this.Vertexes.get_Item(i);return (this.ConstantWidth??0)>0?this.ConstantWidth:v.StartWidthOverride??this.LegacyDefaultStartWidth??0;};
  Type.prototype.GetEffectiveEndWidth=function(i){const v=this.Vertexes.get_Item(i);return (this.ConstantWidth??0)>0?this.ConstantWidth:v.EndWidthOverride??this.LegacyDefaultEndWidth??0;};
  Type.prototype.ValidateVertexFidelity=function(){for(const v of this.Vertexes){if(v===null)throw new InvalidOperationException('A polyline cannot contain a null vertex.');ValidatePolylineWidth(v.StartWidth,'StartWidth');ValidatePolylineWidth(v.EndWidth,'EndWidth');}};
  Type.prototype.GetWidthTransformScale=function(matrix){
    this.ValidateVertexFidelity();let wide=(this.ConstantWidth??0)>0||(this.LegacyDefaultStartWidth??0)>0||(this.LegacyDefaultEndWidth??0)>0;
    if(this.HasStoredRecords)for(const v of this.Vertexes)wide=wide||v.Bulge!==0;
    for(const v of this.Vertexes)wide=wide||v.StartWidth>0||v.EndWidth>0;if(!wide)return 1;
    const ocs=MathHelper.ArbitraryAxis(this.Normal),x=Matrix3.Multiply(matrix,Matrix3.Multiply(ocs,Vector3.UnitX)),y=Matrix3.Multiply(matrix,Matrix3.Multiply(ocs,Vector3.UnitY)),scale=x.Modulus(),other=y.Modulus();
    if(!Number.isFinite(scale)||scale<=0||!Number.isFinite(other)||other<=0||Math.abs(scale-other)>mul(MathHelper.Epsilon,Math.max(scale,other))||Math.abs(Vector3.DotProduct(Vector3.Divide(x,scale),Vector3.Divide(y,other)))>MathHelper.Epsilon)
      throw new NotSupportedException('Wide polylines require nonsingular uniform planar scale.');
    const normal=Matrix3.Multiply(matrix,this.Normal),ns=normal.Modulus();
    if(ns<=0||!Number.isFinite(ns)||Math.abs(Vector3.DotProduct(Vector3.Divide(x,scale),Vector3.Divide(normal,ns)))>MathHelper.Epsilon||Math.abs(Vector3.DotProduct(Vector3.Divide(y,other),Vector3.Divide(normal,ns)))>MathHelper.Epsilon)
      throw new NotSupportedException('Wide transforms must preserve the perpendicular normal.');
    if(Vector3.DotProduct(Vector3.CrossProduct(Vector3.Divide(x,scale),Vector3.Divide(y,other)),Vector3.Divide(normal,ns))<0)
      for(const v of this.Vertexes)if(v.Bulge!==0)throw new NotSupportedException('Reflecting wide arc segments requires geometry conversion.');
    for(const key of ['ConstantWidth','LegacyDefaultStartWidth','LegacyDefaultEndWidth'])if(this[key]!==null)ValidatePolylineWidth(mul(this[key],scale),key);
    for(const v of this.Vertexes){ValidatePolylineWidth(mul(v.StartWidth,scale),'StartWidth');ValidatePolylineWidth(mul(v.EndWidth,scale),'EndWidth');}return scale;
  };
}
