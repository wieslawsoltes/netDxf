// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Original OBJECTS dispatch/import. Callers provide the same physical stream and legacy table state.
import * as api from '../../index.js';
import * as payload from '../../runtime/DatabasePayloadIO.js';
import { DatabaseRecord } from '../../runtime/DatabaseIOContext.js';
import { DictionaryObject } from '../Objects/DictionaryObject.js';
import { XRecord } from '../Objects/XRecord.js';
import { XRecordEntry } from '../Objects/XRecordEntry.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { SourceHandle } from './DxfReader.SourceIdentity.js';
import { ReadMLeaderStylePayload } from '../../runtime/MLeaderStyleIO.js';
import { FormatException,InvalidCastException } from '../../runtime/Errors.js';
const key=value=>value==null?null:OrdinalIgnoreCaseKey(value);
export function ReadDatabaseRecord(context){
  const source=context.CurrentSourceRecord,chunk=context.Chunk,codeName=chunk.ReadString(),tags=[];
  chunk.Next();while(chunk.Code!==0){if(chunk.Code!==999)tags.push(new api.DxfTag(chunk.Code,chunk.Value));chunk.Next();}
  const complete=record=>{record.SourceIdentity=source;context.databaseRecords.Add(record);return record;};
  if(codeName==='TABLESTYLE'&&context.Document.DrawingVariables.AcadVer>=14&&tags.find(t=>t.Code===100)?.Value==='AcDbTableStyle')return complete(payload.ReadTableStyleRecord(context,tags));
  const special={CELLSTYLEMAP:'ReadStoredCellStyleMapRecord',TABLEGEOMETRY:'ReadStoredTableGeometryRecord',TABLECONTENT:'ReadStoredTableContentRecord',SUNSTUDY:'ReadStoredSunStudyRecord',SUN:'ReadSunRecord',DIMASSOC:'ReadStoredDimAssocRecord',DATATABLE:'ReadDataTableRecord',LAYER_INDEX:'ReadLayerIndexRecord'};
  if(Object.hasOwn(special,codeName))return complete(payload[special[codeName]](context,tags));
  if(codeName==='FIELD'||codeName==='ACAD_FIELD')return complete(payload.ReadStoredFieldRecord(context,codeName,tags));
  if(codeName==='SECTIONSETTINGS'||codeName==='SECTION_SETTINGS')return complete(payload.ReadSectionSettingsRecord(context,codeName,tags));
  if(codeName==='SECTIONMANAGER'||codeName==='SECTION_MANAGER')return complete(payload.ReadSectionManagerRecord(context,codeName,tags));
  if(codeName==='LAYER_FILTER'||codeName==='OBJECT_PTR')return complete(payload.ReadLayerFilterPointerRecord(context,codeName,tags));
  if(codeName==='XRECORD'){const output={};if(payload.TryReadPrivateXRecord(context,tags,output))return complete(output.value);}
  const result=new DatabaseRecord();result.SourceIdentity=source;let handle=null,start=0;
  for(;start<tags.length;start++){
    let tag=tags[start];if(tag.Code===100||tag.Code===1001)break;
    if(tag.Code===5)handle=tag.Value;
    else if(tag.Code===330)result.Metadata.Owner=tag.Value;
    else if(tag.Code===102){
      const group=tag.Value;let closed=false,depth=1;
      while(++start<tags.length){tag=tags[start];
        if(tag.Code===102){const control=tag.Value;if(control==='}'&&--depth===0){closed=true;break;}if(control.startsWith('{'))depth++;continue;}
        if(depth===1&&group==='{ACAD_XDICTIONARY'&&tag.Code===360)result.Metadata.Extension=tag.Value;
        if(depth===1&&group==='{ACAD_REACTORS'&&tag.Code===330)result.Metadata.Reactors.push(tag.Value);
      }
      if(!closed)throw new FormatException('Unterminated database control group.');
    }
  }
  if(codeName==='DICTIONARY'||codeName==='ACDBDICTIONARYWDFLT'){
    const dictionary=codeName==='DICTIONARY'?new api.DxfDictionary():new api.DxfDictionaryWithDefault();result.Object=dictionary;dictionary.IsHardOwner=false;let name=null;
    for(let i=start;i<tags.length;i++){const tag=tags[i];if(tag.Code===1001){context.ReadDatabaseXData(dictionary,tags,i);break;}
      switch(tag.Code){
        case 280:dictionary.IsHardOwner=tag.Value!==0;break;case 281:dictionary.Cloning=tag.Value;break;
        case 3:if(name!==null)throw new FormatException('Dictionary name has no associated object handle.');name=DecodeDxfText(tag.Value);break;
        case 350:case 360:if(name===null)throw new FormatException('Dictionary handle has no associated name.');result.Entries.push([name,tag.Value,tag.Code===360]);name=null;break;
        case 340:result.Default=tag.Value;break;
      }
    }
    if(name!==null)throw new FormatException('Dictionary name has no associated object handle.');
  }else if(codeName==='XRECORD'){
    const record=new api.DxfXRecord();result.Object=record;
    if(start<tags.length&&tags[start].Code===100&&tags[start].Value==='AcDbXrecord')start++;else throw new FormatException('XRECORD requires AcDbXrecord subclass data.');
    if(start<tags.length&&tags[start].Code===280)record.Cloning=tags[start++].Value;
    for(;start<tags.length;start++){let tag=tags[start];if(tag.Code===1001){context.ReadDatabaseXData(record,tags,start);break;}if(tag.ValueType===api.DxfTagValueType.String)tag=new api.DxfTag(tag.Code,DecodeDxfText(tag.Value));record.AddLoadedData(tag);}
  }else if(codeName==='DICTIONARYVAR'){
    const variable=new api.DxfDictionaryVariable();result.Object=variable;
    for(let i=start;i<tags.length;i++){const tag=tags[i];if(tag.Code===280)variable.Schema=tag.Value;else if(tag.Code===1)variable.Value=DecodeDxfText(tag.Value);else if(tag.Code===1001){context.ReadDatabaseXData(variable,tags,i);break;}}
  }else if(codeName==='ACDBPLACEHOLDER'){
    result.Object=new api.DxfPlaceholder();for(let i=start;i<tags.length;i++)if(tags[i].Code===1001){context.ReadDatabaseXData(result.Object,tags,i);break;}
  }else{
    const handlers=[payload.ReadStoredEnvelopePayload,payload.ReadContainerPayload,payload.ReadGeoDataPayload,payload.ReadOutputSettingsPayload,ReadMLeaderStylePayload,payload.ReadLightListPayload];
    if(!handlers.some(read=>read(context,result,codeName,tags,start)))result.Object=new api.DxfOpaqueObject(codeName,tags.slice(start));
  }
  result.Object.Handle=handle;context.databaseRecords.Add(result);return result;
}
export function ReadDictionaryDatabaseRecord(context){
  const record=ReadDatabaseRecord(context),typed=record.Object;
  if(!(typed instanceof api.DxfDictionary))throw new InvalidCastException();
  const legacy=new DictionaryObject(null);legacy.Handle=typed.Handle;legacy.IsHardOwner=typed.IsHardOwner;legacy.Cloning=typed.Cloning;
  for(const [name,handle]of record.Entries)if(!legacy.Entries.ContainsKey(handle))legacy.Entries.Add(handle,name);
  legacy.XData.AddRange(typed.XData.Values);return legacy;
}
export function ReadXRecordDatabaseRecord(context){
  const record=ReadDatabaseRecord(context),typed=record.Object;if(!(typed instanceof api.DxfXRecord))return null;
  const legacy=new XRecord();legacy.Handle=typed.Handle;legacy.OwnerHandle=record.Metadata.Owner;legacy.Flags=typed.Cloning;
  for(const tag of typed.Data)legacy.Entries.Add(new XRecordEntry(tag.Code,tag.Value));return legacy;
}
export function ApplyDatabaseMetadata(context,item,metadata){
  if(metadata.Extension!==null&&metadata.Extension!==''&&metadata.Extension!=='0'){
    const target=context.GetObjectBySourceHandle(metadata.Extension);item.ExtensionDictionary=target instanceof api.DxfDictionary?target:null;
    if(item.ExtensionDictionary===null){if(item!==context.Document.Layers||metadata.Extension!==context.layerStateManagerDictionaryHandle||!context.IsAcceptedSourceDictionary(metadata.Extension))throw new FormatException('Unresolved extension dictionary: '+metadata.Extension);}
    else if(item.ExtensionDictionary.Owner!==item)throw new FormatException('Extension dictionary owner mismatch: '+metadata.Extension);
  }
  for(const handle of metadata.Reactors){const target=context.GetObjectBySourceHandle(handle);
    if(target!==null&&!item.PersistentReactors.Contains(target))item.PersistentReactors.Add(target);
    else if(target===null&&handle!=='0'&&!Array.from(context.managedReactorHandles).some(v=>key(v)===key(handle)))throw new FormatException('Unresolved persistent reactor: '+handle);
  }
}
export function ImportDatabaseObjects(context){
  const doc=context.Document,records=context.databaseRecords;
  for(const record of records)context.RecordSourceObject(record.Object,record.SourceIdentity);context.ValidateSourceIdentityDeclarations();
  if(records.Count===0){payload.ResolveSunReferences(context);payload.ResolveOutputSettingsReferences(context);return;}
  const max=0x7fffffffffffffffn;
  for(const record of records){const handle=SourceHandle(record.Object.Handle);if(handle!==null&&handle>=doc.NumHandles&&handle<max)doc.NumHandles=handle+1n;}
  const database=doc.Objects;
  for(const record of records){const item=record.Object;
    const tags=item instanceof api.DxfXRecord?item.Data:item instanceof api.DxfOpaqueObject?item.Tags:
      item instanceof api.DxfStoredTableContent||item instanceof api.DxfStoredTableGeometry||item instanceof api.DxfStoredCellStyleMap||item instanceof api.DxfStoredField||item instanceof api.DxfStoredSunStudy?item.Payload:
      item instanceof api.DxfStoredSectionManager||item instanceof api.DxfTableStyle?item.Tags:[];
    for(const tag of tags)database.ReserveUnresolvedReference(tag);
    for(const data of item.XData.Values)for(const tag of data.XDataRecord)if(tag.Code===api.XDataCode.DatabaseHandle)database.ReserveUnresolvedReference(new api.DxfTag(1005,tag.Value));
  }
  const root=Array.from(records).find(r=>r.Object.Handle===(context.namedDictionary?.Handle??null))??null,managed=new Set();
  if(context.layerStateManagerDictionaryHandle!==null&&context.dictionaries.ContainsKey(context.layerStateManagerDictionaryHandle)){
    const manager=context.dictionaries.get_Item(context.layerStateManagerDictionaryHandle);managed.add(key(manager.Handle));
    for(const child of manager.Entries.Keys){managed.add(key(child));if(context.dictionaries.ContainsKey(child))for(const state of context.dictionaries.get_Item(child).Entries.Keys)managed.add(key(state));}
  }
  if(root!==null){if(!(root.Object instanceof api.DxfDictionary))throw new InvalidCastException();database.ReplaceRoot(root.Object);}
  const collections={ACAD_GROUP:'Groups',ACAD_LAYOUT:'Layouts',ACAD_MLINESTYLE:'MlineStyles',ACAD_IMAGE_DICT:'ImageDefinitions',ACAD_DGNDEFINITIONS:'UnderlayDgnDefinitions',ACAD_DWFDEFINITIONS:'UnderlayDwfDefinitions',ACAD_PDFDEFINITIONS:'UnderlayPdfDefinitions'};
  for(const record of records){if(record===root||managed.has(key(record.Object.Handle)))continue;
    const existing=doc.GetObjectByHandle(record.Object.Handle);
    if(existing!==null){const named={};const collection=record.Object instanceof api.DxfDictionary&&root!==null&&record.Metadata.Owner===root.Object.Handle&&context.namedDictionary.Entries.TryGetValue(record.Object.Handle,named)&&Object.hasOwn(collections,named.value)&&existing===doc[collections[named.value]];
      if(!collection&&!(record.Object instanceof api.DxfXRecord&&existing instanceof api.LayerState))throw new FormatException('Duplicate database identity: '+record.Object.Handle);
      if(collection)context.RecordSourceObject(existing,record.SourceIdentity);managed.add(key(record.Object.Handle));continue;
    }
    database.Register(record.Object,true);
  }
  for(const record of records){if(managed.has(key(record.Object.Handle)))continue;
    if(record!==root&&record.Metadata.Owner!==null&&record.Metadata.Owner!=='0'){
      record.Object.Owner=context.GetObjectBySourceHandle(record.Metadata.Owner);if(record.Object.Owner===null)throw new FormatException('Unresolved database owner: '+record.Metadata.Owner);
    }
  }
  for(const record of records){if(managed.has(key(record.Object.Handle)))continue;const item=record.Object;
    payload.ResolveContainerReferences(context,record);
    if(item instanceof api.DxfDictionary)for(const [name,handle,hard]of record.Entries){
      if(record===root&&api.DxfObjectDatabase.IsReservedName(name))continue;
      const target=context.GetObjectBySourceHandle(handle);if(target===null)throw new FormatException('Unresolved dictionary entry: '+name+' -> '+handle);item.AddLoaded(name,target,hard);
    }
    if(item instanceof api.DxfDictionaryWithDefault&&record.Default!==null&&record.Default!=='0'){
      item.Default=context.GetObjectBySourceHandle(record.Default);if(item.Default===null)throw new FormatException('Unresolved dictionary default: '+record.Default);
    }
    ApplyDatabaseMetadata(context,item,record.Metadata);
  }
  for(const [handle,metadata]of context.entityDatabaseMetadata){const target=context.GetObjectBySourceHandle(handle);if(target!==null)ApplyDatabaseMetadata(context,target,metadata);}
  for(const name of ['ResolveStoredDimAssocReferences','ResolveTableStyleReferences','ResolveStoredTableContentReferences','ResolveStoredTableGeometryReferences','ResolveStoredCellStyleMapReferences','ResolveSunReferences','ResolveStoredFields','ResolveStoredSunStudyReferences','ResolveDataTableReferences','ResolveLayerIndexReferences','ResolveSectionSettingsReferences','ResolveSectionManagerReferences','ResolveDeclaredOwnership','ResolveGeoDataHosts','ResolveOutputSettingsReferences','ResolveLightListReferences'])payload[name](context);
}
