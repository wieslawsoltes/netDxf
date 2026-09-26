import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Light } from '../Entities/Light.js';
import { ArgumentException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
import { DecodeDxfText, EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord, WriteXData, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
const properties=new Map([[90,['VersionNumber','ReadInt']],[70,['LightType','ReadShort']],[290,['IsOn','ReadBool']],[291,['PlotGlyph','ReadBool']],
  [40,['Intensity','ReadDouble']],[72,['AttenuationType','ReadShort']],[292,['UseAttenuationLimits','ReadBool']],[41,['AttenuationStartLimit','ReadDouble']],
  [42,['AttenuationEndLimit','ReadDouble']],[50,['HotspotAngle','ReadDouble']],[51,['FalloffAngle','ReadDouble']],[293,['CastShadows','ReadBool']],
  [73,['ShadowType','ReadShort']],[91,['ShadowMapSize','ReadInt']],[280,['ShadowMapSoftness','ReadShort']]]);
export function ReadLight(chunk, document) {
  if(chunk.Code!==100 || chunk.ReadString()!=='AcDbLight')throw new InvalidDataException('LIGHT requires the AcDbLight subclass.');
  const light=new Light(),seen=new Set(),position=light.Position,target=light.Target;
  chunk.Next();
  while(chunk.Code!==0) {
    const code=chunk.Code;
    if(code===1001){light.XData.Add(ReadXDataRecord(chunk,document));continue;}
    if(code===100 || code===101 || code===102)throw new InvalidDataException('Unsupported or duplicate LIGHT subclass/payload marker.');
    const scalar=properties.get(code),point=[10,20,30].includes(code)?position:[11,21,31].includes(code)?target:null;
    if(scalar || point || code===1){if(seen.has(code))throw new InvalidDataException('Duplicate LIGHT group '+code+'.');seen.add(code);}
    try {
      if(code===1)light.Name=DecodeDxfText(chunk.ReadString());
      else if(scalar)light[scalar[0]]=chunk[scalar[1]]();
      else if(point)point[code<20?'X':code<30?'Y':'Z']=chunk.ReadDouble();
      else if(code>=1000 && code<=1071)throw new InvalidDataException('LIGHT XData must start with an application registry.');
    } catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid LIGHT group '+code+' at position '+chunk.CurrentPosition+'.',error);throw error;}
    chunk.Next();
  }
  for(const start of [10,11]){const count=Number(seen.has(start))+Number(seen.has(start+10))+Number(seen.has(start+20));if(count!==0&&count!==3)throw new InvalidDataException('Incomplete LIGHT point at group '+start+'.');}
  light.Position=position;light.Target=target;return light;
}
export function ValidateLightVersions(document){if(document.DrawingVariables.AcadVer>=15)return;for(const block of document.Blocks)for(const entity of block.Entities)if(entity instanceof Light)throw new NotSupportedException('LIGHT requires AutoCAD 2007 (AC1021) or later in this writer profile.');}
export function WriteLight(chunk, version, light) {
  chunk.Write(100,'AcDbLight');if(light==null)throw new NullReferenceException();chunk.Write(90,light.VersionNumber);chunk.Write(1,EncodeDxfDatabaseText(light.Name,version));
  chunk.Write(70,light.LightType);chunk.Write(290,light.IsOn);chunk.Write(291,light.PlotGlyph);chunk.Write(40,light.Intensity);
  chunk.Write(10,light.Position.X);chunk.Write(20,light.Position.Y);chunk.Write(30,light.Position.Z);
  chunk.Write(11,light.Target.X);chunk.Write(21,light.Target.Y);chunk.Write(31,light.Target.Z);
  chunk.Write(72,light.AttenuationType);chunk.Write(292,light.UseAttenuationLimits);chunk.Write(41,light.AttenuationStartLimit);chunk.Write(42,light.AttenuationEndLimit);
  chunk.Write(50,light.HotspotAngle);chunk.Write(51,light.FalloffAngle);chunk.Write(293,light.CastShadows);chunk.Write(73,light.ShadowType);
  chunk.Write(91,light.ShadowMapSize);chunk.Write(280,light.ShadowMapSoftness);WriteXData(chunk,version,light.XData);
}
