// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Explicit state for original private database payload methods. Not a DXF file loader.
import { XData } from '../netDxf/XData.js';
import { XDataRecord } from '../netDxf/XDataRecord.js';
import { ApplicationRegistry } from '../netDxf/Tables/ApplicationRegistry.js';
import { DecodeDxfText } from './DxfStringEncoding.js';
import { FormatException, ArgumentOutOfRangeException } from './Errors.js';
import { SourceIdentityContext } from '../netDxf/IO/DxfReader.SourceIdentity.js';
export class DatabaseRecord {
  Object=null; SourceIdentity=null; Default=null;
  Metadata={Owner:null,Extension:null,Reactors:[]};
  Entries=[]; ContainerReferences=[]; SortKeys=[];
}
export class DatabaseIOContext extends SourceIdentityContext {
  dataTableReferences=[]; lightListReferences=[]; pendingLayerIndexes=new Map();
  constructor(document){super(document);}
  ReadDatabaseXData(target,tags,start){
    let data=null;
    for(let i=start;i<tags.length;i++){
      const tag=tags[i];
      if(tag.Code===1001){
        const name=DecodeDxfText(tag.Value),found={};
        const app=this.Document.ApplicationRegistries.TryGetValue(name,found)?found.value:this.Document.ApplicationRegistries.Add(new ApplicationRegistry(name));
        data=new XData(app);target.XData.Add(data);
      }else{
        if(data===null||tag.Code<1000||tag.Code>1071)throw new FormatException('Invalid object XData.');
        let value=tag.Value;if(typeof value==='string'&&tag.Code!==1005)value=DecodeDxfText(value);
        data.XDataRecord.Add(new XDataRecord(tag.Code,value));
      }
    }
  }
}
export function PayloadEnd(tags,start){
  if(!Number.isInteger(start)||start<0||start>tags.length)throw new ArgumentOutOfRangeException('startIndex');
  const end=tags.findIndex((tag,i)=>i>=start&&tag.Code===1001);return end<0?tags.length:end;
}
export function WrappedFormat(message,cause){const error=new FormatException(message,{cause});error.InnerException=cause;return error;}
