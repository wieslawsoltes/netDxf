// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfStoredField } from '../Objects/DxfStoredField.js';
import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';
import { ReadStoredObjectHeader } from './DxfReader.LayerFilterPointer.js';
import { PayloadEnd } from '../../runtime/DatabaseIOContext.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException } from '../../runtime/Errors.js';
// Explicit out adapter preserves values assigned before a false return or exception.
export function TryReadStoredFieldHeader(tags,start,end,output) {
  output.evaluator=null;output.code=null;output.children=[];output.objects=[];
  let i=start;
  if(i>=end||tags[i].Code!==1)return false;
  output.evaluator=DecodeDxfText(tags[i++].Value);
  if(i>=end||tags[i].Code!==2)return false;
  const parts=[tags[i++].Value];while(i<end&&tags[i].Code===3)parts.push(tags[i++].Value);
  output.code=DecodeDxfText(parts.join(''));
  if(i>=end||tags[i].Code!==90)return false;
  let count=tags[i++].Value;
  while(i<end&&tags[i].Code===360)output.children.push(tags[i++].Value);
  if(count<0||count!==output.children.length)throw new FormatException('FIELD child count differs from its leading group-360 sequence.');
  if(i>=end||tags[i].Code!==97)return false;
  count=tags[i++].Value;while(i<end&&tags[i].Code===331)output.objects.push(tags[i++].Value);
  if(count<0||count!==output.objects.length)throw new FormatException('FIELD object count differs from its leading group-331 sequence.');
  return true;
}
export function ReadStoredFieldRecord(context,codeName,tags) {
  const {record,opaque,payload:start,handle}=ReadStoredObjectHeader(tags),end=PayloadEnd(tags,start),header={};
  const known=codeName==='FIELD'&&!opaque.length&&start<end&&tags[start].Code===100&&tags[start].Value==='AcDbField'&&tags.slice(start+1,end).every(t=>t.Code!==100&&t.Code!==102);
  if(known&&TryReadStoredFieldHeader(tags,start+1,end,header)) {
    const field=new DxfStoredField(context.Document,tags.slice(start,end),header.evaluator,header.code);record.Object=field;
    if(end<tags.length)context.ReadDatabaseXData(field,tags,end);
    context.storedFields.push([field,header.children,header.objects]);
  } else {for(const tag of tags.slice(start))opaque.push(tag);record.Object=new DxfOpaqueObject(codeName,opaque);}
  record.Object.Handle=handle;return record;
}
export function ResolveStoredFields(context) {
  for(const [field,children,objects] of context.storedFields)field.Resolve(children,objects,h=>context.GetObjectBySourceHandle(h));
}
