// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfTag } from '../IO/DxfTag.js';
import { DxfHandleKind } from '../IO/DxfGroupCode.js';
import { IsAncestor } from '../../runtime/DatabaseModel.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException } from '../../runtime/Errors.js';
export const OwnershipState = new WeakMap();
export function GetOwnership(record) {
  if (!OwnershipState.has(record)) OwnershipState.set(record, { content:null,geometry:null,cellData:null,contentSlot:-1,geometrySlot:-1 });
  return OwnershipState.get(record);
}
export function InstallDeclaredOwnership(Type) {
  Object.defineProperty(Type, 'TableRoundtripMarker', {value:'ACAD_ROUNDTRIP_2008_TABLE_ENTITY'});
  Object.defineProperties(Type.prototype, {
    IsSchemaManaged: {get() { return GetOwnership(this).content !== null; }},
    IsTableRoundtripRecord: {get() { return this.Data.Count > 0 && this.Data.get_Item(0).Code === 102 && this.Data.get_Item(0).Value === Type.TableRoundtripMarker; }},
    DeclaredOwnedObjects: {get() { const s=GetOwnership(this); return s.content === null ? [] : s.cellData === null ? [s.content,s.geometry] : [s.content,s.geometry,s.cellData]; }},
    DatabaseReferences: {get() { return this.DeclaredOwnedObjects; }}
  });
  Type.prototype.CheckPayloadEditable = function() { if (this.IsSchemaManaged) throw new InvalidOperationException('This XRECORD payload is controlled by its typed ownership schema.'); };
  Type.prototype.CheckOwnedCandidate = function(child, slot) {
    if (child.IsErased) throw new InvalidOperationException('An erased object cannot be attached again.');
    if (child.Owner !== null && child.Owner !== this) throw new ArgumentException('A schema child already has another owner.');
    if (IsAncestor(child, this)) throw new ArgumentException('Declared ownership cannot form a cycle.');
    if (child.Database !== this.Database) throw new ArgumentException("Schema children must share the record's registration state and database.");
    if (this.Database !== null) {
      this.Database.CheckRegistered(this); this.Database.CheckRegistered(child);
      if (child.Owner !== this) throw new ArgumentException('An imported schema child must already declare this record as its owner.');
      if (!OrdinalIgnoreCaseEquals(slot.Value, child.Handle)) throw new ArgumentException('The stored ownership handle does not identify the schema child.');
    }
  };
  Type.prototype.BindTableRoundtripChildren = function(content, geometry) {
    if (this.IsErased) throw new InvalidOperationException('An erased record cannot bind owned objects.');
    if (content == null) throw new ArgumentNullException('content'); if (geometry == null) throw new ArgumentNullException('geometry');
    if (this.IsSchemaManaged) throw new InvalidOperationException('The ownership schema is already bound.');
    if (!this.IsTableRoundtripRecord) throw new ArgumentException('The TABLE roundtrip marker is missing.');
    if (content.CodeName !== 'TABLECONTENT' || geometry.CodeName !== 'TABLEGEOMETRY') throw new ArgumentException('The TABLE roundtrip slots require TABLECONTENT and TABLEGEOMETRY targets.');
    let contentSlot=-1,geometrySlot=-1;
    for(let i=1;i<this.Data.Count;i++) {
      const tag=this.Data.get_Item(i);
      if (tag.Code===360 && contentSlot<0) contentSlot=i;
      else if (tag.Code===361 && geometrySlot<0) geometrySlot=i;
      else if (tag.Code===102 || tag.HandleKind===DxfHandleKind.HardOwner || tag.HandleKind===DxfHandleKind.SoftOwner) throw new ArgumentException('The TABLE roundtrip ownership envelope contains an unexpected or duplicate slot.');
    }
    if (contentSlot<0 || geometrySlot<0) throw new ArgumentException('Both TABLE roundtrip ownership slots are required.');
    this.CheckOwnedCandidate(content,this.Data.get_Item(contentSlot)); this.CheckOwnedCandidate(geometry,this.Data.get_Item(geometrySlot));
    Object.assign(GetOwnership(this),{content,geometry,contentSlot,geometrySlot}); content.Owner=this; geometry.Owner=this;
  };
  Type.prototype.CopyDatabaseReferencesTo = function(target, resolve) {
    const s=GetOwnership(this);
    if (s.cellData!==null) target.BindCompositeTableRoundtripChildren(resolve(s.content),resolve(s.geometry),resolve(s.cellData));
    else if (s.content!==null) target.BindTableRoundtripChildren(resolve(s.content),resolve(s.geometry));
  };
  Type.prototype.MaterializeOwnedObjectReferences = function() {
    const s=GetOwnership(this); if(s.content===null)return;
    this.ReplaceLoadedData(s.contentSlot,new DxfTag(360,s.content.Handle));
    this.ReplaceLoadedData(s.geometrySlot,new DxfTag(361,s.geometry.Handle));
    if (s.cellData!==null) this.ReplaceLoadedData(14,new DxfTag(360,s.cellData.Handle));
  };
  Type.prototype.ValidateDatabaseSchema = function(database,errors) {
    const s=GetOwnership(this); if(s.content===null)return;
    this.ValidateCompositeTableOwnership(errors);
    if (!this.IsTableRoundtripRecord || s.contentSlot>=this.Data.Count || s.geometrySlot>=this.Data.Count) { errors.Add('Invalid TABLE roundtrip ownership envelope: '+(this.Handle??'')); return; }
    if (this.Data.get_Item(s.contentSlot).Code!==360 || this.Data.get_Item(s.geometrySlot).Code!==361) errors.Add('Invalid TABLE roundtrip ownership slot codes: '+(this.Handle??''));
    if (s.content.CodeName!=='TABLECONTENT' || s.geometry.CodeName!=='TABLEGEOMETRY') errors.Add('Invalid TABLE roundtrip ownership target types: '+(this.Handle??''));
    if(this.Database!==null && (!OrdinalIgnoreCaseEquals(this.Data.get_Item(s.contentSlot).Value,s.content.Handle)||!OrdinalIgnoreCaseEquals(this.Data.get_Item(s.geometrySlot).Value,s.geometry.Handle))) errors.Add('TABLE roundtrip ownership handle mismatch: '+(this.Handle??''));
  };
}
/** Original database validation body, kept separate from the unported database registration engine. */
export function ValidateDeclaredOwnership(database, parent, candidates, errors, registered) {
  const children=Array.from(parent.DeclaredOwnedObjects); if(!children.length)return;
  const seen=new Set();
  for(const child of children) {
    if(child==null) { errors.Add('Null declared ownership slot: '+(parent.Handle??''));continue; }
    if(seen.has(child))errors.Add('Duplicate declared ownership target: '+(parent.Handle??''));seen.add(child);
    if(child.Owner!==parent)errors.Add('Declared ownership is not reciprocal: '+(parent.Handle??''));
    if(IsAncestor(child,parent))errors.Add('Declared ownership cycle: '+(parent.Handle??''));
    if(registered&&!database.IsRegistered(child))errors.Add('Unregistered declared child: '+(parent.Handle??''));
  }
  for(const candidate of candidates)if(candidate.Owner===parent&&candidate!==parent.ExtensionDictionary&&!seen.has(candidate))errors.Add("An object is owned outside its parent's declared slots: "+(candidate.Handle??''));
  if(!registered)parent.ValidateDatabaseSchema(database,errors);
}
