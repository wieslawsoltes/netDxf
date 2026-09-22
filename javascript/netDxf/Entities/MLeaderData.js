// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {Vector3} from '../Vector3.js';
import {DxfDatabaseObject} from '../Objects/DxfDatabaseObject.js';
import {Copy} from '../../runtime/GeometryRuntime.js';
import {ReferenceList} from '../../runtime/ReferenceList.js';
import {CheckStoredName} from '../../runtime/CheckedCollection.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException,NullReferenceException} from '../../runtime/Errors.js';
export const MLeaderComponentTypes=new Map();
export class MLeaderField {
  constructor(code,type,value,reference,year){this.Code=code;this.Type=type;this.Default=value;this.Reference=reference;this.MinimumVersion=year===2010?16:year===2013?17:15;Object.freeze(this);}
  Accepts(value){const t=this.Type;return typeof t==='function'?value instanceof t():t==='Vector3'?value instanceof Vector3:t==='double'?typeof value==='number':t==='short'||t==='int'?Number.isInteger(value)&&value>=(t==='short'?-32768:-2147483648)&&value<=(t==='short'?32767:2147483647):typeof value===({bool:'boolean',string:'string'}[t]);}
}
/** Stored component data. Referenced document objects remain references; value vectors copy. */
export class MLeaderData {
  #values=new Map(); Parent=null;
  Field(code){return this.Fields.find(f=>f.Code===code)??null;}
  Value(field){if(field==null)throw new NullReferenceException();return Copy(this.#values.has(field.Code)?this.#values.get(field.Code):field.Default);}
  Get(code){return this.Value(this.Field(code));}
  Set(code,value){
    const field=this.Field(code);if(field===null)throw new ArgumentException('Unknown MULTILEADER field.','code');
    if(value==null){if(field.Default!==null&&!field.Reference)throw new ArgumentNullException('value');value=null;}
    else{
      if(!field.Accepts(value))throw new ArgumentException('Invalid MULTILEADER field type.','value');
      if(field.Type==='double'||value instanceof Vector3)MLeaderData.Finite(value);
      if(typeof value==='string')CheckStoredName(value,'value',{allowEmpty:true});
      if(field.Reference&&this.Document!==null)this.Document.Objects.CheckRegistered(value);
    }
    this.#values.set(code,Copy(value));
  }
  get Document(){let parent=this.Parent;while(parent instanceof MLeaderData)parent=parent.Parent;if(parent instanceof DxfDatabaseObject)return parent.Database?.Document??null;return parent?.Type===36?MLeaderData.RegisteredDocument(parent):null;}
  // A typed document host must provide both identity lookup and object registration.
  // This hook does not manufacture a document or treat detached ownership as registration.
  static RegisteredDocument(item){let owner=item;while(owner!=null){if(typeof owner.GetObjectByHandle==='function'&&owner.DrawingVariables)return owner.GetObjectByHandle(item.Handle)===item?owner:null;owner=owner.Owner;}return null;}
  get Children(){return [];}
  Tree(){const root=this;return{*[Symbol.iterator](){yield root;for(const child of root.Children)yield*child.Tree();}};}
  get References(){const root=this;return{*[Symbol.iterator](){for(const item of root.Tree())for(const field of item.Fields){const target=item.Value(field);if(field.Reference&&target!==null)yield target;}}};}
  CheckDocument(document){if(document==null)return;for(const target of this.References)document.Objects.CheckRegistered(target);}
  static Finite(value){if(value instanceof Vector3){this.Finite(value.X);this.Finite(value.Y);this.Finite(value.Z);}else if(!Number.isFinite(value))throw new ArgumentOutOfRangeException('value','MULTILEADER coordinates and parameters must be finite.');}
  static ValidateDirection(value){this.Finite(value);if(value.X===0&&value.Y===0&&value.Z===0)throw new InvalidOperationException('MULTILEADER directions cannot be zero.');}
  ValidateValues(version){for(const item of this.Tree())for(const field of item.Fields)if(item.Value(field)!==null&&version<field.MinimumVersion)throw new NotSupportedException('MULTILEADER group '+field.Code+' is outside the selected qualified DXF profile.');}
  CopyValuesTo(copy){for(const [key,value]of this.#values){if(copy.#values.has(key))throw new ArgumentException('An item with the same key has already been added.');copy.#values.set(key,Copy(value));}}
  MapReferences(resolve){const changes=[];for(const item of this.Tree())for(const field of item.Fields){const old=item.Value(field);if(field.Reference&&old!==null){const next=resolve(old);if(next==null||!field.Accepts(next))throw new ArgumentException('MULTILEADER reference mapping changed the required target type.','resolve');if(this.Document!==null)this.Document.Objects.CheckRegistered(next);changes.push([item,field.Code,next]);}}for(const [item,code,next]of changes)item.#values.set(code,next);}
  Clone(){const copy=new this.constructor();this.CopyValuesTo(copy);this.CopyChildrenTo(copy);return copy;}
  CopyChildrenTo(copy){}
}
export class MLeaderChildCollection extends ReferenceList {
  #owner;#type;
  constructor(owner,type){super();this.#owner=owner;this.#type=type;}
  #check(item){if(item==null)throw new ArgumentNullException('item');const T=MLeaderComponentTypes.get(this.#type);if(!T||!(item instanceof T))throw new ArgumentException('Invalid MULTILEADER component type.','item');if(item.Parent!==null)throw new ArgumentException('The MULTILEADER component already has a parent. Clone it before sharing.','item');item.CheckDocument(this.#owner.Document);}
  Add(item){this.Insert(this.Count,item);}
  Insert(index,item){if(!Number.isInteger(index)||index<0||index>this.Count)throw new ArgumentOutOfRangeException('index');this.#check(item);super.Insert(index,item);item.Parent=this.#owner;}
  set_Item(index,item){const old=this.get_Item(index);if(old===item)return;this.#check(item);super.set_Item(index,item);old.Parent=null;item.Parent=this.#owner;}
  RemoveAt(index){const old=this.get_Item(index);super.RemoveAt(index);old.Parent=null;}
  Clear(){for(const item of this)item.Parent=null;super.Clear();}
}

/** Join original .Fields partial declarations to their value-class prototypes. */
export function InstallMLeaderFields(Type,rows){
  const fields=rows.map(([,code,type,value,reference,year])=>new MLeaderField(code,type,value,reference,year));
  Object.defineProperty(Type.prototype,'Fields',{get(){return fields;}});
  for(const [name,code]of rows)Object.defineProperty(Type.prototype,name,{get(){return this.Get(code);},set(value){this.Set(code,value);}});
}
