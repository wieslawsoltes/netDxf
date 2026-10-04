// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredSunStudy } from '../Objects/DxfStoredSunStudy.js';
export function WriteStoredSunStudyPayload(chunk,version,item){
  if(!(item instanceof DxfStoredSunStudy))return false;
  for(const tag of item.Payload)chunk.Write(tag.Code,tag.Value);return true;
}
