// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredTableContent } from '../Objects/DxfStoredTableContent.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteStoredTableContentPayload(chunk,version,item){
  if(!(item instanceof DxfStoredTableContent))return false;
  for(const tag of item.Payload)chunk.Write(tag.Code,tag.Value);return true;
}
export function PrepareStoredTableContentClass(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='TABLECONTENT').length,typed=Array.from(document.Objects.Items).some(i=>i instanceof DxfStoredTableContent);
  if(definitions.Contains('TABLECONTENT')){const definition=definitions.get_Item('TABLECONTENT');
    if(definition.CppClassName!=='AcDbTableContent'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with stored TABLECONTENT.');return;}
    if(typed||count===0)definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('TABLECONTENT','AcDbTableContent','ObjectDBX Classes');definition.ProxyFlags=1152;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
