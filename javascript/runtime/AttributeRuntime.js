// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Shared storage/geometry for ATTRIB and ATTDEF. Neither is an EntityObject in C#.
import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { Matrix3 } from '../netDxf/Matrix3.js';
import { Matrix4 } from '../netDxf/Matrix4.js';
import { MathHelper } from '../netDxf/MathHelper.js';
import { TextAlignment } from '../netDxf/Entities/TextAligment.js';
import { TableObjectChangedEventArgs } from '../netDxf/Tables/TableObjectChangedEventArgs.js';
import { CommonEntityData } from '../netDxf/Entities/EntityObject.CommonData.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from './GeometryRuntime.js';
import { EventHook } from './EventHook.js';
import { TransformTextAxes } from './PlanarTextGeometry.js';
import { TransformedNormal } from './EntityGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException } from './Errors.js';
const states = new WeakMap();
const scalarNames = ['Height','Width','WidthFactor','ObliqueAngle','Rotation','LinetypeScale'];
const scalarIndex = Object.fromEntries(scalarNames.map((key,index) => [key,index*8]));
export function AttributeState(item) { return states.get(item); }
export function InitializeAttribute(item, styleEvent) {
  const s = { Color:null, Layer:null, Linetype:null, Transparency:null, Style:null, Definition:null,
    Normal:Vector3.Zero, Position:Vector3.Zero, Lineweight:0, Flags:0, Alignment:0,
    IsVisible:false, IsBackward:false, IsUpsideDown:false, Tag:'', Prompt:'', Value:'', scalars:new DataView(new ArrayBuffer(48)) };
  states.set(item,s);
  Object.defineProperty(item,'CommonData',{value:new CommonEntityData()});
  for(const event of ['LayerChanged','LinetypeChanged',styleEvent])Object.defineProperty(item,event,{value:new EventHook(),enumerable:true});
  return s;
}
export function AssignAttributeRaw(target,values) {
  const state=AttributeState(target);
  for(const [key,value] of Object.entries(values)) {
    if(Object.hasOwn(scalarIndex,key))state.scalars.setFloat64(scalarIndex[key],value);
    else state[key]=Copy(value);
  }
}
export function InstallAttributeProperties(Type,styleEvent) {
  for(const key of scalarNames)Object.defineProperty(Type.prototype,key,{
    get(){return AttributeState(this).scalars.getFloat64(scalarIndex[key]);},
    set(value){
      if(['Height','Width','WidthFactor','LinetypeScale'].includes(key)&&value<=0)throw new ArgumentOutOfRangeException('value',value);
      if(key==='ObliqueAngle'&&(value< -85||value>85))throw new ArgumentOutOfRangeException('value',value);
      AttributeState(this).scalars.setFloat64(scalarIndex[key],key==='Rotation'?MathHelper.NormalizeAngle(value):value);
    }
  });
  for(const key of ['Layer','Linetype','Style','Color','Transparency'])Object.defineProperty(Type.prototype,key,{
    get(){return AttributeState(this)[key];},set(value){
      if(value==null)throw new ArgumentNullException('value');
      const s=AttributeState(this),method={Layer:'OnLayerChangedEvent',Linetype:'OnLinetypeChangedEvent',Style:'OnTextStyleChangedEvent'}[key];
      s[key]=method?this[method](s[key],value):value;
    }
  });
  for(const [method,event] of [['OnLayerChangedEvent','LayerChanged'],['OnLinetypeChangedEvent','LinetypeChanged'],['OnTextStyleChangedEvent',styleEvent]])
    Type.prototype[method]=function(oldValue,newValue){const e=new TableObjectChangedEventArgs(oldValue,newValue);this[event].Invoke(this,e);return e.NewValue;};
  for(const key of ['Lineweight','Flags','Alignment','IsVisible','IsBackward','IsUpsideDown'])Object.defineProperty(Type.prototype,key,{
    get(){return AttributeState(this)[key];},set(v){AttributeState(this)[key]=v;}
  });
  Object.defineProperties(Type.prototype,{
    Normal:{get(){return Copy(AttributeState(this).Normal);},set(v){
      const s=AttributeState(this);s.Normal=Vector3.Normalize(v);
      if(Vector3.IsZero(s.Normal))throw new ArgumentException('The normal can not be the zero vector.','value');
    }},
    Position:{get(){return Copy(AttributeState(this).Position);},set(v){AttributeState(this).Position=Copy(v);}},
    Tag:{get(){return AttributeState(this).Tag;}},
    Value:{get(){return AttributeState(this).Value;},set(v){AttributeState(this).Value=v==null||v.length===0?'':v;}}
  });
}
export function CloneAttributeResource(value) {
  if(value==null)throw new NullReferenceException();
  return value.Clone();
}
export function CopyAttributeAppearance(source,target) {
  for(const key of ['Layer','Linetype','Color'])target[key]=CloneAttributeResource(source[key]);
  target.Lineweight=source.Lineweight;target.Transparency=CloneAttributeResource(source.Transparency);
  target.LinetypeScale=source.LinetypeScale;target.Normal=source.Normal;target.IsVisible=source.IsVisible;
}
export function FinishAttributeClone(source,target) {
  for(const d of source.XData.Values)target.XData.Add(d.Clone());source.CopyCommonDataTo(target);return target;
}
const swap=(value,pairs)=>{for(const [a,b] of pairs){if(value===a)return b;if(value===b)return a;}return value;};
export function AttributeTransformArguments(matrix,translation) {
  if(matrix instanceof Matrix4&&translation===undefined)return [new Matrix3(matrix.M11,matrix.M12,matrix.M13,matrix.M21,matrix.M22,matrix.M23,matrix.M31,matrix.M32,matrix.M33),new Vector3(matrix.M14,matrix.M24,matrix.M34)];
  if(!(matrix instanceof Matrix3)||!(translation instanceof Vector3))throw new ArgumentException('Expected Matrix4 or Matrix3 and Vector3.');
  return [matrix,translation];
}
export function TransformAttribute(item,transformation,translation,mirror) {
  const position=Vector3.Add(Matrix3.Multiply(transformation,item.Position),translation),normal=TransformedNormal(transformation,item.Normal);
  const {uv,u,v}=TransformTextAxes(item.Normal,normal,mul(item.Rotation,MathHelper.DegToRad),
    Vector2.Multiply(mul(item.WidthFactor,item.Height),Vector2.UnitX),
    new Vector2(mul(item.Height,M.Tan(mul(item.ObliqueAngle,MathHelper.DegToRad))),item.Height),transformation);
  let rotation=mul(Vector2.Angle(u),MathHelper.RadToDeg),oblique=mul(Vector2.Angle(v),MathHelper.RadToDeg);
  if(Vector2.CrossProduct(u,v)<0){
    oblique=90-(rotation-oblique);
    if(mirror){if(item.Alignment!==TextAlignment.Fit&&item.Alignment!==TextAlignment.Aligned)rotation+=180;item.IsBackward=!item.IsBackward;}
    else if(Vector2.DotProduct(u,uv[0])<0){rotation+=180;item.Alignment=swap(item.Alignment,[[0,2],[3,5],[9,11],[6,8]]);}
    else item.Alignment=swap(item.Alignment,[[0,6],[1,7],[2,8]]);
  }else oblique=90+(rotation-oblique);
  oblique=MathHelper.NormalizeAngle(oblique);if(oblique>180)oblique=180-oblique;
  if(oblique< -85)oblique=-85;else if(oblique>85)oblique=85;
  let height=mul(v.Modulus(),M.Cos(mul(oblique,MathHelper.DegToRad)));height=MathHelper.IsZero(height)?MathHelper.Epsilon:height;
  let width=u.Modulus()/height;if(width<.01)width=.01;else if(width>100)width=100;
  item.Position=position;item.Normal=normal;item.Rotation=rotation;item.Height=height;item.WidthFactor=width;item.ObliqueAngle=oblique;
}
