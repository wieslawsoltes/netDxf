// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { AciColor } from '../AciColor.js';
import { Transparency } from '../Transparency.js';
import { Lineweight } from '../Lineweight.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { TextStyle } from '../Tables/TextStyle.js';
import { Vector3 } from '../Vector3.js';
import { MathHelper } from '../MathHelper.js';
import { Text } from './Text.js';
import { TextAlignment } from './TextAligment.js';
import { AttributeTransformArguments, AttributeState, InitializeAttribute, AssignAttributeRaw, InstallAttributeProperties, TransformAttribute, CopyAttributeAppearance, CloneAttributeResource, FinishAttributeClone } from '../../runtime/AttributeRuntime.js';
import { InstallAttributeDefinitionCommonData } from './AttributeDefinition.CommonData.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException } from '../../runtime/Errors.js';
export class AttributeDefinition extends DxfObject {
  constructor(tag,...args){
    super(DxfObjectCode.AttributeDefinition);
    let style,height;
    if(args.length<2){
      style=args.length===0?TextStyle.Default:args[0];
      if(style==null)throw new ArgumentNullException('style');
      height=MathHelper.IsZero(style.Height)?1:style.Height;
    }else if(args.length===2){[height,style]=args;}else throw new ArgumentException('No matching AttributeDefinition constructor.');
    if(tag==null||tag.length===0)throw new ArgumentNullException('tag');
    InitializeAttribute(this,'TextStyleChange');
    if(style==null)throw new ArgumentNullException('style');
    if(height<=0)throw new ArgumentOutOfRangeException('textHeight','');
    AssignAttributeRaw(this,{Tag:tag,Style:style,Height:height,Width:1,WidthFactor:style.WidthFactor,ObliqueAngle:style.ObliqueAngle,
      Color:AciColor.ByLayer,Layer:Layer.Default,Linetype:Linetype.ByLayer,Lineweight:Lineweight.ByLayer,
      Transparency:Transparency.ByLayer,LinetypeScale:1,IsVisible:true,Normal:Vector3.UnitZ,Alignment:TextAlignment.BaselineLeft});
  }
  get Prompt(){return AttributeState(this).Prompt;}set Prompt(v){AttributeState(this).Prompt=v??'';}
  TransformBy(matrix,translation){
    [matrix,translation]=AttributeTransformArguments(matrix,translation);
    const variables=this.Owner===null?null:this.Owner.Record?.Owner?.Owner?.DrawingVariables;
    if(this.Owner!==null&&variables==null)throw new NullReferenceException();
    TransformAttribute(this,matrix,translation,variables===null?Text.DefaultMirrText:variables.MirrText);
  }
  Clone(){
    const copy=new AttributeDefinition(this.Tag);CopyAttributeAppearance(this,copy);
    for(const key of ['Prompt','Value','Height','Width','WidthFactor','ObliqueAngle'])copy[key]=this[key];
    copy.Style=CloneAttributeResource(this.Style);
    for(const key of ['Position','Flags','Rotation','Alignment','IsBackward','IsUpsideDown'])copy[key]=this[key];
    return FinishAttributeClone(this,copy);
  }
}
InstallAttributeProperties(AttributeDefinition,'TextStyleChange');
InstallAttributeDefinitionCommonData(AttributeDefinition);
