// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceView } from '../../runtime/DatabaseModel.js';
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { DxfTableStyleHeader,DxfTableStyleRow } from './DxfTableStyle.Projection.js';
import { TableSnapshot,IsTableReference,ValidTableUtf16,TableHandleMap } from '../../runtime/TablePayload.js';
import { SetTableStyleState,TableStyleState } from '../../runtime/TableStyleState.js';
import { RegisterDatabaseModel,IsDatabaseModel } from '../../runtime/DatabaseModel.js';
import { NotSupportedException,FormatException,ArgumentException } from '../../runtime/Errors.js';
import { InstallTableStyleEditing } from './DxfTableStyle.Edit.js';
function publicTags(tags) {
  const result=[];let active=false,seen=false,depth=0;
  for(const tag of tags) {
    if(tag.Code===102) {
      const marker=tag.Value;if(marker.startsWith('{'))depth++;else if(marker==='}'&&depth>0)depth--;else throw new FormatException('Invalid TABLESTYLE application control group.');continue;
    }
    if(depth>0)continue;
    if(tag.Code===100){active=tag.Value==='AcDbTableStyle';if(active&&seen)return [];seen||=active;continue;}
    if(active)result.push(tag);
  }
  if(depth!==0)throw new FormatException('Unterminated TABLESTYLE application control group.');return result;
}
/** Internal loader constructor is explicit in JS; this is not a typed DXF reader. */
export class DxfTableStyle extends DxfDatabaseObject {
  constructor(source,tags,decode) {
    super('TABLESTYLE');const version=source.DrawingVariables.AcadVer;
    if(version<14)throw new NotSupportedException('Stored TABLESTYLE requires an AutoCAD 2004 or later source profile.');
    tags=Array.from(tags);const visible=publicTags(tags),first=visible.findIndex(t=>t.Code===7),starts=[];
    for(let i=0;i<visible.length;i++)if(visible[i].Code===7)starts.push(i);
    const rows=starts.length===3?starts.map((start,i)=>new DxfTableStyleRow(visible.slice(start,starts[i+1]??visible.length),decode)):[];
    SetTableStyleState(this,{source,version,tags:TableSnapshot(tags),publicTags:visible,
      header:DxfTableStyleHeader.TryRead(first<0?visible:visible.slice(0,first),decode,version),rows:TableSnapshot(rows),
      references:new ReferenceList(),namedStyles:new Map(),handles:new TableHandleMap(),resolved:false,cellStyleMap:null,editing:false,reentered:false});
  }
  get SourceVersion(){return TableStyleState(this).version;}
  get Tags(){return TableStyleState(this).tags;}
  get Header(){return TableStyleState(this).header;}
  get Rows(){return TableStyleState(this).rows;}
  get References(){return ReadOnlyReferenceView(TableStyleState(this).references);}
  get CellStyleMap(){return TableStyleState(this).cellStyleMap;}
  get StoredCellStyleMap(){return IsDatabaseModel(this.CellStyleMap,'DxfStoredCellStyleMap')?this.CellStyleMap:null;}
  get DatabaseReferences(){const state=TableStyleState(this);return Array.from(state.references).filter(item=>state.source.GetObjectByHandle(item.Handle)===item);}
  get AllocationReservations(){return this.Tags;}
  CloneShell(){throw new NotSupportedException('Stored TABLESTYLE cloning requires its complete application schema.');}
  Resolve(resolve,decode) {
    const state=TableStyleState(this);
    for(const tag of this.Tags)if(IsTableReference(tag)){const target=resolve(tag.Value);if(target===null)continue;state.handles.set(tag.Value,target);state.references.Add(target);}
    for(const tag of state.publicTags.filter(t=>t.Code===7)) {
      const name=decode(tag.Value),out={};
      if(!state.source.TextStyles.TryGetValue(name,out)||resolve(out.value.Handle)!==out.value)continue;
      if(state.namedStyles.has(tag))throw new ArgumentException('An item with the same key has already been added.');
      state.namedStyles.set(tag,[out.value,out.value.Name]);state.references.Add(out.value);
    }
    for(const row of this.Rows){const binding=state.namedStyles.get(row.Tags.get_Item(0));if(binding)row.BindTextStyle(binding[0]);}
    if(this.ExtensionDictionary!==null&&this.ExtensionDictionary.Contains('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP')) {
      const map=this.ExtensionDictionary.get_Item('ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP');
      if(map instanceof DxfDatabaseObject&&map.CodeName==='CELLSTYLEMAP'&&map.Owner===this.ExtensionDictionary)state.cellStyleMap=map;
    }
    state.resolved=true;
  }
  ChangedStyleName(tag){const binding=TableStyleState(this).namedStyles.get(tag);return !binding||binding[0].Name===binding[1]?null:binding[0].Name;}
  ValidateDatabaseSchema(database,errors) {
    const state=TableStyleState(this);
    if(!state.resolved||database.Document!==state.source)errors.Add('Stored TABLESTYLE must remain in its source document.');
    if(database.Document.DrawingVariables.AcadVer!==this.SourceVersion)errors.Add('Stored TABLESTYLE conversion requires complete schema regeneration.');
    for(const [handle,target] of state.handles)if(state.source.StoredTableHandleTarget(handle)!==target)errors.Add('A stored TABLESTYLE handle dependency is no longer registered: '+handle);
    for(const binding of state.namedStyles.values())if(state.source.GetObjectByHandle(binding[0].Handle)!==binding[0])errors.Add('A stored TABLESTYLE text-style dependency is no longer registered.');
    if(this.CellStyleMap!==null&&(state.source.GetObjectByHandle(this.CellStyleMap.Handle)!==this.CellStyleMap||this.CellStyleMap.Owner!==this.ExtensionDictionary))errors.Add('A stored TABLESTYLE map dependency changed its identity or owner.');
    for(const tag of this.Tags)if(typeof tag.Value==='string'&&!ValidTableUtf16(tag.Value))errors.Add('Stored TABLESTYLE contains invalid UTF-16 text.');
  }
}
InstallTableStyleEditing(DxfTableStyle);
RegisterDatabaseModel('DxfTableStyle',DxfTableStyle);
