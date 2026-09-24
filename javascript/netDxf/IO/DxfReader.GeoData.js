// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfGeoData, DxfGeoMeshPoint, DxfGeoMeshFace } from '../Objects/DxfGeoData.js';
import { BlockRecord } from '../Blocks/BlockRecord.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { PayloadEnd, WrappedFormat } from '../../runtime/DatabaseIOContext.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentOutOfRangeException, FormatException } from '../../runtime/Errors.js';
const allowed=new Set([100,90,330,70,10,20,30,11,21,31,40,91,41,92,210,220,230,12,22,95,141,294,142,143,303,301,302,305,306,307,93,13,23,14,24,96,97,98,99]);
const required=[90,330,70,10,20,30,11,21,31,40,91,41,92,210,220,230,12,22,95,141,294,142,143];
export function ReadGeoTag(tags,cursor,code) {
  if(cursor.value<0)throw new ArgumentOutOfRangeException('index');
  if(cursor.value>=tags.length||tags[cursor.value].Code!==code)throw new FormatException('Expected GEODATA group '+code+'.');
  return tags[cursor.value++];
}
export function ReadGeoDataPayload(context,record,codeName,tags,start) {
  if(codeName!=='GEODATA')return false;
  const end=PayloadEnd(tags,start),payload=tags.slice(start,end);
  if(context.Document.DrawingVariables.AcadVer<16||!payload.length||payload[0].Code!==100||payload[0].Value!=='AcDbGeoData')return false;
  const versions=payload.filter(tag=>tag.Code===90);
  if(versions.length===1&&versions[0].Value!==2)return false;
  if(payload.some(tag=>!allowed.has(tag.Code))||payload.slice(1).some(tag=>tag.Code===100))return false;
  try {
    const values=new Map(),definition=[];let final=false;const cursor={value:1};
    for(;cursor.value<payload.length&&payload[cursor.value].Code!==93;cursor.value++) {
      const tag=payload[cursor.value];
      if(tag.Code===301||tag.Code===303) {
        if(final)throw new FormatException('GEODATA coordinate definition has chunks after its final 301 tag.');
        definition.push(tag.Value);final=tag.Code===301;
      } else {
        if(values.has(tag.Code))throw new FormatException('Duplicate GEODATA scalar group: '+tag.Code);
        values.set(tag.Code,tag.Value);
      }
    }
    for(const code of required)if(!values.has(code))throw new FormatException('Missing GEODATA scalar group: '+code);
    if(!final)throw new FormatException('GEODATA coordinate definition requires a final 301 tag.');
    if(Array.from(values.keys()).some(code=>!required.includes(code)&&![302,305,306,307].includes(code)))throw new FormatException('Unexpected GEODATA mesh data before its count.');
    const number=code=>values.get(code),text=code=>values.has(code)?DecodeDxfText(values.get(code)):'';
    const data=new DxfGeoData();
    data.CoordinateType=number(70);data.DesignPoint=new Vector3(number(10),number(20),number(30));data.ReferencePoint=new Vector3(number(11),number(21),number(31));
    data.HorizontalUnitScale=number(40);data.HorizontalUnits=number(91);data.VerticalUnitScale=number(41);data.VerticalUnits=number(92);
    data.UpDirection=new Vector3(number(210),number(220),number(230));data.NorthDirection=new Vector2(number(12),number(22));
    data.ScaleEstimation=number(95);data.UserScaleFactor=number(141);data.SeaLevelCorrection=number(294);data.SeaLevelElevation=number(142);data.CoordinateProjectionRadius=number(143);
    data.CoordinateSystemDefinition=DecodeDxfText(definition.join('')).replaceAll('^J','\n');
    data.GeoRssTag=text(302);data.ObservationFrom=text(305);data.ObservationTo=text(306);data.ObservationCoverage=text(307);
    const take=code=>ReadGeoTag(payload,cursor,code).Value,count=take(93);
    if(count<0||count>Math.floor((payload.length-cursor.value)/4))throw new FormatException('GEODATA mesh point count exceeds the available data.');
    for(let i=0;i<count;i++){const sx=take(13),sy=take(23),tx=take(14),ty=take(24);data.MeshPoints.Add(new DxfGeoMeshPoint(new Vector2(sx,sy),new Vector2(tx,ty)));}
    const faces=take(96);
    if(faces<0||faces>Math.floor((payload.length-cursor.value)/3))throw new FormatException('GEODATA face count exceeds the available data.');
    for(let i=0;i<faces;i++) {
      const a=take(97),b=take(98),c=take(99);
      if(a<0||b<0||c<0||a>=count||b>=count||c>=count)throw new FormatException('GEODATA face index is outside the point array.');
      data.MeshFaces.Add(new DxfGeoMeshFace(a,b,c));
    }
    if(cursor.value!==payload.length)throw new FormatException('Unexpected GEODATA trailing mesh data.');
    if(end<tags.length)context.ReadDatabaseXData(data,tags,end);
    record.Object=data;context.geoDataHosts.push([data,values.get(330)]);return true;
  } catch(error) {if(error instanceof ArgumentException)throw WrappedFormat('Invalid GEODATA public-schema value.',error);throw error;}
}
export function ResolveGeoDataHosts(context) {
  for(const [data,handle] of context.geoDataHosts) {
    const host=context.Document.GetObjectByHandle(handle);data.SetLoadedHost(host instanceof BlockRecord?host:null);
    const errors=new ReferenceList();data.ValidateDatabaseSchema(context.Document.Objects,errors);
    if(errors.Count)throw new FormatException(Array.from(errors).join('; '));
  }
}
