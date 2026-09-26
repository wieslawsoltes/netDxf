// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
const states=new WeakMap();
const state=i=>{if(!states.has(i))states.set(i,{rows:1,columns:1,numbers:new DataView(new ArrayBuffer(16))});return states.get(i);};
function spacing(v){if(!Number.isFinite(v))throw new ArgumentOutOfRangeException('value',v);}
function checked(v){if(!Number.isFinite(v.X)||!Number.isFinite(v.Y)||!Number.isFinite(v.Z))throw new InvalidOperationException('The INSERT array calculation produced a non-finite coordinate.');return v;}
const frame=i=>Matrix3.Multiply(MathHelper.ArbitraryAxis(i.Normal),Matrix3.RotationZ(mul(i.Rotation,MathHelper.DegToRad)));
function offset(i,row,column){
  RequireInteger(row,0,i.RowCount-1,'row');RequireInteger(column,0,i.ColumnCount-1,'column');
  return checked(Matrix3.Multiply(frame(i),new Vector3(mul(column,i.ColumnSpacing),mul(row,i.RowSpacing),0)));
}
function axisLength(value){
  const largest=Math.max(Math.abs(value.X),Math.max(Math.abs(value.Y),Math.abs(value.Z)));
  if(largest===0)throw new NotSupportedException('The transformation collapses an INSERT array axis.');
  const scaled=Vector3.Divide(value,largest),length=mul(largest,M.Sqrt(Vector3.DotProduct(scaled,scaled)));
  if(Math.abs(length)===Infinity||MathHelper.IsZero(length))throw new NotSupportedException('The transformed INSERT array axis has an unsupported length.');
  return length;
}
export function InstallInsertArray(Type){
  for(const [key,field] of [['ColumnCount','columns'],['RowCount','rows']])Object.defineProperty(Type.prototype,key,{get(){return state(this)[field];},set(v){state(this)[field]=RequireInteger(v,1,32767);}});
  for(const [key,index] of [['ColumnSpacing',0],['RowSpacing',8]])Object.defineProperty(Type.prototype,key,{get(){return state(this).numbers.getFloat64(index);},set(v){spacing(v);state(this).numbers.setFloat64(index,v);}});
  Object.defineProperties(Type.prototype,{
    IsMultiple:{get(){return this.ColumnCount>1||this.RowCount>1;}},
    InstanceCount:{get(){return this.ColumnCount*this.RowCount;}}
  });
  Type.prototype.GetGridPosition=function(row,column){return checked(Vector3.Add(this.Position,offset(this,row,column)));};
  Type.prototype.ExplodeCell=function(row,column){const shift=offset(this,row,column),transform=this.GetTransformation(),translation=checked(Vector3.Subtract(Vector3.Add(this.Position,shift),Matrix3.Multiply(transform,this.Block.Origin)));return this.$explodeCellCore(transform,translation,shift);};
  Type.prototype.ExplodeEnumerable=function(){
    const source=this;
    // C# IEnumerable can create fresh independent iterators; work starts on MoveNext.
    return {[Symbol.iterator]:function*(){
      if(source.Block.Entities.Count===0&&source.Attributes.Count===0)return;
      const transform=source.GetTransformation(),grid=frame(source),translation=Vector3.Subtract(source.Position,Matrix3.Multiply(transform,source.Block.Origin));
      const rows=source.RowCount,columns=source.ColumnCount,dx=source.ColumnSpacing,dy=source.RowSpacing;
      for(let row=0;row<rows;row++)for(let column=0;column<columns;column++){
        const shift=checked(Matrix3.Multiply(grid,new Vector3(mul(column,dx),mul(row,dy),0)));
        yield* source.$explodeCellCore(transform,checked(Vector3.Add(translation,shift)),shift);
      }
    }};
  };
  Type.prototype.$transformArray=function(transform,translation){
    const basis=frame(this),x=checked(Matrix3.Multiply(transform,Matrix3.Multiply(basis,Vector3.UnitX))),y=checked(Matrix3.Multiply(transform,Matrix3.Multiply(basis,Vector3.UnitY))),z=checked(Matrix3.Multiply(transform,Matrix3.Multiply(basis,Vector3.UnitZ)));
    const lx=axisLength(x),ly=axisLength(y),lz=axisLength(z),ux=Vector3.Divide(x,lx),uy=Vector3.Divide(y,ly),uz=Vector3.Divide(z,lz);
    if(Math.abs(Vector3.DotProduct(ux,uy))>1e-10||Math.abs(Vector3.DotProduct(ux,uz))>1e-10||Math.abs(Vector3.DotProduct(uy,uz))>1e-10)throw new NotSupportedException('This transformation produces a non-orthogonal INSERT array.');
    const target=MathHelper.ArbitraryAxis(uz),localX=Matrix3.Multiply(target.Transpose(),ux),rotation=M.Atan2(localX.Y,localX.X),targetY=Matrix3.Multiply(target,Matrix3.Multiply(Matrix3.RotationZ(rotation),Vector3.UnitY));
    const signedY=Vector3.DotProduct(uy,targetY)<0?-ly:ly;
    const scale=checked(new Vector3(mul(this.Scale.X,lx),mul(this.Scale.Y,signedY),mul(this.Scale.Z,lz)));
    if(MathHelper.IsZero(scale.X)||MathHelper.IsZero(scale.Y)||MathHelper.IsZero(scale.Z))throw new NotSupportedException('The transformation collapses an INSERT scale component.');
    const position=checked(Vector3.Add(Matrix3.Multiply(transform,this.Position),translation)),dx=mul(this.ColumnSpacing,lx),dy=mul(this.RowSpacing,signedY);spacing(dx);spacing(dy);
    this.Normal=uz;this.Position=position;this.Scale=scale;this.Rotation=mul(rotation,MathHelper.RadToDeg);this.ColumnSpacing=dx;this.RowSpacing=dy;
    for(const a of this.Attributes)a.TransformBy(transform,translation);
  };
}
