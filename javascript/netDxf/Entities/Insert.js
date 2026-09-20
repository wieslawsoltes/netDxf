// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Block } from '../Blocks/Block.js';
import { Attribute } from './Attribute.js';
import { AttributeChangeEventArgs } from './AttributeChangeEventArgs.js';
import { AttributeCollection } from '../Collections/AttributeCollection.js';
import { Ellipse } from './Ellipse.js';
import { Helix } from './Helix.js';
import { Text } from './Text.js';
import { Polyline3D } from './Polyline3D.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { UnitHelper } from '../Units/UnitHelper.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Copy, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { InstallInsertArray } from './Insert.Array.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException, NotSupportedException, InvalidOperationException } from '../../runtime/Errors.js';
const raw=Symbol('internal attribute constructor');
const ref=v=>{if(v==null)throw new NullReferenceException();return v;};
export class Insert extends EntityObject {
  static DefaultInsUnits=0;
  #block;#position=Vector3.Zero;#scale=new Vector3(1);#rotation=new DataView(new ArrayBuffer(8));#attributes;
  constructor(block,position=Vector3.Zero){
    super(EntityType.Insert,DxfObjectCode.Insert);
    for(const event of ['AttributeAdded','AttributeRemoved'])Object.defineProperty(this,event,{value:new EventHook(),enumerable:true});
    if(position===raw){
      if(block==null)throw new ArgumentNullException('attributes');
      this.#attributes=new AttributeCollection(block);
      for(const a of this.#attributes){if(ref(a).Owner!==null)throw new ArgumentException('The attributes list contains an attribute that already has an owner.','attributes');a.Owner=this;}
      this.#block=null;return;
    }
    if(block==null)throw new ArgumentNullException('block');
    this.#block=block;this.#position=position instanceof Vector2?new Vector3(position.X,position.Y,0):Copy(position);
    const attributes=[];
    for(const definition of block.AttributeDefinitions.Values){const a=new Attribute(definition);a.Position=Vector3.Subtract(Vector3.Add(definition.Position,this.#position),this.#block.Origin);a.Owner=this;attributes.push(a);}
    this.#attributes=new AttributeCollection(attributes);
  }
  static CreateOverload(signature,...args){
    if(signature==='System.Collections.Generic.List<netDxf.Entities.Attribute>')return new Insert(args[0],raw);
    if(['netDxf.Blocks.Block','netDxf.Blocks.Block,netDxf.Vector2','netDxf.Blocks.Block,netDxf.Vector3'].includes(signature))return new Insert(...args);
    throw new ArgumentException('Unknown Insert constructor signature.','signature');
  }
  get Attributes(){return this.#attributes;}get Block(){return this.#block;}set Block(v){this.#block=v;}
  get Position(){return Copy(this.#position);}set Position(v){this.#position=Copy(v);}
  get Scale(){return Copy(this.#scale);}set Scale(v){
    if(MathHelper.IsZero(v.X)||MathHelper.IsZero(v.Y)||MathHelper.IsZero(v.Z))throw new ArgumentOutOfRangeException('value',v);
    this.#scale=Copy(v);
  }
  get Rotation(){return this.#rotation.getFloat64(0);}set Rotation(v){this.#rotation.setFloat64(0,MathHelper.NormalizeAngle(v));}
  OnAttributeAddedEvent(item){this.AttributeAdded.Invoke(this,new AttributeChangeEventArgs(item));}
  OnAttributeRemovedEvent(item){this.AttributeRemoved.Invoke(this,new AttributeChangeEventArgs(item));}
  Sync(){
    const document=this.Owner?.Record.Owner?.Owner;
    if(document!=null)for(const a of this.#attributes)if(!this.#block.AttributeDefinitions.ContainsTag(a.Tag)&&document.StoredTableReferencesRemoval(a))throw new InvalidOperationException('A stored TABLE references an attribute that synchronization would remove.');
    const attributes=[];
    for(const a of this.#attributes){if(ref(this.#block).AttributeDefinitions.ContainsTag(ref(a).Tag))attributes.push(a);else{this.OnAttributeRemovedEvent(a);a.Handle=null;a.Owner=null;}}
    for(const definition of this.#block.AttributeDefinitions.Values)if(this.#attributes.AttributeWithTag(definition.Tag)===null){const a=new Attribute(definition);a.Owner=this;attributes.push(a);this.OnAttributeAddedEvent(a);}
    this.#attributes=new AttributeCollection(attributes);this.TransformAttributes();
  }
  GetTransformation(insertionUnits){
    if(arguments.length===0){
      if(this.Owner===null)insertionUnits=Insert.DefaultInsUnits;
      else{const record=ref(this.Owner.Record);insertionUnits=record.Layout===null?record.Units:ref(ref(record.Owner).Owner).DrawingVariables.InsUnits;}
    }
    const factor=UnitHelper.ConversionFactor(ref(this.Block).Record.Units,insertionUnits);
    let transform=MathHelper.ArbitraryAxis(this.Normal);
    transform=Matrix3.Multiply(transform,Matrix3.RotationZ(mul(this.Rotation,MathHelper.DegToRad)));
    return Matrix3.Multiply(transform,Matrix3.Scale(Vector3.Multiply(this.#scale,factor)));
  }
  TransformAttributes(){
    if(this.#attributes.Count===0)return;
    const transform=this.GetTransformation(),translation=Vector3.Subtract(this.Position,Matrix3.Multiply(transform,this.#block.Origin));
    for(const a of this.#attributes){const d=ref(a).Definition;if(d===null)continue;
      for(const key of ['Position','Height','Width','WidthFactor','ObliqueAngle','Rotation','Normal','IsBackward','IsUpsideDown'])a[key]=d[key];
      a.TransformBy(transform,translation);
    }
  }
  Explode(){return new ReferenceList(this.ExplodeEnumerable());}
  $explodeCellCore(transformation,translation,arrayOffset){
    const seen=new Set();
    const reject=block=>{if(block==null||seen.has(block))return;seen.add(block);for(const entity of block.Entities){if(entity.constructor.name==='DxfOpaqueEntity')throw new NotSupportedException('Unknown entity block geometry requires its complete application schema.');if(entity instanceof Insert||entity.constructor.name.endsWith('Dimension'))reject(entity.Block);}};
    reject(this.#block);
    const entities=new ReferenceList();
    const convert=(shape,appearance)=>{
      const ellipse=new Ellipse(shape.Center,mul(2,shape.Radius),mul(2,shape.Radius));
      for(const key of ['Layer','Linetype','Color','Transparency'])ellipse[key]=appearance[key].Clone();
      for(const key of ['Lineweight','LinetypeScale','Normal','IsVisible'])ellipse[key]=appearance[key];
      if(shape.Type===EntityType.Arc){ellipse.StartAngle=shape.StartAngle;ellipse.EndAngle=shape.EndAngle;}
      ellipse.Thickness=shape.Thickness;ellipse.TransformBy(transformation,translation);entities.Add(ellipse);
    };
    for(const entity of ref(this.#block).Entities){
      const localScale=MathHelper.Transform(this.Scale,entity.Normal,CoordinateSystem.World,CoordinateSystem.Object);
      const uniform=MathHelper.IsEqual(localScale.X,localScale.Y);
      if(entity.Reactors.Count>0)continue;
      if(entity instanceof Helix&&!Helix.TryGetSimilarityScale(transformation,{value:0})){const spline=entity.ToSpline();spline.TransformBy(transformation,translation);entities.Add(spline);continue;}
      if(!uniform&&(entity.Type===EntityType.Circle||entity.Type===EntityType.Arc)){convert(entity,entity);continue;}
      if(!uniform&&(entity.Type===EntityType.Polyline2D||entity.Type===EntityType.MLine)){
        for(const child of entity.Explode()){if(child.Type===EntityType.Arc)convert(child,entity);else{child.TransformBy(transformation,translation);entities.Add(child);}}
        continue;
      }
      const copy=entity.Clone();copy.TransformBy(transformation,translation);entities.Add(copy);
    }
    for(const a of this.#attributes){
      const text=new Text();
      for(const key of ['Layer','Linetype','Color','Transparency'])text[key]=a[key].Clone();
      for(const key of ['Lineweight','LinetypeScale','Normal','IsVisible','Height','WidthFactor','ObliqueAngle','Value'])text[key]=a[key];
      text.Style=a.Style.Clone();text.Position=Vector3.Add(a.Position,arrayOffset);
      for(const key of ['Rotation','Alignment','IsBackward','IsUpsideDown'])text[key]=a[key];entities.Add(text);
    }
    return entities;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    if(this.IsMultiple){this.$transformArray(matrix,translation);return;}
    const position=Vector3.Add(Matrix3.Multiply(matrix,this.Position),translation);
    let normal=Matrix3.Multiply(matrix,this.Normal);if(Vector3.Equals(Vector3.Zero,normal))normal=this.Normal;
    const ow=Matrix3.Multiply(MathHelper.ArbitraryAxis(this.Normal),Matrix3.RotationZ(mul(this.Rotation,MathHelper.DegToRad)));
    let wo=MathHelper.ArbitraryAxis(normal).Transpose();
    const v=Matrix3.Multiply(wo,Matrix3.Multiply(matrix,Matrix3.Multiply(ow,Vector3.UnitX))),angle=Vector2.Angle(new Vector2(v.X,v.Y));
    wo=Matrix3.Multiply(Matrix3.RotationZ(angle).Transpose(),wo);
    const s=Matrix3.Multiply(wo,Matrix3.Multiply(matrix,Matrix3.Multiply(ow,this.Scale)));
    const scale=new Vector3(MathHelper.IsZero(s.X)?MathHelper.Epsilon:s.X,MathHelper.IsZero(s.Y)?MathHelper.Epsilon:s.Y,MathHelper.IsZero(s.Z)?MathHelper.Epsilon:s.Z);
    this.Normal=normal;this.Position=position;this.Scale=scale;this.Rotation=mul(angle,MathHelper.RadToDeg);
    for(const a of this.#attributes)a.TransformBy(matrix,translation);
  }
  AssignHandle(number){for(const a of this.#attributes)number=ref(a).AssignHandle(number);return super.AssignHandle(number);}
  Clone(){
    Polyline3D.RejectStoredRecordBlockClone(this.Block);
    const attributes=Array.from(this.#attributes,a=>ref(a).Clone());
    const copy=this.$copyEntityAttributes(new Insert(attributes,raw));copy.Position=this.#position;copy.Block=ref(this.#block).Clone();copy.Scale=this.#scale;copy.Rotation=this.Rotation;
    for(const key of ['ColumnCount','RowCount','ColumnSpacing','RowSpacing'])copy[key]=this[key];
    return this.$finishEntityClone(copy);
  }
}
InstallInsertArray(Insert);
