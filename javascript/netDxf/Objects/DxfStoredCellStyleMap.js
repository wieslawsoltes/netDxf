// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { SetCellStyleMapState,CellStyleMapState } from '../../runtime/CellStyleMapState.js';
import { TableSnapshot,TableHandleMap,IsTableReference,ValidTableUtf16 } from '../../runtime/TablePayload.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView,RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { FormatException,NotSupportedException,ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { InstallCellStyleMapEditing } from './DxfStoredCellStyleMap.Edit.js';
const prefix=(value,amount)=>{if(value.length<amount)throw new ArgumentOutOfRangeException('length');return value.slice(0,-amount);};
export class DxfStoredCellStyleMapEntry {
  constructor(id,type,name,format,nameIndex){this.Id=id;this.StoredType=type;this.Name=name;this.FormatPayload=TableSnapshot(format);this.NameIndex=nameIndex;Object.freeze(this);}
}
/** Internal retained packet adapter. File-profile/admission checks belong to the reader. */
export class DxfStoredCellStyleMap extends DxfDatabaseObject {
  static get MaximumPayloadTags(){return 1048576;}
  static get FrameNames(){return Object.freeze(['TABLEFORMAT','CONTENTFORMAT','CELLMARGIN','GRIDFORMAT','CELLSTYLE']);}
  constructor(source,tags,decode) {
    super('CELLSTYLEMAP');tags=Array.from(tags);let index=0;
    const read=code=>{if(index>=tags.length||tags[index].Code!==code)throw new FormatException('CELLSTYLEMAP requires ordered group '+code+'.');return tags[index++];};
    const marker=(code,value)=>{if(read(code).Value!==value)throw new FormatException('CELLSTYLEMAP requires stored marker '+value+'.');};
    marker(100,'AcDbCellStyleMap');const count=read(90).Value;
    if(count<0||count>DxfStoredCellStyleMap.MaximumPayloadTags||count>Math.trunc((tags.length-index)/8))throw new FormatException('CELLSTYLEMAP count exceeds its stored payload or storage limit.');
    const entries=[];
    for(let item=0;item<count;item++) {
      marker(300,'CELLSTYLE');const start=index;marker(1,'TABLEFORMAT_BEGIN');const frames=['TABLEFORMAT'];
      while(frames.length) {
        if(index>=tags.length)throw new FormatException('CELLSTYLEMAP has an unterminated format packet.');
        const tag=tags[index++];
        if(tag.Code===1) {
          const frame=prefix(tag.Value,6);
          if(frame==='CELLSTYLE'||frames.length>=64)throw new FormatException('CELLSTYLEMAP format nesting is invalid or exceeds its storage limit.');frames.push(frame);
        }else if(tag.Code===309){if(frames.pop()!==prefix(tag.Value,4))throw new FormatException('CELLSTYLEMAP has mismatched format framing.');}
        else if(tag.Code===100)throw new FormatException('CELLSTYLEMAP has an unexpected subclass in its format packet.');
      }
      const format=tags.slice(start,index);marker(1,'CELLSTYLE_BEGIN');const id=read(90).Value,type=read(91).Value,nameIndex=index,name=decode(read(300).Value);marker(309,'CELLSTYLE_END');
      entries.push(new DxfStoredCellStyleMapEntry(id,type,name,format,nameIndex));
    }
    if(index!==tags.length)throw new FormatException('CELLSTYLEMAP contains unexpected data after its counted entries.');
    SetCellStyleMapState(this,{source,version:source.DrawingVariables.AcadVer,payload:TableSnapshot(tags),entries:TableSnapshot(entries),references:new ReferenceList(),handles:new TableHandleMap(),sourceOwner:null,resolved:false,editing:false,reentered:false});
  }
  get SourceVersion(){return CellStyleMapState(this).version;}
  get Payload(){return CellStyleMapState(this).payload;}
  get Entries(){return CellStyleMapState(this).entries;}
  get References(){return ReadOnlyReferenceView(CellStyleMapState(this).references);}
  get DatabaseReferences(){const state=CellStyleMapState(this);return Array.from(state.references).filter(item=>state.source.GetObjectByHandle(item.Handle)===item);}
  get AllocationReservations(){return this.Payload;}
  CloneShell(){throw new NotSupportedException('Stored CELLSTYLEMAP cloning requires its complete application schema.');}
  Resolve(resolve) {
    const state=CellStyleMapState(this);
    for(const tag of this.Payload) {
      if(!IsTableReference(tag)||BigInt('0x'+tag.Value)===0n)continue;
      const target=resolve(tag.Value);if(target===null)throw new FormatException('CELLSTYLEMAP requires an exact source reference identity: '+tag.Value);
      state.handles.set(tag.Value,target);state.references.Add(target);
    }
    state.sourceOwner=this.Owner;
    if(state.sourceOwner===null||state.source.GetObjectByHandle(state.sourceOwner.Handle)!==state.sourceOwner)throw new FormatException('CELLSTYLEMAP requires a registered source owner.');
    const ancestry=new Set();
    for(let ancestor=state.sourceOwner;ancestor!==null&&ancestor!==state.source;ancestor=ancestor.Owner) {
      if(ancestor===this||ancestry.has(ancestor))throw new FormatException('CELLSTYLEMAP source ownership contains a cycle.');ancestry.add(ancestor);
      if(state.source.GetObjectByHandle(ancestor.Handle)!==ancestor)throw new FormatException('CELLSTYLEMAP source ancestry contains an unregistered object.');
    }
    state.resolved=true;
  }
  ValidateDatabaseSchema(database,errors) {
    const state=CellStyleMapState(this);
    if(!state.resolved||database.Document!==state.source){errors.Add('Stored CELLSTYLEMAP must remain in its source document.');return;}
    if(state.source.DrawingVariables.AcadVer!==this.SourceVersion)errors.Add('Stored CELLSTYLEMAP conversion requires complete schema regeneration.');
    if(this.Owner!==state.sourceOwner||state.source.GetObjectByHandle(state.sourceOwner.Handle)!==state.sourceOwner)errors.Add('Stored CELLSTYLEMAP source ownership changed.');
    for(const [handle,target] of state.handles)if(state.source.StoredTableHandleTarget(handle)!==target)errors.Add('A stored CELLSTYLEMAP dependency is no longer registered: '+handle);
    for(const tag of this.Payload)if(typeof tag.Value==='string'&&!ValidTableUtf16(tag.Value))errors.Add('Stored CELLSTYLEMAP contains invalid UTF-16 text.');
  }
}
InstallCellStyleMapEditing(DxfStoredCellStyleMap,DxfStoredCellStyleMapEntry);
RegisterDatabaseModel('DxfStoredCellStyleMap',DxfStoredCellStyleMap);
