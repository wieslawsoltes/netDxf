// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Original common object envelopes, dispatch, text preflight and class preparation.
import * as api from '../../index.js';
import * as payload from '../../runtime/DatabasePayloadIO.js';
import { EncodeDxfText,EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { WriteMLeaderStylePayload } from '../../runtime/MLeaderStyleIO.js';
import { PrepareMultiLeaderClasses,PrepareStoredTableClasses,PrepareSectionClasses } from '../../runtime/DatabaseClassIO.js';
import { InvalidDataException,InvalidOperationException,NullReferenceException } from '../../runtime/Errors.js';
export const EncodeDatabaseString=(value,version)=>EncodeDxfDatabaseText(value,version);
export function CheckDatabaseText(value){
  if(value==null)throw new NullReferenceException();
  if(value.includes('\r')||value.includes('\n'))throw new InvalidDataException('Text DXF cannot encode a database string containing CR or LF; use binary DXF or replace the line break.');
}
export function ValidateDatabaseTransport(document,isBinary){
  for(const item of document.Objects.Items)if(item instanceof api.DxfStoredField)item.ValidateSource(document);
  if(isBinary)return;
  for(const item of document.Objects.Items){
    if(item instanceof api.DxfDictionaryVariable)CheckDatabaseText(item.Value);
    const tags=item instanceof api.DxfXRecord?item.Data:item instanceof api.DxfOpaqueObject?item.Tags:
      item instanceof api.DxfStoredField||item instanceof api.DxfStoredSunStudy||item instanceof api.DxfStoredCellStyleMap||item instanceof api.DxfStoredTableGeometry||item instanceof api.DxfStoredTableContent?item.Payload:
      item instanceof api.DxfTableStyle?item.Tags:[];
    for(const tag of tags)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);
    for(const data of item.XData.Values)for(const tag of data.XDataRecord)if(typeof tag.Value==='string')CheckDatabaseText(tag.Value);
  }
}
export function WriteDatabaseMetadata(chunk,document,item,automaticReactors=null){
  if(item==null)throw new NullReferenceException();item=document.GetObjectByHandle(item.Handle)??item;
  if(item.ExtensionDictionary!==null){chunk.Write(102,'{ACAD_XDICTIONARY');chunk.Write(360,item.ExtensionDictionary.Handle);chunk.Write(102,'}');}
  const reactors=new Map(),add=handle=>{const key=handle==null?null:OrdinalIgnoreCaseKey(handle);if(!reactors.has(key))reactors.set(key,handle);};
  if(automaticReactors!==null)for(const handle of automaticReactors)add(handle);
  if(item instanceof api.EntityObject)for(const reactor of item.Reactors){if(reactor==null)throw new NullReferenceException();add(reactor.Handle);}
  for(const reactor of item.PersistentReactors){if(reactor===null)throw new InvalidOperationException('A persistent reactor cannot be null.');add(reactor.Handle);}
  if(reactors.size){chunk.Write(102,'{ACAD_REACTORS');for(const handle of reactors.values())chunk.Write(330,handle);chunk.Write(102,'}');}
}
export function PrepareDatabaseClasses(document,definitions){
  const names=['DICTIONARYVAR','ACDBDICTIONARYWDFLT','ACDBPLACEHOLDER','IDBUFFER','SORTENTSTABLE','SPATIAL_FILTER','PLOTSETTINGS','WIPEOUTVARIABLES'];
  const cpp=['AcDbDictionaryVar','AcDbDictionaryWithDefault','AcDbPlaceHolder','AcDbIdBuffer','AcDbSortentsTable','AcDbSpatialFilter','AcDbPlotSettings','AcDbWipeoutVariables'];
  for(let i=0;i<names.length;i++){
    const count=Array.from(document.Objects.Items).filter(o=>o.CodeName===names[i]).length;
    if(definitions.Contains(names[i])){const item=definitions.get_Item(names[i]);if(item.CppClassName!==cpp[i]||item.IsEntity)throw new InvalidDataException('CLASS conflicts with a typed database object: '+names[i]);item.InstanceCount=count;}
    else if(count>0){const item=new api.DxfClass(names[i],cpp[i],'ObjectDBX Classes');item.ProxyFlags=0;item.IsEntity=false;item.InstanceCount=count;definitions.Add(item);}
  }
  const functions=[payload.PrepareStoredFieldClass,payload.PrepareStoredEnvelopeClasses,payload.PrepareGeoDataClass,payload.PrepareLayerFilterPointerClasses,payload.PrepareLayerIndexClass,PrepareMultiLeaderClasses,PrepareStoredTableClasses,PrepareSectionClasses,payload.PrepareSectionManagerClasses,payload.PrepareTableStyleClass,payload.PrepareStoredDimAssocClass,payload.PrepareStoredTableContentClass,payload.PrepareStoredTableGeometryClass,payload.PrepareStoredCellStyleMapClass,payload.PrepareLightListClass,payload.PrepareDataTableClass,payload.PrepareSunClass];
  for(const prepare of functions)prepare(document,definitions);
}
export function WriteDatabaseObject(chunk,document,item,generatedRoot=null){
  if(item==null)throw new NullReferenceException();const version=()=>document.DrawingVariables.AcadVer;
  chunk.Write(0,item.CodeName);chunk.Write(5,item.Handle);WriteDatabaseMetadata(chunk,document,item);chunk.Write(330,item.Owner?.Handle??'0');
  if(item instanceof api.DxfDictionary){
    chunk.Write(100,'AcDbDictionary');chunk.Write(280,item.IsHardOwner?1:0);chunk.Write(281,item.Cloning);
    if(generatedRoot!==null)for(const entry of generatedRoot.Entries){chunk.Write(3,EncodeDxfText(entry.Value,version()));chunk.Write(350,entry.Key);}
    for(const entry of item.Entries){chunk.Write(3,EncodeDxfDatabaseText(entry.Name,version()));chunk.Write(entry.IsHardOwner?360:350,entry.Target.Handle);}
    if(item instanceof api.DxfDictionaryWithDefault){chunk.Write(100,'AcDbDictionaryWithDefault');chunk.Write(340,item.Default?.Handle??'0');}
  }else if(item instanceof api.DxfXRecord){
    chunk.Write(100,'AcDbXrecord');chunk.Write(280,item.Cloning);for(const tag of item.Data)WriteDatabaseTag(chunk,version(),tag,true);
  }else if(item instanceof api.DxfDictionaryVariable){
    chunk.Write(100,'DictionaryVariables');chunk.Write(280,item.Schema);chunk.Write(1,EncodeDxfDatabaseText(item.Value,version()));
  }else if(!(item instanceof api.DxfPlaceholder)){
    const writers=[payload.WriteStoredFieldPayload,payload.WriteLayerIndexPayload,payload.WriteSectionSettingsPayload,payload.WriteSectionManagerPayload,payload.WriteStoredEnvelopePayload,
      (c,v,o)=>payload.WriteContainerPayload(c,o),payload.WriteGeoDataPayload,payload.WriteOutputSettingsPayload,WriteMLeaderStylePayload,payload.WriteLayerFilterPointerPayload,payload.WriteLightListPayload,payload.WriteDataTablePayload,payload.WriteTableStylePayload,payload.WriteStoredTableContentPayload,payload.WriteStoredSunStudyPayload,payload.WriteStoredTableGeometryPayload,payload.WriteStoredCellStyleMapPayload,payload.WriteSunPayload,payload.WriteStoredDimAssocPayload];
    if(!writers.some(write=>write(chunk,version(),item,document))&&item instanceof api.DxfOpaqueObject)for(const tag of item.Tags)WriteDatabaseTag(chunk,version(),tag,false);
  }
  WriteXData(chunk,version(),item.XData);
}
export function WriteDatabaseTag(chunk,version,tag,encodeStrings){
  let value=tag.Value;if(encodeStrings&&tag.ValueType===api.DxfTagValueType.String)value=EncodeDxfDatabaseText(value,version);chunk.Write(tag.Code,value);
}
