// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDataTable } from '../Objects/DxfDataTable.js';import { PrepareStoredEnvelopeClass } from './DxfWriter.LayerFilterPointer.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
export function PrepareDataTableClass(document,definitions){
  PrepareStoredEnvelopeClass(document,definitions,'DATATABLE','AcDbDataTable','ObjectDBX Classes',0,Array.from(document.Objects.Items).some(item=>item instanceof DxfDataTable));
}
export function WriteDataTablePayload(chunk,version,item){
  if(!(item instanceof DxfDataTable))return false;chunk.Write(100,'AcDbDataTable');chunk.Write(70,item.StoredVersion);chunk.Write(90,item.Columns.Count);chunk.Write(91,item.RowCount);chunk.Write(1,EncodeDxfDatabaseText(item.Name,version));
  for(const column of item.Columns){
    chunk.Write(92,column.Type);chunk.Write(2,EncodeDxfDatabaseText(column.Name,version));
    for(const value of column.Values){
      switch(column.Type){
        case 1:chunk.Write(93,value);break;case 2:chunk.Write(40,value);break;case 3:chunk.Write(3,EncodeDxfDatabaseText(value,version));break;case 10:chunk.Write(71,value?1:0);break;
        case 4:case 11:{const first=column.Type===4?10:11;chunk.Write(first,value.X);chunk.Write(first+10,value.Y);chunk.Write(first+20,value.Z);break;}
        default:chunk.Write(column.Type===5?331:column.Type===6?360:column.Type===7?350:column.Type===8?340:330,value?.Handle??'0');break;
      }
    }
  }
  return true;
}
