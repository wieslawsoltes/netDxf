// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfSpatialIndex } from '../Objects/DxfSpatialIndex.js';
import { DxfVbaProject } from '../Objects/DxfVbaProject.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteStoredEnvelopePayload(chunk,version,item){
  if(item instanceof DxfSpatialIndex){chunk.Write(100,'AcDbIndex');chunk.Write(40,item.Timestamp);chunk.Write(100,'AcDbSpatialIndex');}
  else if(item instanceof DxfVbaProject){chunk.Write(100,'AcDbVbaProject');chunk.Write(90,item.DataLength);for(const data of item.StoredChunks)chunk.Write(310,data);}
  else return false;return true;
}
export function PrepareStoredEnvelopeClasses(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='SPATIAL_INDEX').length;
  if(!Array.from(document.Objects.Items).some(i=>i instanceof DxfSpatialIndex)){
    if(definitions.Contains('SPATIAL_INDEX')&&definitions.get_Item('SPATIAL_INDEX').CppClassName==='AcDbSpatialIndex'&&!definitions.get_Item('SPATIAL_INDEX').IsEntity)definitions.get_Item('SPATIAL_INDEX').InstanceCount=count;return;
  }
  if(definitions.Contains('SPATIAL_INDEX')){const definition=definitions.get_Item('SPATIAL_INDEX');
    if(definition.CppClassName!=='AcDbSpatialIndex'||definition.IsEntity)throw new InvalidDataException('CLASS conflicts with typed SPATIAL_INDEX.');definition.InstanceCount=count;
  }else{const definition=new DxfClass('SPATIAL_INDEX','AcDbSpatialIndex','ObjectDBX Classes');definition.ProxyFlags=0;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
