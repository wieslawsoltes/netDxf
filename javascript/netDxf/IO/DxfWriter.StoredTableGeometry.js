// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredTableGeometry } from '../Objects/DxfStoredTableGeometry.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteStoredTableGeometryPayload(chunk,version,item){
  if(!(item instanceof DxfStoredTableGeometry))return false;
  for(const tag of item.Payload)chunk.Write(tag.Code,tag.Value);return true;
}
export function PrepareStoredTableGeometryClass(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='TABLEGEOMETRY').length,typed=Array.from(document.Objects.Items).some(i=>i instanceof DxfStoredTableGeometry);
  if(definitions.Contains('TABLEGEOMETRY')){const definition=definitions.get_Item('TABLEGEOMETRY');
    if(definition.CppClassName!=='AcDbTableGeometry'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with stored TABLEGEOMETRY.');return;}
    if(typed||count===0)definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('TABLEGEOMETRY','AcDbTableGeometry','ObjectDBX Classes');definition.ProxyFlags=1152;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
