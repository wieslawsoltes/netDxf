// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// The original DxfReader.cs ReadVertex and ReadPolyline2D routines needed by fitted legacy transport.
// These selected routines do not count as a complete DxfReader.cs source mirror.
import * as api from '../index.js';
import { Vertex } from '../netDxf/Entities/Vertex.js';
import { DecodeDxfText } from './DxfStringEncoding.js';
import { GetRecordResource } from './RetainedRecordIO.js';
import { FormatException } from './Errors.js';
export function ReadVertex(context,polyface=false) {
  const chunk=context.Chunk,position=api.Vector3.Zero;let handle='',layer=null,color=null,linetype=api.Linetype.ByLayer,startWidth=0,endWidth=0,bulge=0;
  let indices=[0,0,0,0],indexSlots=0,duplicateIndex=false,depth=0,publicSubclass=true,extendedData=false,flags=0;
  chunk.Next();while(chunk.Code!==0) {
    if(polyface) {
      if(extendedData){chunk.Next();continue;}
      if(chunk.Code===102){const control=chunk.ReadString();if(control.startsWith('{'))depth++;else if(control==='}'&&depth>0)depth--;chunk.Next();continue;}
      if(depth>0){chunk.Next();continue;}
      if(chunk.Code===1001){extendedData=true;chunk.Next();continue;}
      if(chunk.Code===100){publicSubclass=[api.SubclassMarker.Entity,api.SubclassMarker.Vertex,api.SubclassMarker.PolyfaceMeshVertex,api.SubclassMarker.PolyfaceMeshFace].includes(chunk.ReadString());chunk.Next();continue;}
      if(!publicSubclass){chunk.Next();continue;}
    }
    switch(chunk.Code) {
      case 5:handle=chunk.ReadHex();break;
      case 8:layer=GetRecordResource(context,8,DecodeDxfText(chunk.ReadString()));break;
      case 62:color=api.AciColor.FromCadIndex(chunk.ReadShort());break;
      case 420:color=api.AciColor.FromTrueColor(chunk.ReadInt());break;
      case 6:linetype=GetRecordResource(context,6,DecodeDxfText(chunk.ReadString()));break;
      case 10:position.X=chunk.ReadDouble();break;
      case 20:position.Y=chunk.ReadDouble();break;
      case 30:position.Z=chunk.ReadDouble();break;
      case 40:startWidth=chunk.ReadDouble();if(startWidth<0)startWidth=0;break;
      case 41:endWidth=chunk.ReadDouble();if(endWidth<0)endWidth=0;break;
      case 42:bulge=chunk.ReadDouble();break;
      case 70:flags=chunk.ReadShort();break;
      case 71:case 72:case 73:case 74:{const slot=chunk.Code-71;duplicateIndex=duplicateIndex||!!(indexSlots&(1<<slot));indexSlots|=1<<slot;indices[slot]=chunk.ReadShort();break;}
    }
    chunk.Next();
  }
  if(polyface&&depth!==0)throw new FormatException('Unclosed private POLYFACE vertex control group.');
  const face=polyface&&!!(flags&128)&&!(flags&64);if(face&&duplicateIndex)throw new FormatException('A POLYFACE face contains a duplicate vertex-index group.');
  if(face){let count=0;while(count<indices.length&&indices[count]!==0)count++;indices=indices.slice(0,count);}
  const v=new Vertex();v.Flags=flags;v.Position=position;v.StartWidth=startWidth;v.Bulge=bulge;v.Color=color;v.EndWidth=endWidth;v.Layer=layer;v.Linetype=linetype;v.VertexIndexes=indices;v.Handle=handle;return v;
}
export function ReadPolyline2D(polyline) {
  const vertices=[],spline=!!(polyline.Flags&4);
  for(const v of polyline.Vertexes) {
    if(spline&&!(v.Flags&16))continue;
    const vertex=new api.Polyline2DVertex();vertex.Position=new api.Vector2(v.Position.X,v.Position.Y);
    if(!spline)vertex.Bulge=v.Bulge;vertex.StartWidth=v.StartWidth;vertex.EndWidth=v.EndWidth;vertices.push(vertex);
  }
  const result=new api.Polyline2D(vertices,!!(polyline.Flags&1));result.SmoothType=polyline.SmoothType;result.Thickness=polyline.Thickness;result.Elevation=polyline.Elevation;result.Normal=polyline.Normal;result.Flags=polyline.Flags;result.XData.AddRange(polyline.XData.Values);return result;
}
