import { NullReferenceException } from '../../runtime/Errors.js';
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Polyline2D } from '../Entities/Polyline2D.js';
import { Polyline2DVertex } from '../Entities/Polyline2DVertex.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { InvalidDataException, NotSupportedException } from '../../runtime/Errors.js';
import { ReadXDataRecord, WriteXData } from '../../runtime/DxfXDataIO.js';
/** Source ReadLwPolyline; as in C#, the caller positions the subclass marker. */
export function ReadLwPolyline(chunk, document){
  let elevation=0,thickness=0,constantWidth=null,flags=0,vertex=null,hasY=false,declaredCount=null;
  const vertexes=[],normal=Vector3.UnitZ,xdata=[];
  chunk.Next();
  while(chunk.Code!==0){
    switch(chunk.Code){
      case 38:elevation=chunk.ReadDouble();break;case 39:thickness=chunk.ReadDouble();break;
      case 43:
        if(constantWidth!==null)throw new InvalidDataException('LWPOLYLINE has duplicate group 43 constant widths.');
        constantWidth=chunk.ReadDouble();if(!Number.isFinite(constantWidth)||constantWidth<0)throw new InvalidDataException('LWPOLYLINE group 43 must be finite and nonnegative.');break;
      case 70:flags=chunk.ReadShort();break;
      case 90:
        if(declaredCount!==null)throw new InvalidDataException('LWPOLYLINE has duplicate group 90 vertex counts.');
        {const count=chunk.ReadInt();if(count<0)throw new InvalidDataException('LWPOLYLINE group 90 vertex count is negative.');declaredCount=count;}break;
      case 10:
        if(vertex!==null&&!hasY)throw new InvalidDataException('LWPOLYLINE vertex is missing group 20.');
        vertex=new Polyline2DVertex(chunk.ReadDouble(),0);vertexes.push(vertex);hasY=false;break;
      case 20:
        if(vertex===null||hasY)throw new InvalidDataException('LWPOLYLINE group 20 must belong to one group 10 vertex.');
        vertex.Position=new Vector2(vertex.Position.X,chunk.ReadDouble());hasY=true;break;
      case 91:
        if(vertex===null)throw new InvalidDataException('LWPOLYLINE group 91 precedes its group 10 vertex.');
        if(vertex.VertexIdentifier!==null)throw new InvalidDataException('LWPOLYLINE vertex has duplicate group 91 identifiers.');
        vertex.VertexIdentifier=chunk.ReadInt();break;
      case 40:case 41:case 42:{
        if(vertex===null)throw new InvalidDataException('LWPOLYLINE vertex data precedes its group 10 vertex.');
        const code=chunk.Code,value=chunk.ReadDouble();
        if(code===42)vertex.Bulge=value;
        else {
          if(!Number.isFinite(value)||value<0)throw new InvalidDataException('LWPOLYLINE vertex width must be finite and nonnegative.');
          if(code===40){if(vertex.StartWidthOverride!==null)throw new InvalidDataException('LWPOLYLINE vertex has duplicate group 40 widths.');vertex.StartWidth=value;}
          else{if(vertex.EndWidthOverride!==null)throw new InvalidDataException('LWPOLYLINE vertex has duplicate group 41 widths.');vertex.EndWidth=value;}
        }break;
      }
      case 210:normal.X=chunk.ReadDouble();break;case 220:normal.Y=chunk.ReadDouble();break;case 230:normal.Z=chunk.ReadDouble();break;
      case 1001:xdata.push(ReadXDataRecord(chunk,document));continue;
      // The native Debug build asserts on unprefixed XData in this default branch;
      // the portable parser retains Release advancement, never a process abort.
    }
    chunk.Next();
  }
  if(vertex!==null&&!hasY)throw new InvalidDataException('LWPOLYLINE final vertex is missing group 20.');
  if(declaredCount===null||declaredCount!==vertexes.length)throw new InvalidDataException('LWPOLYLINE group 90 does not match its actual vertex count.');
  const entity=new Polyline2D(vertexes);entity.Elevation=elevation;entity.Thickness=thickness;entity.Flags=flags;entity.Normal=normal;entity.ConstantWidth=constantWidth;
  entity.XData.AddRange(xdata);return entity;
}
export function ValidateLwPolylineFidelity(document){
  for(const block of document.Blocks)for(const entity of block.Entities){
    if(!(entity instanceof Polyline2D)||entity.HasStoredRecords)continue;
    entity.ValidateVertexFidelity();let identifiers=false;for(const vertex of entity.Vertexes)identifiers||=vertex.VertexIdentifier!==null;
    if(identifiers&&document.DrawingVariables.AcadVer<17)throw new NotSupportedException('LWPOLYLINE group 91 vertex identifiers require the qualified DXF 2013 or later writer profile. Remove identifiers explicitly before down-saving.');
    if(entity.SmoothType!==0&&(entity.ConstantWidth!==null||identifiers))throw new NotSupportedException('Smoothed POLYLINE output cannot retain LWPOLYLINE constant-width presence or vertex identifiers. Convert these fields explicitly before enabling smoothing.');
  }
}
/** Source WriteLwPolyline from the main writer; no common/entity envelope added. */
export function WriteLwPolyline(chunk, version, entity){
  chunk.Write(100,'AcDbPolyline');if(entity==null)throw new NullReferenceException();chunk.Write(90,entity.Vertexes.Count);chunk.Write(70,entity.Flags);chunk.Write(38,entity.Elevation);chunk.Write(39,entity.Thickness);
  if(entity.ConstantWidth!==null)chunk.Write(43,entity.ConstantWidth);
  for(const vertex of entity.Vertexes){
    chunk.Write(10,vertex.Position.X);chunk.Write(20,vertex.Position.Y);
    if(vertex.VertexIdentifier!==null)chunk.Write(91,vertex.VertexIdentifier);
    if(vertex.StartWidthOverride!==null)chunk.Write(40,vertex.StartWidthOverride);
    if(vertex.EndWidthOverride!==null)chunk.Write(41,vertex.EndWidthOverride);
    chunk.Write(42,vertex.Bulge);
  }
  chunk.Write(210,entity.Normal.X);chunk.Write(220,entity.Normal.Y);chunk.Write(230,entity.Normal.Z);WriteXData(chunk,version,entity.XData);
}
