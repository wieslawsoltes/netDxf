// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Explicit adapters for the pinned reader partial; not a complete document loader.
import { DxfSectionSettings, DxfSectionTypeSettings, DxfSectionGeometrySettings } from '../Objects/DxfSectionSettings.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { BlockRecord } from '../Blocks/BlockRecord.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { SourceHandle } from './DxfReader.SourceIdentity.js';
import { PayloadEnd, WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ArgumentException, FormatException } from '../../runtime/Errors.js';
class SectionSettingsPrivateVariant extends Error {}
function unexpected(tag) {
  if(tag.Code===100 && tag.Value!=='AcDbSectionSettings')throw new SectionSettingsPrivateVariant();
  if([1,2,3].includes(tag.Code) && !tag.Value.startsWith('Section'))throw new SectionSettingsPrivateVariant();
  if([1,2,3,90,91,92,93,330,331,100].includes(tag.Code))throw new FormatException('SECTIONSETTINGS has misplaced public data or a missing marker.');
  throw new SectionSettingsPrivateVariant();
}
function unique(fields,tag) {
  if(fields.has(tag.Code))throw new FormatException('SECTIONSETTINGS repeats group '+tag.Code+' in one public bundle.');
  fields.set(tag.Code,tag);
}
function required(fields,...codes) {
  if(codes.some(code=>!fields.has(code)))throw new FormatException('SECTIONSETTINGS has an incomplete public bundle.');
}
function count(tag,maximum,available) {
  const value=tag.Value;
  if(value<0 || value>maximum || value>available)throw new FormatException('SECTIONSETTINGS count exceeds its admission budget or available data.');
  return value;
}
function handle(tag) {
  const value=SourceHandle(tag.Value);
  if(value===null)throw new FormatException('SECTIONSETTINGS contains an invalid reference handle.');
  return value===0n?null:tag.Value;
}
function marker(tags,state,end,code,value) {
  if(state.cursor>=end)throw new FormatException('SECTIONSETTINGS is missing '+value+'.');
  const tag=tags[state.cursor];
  if(tag.Code!==code || tag.Value!==value)unexpected(tag);
  state.cursor++;
}
function geometry(tags,state,end) {
  const fields=new Map(),known=new Set([90,91,92,62,63,8,6,40,1,370,70,71,72,2,41,42,43]);
  while(state.cursor<end && tags[state.cursor].Code!==3) {
    const tag=tags[state.cursor++];if(!known.has(tag.Code))unexpected(tag);unique(fields,tag);
  }
  marker(tags,state,end,3,'SectionGeometrySettingsEnd');
  required(fields,90,91,92,8,6,40,1,370,70,71,72,2,41,42,43);
  if(fields.has(62)&&fields.has(63))throw new SectionSettingsPrivateVariant();
  const color=fields.has(62)?62:63;required(fields,color);
  const result=new DxfSectionGeometrySettings(),get=code=>fields.get(code).Value;
  // Assignment order is observable through the source's setter validation errors.
  result.SectionType=get(90);result.GeometryValue=get(91);result.Flags=get(92);
  result.ColorCode=color;result.ColorIndex=get(color);
  result.LayerName=DecodeDxfText(get(8));result.LinetypeName=DecodeDxfText(get(6));
  result.LinetypeScale=get(40);result.PlotStyleName=DecodeDxfText(get(1));result.Lineweight=get(370);
  result.FaceTransparency=get(70);result.EdgeTransparency=get(71);result.HatchPatternType=get(72);
  result.HatchPatternName=DecodeDxfText(get(2));result.HatchAngle=get(41);result.HatchScale=get(42);result.HatchSpacing=get(43);
  return result;
}
function typeInput(tags,state,end) {
  marker(tags,state,end,1,'SectionTypeSettings');
  const fields=new Map(),input={Type:0,Options:0,Destination:null,File:null,RepeatMarkers:false,Sources:[],Geometry:[]};
  while(state.cursor<end && tags[state.cursor].Code!==2 && tags[state.cursor].Code!==3) {
    const tag=tags[state.cursor++];
    if(tag.Code===330) {
      if(input.Sources.length===state.sourcesRemaining)throw new FormatException('SECTIONSETTINGS exceeds its aggregate source-reference budget.');
      input.Sources.push(handle(tag));
    }else{if(![90,91,92,331,1,93].includes(tag.Code))unexpected(tag);unique(fields,tag);}
  }
  required(fields,90,91,92,331,1,93);
  if(count(fields.get(92),state.sourcesRemaining,input.Sources.length)!==input.Sources.length)throw new FormatException('SECTIONSETTINGS source count differs from the complete source sequence.');
  state.sourcesRemaining-=input.Sources.length;
  input.Type=fields.get(90).Value;input.Options=fields.get(91).Value;input.Destination=handle(fields.get(331));
  input.File=DxfSectionSettings.StoredText(DecodeDxfText(fields.get(1).Value),'fileName');
  const size=count(fields.get(93),state.geometryRemaining,Math.trunc((end-state.cursor)/17));state.geometryRemaining-=size;
  const initial=state.cursor<end && tags[state.cursor].Code===2;
  if(initial)marker(tags,state,end,2,'SectionGeometrySettings');
  else if(size>0)throw new FormatException('SECTIONSETTINGS geometry requires its initial marker.');
  let repeat=null;
  for(let i=0;i<size;i++) {
    if(i>0) {
      const present=state.cursor<end && tags[state.cursor].Code===2;
      if(repeat!==null && repeat!==present)throw new FormatException('SECTIONSETTINGS mixes repeated and sequence geometry markers.');
      repeat=present;if(present)marker(tags,state,end,2,'SectionGeometrySettings');
    }
    input.Geometry.push(geometry(tags,state,end));
  }
  input.RepeatMarkers=size===0?!initial:repeat??true;
  marker(tags,state,end,3,'SectionTypeSettingsEnd');return input;
}
export function ReadSectionSettingsRecord(context,codeName,tags) {
  const {record,opaque,payload:start,handle:identity}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),version=context.Document.DrawingVariables.AcadVer;
  if(version>=15 && (start>=end || tags[start].Code!==100))throw new FormatException('SECTIONSETTINGS requires a public subclass marker before its stored fields.');
  let typed=false;
  if(!opaque.length && version>=15)try {
    if(start>=end || tags[start].Code!==100)throw new FormatException('SECTIONSETTINGS requires a public subclass marker.');
    if(tags[start].Value!=='AcDbSectionSettings')throw new SectionSettingsPrivateVariant();
    const state={cursor:start+1,sourcesRemaining:DxfSectionSettings.MaximumSourceReferences,geometryRemaining:DxfSectionSettings.MaximumGeometrySettings},fields=new Map();
    while(state.cursor<end && tags[state.cursor].Code!==1) {
      const tag=tags[state.cursor++];if(tag.Code!==90 && tag.Code!==91)unexpected(tag);unique(fields,tag);
    }
    required(fields,90,91);const size=count(fields.get(91),DxfSectionSettings.MaximumTypeSettings,end-state.cursor),types=[];
    for(let i=0;i<size;i++)types.push(typeInput(tags,state,end));
    if(state.cursor!==end)unexpected(tags[state.cursor]);
    const settings=new DxfSectionSettings(codeName);settings.SectionType=fields.get(90).Value;
    if(end<tags.length)context.ReadDatabaseXData(settings,tags,end);
    record.Object=settings;context.pendingSectionSettings.set(settings,types);typed=true;
  }catch(error) {
    if(error instanceof ArgumentException)throw WrappedFormat('Invalid SECTIONSETTINGS stored value.',error);
    if(!(error instanceof SectionSettingsPrivateVariant))throw error;
  }
  if(!typed){for(let i=start;i<tags.length;i++)opaque.push(tags[i]);record.Object=new DxfOpaqueObject(codeName,opaque);}
  record.Object.Handle=identity;return record;
}
export function ResolveSectionSettingsReferences(context) {
  for(const [settings,types] of context.pendingSectionSettings) {
    const values=[];
    for(const type of types) {
      const sources=[];
      for(const key of type.Sources) {
        const source=key===null?null:context.GetObjectBySourceHandle(key);
        if(key!==null && source===null)throw new FormatException('SECTIONSETTINGS source object is absent from the source file: '+key);
        sources.push(source);
      }
      let destination=type.Destination===null?null:context.GetObjectBySourceHandle(type.Destination);
      if(!(destination instanceof BlockRecord))destination=null;
      if(type.Destination!==null && destination===null)throw new FormatException('SECTIONSETTINGS destination must be an actual source BLOCK_RECORD: '+type.Destination);
      values.push(new DxfSectionTypeSettings(type.Type,type.Options,sources,destination,type.File,type.Geometry,type.RepeatMarkers));
    }
    settings.SetTypeSettings(values);
  }
}
