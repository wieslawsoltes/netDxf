// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
// Source: pinned DxfReader.ReadSpline and DxfWriter.WriteSpline.
import { Spline } from '../netDxf/Entities/Spline.js';
import { SplineTypeFlags as F } from '../netDxf/Entities/SplineTypeFlags.js';
import { SplineCreationMethod as M } from '../netDxf/Entities/SplineCreationMethod.js';
import { Vector3 } from '../netDxf/Vector3.js';
import { ReadXDataRecord, WriteXData } from './DxfXDataIO.js';
import { NullReferenceException, InvalidDataException, NotSupportedException, InvalidOperationException, ArgumentOutOfRangeException } from './Errors.js';
export function ReadSpline(chunk,document,stopAtHelix=false) {
  if(chunk==null)throw new NullReferenceException();
  let flags=F.Open,degree=3,knotTolerance=1e-7,ctrlPointTolerance=1e-7,fitTolerance=1e-10;
  const normal=Vector3.UnitZ,knots=[],controls=[],fit=[],data=[];let weights=[];
  let cx=0,cy=0,fx=0,fy=0,sx=0,sy=0,ex=0,ey=0,start=null,end=null;
  chunk.Next();
  while(chunk.Code!==0&&!(stopAtHelix&&chunk.Code===100&&chunk.ReadString()==='AcDbHelix')) {
    switch(chunk.Code) {
      case 210:normal.X=chunk.ReadDouble();break;case 220:normal.Y=chunk.ReadDouble();break;case 230:normal.Z=chunk.ReadDouble();break;
      case 70:flags=chunk.ReadShort();break;case 71:degree=chunk.ReadShort();if(degree>Spline.MaxDegree)degree=Spline.MaxDegree;break;
      // Declared counts (72..74) are cosmetic in the source. Do not allocate from them.
      case 42:knotTolerance=chunk.ReadDouble();if(knotTolerance<=0)knotTolerance=1e-7;break;
      case 43:ctrlPointTolerance=chunk.ReadDouble();if(ctrlPointTolerance<=0)ctrlPointTolerance=1e-7;break;
      case 44:fitTolerance=chunk.ReadDouble();if(fitTolerance<=0)fitTolerance=1e-10;break;
      case 12:sx=chunk.ReadDouble();break;case 22:sy=chunk.ReadDouble();break;case 32:start=new Vector3(sx,sy,chunk.ReadDouble());break;
      case 13:ex=chunk.ReadDouble();break;case 23:ey=chunk.ReadDouble();break;case 33:end=new Vector3(ex,ey,chunk.ReadDouble());break;
      case 40:knots.push(chunk.ReadDouble());break;
      case 10:cx=chunk.ReadDouble();break;case 20:cy=chunk.ReadDouble();break;case 30:controls.push(new Vector3(cx,cy,chunk.ReadDouble()));break;
      case 41:weights.push(chunk.ReadDouble());break;
      case 11:fx=chunk.ReadDouble();break;case 21:fy=chunk.ReadDouble();break;case 31:fit.push(new Vector3(fx,fy,chunk.ReadDouble()));break;
      case 1001:data.push(ReadXDataRecord(chunk,document));continue;
      // Native Debug orphan-XData assertion is not emulated as process termination.
    }
    chunk.Next();
  }
  if(stopAtHelix&&chunk.Code===0)throw new InvalidDataException('HELIX is missing its AcDbHelix subclass.');
  if(weights.length===0||weights.length!==controls.length)weights=null;
  const method=fit.length!==0&&(flags&F.FitPointCreationMethod)!==0?M.FitPoints:M.ControlPoints;
  const periodic=(flags&(F.Periodic|F.ClosedPeriodicSpline))!==0;
  if(periodic) {
    if(controls.length<2*degree+1)throw new NotSupportedException('Periodic SPLINE requires a degree-fold cyclic control overlap in this typed model.');
    for(let i=0;i<degree;i++) {
      const tail=controls.length-degree+i,a=controls[i],b=controls[tail];
      if(a.X!==b.X||a.Y!==b.Y||a.Z!==b.Z||(weights!==null&&weights[i]!==weights[tail]))throw new NotSupportedException('Periodic SPLINE control points and weights must have an exact degree-fold cyclic overlap; refusing a lossy import.');
    }
    // List.RemoveRange validates count before the Spline constructor validates degree.
    if(degree<0)throw new ArgumentOutOfRangeException('count');
    controls.splice(0,degree);if(weights!==null)weights.splice(0,degree);
  }
  const e=new Spline(controls,weights,knots,degree,fit,method,periodic);
  e.KnotTolerance=knotTolerance;e.CtrlPointTolerance=ctrlPointTolerance;e.FitTolerance=fitTolerance;e.StartTangent=start;e.EndTangent=end;
  for(const flag of [F.FitChord,F.FitSqrtChord,F.FitUniform,F.FitCustom])if((flags&flag)!==0){e.KnotParameterization=flag;break;}
  if(stopAtHelix)e.Normal=normal;e.XData.AddRange(data);return e;
}
const tangentValue=value=>{if(value===null)throw new InvalidOperationException('Nullable object must have a value.');return value;};
export function WriteSpline(chunk,version,e,writeXData=true) {
  if(chunk==null)throw new NullReferenceException();chunk.Write(100,'AcDbSpline');if(e==null)throw new NullReferenceException();
  let flags=F.Rational;if(e.IsClosed||e.IsClosedPeriodic)flags|=F.Closed;
  if(e.IsClosedPeriodic)flags|=F.Periodic|F.ClosedPeriodicSpline;
  if(e.CreationMethod===M.FitPoints)flags|=F.FitPointCreationMethod;flags|=e.KnotParameterization;
  chunk.Write(70,(flags<<16)>>16);chunk.Write(71,e.Degree);
  chunk.Write(42,e.KnotTolerance);chunk.Write(43,e.CtrlPointTolerance);chunk.Write(44,e.FitTolerance);
  if(e.StartTangent!==null){chunk.Write(12,tangentValue(e.StartTangent).X);chunk.Write(22,tangentValue(e.StartTangent).Y);chunk.Write(32,tangentValue(e.StartTangent).Z);}
  if(e.EndTangent!==null){chunk.Write(13,tangentValue(e.EndTangent).X);chunk.Write(23,tangentValue(e.EndTangent).Y);chunk.Write(33,tangentValue(e.EndTangent).Z);}
  for(const knot of e.Knots)chunk.Write(40,knot);
  if(e.IsClosedPeriodic)for(let i=0;i<e.Degree;i++) {
    const p=e.ControlPoints[e.ControlPoints.length-e.Degree+i];chunk.Write(10,p.X);chunk.Write(20,p.Y);chunk.Write(30,p.Z);chunk.Write(41,e.Weights[e.Weights.length-e.Degree+i]);
  }
  for(let i=0;i<e.ControlPoints.length;i++) {chunk.Write(10,e.ControlPoints[i].X);chunk.Write(20,e.ControlPoints[i].Y);chunk.Write(30,e.ControlPoints[i].Z);chunk.Write(41,e.Weights[i]);}
  for(const p of e.FitPoints) {chunk.Write(11,p.X);chunk.Write(21,p.Y);chunk.Write(31,p.Z);}
  if(writeXData)WriteXData(chunk,version,e.XData);
}
