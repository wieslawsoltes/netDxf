// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredDimAssoc } from '../Objects/DxfStoredDimAssoc.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteStoredDimAssocPayload(chunk,version,item){
  if(!(item instanceof DxfStoredDimAssoc))return false;
  for(const tag of item.Tags)chunk.Write(tag.Code,tag.Value);return true;
}
export function PrepareStoredDimAssocClass(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='DIMASSOC').length,typed=Array.from(document.Objects.Items).some(i=>i instanceof DxfStoredDimAssoc);
  if(definitions.Contains('DIMASSOC')){const definition=definitions.get_Item('DIMASSOC');
    if(definition.CppClassName!=='AcDbDimAssoc'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with stored DIMASSOC.');return;}
    if(typed||count===0)definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('DIMASSOC','AcDbDimAssoc','ObjectDBX Classes');definition.ProxyFlags=0;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
