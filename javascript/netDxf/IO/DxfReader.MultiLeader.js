// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { DxfTag } from './DxfTag.js';
import { DxfMLeaderStyle } from '../Objects/DxfMLeaderStyle.js';
import { DecodeDxfText } from '../../runtime/DxfStringEncoding.js';
import { ReadXDataRecord, WrappedInvalidData } from '../../runtime/DxfXDataIO.js';
import { ArgumentException, InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
/** Stateful parser for the complete stored public MULTILEADER packet grammar. */
export class MLeaderParser {
  constructor(context,tags){this.context=context;this.tags=tags;this.at=0;}
  get End(){return this.at>=this.tags.length;}
  get Code(){return this.End?-1:this.tags[this.at].Code;}
  Error(message){return new InvalidDataException(message+' (MULTILEADER payload tag '+this.at+').');}
  Take(code){if(this.Code!==code)throw this.Error('Expected group '+code+', found '+this.Code);return this.tags[this.at++];}
  Marker(code,value){const tag=this.Take(code);if(typeof tag.Value!=='string'||tag.Value!==value)throw this.Error('Invalid context delimiter');}
  Flag(code){return this.Take(code).Value;}
  Point(code){const x=this.Take(code).Value,y=this.Take(code+10).Value,z=this.Take(code+20).Value;return new api.Vector3(x,y,z);}
  Scalar(data,seen) {
    const code=this.Code,field=data.Fields.find(f=>f.Code===code||f.Type==='Vector3'&&(f.Code+10===code||f.Code+20===code));
    if(!field)return false;
    if(seen.has(code))throw this.Error('Duplicate group '+code);seen.add(code);
    let value=this.tags[this.at++].Value;
    try {
      if(field.Reference)this.context.mleaderReferences.push([data,field.Code,value]);
      else if(field.Type==='Vector3') {
        const vector=data.Get(field.Code);vector[code===field.Code?'X':code===field.Code+10?'Y':'Z']=value;data.Set(field.Code,vector);
      }else {if(typeof value==='string')value=DecodeDxfText(value);data.Set(code,value);}
    }catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid MULTILEADER group '+code,error);throw error;}
    return true;
  }
  Complete(data,seen) {
    for(const field of data.Fields)if(field.Type==='Vector3') {
      const count=[field.Code,field.Code+10,field.Code+20].filter(code=>seen.has(code)).length;
      if(count!==0&&count!==3)throw this.Error('Incomplete vector starting at group '+field.Code);
    }
  }
  ReadEntity(leader) {
    if(this.Code===270) {
      if(this.Take(270).Value!==2)throw this.Error('Only AcDbMLeader version 2 is qualified');leader.StoredVersion=2;
    }else {
      if(this.Code!==300)throw this.Error('An omitted group 270 must be followed by the context envelope');leader.StoredVersion=null;
    }
    let context=false;const seen=new Set();
    while(!this.End) {
      if(this.Code===300) {
        if(context)throw this.Error('Duplicate context');this.ReadContext(leader.Context);context=true;
      }else if(this.Code===94) {
        const arrow=new api.MLeaderArrowHead(),fields=new Set();this.Scalar(arrow,fields);
        if(this.Code!==345)throw this.Error('Arrow index requires an arrow block handle');this.Scalar(arrow,fields);leader.Properties.ArrowHeads.Add(arrow);
      }else if(this.Code===330) {
        const attribute=new api.MLeaderBlockAttribute(),fields=new Set();
        for(const code of [330,177,44,302]){if(this.Code!==code)throw this.Error('Incomplete block attribute packet');this.Scalar(attribute,fields);}
        leader.Properties.BlockAttributes.Add(attribute);
      }else if(!this.Scalar(leader.Properties,seen))throw this.Error('Unsupported entity group '+this.Code);
    }
    if(!context)throw this.Error('Missing context');this.Complete(leader.Properties,seen);
  }
  ReadContext(context) {
    this.Marker(300,'CONTEXT_DATA{');const seen=new Set();let mtext=false,block=false;
    while(this.Code!==301) {
      if(this.Code===290) {
        if(mtext||block)throw this.Error('Duplicate or misplaced text-content flag');mtext=true;
        if(this.Flag(290)) {
          const content=new api.MLeaderMTextContent(),fields=new Set();
          while(this.Code!==296) {
            if(this.Code===144)content.ColumnHeights.Add(this.Take(144).Value);
            else if(!this.Scalar(content,fields))throw this.Error('Unsupported or unterminated embedded text');
          }
          this.Complete(content,fields);context.MText=content;
        }
      }else if(this.Code===296) {
        if(!mtext||block)throw this.Error('Duplicate or misplaced block-content flag');block=true;
        if(this.Flag(296)) {
          if(context.MText!==null)throw this.Error('Text and block content are mutually exclusive');
          const content=new api.MLeaderBlockContent(),fields=new Set();
          while(true) {
            if(this.Code===47)content.TransformationMatrix.Add(this.Take(47).Value);
            else if(!this.Scalar(content,fields))break;
          }
          this.Complete(content,fields);
          if(content.TransformationMatrix.Count!==0&&content.TransformationMatrix.Count!==16)throw this.Error('Incomplete block transformation matrix');
          context.Block=content;
        }
      }else if(this.Code===302) {
        if(!block)throw this.Error('Leader precedes content flags');context.Leaders.Add(this.ReadNode());
      }else if(!this.Scalar(context,seen))throw this.Error('Unsupported or unterminated context');
    }
    if(!mtext||!block)throw this.Error('Context requires both content-presence flags');this.Marker(301,'}');this.Complete(context,seen);
  }
  ReadNode() {
    this.Marker(302,'LEADER{');const node=new api.MLeaderNode(),seen=new Set();
    while(this.Code!==303) {
      if(this.Code===304)node.Lines.Add(this.ReadLine());
      else if(this.Code===12)node.Breaks.Add(new api.MLeaderBreak(this.Point(12),this.Point(13)));
      else if(!this.Scalar(node,seen))throw this.Error('Unsupported or unterminated leader branch');
    }
    this.Marker(303,'}');this.Complete(node,seen);return node;
  }
  ReadLine() {
    this.Marker(304,'LEADER_LINE{');const line=new api.MLeaderLine(),seen=new Set();
    while(this.Code!==305) {
      if(this.Code===10)line.Vertices.Add(this.Point(10));
      else if(this.Code===90) {
        const group=new api.MLeaderLineBreaks();group.Index=this.Take(90).Value;
        while(this.Code===11)group.Breaks.Add(new api.MLeaderBreak(this.Point(11),this.Point(12)));
        if(group.Breaks.Count===0)throw this.Error('A break index requires at least one complete endpoint pair');line.Breaks.Add(group);
      }else if(!this.Scalar(line,seen))throw this.Error('Unsupported or unterminated leader line');
    }
    this.Marker(305,'}');this.Complete(line,seen);return line;
  }
}
export function ReadMultiLeader(context) {
  const document=context.Document,chunk=context.Chunk;
  if(document.DrawingVariables.AcadVer<15)throw new NotSupportedException('Typed MULTILEADER input requires the qualified R2007 or later profile.');
  if(chunk.Code!==100||chunk.ReadString()!=='AcDbMLeader')throw new InvalidDataException('MULTILEADER requires AcDbMLeader subclass data.');
  const leader=new api.MultiLeader(),tags=[];leader.PendingInputReferences=true;let extended=false;chunk.Next();
  while(chunk.Code!==0) {
    if(chunk.Code===1001){extended=true;leader.XData.Add(ReadXDataRecord(chunk,document));continue;}
    if(extended)throw new InvalidDataException('MULTILEADER XData must follow the complete entity payload.');
    tags.push(new DxfTag(chunk.Code,chunk.Value));chunk.Next();
  }
  new MLeaderParser(context,tags).ReadEntity(leader);context.loadedMLeaders.push(leader);return leader;
}
export function ResolveMultiLeaderReferences(context) {
  const document=context.Document;
  for(const [data,code,handle] of context.mleaderReferences) {
    const target=handle==='0'?null:document.GetObjectByHandle(handle);
    if(target===null&&handle!=='0')throw new InvalidDataException('Unresolved MULTILEADER reference: '+handle);
    try{data.Set(code,target);}catch(error){if(error instanceof ArgumentException)throw WrappedInvalidData('Invalid MULTILEADER reference target type or ownership.',error);throw error;}
  }
  for(const leader of context.loadedMLeaders){leader.PendingInputReferences=false;leader.Validate(document,document.DrawingVariables.AcadVer);}
  for(const style of document.Objects.Items)if(style instanceof DxfMLeaderStyle)style.ValidateValues(document.DrawingVariables.AcadVer);
}
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
