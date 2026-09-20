// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Text } from './Text.js';
import { AttributeTransformArguments, AttributeState, InitializeAttribute, AssignAttributeRaw, InstallAttributeProperties, TransformAttribute, CopyAttributeAppearance, CloneAttributeResource, FinishAttributeClone } from '../../runtime/AttributeRuntime.js';
import { InstallAttributeCommonData } from './Attribute.CommonData.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
const raw=Symbol('internal tag constructor');
export class Attribute extends DxfObject {
  constructor(definition,mode){
    super(DxfObjectCode.Attribute);InitializeAttribute(this,'TextStyleChanged');
    if(mode===raw){AssignAttributeRaw(this,{Tag:definition??''});return;}
    if(definition==null)throw new ArgumentNullException('definition');
    const values={Definition:definition};
    for(const key of ['Color','Layer','Linetype','Lineweight','LinetypeScale','Transparency','IsVisible','Normal','Tag','Value','Style','Position','Flags','Height','Width','WidthFactor','ObliqueAngle','Rotation','Alignment','IsBackward','IsUpsideDown'])values[key]=definition[key];
    AssignAttributeRaw(this,values);definition.CommonData.CopyTo(this.CommonData);
  }
  static CreateOverload(signature,...args){
    if(signature==='string')return new Attribute(args[0],raw);
    if(signature==='netDxf.Entities.AttributeDefinition')return new Attribute(args[0]);
    throw new ArgumentException('Unknown Attribute constructor signature.','signature');
  }
  get Definition(){return AttributeState(this).Definition;}set Definition(v){AttributeState(this).Definition=v;}
  TransformBy(matrix,translation){
    [matrix,translation]=AttributeTransformArguments(matrix,translation);
    const owner=this.Owner?.Owner,variables=owner==null?null:owner.Record?.Owner?.Owner?.DrawingVariables;
    if(owner!=null&&variables==null)throw new NullReferenceException();
    TransformAttribute(this,matrix,translation,variables===null?Text.DefaultMirrText:variables.MirrText);
  }
  Clone(){
    const copy=new Attribute(this.Tag,raw);CopyAttributeAppearance(this,copy);
    copy.Definition=this.Definition==null?null:this.Definition.Clone();
    for(const key of ['Height','Width','WidthFactor','ObliqueAngle','Value'])copy[key]=this[key];
    copy.Style=CloneAttributeResource(this.Style);
    for(const key of ['Position','Flags','Rotation','Alignment','IsBackward','IsUpsideDown'])copy[key]=this[key];
    return FinishAttributeClone(this,copy);
  }
}
InstallAttributeProperties(Attribute,'TextStyleChanged');InstallAttributeCommonData(Attribute);
