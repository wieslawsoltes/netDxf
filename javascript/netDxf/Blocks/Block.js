// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { TableObject } from '../Tables/TableObject.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Layer } from '../Tables/Layer.js';
import { Vector3 } from '../Vector3.js';
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { EntityType } from '../Entities/EntityType.js';
import { Hatch } from '../Entities/Hatch.js';
import { Section } from '../Entities/Section.js';
import { HatchSourceRelations } from '../Entities/HatchSourceRelations.js';
import { Polyline3D } from '../Entities/Polyline3D.js';
import { MText } from '../Entities/MText.js';
import { EntityCollection } from '../Collections/EntityCollection.js';
import { AttributeDefinitionDictionary } from '../Collections/AttributeDefinitionDictionary.js';
import { BlockRecord } from './BlockRecord.js';
import { EndBlock } from './EndBlock.js';
import { BlockTypeFlags as F } from './BlockTypeFlags.js';
import { BlockEntityChangeEventArgs } from './BlockEntityChangeEventArgs.js';
import { BlockAttributeDefinitionChangeEventArgs } from './BlockAttributeDefinitionChangeEventArgs.js';
import { InstallBlockReferenceRename } from './Block.ReferenceRename.js';
import { EventHook } from '../../runtime/EventHook.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { SupportFileSystem } from '../../runtime/SupportFileSystem.js';
import { ArgumentException, ArgumentNullException, NullReferenceException } from '../../runtime/Errors.js';
const exact=Symbol('exact block constructor');
const ref=value=>{if(value==null)throw new NullReferenceException();return value;};
export class Block extends TableObject {
  static DefaultModelSpaceName='*Model_Space';static DefaultPaperSpaceName='*Paper_Space';
  #entities;#attributes;#end;#description='';#origin=Vector3.Zero;#layer=Layer.Default;#flags=0;#xref='';#internal;
  static get ModelSpace(){return new Block(exact,{name:Block.DefaultModelSpaceName,checkName:false});}
  static get PaperSpace(){return new Block(exact,{name:Block.DefaultPaperSpaceName,checkName:false});}
  constructor(...args){
    let name,entities=null,attributes=null,checkName=true,xref,overlay=false;
    if(args[0]===exact)({name,entities=null,attributes=null,checkName=true,xref,overlay=false}=args[1]);
    else{
      name=args[0];
      if(typeof args[1]==='string'||typeof args[2]==='boolean'){xref=args[1];overlay=args[2]??false;}
      else{entities=args[1]??null;attributes=args[2]??null;}
      if(args.length<1||args.length>3)throw new ArgumentException('No matching Block constructor.');
    }
    super(name,DxfObjectCode.Block,checkName);
    if(name==null||name==='')throw new ArgumentNullException('name');
    this.IsReserved=OrdinalIgnoreCaseEquals(name,Block.DefaultModelSpaceName);
    this.#internal=name.startsWith('*');this.Owner=new BlockRecord(name);this.#end=new EndBlock(this);
    for(const event of ['LayerChanged','EntityAdded','EntityRemoved','AttributeDefinitionAdded','AttributeDefinitionRemoved'])
      Object.defineProperty(this,event,{value:new EventHook(),enumerable:true});
    this.#entities=new EntityCollection();
    this.#entities.BeforeAddItem.Add((_,e)=>{
      const item=e.Item;
      if(item instanceof Hatch&&item.Owner===null)HatchSourceRelations.ValidateOwner(item,this);
      if(item instanceof Section&&this.Record.Owner!==null)item.Validate(this.Record.Owner.Owner);
      if(item!==null&&item.Owner===null&&this.Record.Owner!==null)this.Record.Owner.Owner.ValidateStoredTableEntityAdoption(item);
      e.Cancel=item==null||!!(this.Flags&F.ExternallyDependent)||item.Owner!==null;
    });
    this.#entities.AddItem.Add((_,e)=>{
      const item=ref(e.Item);
      if(item.Type===EntityType.Leader&&item.Annotation!==null)this.#entities.Add(item.Annotation);
      else if(item instanceof Hatch){for(const path of item.BoundaryPaths)for(const entity of path.Entities)if(entity.Owner===null)this.#entities.Add(entity);}
      else if(item.Type===EntityType.Viewport&&item.ClippingBoundary!==null)this.#entities.Add(item.ClippingBoundary);
      this.OnEntityAddedEvent(item);item.Owner=this;
    });
    this.#entities.BeforeRemoveItem.Add((_,e)=>{
      const item=ref(e.Item);
      e.Cancel=item.Reactors.Count>0||(this.Record.Owner!==null&&this.Record.Owner.Owner.StoredTableReferencesRemoval(item))||item.Owner!==this;
    });
    this.#entities.RemoveItem.Add((_,e)=>{this.OnEntityRemovedEvent(e.Item);ref(e.Item).Owner=null;});
    if(entities!==null)this.#entities.AddRange(entities);
    this.#attributes=new AttributeDefinitionDictionary();
    this.#attributes.BeforeAddItem.Add((_,e)=>{e.Cancel=e.Item==null||!!(this.Flags&F.ExternallyDependent)||this.#attributes.ContainsTag(e.Item.Tag)||e.Item.Owner!==null;});
    this.#attributes.AddItem.Add((_,e)=>{this.OnAttributeDefinitionAddedEvent(e.Item);ref(e.Item).Owner=this;this.#flags|=F.NonConstantAttributeDefinitions;});
    this.#attributes.BeforeRemoveItem.Add((_,e)=>{e.Cancel=ref(e.Item).Owner!==this||(this.Record.Owner!==null&&this.Record.Owner.Owner.MLeaderReferences(e.Item).Count>0);});
    this.#attributes.RemoveItem.Add((_,e)=>{this.OnAttributeDefinitionRemovedEvent(e.Item);ref(e.Item).Owner=null;if(this.#attributes.Count===0)this.#flags&=~F.NonConstantAttributeDefinitions;});
    if(attributes!==null)this.#attributes.AddRange(attributes);
    if(xref!==undefined){
      if(xref==null||xref==='')throw new ArgumentNullException('xrefFile');
      // Preserve the original index==0 check rather than silently repairing it.
      if(SupportFileSystem.InvalidPathChars.includes(xref[0]))throw new ArgumentException('File path contains invalid characters.','xrefFile');
      this.#xref=xref;this.#flags=F.XRef|F.ResolvedExternalReference|(overlay?F.XRefOverlay:0);
    }
  }
  static CreateOverload(signature,...args){
    const select=options=>new Block(exact,options);
    if(signature==='string')return select({name:args[0]});
    if(signature==='string,string'||signature==='string,string,bool')return select({name:args[0],xref:args[1],overlay:args[2]??false});
    const prefix='string,System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>';
    if(signature===prefix)return select({name:args[0],entities:args[1]});
    if(signature===prefix+',System.Collections.Generic.IEnumerable<netDxf.Entities.AttributeDefinition>')return select({name:args[0],entities:args[1],attributes:args[2]});
    if(signature===prefix+',System.Collections.Generic.IEnumerable<netDxf.Entities.AttributeDefinition>,bool')return select({name:args[0],entities:args[1],attributes:args[2],checkName:args[3]});
    throw new ArgumentException('Unknown Block constructor signature.','signature');
  }
  get Name(){return super.Name;}
  set Name(value){
    if(this.#internal&&!/^\*[ut]/i.test(this.Name))throw new ArgumentException('Blocks for internal use cannot be renamed.','value');
    // TableObject.SetName is nonvirtual in C#; do not dispatch to the hidden internal method.
    TableObject.prototype.SetName.call(this,value,true);
    if(this.#internal)this.#flags&=~F.AnonymousBlock;
    this.Record.Name=value;
  }
  SetName(name,checkName){TableObject.prototype.SetName.call(this,name,checkName);this.Record.Name=name;this.#internal=name.startsWith('*');}
  get Description(){return this.#description;}set Description(v){this.#description=v==null||v===''?'':v;}
  get Origin(){return Copy(this.#origin);}set Origin(v){this.#origin=Copy(v);}
  get Layer(){return this.#layer;}set Layer(v){if(v==null)throw new ArgumentNullException('value');this.#layer=this.OnLayerChangedEvent(this.#layer,v);}
  get Entities(){return this.#entities;}get AttributeDefinitions(){return this.#attributes;}
  get Record(){return this.Owner;}get End(){return this.#end;}
  get Flags(){return this.#flags;}set Flags(v){this.#flags=v;}
  get XrefFile(){return this.#xref;}get IsXRef(){return !!(this.#flags&F.XRef);}
  get IsForInternalUseOnly(){return this.#internal;}
  OnLayerChangedEvent(oldValue,newValue){const e=new TableObjectChangedEventArgs(oldValue,newValue);this.LayerChanged.Invoke(this,e);return e.NewValue;}
  OnEntityAddedEvent(item){this.EntityAdded.Invoke(this,new BlockEntityChangeEventArgs(item));}
  OnEntityRemovedEvent(item){this.EntityRemoved.Invoke(this,new BlockEntityChangeEventArgs(item));}
  OnAttributeDefinitionAddedEvent(item){this.AttributeDefinitionAdded.Invoke(this,new BlockAttributeDefinitionChangeEventArgs(item));}
  OnAttributeDefinitionRemovedEvent(item){this.AttributeDefinitionRemoved.Invoke(this,new BlockAttributeDefinitionChangeEventArgs(item));}
  HasReferences(){return ref(this.Owner).Owner!==null&&this.Owner.Owner.HasReferences(this.Name);}
  GetReferences(){return ref(this.Owner).Owner===null?null:this.Owner.Owner.GetReferences(this.Name);}
  AddPreparedSection(section){this.#entities.AddPreparedSection(section);ref(section).Owner=this;}
  RemovePreparedSection(section){this.#entities.RemovePreparedSection(section);}
  Clone(newName){
    const checkName=arguments.length>0||!(this.#flags&F.AnonymousBlock);if(arguments.length===0)newName=this.Name;
    Polyline3D.RejectStoredRecordBlockClone(this);
    if(this.Record.Layout!==null&&!TableObject.IsValidName(newName))throw new ArgumentException('*Model_Space and *Paper_Space# blocks can only be cloned with a new valid name.');
    const copy=new Block(exact,{name:newName,checkName});copy.Description=this.#description;copy.Flags=this.#flags;copy.Layer=ref(this.Layer).Clone();copy.Origin=this.#origin;
    if(checkName)copy.Flags&=~F.AnonymousBlock;
    const copies=new Map();for(const entity of this.#entities){const cloned=entity.Clone();if(copies.has(entity))throw new ArgumentException('Duplicate entity key.');copies.set(entity,cloned);copy.Entities.Add(cloned);}
    MText.RelinkClonedColumns(copies);
    for(const a of this.#attributes.Values)copy.AttributeDefinitions.Add(a.Clone());
    for(const data of this.XData.Values)copy.XData.Add(data.Clone());
    for(const data of this.Record.XData.Values)copy.Record.XData.Add(data.Clone());
    for(const data of this.End.XData.Values)copy.End.XData.Add(data.Clone());
    return copy;
  }
  AssignHandle(number){number=this.Owner.AssignHandle(number);number=this.#end.AssignHandle(number);for(const a of this.#attributes.Values)number=a.AssignHandle(number);return DxfObject.prototype.AssignHandle.call(this,number);}
}
InstallBlockReferenceRename(Block);
