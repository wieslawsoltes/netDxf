// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfSun } from '../Objects/DxfSun.js';
import { DxfDatabaseObject } from '../Objects/DxfDatabaseObject.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { SunReferences } from '../Objects/SunReferences.js';
import { DatabaseRecord, PayloadEnd, WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { SourceHandle } from './DxfReader.SourceIdentity.js';
import { ArgumentException, FormatException, NullReferenceException } from '../../runtime/Errors.js';
export class SunOwnerContext {
  #marker;#public=true;#controls=0;#xdata=false;
  constructor(marker){this.#marker=marker;}
  Observe(code,value) {
    if(code===1001)this.#xdata=true;
    if(code===102){if(value==null)throw new NullReferenceException();if(value.startsWith('{'))this.#controls++;else if(value==='}'&&this.#controls>0)this.#controls--;}
    else if(code===100&&this.#controls===0)this.#public=value===this.#marker;
  }
  get IsPublic(){return this.#public&&this.#controls===0&&!this.#xdata;}
}
export function AddSunReference(context,owner,handle) {
  if(context.sunReferences.some(([item])=>item===owner))throw new FormatException('Repeated SUN ownership group 361.');
  context.sunReferences.push([owner,handle]);
}
export function ReadSunRecord(context,tags) {
  const record=new DatabaseRecord(),privateHeader=[];let handle=null,reactors=false,extension=false,start=0;
  for(;start<tags.length;start++) {
    const tag=tags[start];if(tag.Code===100||tag.Code===1001)break;
    if(tag.Code===5){if(handle!==null)throw new FormatException('SUN repeats its identity.');handle=tag.Value;}
    else if(tag.Code===330){if(record.Metadata.Owner!==null)throw new FormatException('SUN repeats its owner.');record.Metadata.Owner=tag.Value;}
    else if(tag.Code===102) {
      const group=tag.Value,begin=start;
      if(!group.startsWith('{'))throw new FormatException('Invalid SUN control group.');
      while(++start<tags.length&&!(tags[start].Code===102&&tags[start].Value==='}'))if([102,100,1001].includes(tags[start].Code))throw new FormatException('Invalid SUN control framing.');
      if(start===tags.length)throw new FormatException('Unterminated SUN control group.');
      const content=tags.slice(begin+1,start);
      if(group==='{ACAD_REACTORS') {
        if(reactors||content.some(value=>value.Code!==330))throw new FormatException('Invalid SUN reactor group.');
        reactors=true;for(const value of content)record.Metadata.Reactors.push(value.Value);
      } else if(group==='{ACAD_XDICTIONARY') {
        if(extension||content.length!==1||content[0].Code!==360)throw new FormatException('Invalid SUN extension group.');
        extension=true;record.Metadata.Extension=content[0].Value;
      } else for(let i=begin;i<=start;i++)privateHeader.push(tags[i]);
    } else privateHeader.push(tag);
  }
  if(handle===null)throw new FormatException('SUN requires an identity.');
  if(privateHeader.length!==0||!ReadSunPayload(context,record,tags,start)) {
    for(let i=start;i<tags.length;i++)privateHeader.push(tags[i]);record.Object=new DxfOpaqueObject('SUN',privateHeader);
  }
  record.Object.Handle=handle;return record;
}
const publicCodes=new Set([90,290,63,421,40,291,91,92,292,70,71,280]);
export function ReadSunPayload(context,record,tags,start) {
  if(context.Document.DrawingVariables.AcadVer<15)return false;
  const end=PayloadEnd(tags,start);
  if(start===end||tags[start].Code!==100||tags[start].Value!=='AcDbSun')return false;
  for(let j=start+1;j<end;j++) {
    if(tags[j].Code===100&&tags[j].Value==='AcDbSun')throw new FormatException('SUN repeats its public subclass marker.');
    if(!publicCodes.has(tags[j].Code)||tags[j].Code===90&&tags[j].Value!==1)return false;
  }
  let i=start+1;
  const take=code=>{if(i>=end||tags[i].Code!==code)throw new FormatException('SUN requires ordered group '+code+'.');return tags[i++].Value;};
  take(90);const sun=new DxfSun();
  try {
    sun.Enabled=take(290);sun.ColorIndex=take(63);sun.TrueColor=i<end&&tags[i].Code===421?take(421):null;sun.Intensity=take(40);sun.ShadowsEnabled=take(291);sun.JulianDay=take(91);sun.StoredTime=take(92);sun.DaylightSavingTime=take(292);sun.ShadowType=take(70);sun.ShadowMapSize=take(71);
    const softness=take(280);if(softness<0||softness>255)throw new FormatException('SUN shadow softness must fit an unsigned byte.');sun.ShadowSoftness=softness;
  } catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid SUN stored value.',error);throw error;}
  if(i!==end)throw new FormatException('SUN contains trailing or repeated public fields.');
  if(end<tags.length)context.ReadDatabaseXData(sun,tags,end);
  record.Object=sun;return true;
}
export function IsNullSourceHandle(handle){return SourceHandle(handle)===0n;}
export function ResolveSunReferences(context) {
  for(const [owner,handle] of context.sunReferences) {
    if(owner==null)throw new NullReferenceException();
    if(context.GetObjectBySourceHandle(owner.Handle)!==owner)throw new FormatException('SUN owner was not retained from its source record.');
    SunReferences.CheckProfile(owner,context.Document.DrawingVariables.AcadVer);
    const empty=IsNullSourceHandle(handle),resolved=empty?null:context.GetObjectBySourceHandle(handle),sun=resolved instanceof DxfDatabaseObject?resolved:null;
    if(!empty&&(sun===null||!(sun instanceof DxfSun||sun instanceof DxfOpaqueObject&&sun.CodeName==='SUN')||sun.Owner!==owner))throw new FormatException('Unresolved, incompatible or nonreciprocal SUN ownership: '+(handle??''));
    SunReferences.Set(owner,sun);
  }
  for(const sun of context.Document.Objects.Items)if(sun instanceof DxfSun&&SunReferences.Get(sun.Owner)!==sun)throw new FormatException('SUN has no reciprocal owner group 361: '+sun.Handle);
}
