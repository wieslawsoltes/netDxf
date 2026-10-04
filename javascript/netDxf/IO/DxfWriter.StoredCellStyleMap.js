// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredCellStyleMap } from '../Objects/DxfStoredCellStyleMap.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteStoredCellStyleMapPayload(chunk,version,item){
  if(!(item instanceof DxfStoredCellStyleMap))return false;
  for(const tag of item.Payload)chunk.Write(tag.Code,tag.Value);return true;
}
export function PrepareStoredCellStyleMapClass(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='CELLSTYLEMAP').length,typed=Array.from(document.Objects.Items).some(i=>i instanceof DxfStoredCellStyleMap);
  if(definitions.Contains('CELLSTYLEMAP')){const definition=definitions.get_Item('CELLSTYLEMAP');
    if(definition.CppClassName!=='AcDbCellStyleMap'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with stored CELLSTYLEMAP.');return;}
    if(typed||count===0)definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('CELLSTYLEMAP','AcDbCellStyleMap','ObjectDBX Classes');definition.ProxyFlags=1152;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
