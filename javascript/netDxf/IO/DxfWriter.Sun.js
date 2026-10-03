// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfSun } from '../Objects/DxfSun.js';
import { SunReferences } from '../Objects/SunReferences.js';
import { PrepareStoredEnvelopeClass } from './DxfWriter.LayerFilterPointer.js';
export function PrepareSunClass(document,definitions) {
  PrepareStoredEnvelopeClass(document,definitions,'SUN','AcDbSun','SCENEOE',1153,Array.from(document.Objects.Items).some(item=>item instanceof DxfSun));
}
export function WriteSunPayload(chunk,version,item) {
  if(!(item instanceof DxfSun))return false;
  chunk.Write(100,'AcDbSun');chunk.Write(90,item.StoredVersion);chunk.Write(290,item.Enabled);chunk.Write(63,item.ColorIndex);
  if(item.TrueColor!==null)chunk.Write(421,item.TrueColor);
  chunk.Write(40,item.Intensity);chunk.Write(291,item.ShadowsEnabled);chunk.Write(91,item.JulianDay);chunk.Write(92,item.StoredTime);chunk.Write(292,item.DaylightSavingTime);
  chunk.Write(70,(item.ShadowType<<16)>>16);chunk.Write(71,item.ShadowMapSize);chunk.Write(280,item.ShadowSoftness);return true;
}
export function WriteSunReference(chunk,version,owner) {
  if(!SunReferences.IsPresent(owner))return;
  SunReferences.CheckProfile(owner,version);chunk.Write(361,SunReferences.Get(owner)?.Handle??'0');
}
