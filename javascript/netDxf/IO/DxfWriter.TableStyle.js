// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfTableStyle } from '../Objects/DxfTableStyle.js';
import { DxfClass } from '../DxfClass.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function WriteTableStylePayload(chunk,version,item){
  if(!(item instanceof DxfTableStyle))return false;
  for(const tag of item.Tags){const name=item.ChangedStyleName(tag);chunk.Write(tag.Code,name===null?tag.Value:EncodeDxfDatabaseText(name,version));}return true;
}
export function PrepareTableStyleClass(document,definitions){
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='TABLESTYLE').length,typed=Array.from(document.Objects.Items).some(i=>i instanceof DxfTableStyle);
  if(definitions.Contains('TABLESTYLE')){const definition=definitions.get_Item('TABLESTYLE');
    if(definition.CppClassName!=='AcDbTableStyle'||definition.IsEntity){if(typed)throw new InvalidDataException('CLASS conflicts with stored TABLESTYLE.');return;}
    if(typed||count===0)definition.InstanceCount=count;
  }else if(typed){const definition=new DxfClass('TABLESTYLE','AcDbTableStyle','ObjectDBX Classes');definition.ProxyFlags=4095;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
