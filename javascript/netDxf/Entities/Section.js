// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Vector3 } from '../Vector3.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { XDataCode } from '../XDataCode.js';
import { ValueList } from '../../runtime/ValueList.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { CheckStoredName } from '../../runtime/CheckedCollection.js';
import { RequireInertIdentity } from '../../runtime/InertEntity.js';
import { ArgumentException, ArgumentOutOfRangeException, InvalidOperationException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
class SectionVertices extends ValueList {
  #index(index, insert=false) { if (!Number.isInteger(index) || index<0 || index>=this.Count+(insert?1:0)) throw new ArgumentOutOfRangeException('index',index); }
  Add(item) { this.Insert(this.Count,item); }
  Insert(index,item) {
    this.#index(index,true);
    if (this.Count>=Section.MaximumVertices) throw new ArgumentOutOfRangeException('item');
    Section.CheckVector(item); super.Insert(index,item);
  }
  set_Item(index,item) { this.#index(index); Section.CheckVector(item); super.set_Item(index,item); }
}
export class Section extends EntityObject {
  static get MaximumVertices() { return 1048576; }
  #name=''; #colorName=null; #vertical=Vector3.UnitZ; #top=0; #bottom=0;
  #vertices=new SectionVertices(); #back=new SectionVertices();
  #state=0; #flags=0; #transparency=0; #aci=null; #nativeAci=null;
  PendingInputReferences=false; IsErased=false; HasSettingsField=true; GeometrySettings=null;
  constructor(codeName='SECTIONOBJECT') {
    super(EntityType.Section,codeName);
    if (codeName!=='SECTION'&&codeName!=='SECTIONOBJECT') throw new ArgumentException('Unsupported section wire name.','codeName');
  }
  get State(){return this.#state;} set State(value){this.#state=RequireInteger(value,-2147483648,2147483647);}
  get Flags(){return this.#flags;} set Flags(value){this.#flags=RequireInteger(value,-2147483648,2147483647);}
  get Name(){return this.#name;} set Name(value){Section.CheckText(value,false);this.#name=value;}
  get VerticalDirection(){return Copy(this.#vertical);} set VerticalDirection(value){Section.CheckVector(value);this.#vertical=Copy(value);}
  get TopHeight(){return this.#top;} set TopHeight(value){Section.CheckFinite(value);this.#top=value;}
  get BottomHeight(){return this.#bottom;} set BottomHeight(value){Section.CheckFinite(value);this.#bottom=value;}
  get IndicatorTransparency(){return this.#transparency;} set IndicatorTransparency(value){this.#transparency=RequireInteger(value,-32768,32767);}
  get StoredIndicatorColor(){return this.#aci;} set StoredIndicatorColor(value){this.#aci=value===null?null:RequireInteger(value,-32768,32767);}
  get StoredNativeIndicatorColor(){return this.#nativeAci;} set StoredNativeIndicatorColor(value){this.#nativeAci=value===null?null:RequireInteger(value,-32768,32767);}
  get IndicatorColorName(){return this.#colorName;} set IndicatorColorName(value){Section.CheckText(value,true);this.#colorName=value;}
  get Vertices(){return this.#vertices;} get BackLineVertices(){return this.#back;}
  get HasStoredGeometrySettings(){return this.HasSettingsField;}
  static CheckFinite(value){if(!Number.isFinite(value))throw new ArgumentOutOfRangeException('value');}
  static CheckVector(value){Section.CheckFinite(value.X);Section.CheckFinite(value.Y);Section.CheckFinite(value.Z);}
  static CheckText(value,optional){if(value===null&&optional)return;CheckStoredName(value,'value',{allowEmpty:true,nullIsArgumentNull:true});}
  *$xdataReferences(){for(const data of this.XData.Values)for(const tag of data.XDataRecord)if(tag.Code===XDataCode.DatabaseHandle&&tag.Value.replace(/^0+/,'').length!==0)yield tag.Value;}
  /** Internal structural document adapter; it does not register or generate an ownership graph. */
  Validate(document){
    if(this.IsErased)throw new InvalidOperationException('An erased section cannot be adopted, cloned or written.');
    if(document.DrawingVariables.AcadVer<DxfVersion.AutoCad2007)throw new NotSupportedException('SECTION requires R2007 or later.');
    if(this.PendingInputReferences)return;
    if(this.ExtensionDictionary!==null&&(this.ExtensionDictionary.Database!==document.Objects||this.ExtensionDictionary.Owner!==this))throw new InvalidOperationException('A section extension dictionary must have its reciprocal registered owner.');
    for(const reactor of this.PersistentReactors)if(!document.Objects.IsRegistered(reactor))throw new InvalidOperationException('Section reactors must be registered.');
    for(const reactor of this.Reactors)if(!document.Objects.IsRegistered(reactor))throw new InvalidOperationException('Section reactors must be registered.');
    for(const handle of this.$xdataReferences())if(document.GetObjectByHandle(handle)==null)throw new InvalidOperationException('Section XData references must resolve.');
    if(this.GeometrySettings!==null&&(this.GeometrySettings.Database!==document.Objects||this.GeometrySettings.Owner!==this))throw new InvalidOperationException('Section settings must be registered and owned by that section.');
  }
  TransformBy(transformation,translation){RequireInertIdentity(this,transformation,translation,true);}
  CloneValues(cloneResources=true){
    const copy=new Section(this.CodeName);
    for(const name of ['Name','State','Flags','VerticalDirection','TopHeight','BottomHeight','IndicatorTransparency','StoredIndicatorColor','StoredNativeIndicatorColor','IndicatorColorName','HasSettingsField'])copy[name]=this[name];
    copy.Layer=cloneResources?this.Layer.Clone():this.Layer;copy.Linetype=cloneResources?this.Linetype.Clone():this.Linetype;
    copy.Color=this.Color.Clone();copy.Lineweight=this.Lineweight;copy.Transparency=this.Transparency.Clone();copy.LinetypeScale=this.LinetypeScale;copy.IsVisible=this.IsVisible;copy.Normal=this.Normal;
    for(const v of this.Vertices)copy.Vertices.Add(v);for(const v of this.BackLineVertices)copy.BackLineVertices.Add(v);
    for(const data of this.XData.Values)copy.XData.Add(cloneResources?data.Clone():data.CopyStoredGraph());
    this.CopyCommonDataTo(copy);return copy;
  }
  Clone(){
    if(this.IsErased)throw new InvalidOperationException('An erased section cannot be cloned.');
    if(this.GeometrySettings!==null||this.ExtensionDictionary!==null||this.PersistentReactors.Count!==0||this.Reactors.Count!==0)throw new NotSupportedException('Section ownership graphs require explicit reference mapping.');
    for(const handle of this.$xdataReferences())throw new NotSupportedException('Section XData references require explicit reference mapping.');
    return this.CloneValues();
  }
}
