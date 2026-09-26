// Port of the complete pinned tests/netDxf.Conformance/BezierKnotTests.cs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument, Spline, BezierCurveQuadratic, BezierCurveCubic, Vector3, MemoryStream } from '../../index.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Near, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../artifacts/conformance/fixtures');
export function RegisterBezierKnotTests() {
  for (const v of SupportedVersions) for (const b of [false,true]) for (const degree of [2,3]) for (const count of [1,2,3,5])
    Run(`spline/bezier-knots/${VersionName(v)}/${BooleanName(b)}/${degree}/${count}`,()=>BezierKnots(v,b,degree,count));
  for (const [name,type] of [['cubic','BezierCurveCubic'],['quadratic','BezierCurveQuadratic']]) {
    const construct=curves=>Spline.CreateOverload(`System.Collections.Generic.IEnumerable<netDxf.${type}>`,curves);
    Run(`spline/bezier-knots/${name}-empty`,()=>Throws(ArgumentException,()=>construct([])));
    Run(`spline/bezier-knots/${name}-null-list`,()=>Throws(ArgumentNullException,()=>construct(null)));
    Run(`spline/bezier-knots/${name}-null-element`,()=>Throws(ArgumentException,()=>construct([null])));
  }
}
export function BezierEndpoint(i) { return new Vector3(5*i,i*i,2*i); }
export function CompositeBezier(degree,count) {
  const curves=Array.from({length:count},(_,i)=>degree===2
    ?new BezierCurveQuadratic(BezierEndpoint(i),Vector3.Add(BezierEndpoint(i),new Vector3(2,3,4)),BezierEndpoint(i+1))
    :new BezierCurveCubic(BezierEndpoint(i),Vector3.Add(BezierEndpoint(i),new Vector3(1,2,3)),Vector3.Subtract(BezierEndpoint(i+1),new Vector3(2,3,-1)),BezierEndpoint(i+1)));
  return new Spline(curves);
}
export function BezierKnots(version,binary,degree,count) {
  const spline=CompositeBezier(degree,count),controls=Array.from(spline.ControlPoints);
  for(let i=0;i<spline.Knots.length;i++) Near(Math.trunc(i/(degree+1))/count,spline.Knots[i],'Uniform composite-Bezier knot '+i);
  Equal((degree+1)*(count+1),spline.Knots.length,'Composite knot count');
  Equal(count*(degree+1),spline.ControlPoints.length,'Composite control count');
  const clone=spline.Clone();Equal(Array.from(spline.Knots),Array.from(clone.Knots),'Cloning changed corrected knots');
  const doc=new DxfDocument(version);doc.Entities.Add(spline);doc.Entities.Add(clone);
  const stream=new MemoryStream();Check(doc.Save(stream,binary),'Composite save');
  if(count===3){fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`bezier-knots-${VersionName(version)}-${BooleanName(binary)}-${degree}.dxf`),stream.ToArray());}
  stream.Position=0;const loaded=DxfDocument.Load(stream);if(!loaded)throw new InvalidOperationException('Composite reload');
  for(const actual of loaded.Entities.Splines) {
    const points=Array.from(actual.ControlPoints);Check(points.length===controls.length&&points.every((p,i)=>p.Equals(controls[i])),'Corrected knots changed controls');
    for(let i=0;i<actual.Knots.length;i++)Near(Math.trunc(i/(degree+1))/count,actual.Knots[i],'Wire knot '+i);
  }
  Check(stream.CanRead,'Composite closed caller stream');stream.Dispose();
}
