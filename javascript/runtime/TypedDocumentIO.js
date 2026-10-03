// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Shared tag cursors and field definitions for the original whole-document reader/writer.
import * as api from '../index.js';
import { SourceRecordIdentity, SourceHandle } from '../netDxf/IO/DxfReader.SourceIdentity.js';
import { DxfTag } from '../netDxf/IO/DxfTag.js';
import { ReadXDataRecord } from './DxfXDataIO.js';
import { DecodeDxfText } from './DxfStringEncoding.js';
import { ArgumentException, NotSupportedException, EndOfStreamException, FormatException, InvalidCastException } from './Errors.js';

export const tableKinds = Object.freeze([
  ['APPID','ApplicationRegistries','ApplicationRegistries'],['VPORT','VPorts','VPorts'],
  ['LTYPE','Linetypes','Linetypes'],['LAYER','Layers','Layers'],['STYLE','TextStyles','TextStyles'],
  ['DIMSTYLE','DimensionStyles','DimensionStyles'],['VIEW','Views','Views'],['UCS','UCSs','UCSs'],
  ['BLOCK_RECORD','Blocks','BlockRecords']
]);
export const managedDictionaries = Object.freeze([
  ['ACAD_GROUP','Groups','Groups'],['ACAD_LAYOUT','Layouts','Layouts'],
  ['ACAD_MLINESTYLE','MlineStyles','MLineStyles'],['ACAD_IMAGE_DICT','ImageDefinitions','ImageDefinitions'],
  ['ACAD_DGNDEFINITIONS','UnderlayDgnDefinitions','UnderlayDgnDefinitions'],
  ['ACAD_DWFDEFINITIONS','UnderlayDwfDefinitions','UnderlayDwfDefinitions'],
  ['ACAD_PDFDEFINITIONS','UnderlayPdfDefinitions','UnderlayPdfDefinitions']
]);
export function value(tags,code,...fallback) {
  for(const tag of tags)if(tag.Code===code)return tag.Value;
  return fallback.length?fallback[0]:null;
}
export const lastValue = (tags, code, fallback = null) => {
  for (let i=tags.length-1;i>=0;i--) if(tags[i].Code===code)return tags[i].Value;
  return fallback;
};
export function point(tags, first, dimension=3, fallback=null) {
  const x=value(tags,first,fallback?.X??0),y=value(tags,first+10,fallback?.Y??0);
  return dimension===2?new api.Vector2(x,y):new api.Vector3(x,y,value(tags,first+20,fallback?.Z??0));
}
export function writePoint(chunk,first,p,dimension=3){chunk.Write(first,p.X);chunk.Write(first+10,p.Y);if(dimension===3)chunk.Write(first+20,p.Z);}
export const canonicalHandle = handle => {
  const number=SourceHandle(handle);return number===null?null:number.toString(16).toUpperCase();
};

/** A bounded, typed cursor over the already decoded input. It never owns a stream. */
export class DocumentTagReader {
  constructor(tags, start=-1, identities=new Map()) {this.tags=tags;this.index=start;this.identities=identities;this.SourceRecord=identities.get(start)??new SourceRecordIdentity();this.Code5IsString=false;}
  get Code(){return this.tags[this.index]?.Code??0;}
  get Value(){return this.tags[this.index]?.Value??null;}
  get CurrentPosition(){return this.index;}
  SetSkipComments(value){this.skipComments=value;}
  Next(){do{if(++this.index>=this.tags.length)throw new EndOfStreamException('Unexpected end of typed DXF input.');if(this.Code===0)this.SourceRecord=this.identities.get(this.index)??new SourceRecordIdentity();}while(this.skipComments!==false&&this.Code===999);}
  ReadString(){if(typeof this.Value!=='string')throw new InvalidCastException('DXF string expected.');return this.Value;}
  ReadHex(){const result=canonicalHandle(this.ReadString());if(result===null)throw new FormatException('Invalid DXF object handle.');return result;}
  ReadDouble(){if(typeof this.Value!=='number')throw new InvalidCastException('DXF double expected.');return this.Value;}
  ReadShort(){return this.ReadDouble();}ReadInt(){return this.ReadDouble();}ReadByte(){return this.ReadDouble();}
  ReadLong(){if(typeof this.Value!=='bigint')throw new InvalidCastException('DXF Int64 expected.');return this.Value;}
  ReadBool(){if(typeof this.Value!=='boolean')throw new InvalidCastException('DXF Boolean expected.');return this.Value;}
  ReadBytes(){if(!(this.Value instanceof Uint8Array))throw new InvalidCastException('DXF byte array expected.');return this.Value;}
}

/** Scan only a record's common envelope; payload 5/330/102 tags are not identities. */
export function envelope(tags, codeName) {
  const result={Handle:null,Owner:null,Extension:null,Reactors:[],Source:new SourceRecordIdentity(),Common:[],Body:0};
  if(tags[0]?.Code!==0)return result;
  let common=false,depth=0,group=null;
  for(let i=1;i<tags.length;i++) {
    const tag=tags[i],code=tag.Code,v=tag.Value;
    if(code===102){if(v==='}'){if(depth>0&&--depth===0)group=null;}else if(v.startsWith('{')){if(depth++===0)group=v;}continue;}
    if(depth){if(depth===1&&group==='{ACAD_XDICTIONARY'&&code===360)result.Extension=canonicalHandle(v);
      else if(depth===1&&group==='{ACAD_REACTORS'&&code===330)result.Reactors.push(canonicalHandle(v));continue;}
    if(code===100){
      if(v==='AcDbEntity'&&!common){common=true;continue;}
      result.Body=i;break;
    }
    if(code===1001){result.Body=i;break;}
    if(common){result.Common.push(tag);continue;}
    if(code===(codeName==='DIMSTYLE'?105:5)){
      if(result.Source.IdentitySeen)result.Source.Ambiguous=true;
      result.Source.IdentitySeen=true;result.Handle=canonicalHandle(v);result.Source.Handle=SourceHandle(v)??0n;
    }else if(code===330)result.Owner=canonicalHandle(v);
    result.Body=i+1;
  }
  if(depth)throw new FormatException('Unterminated common object control group.');
  return result;
}
export function readXData(target, tags, document) {
  const start=tags.findIndex(t=>t.Code===1001);if(start<0)return;
  const cursor=new DocumentTagReader([...tags,new DxfTag(0,'EOF')],start);
  while(cursor.Code===1001)target.XData.Add(ReadXDataRecord(cursor,document));
}
export function subclass(tags,name) {
  const at=tags.findIndex(t=>t.Code===100&&t.Value===name);
  if(at<0)return [];
  let end=at+1;while(end<tags.length&&tags[end].Code!==100&&tags[end].Code!==1001)end++;
  return tags.slice(at+1,end);
}
export function publicPayload(tags) {
  const result=[];let depth=0;
  for(const t of tags){if(t.Code===1001)break;if(t.Code===102){if(t.Value==='}')depth=Math.max(0,depth-1);else if(t.Value.startsWith('{'))depth++;continue;}if(!depth)result.push(t);}
  return result;
}
export function getProperty(target,path){for(const part of path.split('.'))target=target[part];return target;}
export function setProperty(target,path,v){const parts=path.split('.'),name=parts.pop();for(const part of parts)target=target[part];target[name]=v;}
// Direct DIMSTYLE fields. Composite unit/zero suppression and references are handled separately.
export const dimensionStyleFields = Object.freeze([
  [40,'DimScaleOverall'],[41,'ArrowSize'],[42,'ExtLineOffset'],[43,'DimBaselineSpacing'],[44,'ExtLineExtend'],
  [45,'DimRoundoff'],[46,'DimLineExtend'],[47,'Tolerances.UpperLimit'],[48,'Tolerances.LowerLimit'],[49,'ExtLineFixedLength'],
  [140,'TextHeight'],[141,'CenterMarkSize'],[143,'AlternateUnits.Multiplier'],[144,'DimScaleLinear'],
  [145,'TextVerticalPosition'],[146,'TextFractionHeightScale'],[147,'TextOffset'],[148,'AlternateUnits.Roundoff'],
  [271,'LengthPrecision'],[272,'Tolerances.Precision'],[171,'AlternateUnits.LengthPrecision'],[274,'Tolerances.AlternatePrecision'],
  [275,'DimAngularUnits'],[276,'FractionType'],[277,'DimLengthUnits'],[278,'DecimalSeparator'],
  [279,'FitTextMove'],[280,'TextHorizontalPlacement'],[283,'Tolerances.VerticalPlacement'],
  [289,'FitOptions'],[340,'TextStyle','TextStyles'],
  [341,'LeaderArrow','Blocks'],[343,'DimArrow1','Blocks'],[344,'DimArrow2','Blocks'],
  [345,'DimLineLinetype','Linetypes'],[346,'ExtLine1Linetype','Linetypes'],[347,'ExtLine2Linetype','Linetypes'],
  [371,'DimLineLineweight'],[372,'ExtLineLineweight'],[142,'TickSize'],[179,'AngularPrecision'],[77,'TextVerticalPlacement'],
]);
export const dimensionStyleBooleans = Object.freeze([
  [73,'TextInsideAlign'],[74,'TextOutsideAlign'],[75,'ExtLine1Off'],[76,'ExtLine2Off'],
  [170,'AlternateUnits.Enabled'],[172,'FitDimLineForce'],[174,'FitTextInside'],[175,'FitDimLineInside'],
  [281,'DimLine1Off'],[282,'DimLine2Off'],[290,'ExtLineFixed'],[288,'UserPositionedText'],[294,'TextDirection']
]);
export function suppression(leading,trailing,feet,inches){return (leading?4:0)|(trailing?8:0)|(feet?(inches?0:3):(inches?2:1));}
export function applySuppression(style,n,prefix='') {
  style[prefix+'SuppressLinearLeadingZeros']=!!(n&4);style[prefix+'SuppressLinearTrailingZeros']=!!(n&8);
  style[prefix+'SuppressZeroFeet']=(n&3)===0||(n&3)===3;style[prefix+'SuppressZeroInches']=(n&3)===0||(n&3)===2;
}

// Explicit host and build-profile adaptation. The browser-safe default opens no files.
let fileHost=null, configuration='Release';
export function SetTypedDocumentFileHost(value){const previous=fileHost;if(value===null){fileHost=null;return previous;}const names=['GetFullPath','GetDirectoryName','GetFileNameWithoutExtension','OpenRead','Create'];if(!value||names.some(n=>typeof value[n]!=='function'))throw new ArgumentException('A complete typed document file host is required.','value');fileHost=Object.freeze(Object.fromEntries(names.map(n=>[n,value[n].bind(value)])));return previous;}
export function GetTypedDocumentFileHost(){if(fileHost===null)throw new NotSupportedException('Typed file IO requires @netdxf/javascript/node or an explicit file host.');return fileHost;}
export function SetTypedIOConfiguration(value){if(value!=='Debug'&&value!=='Release')throw new ArgumentException('Expected Debug or Release.','value');const previous=configuration;configuration=value;return previous;}
export function GetTypedIOConfiguration(){return configuration;}

// DSTYLE stores its own field identifiers inside ACAD XData, not normal DXF group codes.
export const dimensionOverrideFields=Object.freeze([
  ...dimensionStyleFields.filter(([_,name])=>name!=='DimBaselineSpacing').map(([code,name,table])=>[code,
    name==='FractionType'?'FractionalType':name.startsWith('AlternateUnits.')?'AltUnits'+name.slice(15):name.replace('Tolerances.','Tolerances'),
    table?'handle':code<70||code>=140&&code<150?'real':name==='DecimalSeparator'?'char':'short',table]),
  ...dimensionStyleBooleans.map(([code,name])=>[code,name.startsWith('AlternateUnits.')?'AltUnits'+name.slice(15):name,name==='TextDirection'?'short':'bool']),
  [176,'DimLineColor','color'],[177,'ExtLineColor','color'],[178,'TextColor','color']
]);
export const zeroSuppressionGroups=Object.freeze([
  [78,'',['SuppressLinearLeadingZeros','SuppressLinearTrailingZeros','SuppressZeroFeet','SuppressZeroInches']],
  [285,'AltUnits',['SuppressLinearLeadingZeros','SuppressLinearTrailingZeros','SuppressZeroFeet','SuppressZeroInches']],
  [284,'Tolerances',['SuppressLinearLeadingZeros','SuppressLinearTrailingZeros','SuppressZeroFeet','SuppressZeroInches']],
  [286,'TolerancesAlt',['SuppressLinearLeadingZeros','SuppressLinearTrailingZeros','SuppressZeroFeet','SuppressZeroInches']]
]);
export function dimensionOverrideBase(style,name){
  if(name.startsWith('TolerancesAltSuppress'))return style.Tolerances['Alternate'+name.slice(13)];
  if(name.startsWith('Tolerances'))return style.Tolerances[name.slice(10)];
  if(name.startsWith('AltUnits'))return style.AlternateUnits[name==='AltUnitsStackedUnits'?'StackUnits':name.slice(8)];
  return style[name==='FractionalType'?'FractionType':name];
}
/** Read only top-level DSTYLE containers; arbitrary nested application data is opaque. */
export function dstyleSections(records){const list=Array.from(records??[]),sections=[];let depth=0;
  for(let i=0;i<list.length;i++){const record=list[i];if(depth===0&&record.Code===1000&&record.Value==='DSTYLE'&&list[i+1]?.Code===1002&&list[i+1].Value==='{'){let nesting=1,end=i+2;for(;end<list.length;end++){if(list[end].Code===1002){if(list[end].Value==='{')nesting++;else if(list[end].Value==='}'&&--nesting===0)break;}}if(nesting)throw new FormatException('Unterminated DSTYLE control string.');sections.push({Start:i,End:end,Payload:list.slice(i+2,end)});i=end;continue;}if(record.Code===1002){if(record.Value==='{')depth++;else depth=Math.max(0,depth-1);}}
  return sections;
}
