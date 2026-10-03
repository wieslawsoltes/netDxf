// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { EncodeDxfDatabaseText } from '../../runtime/DxfStringEncoding.js';
import { WriteXData } from '../../runtime/DxfXDataIO.js';
import { InvalidDataException } from '../../runtime/Errors.js';
const allEntities=doc=>Array.from(doc.Blocks).flatMap(block=>Array.from(block.Entities));
const profile=version=>typeof version==='function'?version():version;
function prepare(definitions,name,cpp,app,isEntity,count,typed,flags){
  if(definitions.Contains(name)){
    const item=definitions.get_Item(name);
    if(item.CppClassName!==cpp||item.IsEntity!==isEntity){if(typed)throw new InvalidDataException('CLASS conflicts with '+name);return;}
    item.InstanceCount=count;
  }else if(typed){const item=new api.DxfClass(name,cpp,app);item.ProxyFlags=flags;item.IsEntity=isEntity;item.InstanceCount=count;definitions.Add(item);}
}
export function PrepareMultiLeaderClasses(doc,definitions){
  const count=allEntities(doc).filter(e=>e instanceof api.MultiLeader).length;
  const objects=Array.from(doc.Objects.Items),styles=objects.filter(o=>o.CodeName==='MLEADERSTYLE').length;
  prepare(definitions,'MULTILEADER','AcDbMLeader','ACDB_MLEADER_CLASS',true,count,count>0,1025);
  prepare(definitions,'MLEADERSTYLE','AcDbMLeaderStyle','ACDB_MLEADERSTYLE_CLASS',false,styles,objects.some(o=>o instanceof api.DxfMLeaderStyle),4095);
}

export function ValidateMultiLeaders(document) {
  for(const block of document.Blocks) for(const leader of block.Entities)
    if(leader instanceof api.MultiLeader) leader.Validate(document,document.DrawingVariables.AcadVer);
}
export function WriteMLeaderVector(chunk,code,value) {
  value=Copy(value);chunk.Write(code,value.X); chunk.Write(code+10,value.Y); chunk.Write(code+20,value.Z);
}
export function WriteMLeaderFields(chunk,version,data,include=null) {
  for(const field of data.Fields) {
    if(include!==null&&!include(field))continue;
    const value=data.Value(field); if(value===null)continue;
    if(field.Reference)chunk.Write(field.Code,value.Handle);
    else if(value instanceof api.Vector3)WriteMLeaderVector(chunk,field.Code,value);
    else if(typeof value==='string')chunk.Write(field.Code,EncodeDxfDatabaseText(value,profile(version)));
    else chunk.Write(field.Code,value);
  }
}
export function WriteMLeaderContext(chunk,version,context) {
  const fields=(data,include=null)=>WriteMLeaderFields(chunk,version,data,include);
  chunk.Write(300,'CONTEXT_DATA{');
  fields(context,f=>![110,111,112,297,272,273].includes(f.Code));
  chunk.Write(290,context.MText!==null);
  if(context.MText!==null) {
    fields(context.MText,f=>f.Code!==295);
    for(const value of context.MText.ColumnHeights)chunk.Write(144,value);
    chunk.Write(295,context.MText.UseWordBreak);
  }
  chunk.Write(296,context.Block!==null);
  if(context.Block!==null) {
    fields(context.Block); for(const value of context.Block.TransformationMatrix)chunk.Write(47,value);
  }
  fields(context,f=>[110,111,112,297].includes(f.Code));
  for(const node of context.Leaders) {
    chunk.Write(302,'LEADER{'); fields(node,f=>![90,40,271].includes(f.Code));
    for(const pair of node.Breaks){WriteMLeaderVector(chunk,12,pair.Start);WriteMLeaderVector(chunk,13,pair.End);}
    fields(node,f=>f.Code===90||f.Code===40);
    for(const line of node.Lines) {
      chunk.Write(304,'LEADER_LINE{');for(const point of line.Vertices)WriteMLeaderVector(chunk,10,point);
      for(const group of line.Breaks) {
        chunk.Write(90,group.Index);
        for(const pair of group.Breaks){WriteMLeaderVector(chunk,11,pair.Start);WriteMLeaderVector(chunk,12,pair.End);}
      }
      fields(line);chunk.Write(305,'}');
    }
    fields(node,f=>f.Code===271);chunk.Write(303,'}');
  }
  fields(context,f=>f.Code===272||f.Code===273);chunk.Write(301,'}');
}
export function WriteMultiLeader(chunk,document,leader) {
  const version=()=>document.DrawingVariables.AcadVer;
  chunk.Write(100,'AcDbMLeader');if(leader.StoredVersion!==null)chunk.Write(270,leader.StoredVersion);
  WriteMLeaderContext(chunk,version,leader.Context);
  const tail=[294,178,179,45,271,272,273,295];
  WriteMLeaderFields(chunk,version,leader.Properties,f=>!tail.includes(f.Code));
  for(const arrow of leader.Properties.ArrowHeads)WriteMLeaderFields(chunk,version,arrow);
  for(const attribute of leader.Properties.BlockAttributes)WriteMLeaderFields(chunk,version,attribute);
  WriteMLeaderFields(chunk,version,leader.Properties,f=>tail.includes(f.Code));
  WriteXData(chunk,version,leader.XData);
}
export function WriteMLeaderStylePayload(chunk,version,item) {
  if(!(item instanceof api.DxfMLeaderStyle))return false;
  chunk.Write(100,'AcDbMLeaderStyle');if(item.StoredEnvelopeValue!==null)chunk.Write(179,item.StoredEnvelopeValue);
  WriteMLeaderFields(chunk,version,item.Properties);return true;
}
