// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfGeoData } from '../Objects/DxfGeoData.js';
import { DxfClass } from '../DxfClass.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { InvalidDataException, NullReferenceException } from '../../runtime/Errors.js';
export function PrepareGeoDataClass(document,definitions) {
  if(!Array.from(document.Objects.Items).some(item=>item instanceof DxfGeoData))return;
  const count=Array.from(document.Objects.Items).filter(item=>item.CodeName==='GEODATA').length;
  if(definitions.Contains('GEODATA')) {
    const definition=definitions.get_Item('GEODATA');
    if(definition.CppClassName!=='AcDbGeoData'||definition.IsEntity)throw new InvalidDataException('CLASS conflicts with typed GEODATA.');
    definition.InstanceCount=count;
  } else {
    const definition=new DxfClass('GEODATA','AcDbGeoData','ObjectDBX Classes');definition.ProxyFlags=4095;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);
  }
}
export function WriteGeoVector(chunk,code,value) {
  // The C# argument is a struct copy even when called through this explicit helper.
  const vector=Copy(value);chunk.Write(code,vector.X);chunk.Write((code+10<<16)>>16,vector.Y);chunk.Write((code+20<<16)>>16,vector.Z);
}
export function SplitGeoDefinition(text) {
  if(text==null)throw new NullReferenceException();
  const result=[];let chunk='';
  for(let i=0;i<text.length;) {
    const code=text.charCodeAt(i),next=text.charCodeAt(i+1);
    const length=text[i]==='\\'&&i+6<text.length&&text[i+1]==='U'&&text[i+2]==='+'?7:code>=0xd800&&code<=0xdbff&&i+1<text.length&&next>=0xdc00&&next<=0xdfff?2:1;
    if(chunk.length+length>255){result.push(chunk);chunk='';}
    chunk+=text.slice(i,i+length);i+=length;
  }
  result.push(chunk);return result;
}
export function WriteGeoDataPayload(chunk,version,item) {
  if(!(item instanceof DxfGeoData))return false;
  const data=item;
  chunk.Write(100,'AcDbGeoData');chunk.Write(90,data.Version);if(data.HostBlock===null)throw new NullReferenceException();chunk.Write(330,data.HostBlock.Handle);
  chunk.Write(70,(data.CoordinateType<<16)>>16);WriteGeoVector(chunk,10,data.DesignPoint);WriteGeoVector(chunk,11,data.ReferencePoint);
  chunk.Write(40,data.HorizontalUnitScale);chunk.Write(91,data.HorizontalUnits);chunk.Write(41,data.VerticalUnitScale);chunk.Write(92,data.VerticalUnits);WriteGeoVector(chunk,210,data.UpDirection);
  chunk.Write(12,data.NorthDirection.X);chunk.Write(22,data.NorthDirection.Y);chunk.Write(95,data.ScaleEstimation);chunk.Write(141,data.UserScaleFactor);
  chunk.Write(294,data.SeaLevelCorrection);chunk.Write(142,data.SeaLevelElevation);chunk.Write(143,data.CoordinateProjectionRadius);
  const chunks=SplitGeoDefinition(EncodeDxfDatabaseText(data.CoordinateSystemDefinition.replaceAll('\n','^J'),version));
  for(let i=0;i<chunks.length;i++)chunk.Write(i===chunks.length-1?301:303,chunks[i]);
  chunk.Write(302,EncodeDxfDatabaseText(data.GeoRssTag,version));chunk.Write(305,EncodeDxfDatabaseText(data.ObservationFrom,version));chunk.Write(306,EncodeDxfDatabaseText(data.ObservationTo,version));chunk.Write(307,EncodeDxfDatabaseText(data.ObservationCoverage,version));
  chunk.Write(93,data.MeshPoints.Count);
  for(const point of data.MeshPoints){chunk.Write(13,point.Source.X);chunk.Write(23,point.Source.Y);chunk.Write(14,point.Target.X);chunk.Write(24,point.Target.Y);}
  chunk.Write(96,data.MeshFaces.Count);
  for(const face of data.MeshFaces){chunk.Write(97,face.First);chunk.Write(98,face.Second);chunk.Write(99,face.Third);}
  return true;
}
