// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredField } from '../Objects/DxfStoredField.js';
import { DxfClass } from '../DxfClass.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function PrepareStoredFieldClass(document,definitions){
  const fields=Array.from(document.Objects.Items).filter(i=>i instanceof DxfStoredField);
  for(const field of fields)field.ValidateSource(document);
  const count=Array.from(document.Objects.Items).filter(i=>i.CodeName==='FIELD').length;if(!fields.length)return;
  if(definitions.Contains('FIELD')){const definition=definitions.get_Item('FIELD');
    if(definition.CppClassName!=='AcDbField'||definition.IsEntity)throw new InvalidDataException('CLASS conflicts with stored FIELD objects.');definition.InstanceCount=count;
  }else{const definition=new DxfClass('FIELD','AcDbField','ObjectDBX Classes');definition.ProxyFlags=1152;definition.IsEntity=false;definition.InstanceCount=count;definitions.Add(definition);}
}
export function WriteStoredFieldPayload(chunk,version,item,document){
  if(!(item instanceof DxfStoredField))return false;
  item.ValidateSource(document);for(const tag of item.Payload)chunk.Write(tag.Code,tag.Value);return true;
}
