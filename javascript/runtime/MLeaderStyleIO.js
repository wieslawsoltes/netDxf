// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
// Selected MLEADERSTYLE methods from the MultiLeader partials; not complete partial mirrors.
import { DxfMLeaderStyle } from '../netDxf/Objects/DxfMLeaderStyle.js';
import { DecodeDxfText, EncodeDxfDatabaseText } from './DxfStringEncoding.js';
import { ArgumentException, InvalidDataException } from './Errors.js';
import { WrappedInvalidData } from './DxfXDataIO.js';
export function ReadMLeaderStylePayload(context,record,name,tags,start){
  if(name!=='MLEADERSTYLE'||context.Document.DrawingVariables.AcadVer<15)return false;
  if(start>=tags.length||tags[start].Code!==100)throw new InvalidDataException('MLEADERSTYLE requires subclass data.');
  if(tags[start].Value!=='AcDbMLeaderStyle')return false;
  let end=tags.findIndex((t,i)=>i>=start&&t.Code===1001);if(end<0)end=tags.length;
  const style=new DxfMLeaderStyle(),body=tags.slice(start+1,end),seen=new Set(),pendingStart=context.mleaderReferences.length;
  let at=0,typed=false,known=true,publicScope=true;
  const error=message=>new InvalidDataException(message+' (MULTILEADER payload tag '+at+').');
  try{
    if(body[at]?.Code===179){if(body[at++].Value!==2)throw error('Only MLEADERSTYLE envelope value 179=2 is qualified');style.StoredEnvelopeValue=2;}
    else style.StoredEnvelopeValue=null;
    while(at<body.length){
      const code=body[at].Code;
      if(code===100){if(body[at++].Value==='AcDbMLeaderStyle')throw error('Duplicate MLEADERSTYLE subclass');known=false;publicScope=false;}
      else if(!publicScope)at++;
      else if(code===179)throw error('Duplicate or misplaced MLEADERSTYLE envelope value');
      else{
        const field=style.Properties.Fields.find(f=>f.Code===code||f.Type==='Vector3'&&(f.Code+10===code||f.Code+20===code));
        if(!field){known=false;at++;continue;}
        if(seen.has(code))throw error('Duplicate group '+code);seen.add(code);
        let value=body[at++].Value;
        try{
          if(field.Reference)context.mleaderReferences.push([style.Properties,field.Code,value]);
          else if(field.Type==='Vector3'){
            const vector=style.Properties.Get(field.Code);vector[code===field.Code?'X':code===field.Code+10?'Y':'Z']=value;style.Properties.Set(field.Code,vector);
          }else{if(typeof value==='string')value=DecodeDxfText(value);style.Properties.Set(code,value);}
        }catch(e){if(e instanceof ArgumentException)throw WrappedInvalidData('Invalid MULTILEADER group '+code,e);throw e;}
      }
    }
    for(const field of style.Properties.Fields)if(field.Type==='Vector3'){
      const count=[field.Code,field.Code+10,field.Code+20].filter(code=>seen.has(code)).length;
      if(count!==0&&count!==3)throw error('Incomplete vector starting at group '+field.Code);
    }
    if(!known)return false;
    if(end<tags.length)context.ReadDatabaseXData(style,tags,end);
    record.Object=style;typed=true;return true;
  }finally{if(!typed)context.mleaderReferences.splice(pendingStart);}
}
export function WriteMLeaderStylePayload(chunk,version,item){
  if(!(item instanceof DxfMLeaderStyle))return false;
  chunk.Write(100,'AcDbMLeaderStyle');if(item.StoredEnvelopeValue!==null)chunk.Write(179,item.StoredEnvelopeValue);
  for(const field of item.Properties.Fields){const value=item.Properties.Value(field);if(value===null)continue;
    if(field.Reference)chunk.Write(field.Code,value.Handle);
    else if(field.Type==='Vector3'){chunk.Write(field.Code,value.X);chunk.Write(field.Code+10,value.Y);chunk.Write(field.Code+20,value.Z);}
    else chunk.Write(field.Code,typeof value==='string'?EncodeDxfDatabaseText(value,version):value);
  }
  return true;
}
