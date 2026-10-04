// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Source: pinned IO/DxfReader.cs and IO/DxfWriter.cs primitive methods.
// Explicit entity-body adapters; no common envelope, registration or file dispatch.
import { Arc } from '../netDxf/Entities/Arc.js';
import { Circle } from '../netDxf/Entities/Circle.js';
import { Ellipse } from '../netDxf/Entities/Ellipse.js';
import { Line } from '../netDxf/Entities/Line.js';
import { Point } from '../netDxf/Entities/Point.js';
import { Ray } from '../netDxf/Entities/Ray.js';
import { XLine } from '../netDxf/Entities/XLine.js';
import { Face3D } from '../netDxf/Entities/Face3D.js';
import { Solid } from '../netDxf/Entities/Solid.js';
import { Trace } from '../netDxf/Entities/Trace.js';
import { Vector2 } from '../netDxf/Vector2.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { MathHelper } from '../netDxf/MathHelper.js';
import { CoordinateSystem } from '../netDxf/CoordinateSystem.js';
import { SubclassMarker } from '../netDxf/SubclassMarker.js';
import { DotNetMath, MultiplyDouble as mul } from './GeometryRuntime.js';
import { NullReferenceException } from './Errors.js';
import { ReadXDataRecord, WriteXData } from './DxfXDataIO.js';
const requireValue = value => { if (value == null) throw new NullReferenceException(); return value; };
const normalFields = {210:['normal','X'],220:['normal','Y'],230:['normal','Z']};
function vectorFields(first, name) { return {[first]:[name,'X'],[first+10]:[name,'Y'],[first+20]:[name,'Z']}; }
/** Dispatch contains source field locations, never oracle outputs. All state is per call. */
function readFields(chunk, document, state, fields) {
  requireValue(chunk).Next(); const data=[];
  while (chunk.Code !== 0) {
    if (chunk.Code === 1001) { data.push(ReadXDataRecord(chunk,document)); continue; }
    const field=fields[chunk.Code];
    if (typeof field === 'function') field(state,chunk);
    else if (Array.isArray(field)) state[field[0]][field[1]]=chunk.ReadDouble();
    else if (field) state[field]=chunk.ReadDouble();
    // Native Debug.Assert on orphan XData has no portable process-abort equivalent.
    // Unknown fields retain source Release advancement; qualified inputs document this boundary.
    chunk.Next();
  }
  return data;
}
function finish(entity,data) { entity.XData.AddRange(data); return entity; }
const centerFields={...vectorFields(10,'center'),...normalFields,39:'thickness',40:(s,c)=>{s.radius=c.ReadDouble();if(s.radius<=0)s.radius=1;}};
export function ReadArc(chunk,document) {
  const s={center:Vector3.Zero,normal:Vector3.UnitZ,radius:1,start:0,end:180,thickness:0};
  const data=readFields(chunk,document,s,{...centerFields,50:'start',51:'end'});
  const center=MathHelper.Transform(s.center,s.normal,CoordinateSystem.Object,CoordinateSystem.World),e=new Arc();
  e.Center=center;e.Radius=s.radius;e.StartAngle=s.start;e.EndAngle=s.end;e.Thickness=s.thickness;e.Normal=s.normal;
  return finish(e,data);
}
export function ReadCircle(chunk,document) {
  const s={center:Vector3.Zero,normal:Vector3.UnitZ,radius:1,thickness:0},data=readFields(chunk,document,s,centerFields);
  const center=MathHelper.Transform(s.center,s.normal,CoordinateSystem.Object,CoordinateSystem.World),e=new Circle();
  e.Center=center;e.Radius=s.radius;e.Thickness=s.thickness;e.Normal=s.normal;return finish(e,data);
}
export function ReadLine(chunk,document) {
  const s={start:Vector3.Zero,end:Vector3.Zero,normal:Vector3.UnitZ,thickness:0};
  const data=readFields(chunk,document,s,{...vectorFields(10,'start'),...vectorFields(11,'end'),...normalFields,39:'thickness'}),e=new Line();
  e.StartPoint=s.start;e.EndPoint=s.end;e.Normal=s.normal;e.Thickness=s.thickness;return finish(e,data);
}
export function ReadPoint(chunk,document) {
  const s={location:Vector3.Zero,normal:Vector3.UnitZ,thickness:0,rotation:0};
  const data=readFields(chunk,document,s,{...vectorFields(10,'location'),...normalFields,39:'thickness',50:(s,c)=>{s.rotation=360-c.ReadDouble();}}),e=new Point();
  e.Position=s.location;e.Thickness=s.thickness;e.Rotation=s.rotation;e.Normal=s.normal;return finish(e,data);
}
function readInfinite(Type,chunk,document) {
  const s={origin:Vector3.Zero,direction:Vector3.UnitX};
  const data=readFields(chunk,document,s,{...vectorFields(10,'origin'),...vectorFields(11,'direction')}),e=new Type();
  e.Origin=s.origin;e.Direction=s.direction;return finish(e,data);
}
export function ReadRay(chunk,document) {return readInfinite(Ray,chunk,document);}
export function ReadXLine(chunk,document) {return readInfinite(XLine,chunk,document);}
const cornerFields={...vectorFields(10,'v0'),...vectorFields(11,'v1'),...vectorFields(12,'v2'),...vectorFields(13,'v3')};
function readFace(Type,chunk,document) {
  const face=Type===Face3D,s={v0:Vector3.Zero,v1:Vector3.Zero,v2:Vector3.Zero,v3:Vector3.Zero,normal:Vector3.UnitZ,thickness:0,flags:0};
  const data=readFields(chunk,document,s,face?{...cornerFields,70:(s,c)=>{s.flags=c.ReadShort();}}:{...cornerFields,...normalFields,39:'thickness'}),e=new Type();
  for (const [index,key] of ['FirstVertex','SecondVertex','ThirdVertex','FourthVertex'].entries()) {
    const v=s['v'+index];e[key]=face?v:new Vector2(v.X,v.Y);
  }
  if(face)e.EdgeFlags=s.flags;else {e.Elevation=s.v0.Z;e.Thickness=s.thickness;e.Normal=s.normal;}
  return finish(e,data);
}
// The source's reader method spells the final d in lower case; preserve both explicit adapters.
export function ReadFace3d(chunk,document) {return readFace(Face3D,chunk,document);}
export const ReadFace3D=ReadFace3d;
export function ReadSolid(chunk,document) {return readFace(Solid,chunk,document);}
export function ReadTrace(chunk,document) {return readFace(Trace,chunk,document);}
export function SetEllipseParameters(ellipse,param) {
  if(MathHelper.IsZero(param[0])&&MathHelper.IsEqual(param[1],MathHelper.TwoPI)) {ellipse.StartAngle=0;ellipse.EndAngle=0;return;}
  const a=mul(ellipse.MajorAxis,.5),b=mul(ellipse.MinorAxis,.5);
  const start=new Vector2(mul(a,DotNetMath.Cos(param[0])),mul(b,DotNetMath.Sin(param[0]))),end=new Vector2(mul(a,DotNetMath.Cos(param[1])),mul(b,DotNetMath.Sin(param[1])));
  if(Vector2.Equals(start,end)){ellipse.StartAngle=0;ellipse.EndAngle=0;}
  else{ellipse.StartAngle=mul(Vector2.Angle(start),MathHelper.RadToDeg);ellipse.EndAngle=mul(Vector2.Angle(end),MathHelper.RadToDeg);}
}
export function GetEllipseParameters(ellipse) {
  if(ellipse.IsFullEllipse)return [0,MathHelper.TwoPI];
  const start=ellipse.PolarCoordinateRelativeToCenter(ellipse.StartAngle),end=ellipse.PolarCoordinateRelativeToCenter(ellipse.EndAngle),a=1/mul(.5,ellipse.MajorAxis),b=1/mul(.5,ellipse.MinorAxis);
  return [DotNetMath.Atan2(mul(start.Y,b),mul(start.X,a)),DotNetMath.Atan2(mul(end.Y,b),mul(end.X,a))];
}
export function ReadEllipse(chunk,document) {
  const s={center:Vector3.Zero,axis:Vector3.Zero,normal:Vector3.UnitZ,ratio:0,start:0,end:0};
  const data=readFields(chunk,document,s,{...vectorFields(10,'center'),...vectorFields(11,'axis'),...normalFields,40:'ratio',41:'start',42:'end'});
  const ocs=MathHelper.Transform(s.axis,s.normal,CoordinateSystem.World,CoordinateSystem.Object),rotation=Vector2.Angle(new Vector2(ocs.X,ocs.Y)),major=mul(2,s.axis.Modulus());
  const e=new Ellipse(s.center,major,mul(major,s.ratio));e.Rotation=mul(rotation,MathHelper.RadToDeg);e.Normal=s.normal;finish(e,data);SetEllipseParameters(e,[s.start,s.end]);return e;
}
// Read each model property at each Write call. A re-entrant writer may change it
// between components, as it can in C#; eagerly caching vectors would change behavior.
function vector(chunk,first,get) {chunk.Write(first,get().X);chunk.Write(first+10,get().Y);chunk.Write(first+20,get().Z);}
function begin(chunk,marker,entity) {requireValue(chunk).Write(100,marker);return requireValue(entity);}
export function WriteArc(chunk,version,e) {
  begin(chunk,SubclassMarker.Circle,e);chunk.Write(39,e.Thickness);
  const center=MathHelper.Transform(e.Center,e.Normal,CoordinateSystem.World,CoordinateSystem.Object);
  vector(chunk,10,()=>center);chunk.Write(40,e.Radius);vector(chunk,210,()=>e.Normal);
  chunk.Write(100,SubclassMarker.Arc);chunk.Write(50,e.StartAngle);chunk.Write(51,e.EndAngle);WriteXData(chunk,version,e.XData);
}
export function WriteCircle(chunk,version,e) {
  begin(chunk,SubclassMarker.Circle,e);const center=MathHelper.Transform(e.Center,e.Normal,CoordinateSystem.World,CoordinateSystem.Object);
  vector(chunk,10,()=>center);chunk.Write(40,e.Radius);chunk.Write(39,e.Thickness);vector(chunk,210,()=>e.Normal);WriteXData(chunk,version,e.XData);
}
export function WriteEllipse(chunk,version,e) {
  begin(chunk,SubclassMarker.Ellipse,e);vector(chunk,10,()=>e.Center);
  const axis=Vector2.Rotate(new Vector2(mul(.5,e.MajorAxis),0),mul(e.Rotation,MathHelper.DegToRad));
  const p=MathHelper.Transform(new Vector3(axis.X,axis.Y,0),e.Normal,CoordinateSystem.Object,CoordinateSystem.World);
  vector(chunk,11,()=>p);vector(chunk,210,()=>e.Normal);chunk.Write(40,e.MinorAxis/e.MajorAxis);
  const parameters=GetEllipseParameters(e);chunk.Write(41,parameters[0]);chunk.Write(42,parameters[1]);WriteXData(chunk,version,e.XData);
}
export function WriteLine(chunk,version,e) {
  begin(chunk,SubclassMarker.Line,e);vector(chunk,10,()=>e.StartPoint);vector(chunk,11,()=>e.EndPoint);chunk.Write(39,e.Thickness);vector(chunk,210,()=>e.Normal);WriteXData(chunk,version,e.XData);
}
export function WritePoint(chunk,version,e) {
  begin(chunk,SubclassMarker.Point,e);vector(chunk,10,()=>e.Position);chunk.Write(39,e.Thickness);vector(chunk,210,()=>e.Normal);chunk.Write(50,360-e.Rotation);WriteXData(chunk,version,e.XData);
}
function writeInfinite(chunk,version,e,marker) {begin(chunk,marker,e);vector(chunk,10,()=>e.Origin);vector(chunk,11,()=>e.Direction);WriteXData(chunk,version,e.XData);}
export function WriteRay(chunk,version,e) {writeInfinite(chunk,version,e,SubclassMarker.Ray);}
export function WriteXLine(chunk,version,e) {writeInfinite(chunk,version,e,SubclassMarker.XLine);}
function writeFace(chunk,version,e,marker,face) {
  begin(chunk,marker,e);
  for (const [index,key] of ['FirstVertex','SecondVertex','ThirdVertex','FourthVertex'].entries()) {
    chunk.Write(10+index,e[key].X);chunk.Write(20+index,e[key].Y);chunk.Write(30+index,face?e[key].Z:e.Elevation);
  }
  if(face)chunk.Write(70,(e.EdgeFlags<<16)>>16);else {chunk.Write(39,e.Thickness);vector(chunk,210,()=>e.Normal);}
  WriteXData(chunk,version,e.XData);
}
export function WriteFace3D(chunk,version,e) {writeFace(chunk,version,e,SubclassMarker.Face3D,true);}
export function WriteSolid(chunk,version,e) {writeFace(chunk,version,e,SubclassMarker.Solid,false);}
export function WriteTrace(chunk,version,e) {writeFace(chunk,version,e,SubclassMarker.Trace,false);}
