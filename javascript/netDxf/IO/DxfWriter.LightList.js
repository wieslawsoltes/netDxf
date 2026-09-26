// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfLightList } from '../Objects/DxfLightList.js';import { DxfClass } from '../DxfClass.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function PrepareLightListClass(document,definitions){
  const typed=Array.from(document.Objects.Items).some(item=>item instanceof DxfLightList),count=Array.from(document.Objects.Items).filter(item=>item.CodeName==='LIGHTLIST').length;
  if(definitions.Contains('LIGHTLIST')){
    const definition=definitions.get_Item('LIGHTLIST');
    if(definition.CppClassName!=='AcDbLightList'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with typed LIGHTLIST.');return;}definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('LIGHTLIST','AcDbLightList','SCENEOE');definition.ProxyFlags=1025;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
export function WriteLightListPayload(chunk,version,item){
  if(!(item instanceof DxfLightList))return false;chunk.Write(100,'AcDbLightList');chunk.Write(90,item.StoredVersion);chunk.Write(90,item.Entries.Count);
  for(const entry of item.Entries){chunk.Write(5,entry.Light.Handle);chunk.Write(1,EncodeDxfDatabaseText(entry.Name,version));}return true;
}
