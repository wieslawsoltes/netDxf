// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { MLineVertex } from './MLineVertex.js';
import { MLineStyle } from '../Objects/MLineStyle.js';
import { Line } from './Line.js';
import { Arc } from './Arc.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { EventHook } from '../../runtime/EventHook.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { DotNetMath as M, MultiplyDouble as mul, GetElement } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
const ref=value=>{if(value==null)throw new NullReferenceException();return value;};
const at=(values,index)=>ref(values).get_Item?values.get_Item(index):GetElement(values,index);
const count=values=>ref(values).Count??values.length;
/** Detached MLINE geometry with explicitly rebuilt miters and editable break distances. */
export class MLine extends EntityObject {
  #style; #flags; #vertexes=new ReferenceList(); #scalars=new DataView(new ArrayBuffer(16));
  Justification=1;
  constructor(...args) {
    super(EntityType.MLine,DxfObjectCode.MLine);
    let vertexes=[],style,scale=1,closed=false;
    if(args.length>4)throw new ArgumentException('No matching MLine constructor.');
    if(args.length){vertexes=args[0];
      if(args.length===2){if(typeof args[1]==='boolean')closed=args[1];else scale=args[1];}
      if(args.length===3){if(typeof args[2]==='boolean'){scale=args[1];closed=args[2];}else{style=args[1];scale=args[2];}}
      if(args.length===4){style=args[1];scale=args[2];closed=args[3];}
    }
    if(style===undefined)style=MLineStyle.Default;
    this.Scale=scale;if(style===null)throw new ArgumentNullException('style');
    this.#flags=closed?3:1;this.#style=style;
    Object.defineProperty(this,'MLineStyleChanged',{value:new EventHook(),enumerable:true});
    if(vertexes==null)throw new ArgumentNullException('vertexes');
    for(const p of vertexes)this.#vertexes.Add(new MLineVertex(p,Vector2.Zero,Vector2.Zero,null));
    this.Update();
  }
  get Vertexes(){return this.#vertexes;}
  get Elevation(){return this.#scalars.getFloat64(0);}set Elevation(v){this.#scalars.setFloat64(0,v);}
  get Scale(){return this.#scalars.getFloat64(8);}set Scale(v){this.#scalars.setFloat64(8,v);}
  get Flags(){return this.#flags;}set Flags(v){this.#flags=v;}
  get IsClosed(){return (this.#flags&2)!==0;}set IsClosed(v){this.#flags=v?this.#flags|2:this.#flags&~2;}
  get NoStartCaps(){return (this.#flags&4)!==0;}set NoStartCaps(v){this.#flags=v?this.#flags|4:this.#flags&~4;}
  get NoEndCaps(){return (this.#flags&8)!==0;}set NoEndCaps(v){this.#flags=v?this.#flags|8:this.#flags&~8;}
  get Style(){return this.#style;}set Style(v){if(v==null)throw new ArgumentNullException('value');this.#style=this.OnMLineStyleChangedEvent(this.#style,v);}
  OnMLineStyleChangedEvent(oldValue,newValue){const e=new TableObjectChangedEventArgs(oldValue,newValue);this.MLineStyleChanged.Invoke(this,e);return e.NewValue;}
  Update(){
    const vertices=this.#vertexes,n=vertices.Count;if(n===0)return;
    const style=ref(this.#style),elements=style.Elements;
    const vertex=i=>ref(vertices.get_Item(i));let reference=0;
    if(this.Justification===0)reference=-ref(elements.get_Item(0)).Offset;
    else if(this.Justification===2)reference=-ref(elements.get_Item(elements.Count-1)).Offset;
    let previous=vertex(0).Position.Equals(vertex(n-1).Position)?Vector2.UnitY:Vector2.Subtract(vertex(0).Position,vertex(n-1).Position);
    if(!vertex(0).Position.Equals(vertex(n-1).Position))previous.Normalize();
    for(let i=0;i<n;i++){
      const position=vertex(i).Position;let direction,miter;
      if(i===0){
        const next=vertex(i+1).Position;direction=next.Equals(position)?Vector2.UnitY:Vector2.Subtract(next,position);
        if(!next.Equals(position))direction.Normalize();
        miter=this.IsClosed?Vector2.Subtract(direction,previous):Vector2.Negate(MathHelper.Transform(direction,mul(style.StartAngle,MathHelper.DegToRad),CoordinateSystem.Object,CoordinateSystem.World));miter.Normalize();
      }else if(i+1===n){
        if(this.IsClosed){const next=vertex(0).Position;direction=next.Equals(position)?Vector2.UnitY:Vector2.Subtract(next,position);if(!next.Equals(position))direction.Normalize();miter=Vector2.Subtract(direction,previous);miter.Normalize();}
        else{direction=previous;miter=Vector2.Negate(MathHelper.Transform(direction,mul(style.EndAngle,MathHelper.DegToRad),CoordinateSystem.Object,CoordinateSystem.World));miter.Normalize();}
      }else{
        const next=vertex(i+1).Position;direction=next.Equals(position)?Vector2.UnitY:Vector2.Subtract(next,position);if(!next.Equals(position))direction.Normalize();miter=Vector2.Subtract(direction,previous);miter.Normalize();
      }
      previous=direction;
      const cosine=M.Cos(Vector2.Angle(miter)-(MathHelper.HalfPI+Vector2.Angle(direction))),distances=[];
      for(let j=0;j<elements.Count;j++){const distance=(ref(elements.get_Item(j)).Offset+reference)/cosine;distances.push(new ReferenceList([mul(distance,this.Scale),0]));}
      vertices.set_Item(i,new MLineVertex(position,direction,miter,FixedArray(distances,value=>value)));
    }
  }
  #appearance(entity,color,linetype){
    entity.Layer=this.Layer.Clone();entity.Linetype=ref(linetype).Clone();entity.Color=ref(color).Clone();entity.Lineweight=this.Lineweight;
    entity.Transparency=this.Transparency.Clone();entity.LinetypeScale=this.LinetypeScale;entity.Normal=this.Normal;return entity;
  }
  #line(start,end,color,linetype){return this.#appearance(new Line(start,end),color,linetype);}
  #arc(center,radius,start,end,color,linetype){const arc=this.#appearance(new Arc(center,radius,start,end),color,linetype);arc.IsVisible=this.IsVisible;return arc;}
  #cap(round,start,end,transformation,color1,linetype1,color2,linetype2){
    const result=new ReferenceList(),middle=Vector2.MidPoint(start,end);
    const emit=e=>{e.TransformBy(transformation,Vector3.Zero);result.Add(e);};
    if(round){
      const offset=Vector2.Subtract(start,middle),startAngle=mul(Vector2.Angle(offset),MathHelper.RadToDeg),endAngle=startAngle+180,radius=offset.Modulus();
      if(!MathHelper.IsZero(radius)){
        if(!ref(color1).Equals(color2)||!ref(linetype1).Equals(linetype2)){const angle=startAngle+90;emit(this.#arc(middle,radius,startAngle,angle,color1,linetype1));emit(this.#arc(middle,radius,angle,endAngle,color2,linetype2));}
        else emit(this.#arc(middle,radius,startAngle,endAngle,color1,linetype1));
      }
    }else{
      if(!ref(color1).Equals(color2)||!ref(linetype1).Equals(linetype2)){emit(this.#line(start,middle,color1,linetype1));emit(this.#line(middle,end,color2,linetype2));}
      else emit(this.#line(start,end,color1,linetype1));
    }return result;
  }
  Explode(){
    const result=new ReferenceList(),transformation=MathHelper.ArbitraryAxis(this.Normal),n=this.Vertexes.Count;
    const style=ref(this.#style),elements=style.Elements,size=elements.Count,corners=Array(n).fill(null),vertex=i=>ref(this.Vertexes.get_Item(i));
    const row=()=>Array.from({length:size},()=>Vector2.Zero),distances=(v,j)=>ref(at(ref(v).Distances,j));
    for(let i=0;i<n;i++){
      const v=vertex(i);let next;
      if(this.IsClosed&&i===n-1)next=vertex(0);
      else if(!this.IsClosed&&i===n-1)continue;
      else{next=vertex(i+1);corners[i+1]=row();}
      corners[i]=row();
      for(let j=0;j<size;j++){
        const d=distances(v,j);if(count(d)===0)continue;
        const startReference=Vector2.Add(v.Position,Vector2.Multiply(v.Miter,at(d,0)));corners[i][j]=startReference;
        for(let k=1;k<count(d);k++){
          const start=Vector2.Add(startReference,Vector2.Multiply(v.Direction,at(d,k)));let end;
          if(k>=count(d)-1){end=Vector2.Add(next.Position,Vector2.Multiply(next.Miter,at(distances(next,j),0)));if(!this.IsClosed)corners[i+1][j]=end;}
          else{end=Vector2.Add(startReference,Vector2.Multiply(v.Direction,at(d,k+1)));k++;}
          const element=ref(elements.get_Item(j)),line=this.#line(start,end,element.Color,element.Linetype);line.TransformBy(transformation,Vector3.Zero);result.Add(line);
        }
      }
    }
    const corner=(i,j)=>GetElement(ref(GetElement(corners,i)),j);
    const cap=(round,i,a,b,reverse)=>{
      const first=ref(elements.get_Item(a)),last=ref(elements.get_Item(b)),start=corner(i,a),end=corner(i,b);
      result.AddRange(reverse?this.#cap(round,end,start,transformation,last.Color,last.Linetype,first.Color,first.Linetype):this.#cap(round,start,end,transformation,first.Color,first.Linetype,last.Color,last.Linetype));
    };
    if(style.Flags&2){
      // Read endpoints before looping even for empty source geometry, as in C#.
      const first=ref(elements.get_Item(0)),last=ref(elements.get_Item(size-1));
      for(let i=0;i<n;i++){if(!this.IsClosed&&(i===0||i===n-1))continue;result.AddRange(this.#cap(false,corner(i,0),corner(i,count(GetElement(corners,0))-1),transformation,first.Color,first.Linetype,last.Color,last.Linetype));}
    }
    if(this.IsClosed)return result;
    for(const end of [false,true]){
      if(end?this.NoEndCaps:this.NoStartCaps)continue;
      const i=end?n-1:0,a=end?size-1:0,b=end?0:size-1;
      if(style.Flags&(end?1024:64))cap(true,i,a,b,!(this.Scale>=0));
      if(style.Flags&(end?512:32))for(let j=1;j<Math.trunc(mul(size,.5));j++)cap(true,i,end?size-1-j:j,end?j:size-1-j,!(this.Scale>=0));
      if(style.Flags&(end?256:16))cap(false,i,a,b,false);
    }return result;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    let normal=Matrix3.Multiply(matrix,this.Normal);if(Vector3.Equals(Vector3.Zero,normal))normal=this.Normal;
    let elevation=this.Elevation;
    const ow=MathHelper.ArbitraryAxis(this.Normal),wo=MathHelper.ArbitraryAxis(normal).Transpose();
    const direction=v=>Matrix3.Multiply(wo,Matrix3.Multiply(matrix,Matrix3.Multiply(ow,v)));
    const axis=direction(Vector3.UnitX);let scale=new Vector2(axis.X,axis.Y).Modulus();
    for(let i=0;i<this.Vertexes.Count;i++){
      const old=ref(this.Vertexes.get_Item(i)),p=old.Position,d=old.Direction,m=old.Miter;
      const p3=Matrix3.Multiply(wo,Vector3.Add(Matrix3.Multiply(matrix,Matrix3.Multiply(ow,new Vector3(p.X,p.Y,this.Elevation))),translation));elevation=p3.Z;
      const d3=direction(new Vector3(d.X,d.Y,0)),m3=direction(new Vector3(m.X,m.Y,0)),distances=[];
      for(let j=0;j<ref(this.#style).Elements.Count;j++){
        const list=ref(at(old.Distances,j)),copy=new ReferenceList();for(let k=0;k<count(list);k++)copy.Add(mul(at(list,k),scale));distances.push(copy);
      }
      this.Vertexes.set_Item(i,new MLineVertex(new Vector2(p3.X,p3.Y),new Vector2(d3.X,d3.Y),new Vector2(m3.X,m3.Y),FixedArray(distances,value=>value)));
    }
    const first=ref(this.Vertexes.get_Item(0));if(Vector2.CrossProduct(first.Miter,first.Direction)<0)scale=-scale;
    this.Elevation=elevation;this.Normal=normal;this.Scale=mul(this.Scale,scale);
  }
  Clone(){
    const copy=this.$copyEntityAttributes(new MLine());copy.Elevation=this.Elevation;copy.Scale=this.Scale;copy.Justification=this.Justification;copy.Style=ref(this.#style).Clone();copy.Flags=this.Flags;
    for(const v of this.Vertexes)copy.Vertexes.Add(ref(v).Clone());return this.$finishEntityClone(copy);
  }
}
