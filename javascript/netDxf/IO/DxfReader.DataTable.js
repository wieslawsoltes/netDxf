// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDataTable,DxfDataColumn } from '../Objects/DxfDataTable.js';import { DxfOpaqueObject } from '../Objects/DxfOpaqueObject.js';import { Vector3 } from '../Vector3.js';
import { DatabaseRecord,PayloadEnd,WrappedFormat } from '../../runtime/DatabaseIOContext.js';import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { FormatException,ArgumentException } from '../../runtime/Errors.js';
export class DataTableColumnInput {Type=0;Name=null;Values=[];}
export function ReadDataTableRecord(context,tags){
  const record=new DatabaseRecord(),privateHeader=[];let handle=null,reactors=false,extension=false,start=0;
  for(;start<tags.length;start++){
    const tag=tags[start];if(tag.Code===100||tag.Code===1001)break;
    if(tag.Code===5){if(handle!==null)throw new FormatException('DATATABLE repeats its identity.');handle=tag.Value;}
    else if(tag.Code===330){if(record.Metadata.Owner!==null)throw new FormatException('DATATABLE repeats its owner.');record.Metadata.Owner=tag.Value;}
    else if(tag.Code===102){
      const group=tag.Value,begin=start;if(!group.startsWith('{'))throw new FormatException('Invalid DATATABLE control group.');
      while(++start<tags.length&&!(tags[start].Code===102&&tags[start].Value==='}'))
        if([102,100,1001].includes(tags[start].Code))throw new FormatException('Invalid DATATABLE control framing.');
      if(start===tags.length)throw new FormatException('Unterminated DATATABLE control group.');const content=tags.slice(begin+1,start);
      if(group==='{ACAD_REACTORS'){
        if(reactors||content.some(v=>v.Code!==330))throw new FormatException('Invalid DATATABLE reactor group.');reactors=true;for(const value of content)record.Metadata.Reactors.push(value.Value);
      }else if(group==='{ACAD_XDICTIONARY'){
        if(extension||content.length!==1||content[0].Code!==360)throw new FormatException('Invalid DATATABLE extension group.');extension=true;record.Metadata.Extension=content[0].Value;
      }else for(const value of tags.slice(begin,start+1))privateHeader.push(value);
    }else privateHeader.push(tag);
  }
  if(handle===null)throw new FormatException('DATATABLE requires an identity.');
  if(privateHeader.length!==0||!ReadDataTablePayload(context,record,tags,start)){
    for(const tag of tags.slice(start))privateHeader.push(tag);record.Object=new DxfOpaqueObject('DATATABLE',privateHeader);
  }
  record.Object.Handle=handle;return record;
}
const publicCodes=new Set([100,70,90,91,1,92,2,71,93,40,3,10,20,30,11,21,31,331,360,350,340,330]);
export function ReadDataTablePayload(context,record,tags,start=0){
  if(context.Document.DrawingVariables.AcadVer<14)return false;
  const end=PayloadEnd(tags,start);if(start===end||tags[start].Code!==100||tags[start].Value!=='AcDbDataTable')return false;
  for(let j=start+1;j<end;j++){
    const tag=tags[j];if(!publicCodes.has(tag.Code)||tag.Code===100&&tag.Value!=='AcDbDataTable')return false;
    if(tag.Code===70&&tag.Value!==2||tag.Code===92&&(tag.Value<1||tag.Value>11))return false;
  }
  let i=start+1;const take=code=>{if(i>=end||tags[i].Code!==code)throw new FormatException('DATATABLE requires ordered group '+code+' at payload slot '+(i-start)+'.');return tags[i++].Value;};
  take(70);const count=take(90),rows=take(91),max=DxfDataTable.MaximumCells;
  if(count<0||count>max||rows<0||rows>max||count*rows>max)throw new FormatException('DATATABLE dimensions exceed the admitted range.');
  const table=new DxfDataTable(),columns=[];
  try{
    table.Name=DecodeDxfText(take(1));
    for(let c=0;c<count;c++){
      const column=new DataTableColumnInput();column.Type=take(92);column.Name=DecodeDxfText(take(2));DxfDataColumn.CheckText(column.Name);
      for(let r=0;r<rows;r++){
        let value;
        switch(column.Type){
          case 1:value=take(93);break;case 2:value=take(40);break;case 3:value=DecodeDxfText(take(3));break;
          case 10:{const bit=take(71);if(bit!==0&&bit!==1)throw new FormatException('DATATABLE Boolean must be 0 or 1.');value=bit===1;break;}
          case 4:value=new Vector3(take(10),take(20),take(30));break;case 11:value=new Vector3(take(11),take(21),take(31));break;
          case 5:value=take(331);break;case 6:value=take(360);break;case 7:value=take(350);break;case 8:value=take(340);break;case 9:value=take(330);break;
          default:throw new FormatException('Invalid DATATABLE column type.');
        }
        column.Values.push(value);
      }
      if(column.Type<5||column.Type>9)new DxfDataColumn(column.Type,column.Name,column.Values);columns.push(column);
    }
  }catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid DATATABLE value.',error);throw error;}
  if(i!==end)throw new FormatException('DATATABLE contains trailing or repeated public fields.');
  if(end<tags.length)context.ReadDatabaseXData(table,tags,end);record.Object=table;context.dataTableReferences.push([table,rows,columns]);return true;
}
export function ResolveDataTableReferences(context){
  for(const [table,rows,pending]of context.dataTableReferences){
    const columns=[];
    try{
      for(const column of pending){
        const values=[];
        for(const input of column.Values){
          if(column.Type<5||column.Type>9){values.push(input);continue;}
          const target=input==='0'?null:context.GetObjectBySourceHandle(input);
          if(target===null&&input!=='0')throw new FormatException('Unresolved DATATABLE cell reference: '+input);values.push(target);
        }
        columns.push(new DxfDataColumn(column.Type,column.Name,values));
      }
      table.SetLoadedColumns(rows,columns);
    }catch(error){if(error instanceof ArgumentException)throw WrappedFormat('Invalid DATATABLE ownership or reference graph.',error);throw error;}
  }
}
