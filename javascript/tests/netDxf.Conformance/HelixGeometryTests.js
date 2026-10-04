// Complete original authoring, wire, regeneration and extreme-value cases from the pinned source.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { NewHelixFixture } from './HelixApiTests.js';
import {Helix,Vector3,Matrix3,DxfDocument,DxfVersion,Layer,MemoryStream} from '../../index.js';
import {ArgumentException,ArgumentOutOfRangeException} from '../../runtime/Errors.js';
import {Run,Check,Equal,Near,Throws,BooleanName,SupportedVersions,VersionName,SameDoubleBits} from './TestHarness.js';
const nearVector=(expected,actual,label)=>{for(const key of ['X','Y','Z'])Near(expected[key],actual[key],label+' '+key);};
export function RegisterHelixGeometryTests(){
  for(let shape=0;shape<6;shape++)for(const right of [false,true])for(let pose=0;pose<3;pose++)Run(`helix/authoring/${shape}/${BooleanName(right)}/${pose}`,()=>HelixAuthoring(shape,right,pose));
  for(const v of SupportedVersions.filter(v=>v>=DxfVersion.AutoCad2007))for(const b of [false,true]){
    for(let shape=0;shape<6;shape++)Run(`helix/authoring/wire/${VersionName(v)}/${BooleanName(b)}/${shape}`,()=>HelixAuthoringWire(v,b,shape));
    Run(`helix/authoring/zero-radius-phase/${VersionName(v)}/${BooleanName(b)}`,()=>HelixZeroRadiusPhase(v,b));
  }
  Run('helix/authoring/budgets-and-invalid',HelixAuthoringInvalid);
  Run('helix/authoring/regenerate-isolation',HelixAuthoringRegenerate);
  Run('helix/authoring/error-bound-extremes',HelixBoundExtremes);
}
export function AuthoredHelix(shape,right,pose,tolerance=1e-5){
  const matrix=pose===0?Matrix3.Identity:Matrix3.Multiply(Matrix3.RotationY(.45),Matrix3.RotationX(-.3)),translation=pose===2?new Vector3(100,-200,30):Vector3.Zero;
  const initial=shape===4?0:5,final=shape===0?5:shape===5?0:2,pitch=shape===2?0:shape===3?-1.5:1.5;
  return Helix.Create(translation,Vector3.Add(Matrix3.Multiply(matrix,new Vector3(initial,0,0)),translation),Matrix3.Multiply(matrix,new Vector3(0,0,3)),final,2.25,pitch,right,tolerance);
}
export function CubicSample(helix,parameter){
  const segments=helix.ControlPoints.length/4,span=Math.min(segments-1,Math.trunc(parameter*segments)),t=parameter*segments-span,c=1-t,i=span*4,p=helix.ControlPoints;
  return Vector3.Add(Vector3.Add(Vector3.Add(Vector3.Multiply(c*c*c,p[i]),Vector3.Multiply(3*c*c*t,p[i+1])),Vector3.Multiply(3*c*t*t,p[i+2])),Vector3.Multiply(t*t*t,p[i+3]));
}
export function HelixAuthoring(shape,right,pose){
  const h=AuthoredHelix(shape,right,pose),segments=h.ControlPoints.length/4;
  Check(segments>=9&&segments<=65536,'Authoring segment budget');Equal(4*(segments+1),h.Knots.length,'Authored cubic knot count');
  const bound=h.GetApproximationErrorBound(segments);Check(bound>0&&bound<=1e-5,'Selected approximation bound exceeds tolerance');
  for(let i=0;i<h.Knots.length;i++)Near(Math.trunc(i/4)/segments,h.Knots[i],'Hermite span knots');
  for(let i=0;i<=250;i++){
    const t=i/250,expected=h.EvaluateDefinition(t),actual=CubicSample(h,t);
    Check(Vector3.Subtract(expected,actual).Modulus()<=bound+1e-10,'Authored spline violates analytic error bound');
    if(i>0&&i<250){const delta=1e-6,derivative=Vector3.Divide(Vector3.Subtract(h.EvaluateDefinition(t+delta),h.EvaluateDefinition(t-delta)),2*delta);Check(Vector3.Subtract(derivative,h.EvaluateDefinitionDerivative(t)).Modulus()<2e-7,'Analytic derivative differs from independent centered difference');}
  }
  nearVector(h.StartPoint,h.EvaluateDefinition(0),'Analytic start');nearVector(h.EvaluateDefinitionDerivative(0),h.StartTangent,'Stored start tangent');nearVector(h.EvaluateDefinitionDerivative(1),h.EndTangent,'Stored end tangent');
  const axis=Vector3.Normalize(h.AxisVector),offset=Vector3.Subtract(h.EvaluateDefinition(1),h.AxisBasePoint);
  Near(h.Turns*h.TurnHeight,Vector3.DotProduct(axis,offset),'Analytic signed total height');Near(h.Radius,Vector3.Subtract(offset,Vector3.Multiply(axis,Vector3.DotProduct(axis,offset))).Modulus(),'Analytic terminal radius');
  const transformed=h.Clone(),reflection=Matrix3.Multiply(Matrix3.Multiply(Matrix3.Reflection(Vector3.UnitX),Matrix3.RotationZ(.5)),Matrix3.Scale(2)),move=new Vector3(7,8,9);
  transformed.TransformBy(reflection,move);
  for(const t of [0,.123,.875,1]){nearVector(Vector3.Add(Matrix3.Multiply(reflection,h.EvaluateDefinition(t)),move),transformed.EvaluateDefinition(t),'Transformed analytic definition');nearVector(Matrix3.Multiply(reflection,h.EvaluateDefinitionDerivative(t)),transformed.EvaluateDefinitionDerivative(t),'Transformed analytic derivative');}
}
export function HelixAuthoringInvalid(){
  for(const t of [-1,2,NaN,Infinity]){const h=AuthoredHelix(1,true,0);Throws(ArgumentOutOfRangeException,()=>h.EvaluateDefinition(t));Throws(ArgumentOutOfRangeException,()=>h.EvaluateDefinitionDerivative(t));}
  for(const tolerance of [0,-1,NaN,Infinity])Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,tolerance));
  for(const budget of [-1,0,1048577,2147483647])Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,1e-5,budget));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,100,1,true,1e-5,8));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,1,1,true,1e-20,8));
  Throws(ArgumentException,()=>Helix.Create(Vector3.Zero,new Vector3(1,0,1),Vector3.UnitZ,1,1,1));
  Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.Zero,1,1,1));
  Throws(ArgumentOutOfRangeException,()=>Helix.Create(Vector3.Zero,Vector3.UnitX,Vector3.UnitZ,1,Number.MAX_VALUE,1));
  const h2=AuthoredHelix(0,true,0);h2.StartPoint=Vector3.Add(h2.StartPoint,Vector3.UnitZ);Throws(ArgumentException,()=>h2.WithRegeneratedSpline());Throws(ArgumentOutOfRangeException,()=>h2.GetApproximationErrorBound(0));
}

const sameGeometry=(a,b)=>{a=Array.from(a);b=Array.from(b);return a.length===b.length&&a.every((p,i)=>p?.Equals?p.Equals(b[i]):p===b[i]);};
const artifactDirectory=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
export function HelixAuthoringWire(version,binary,shape){
  const helix=AuthoredHelix(shape,shape!==1,0),doc=new DxfDocument(version);doc.Entities.Add(helix);doc.Entities.Add(helix.Clone());
  const output=new MemoryStream();
  try{
    Check(doc.Save(output,binary),'Authored HELIX save failed.');fs.mkdirSync(artifactDirectory,{recursive:true});fs.writeFileSync(path.join(artifactDirectory,`helix-authored-${VersionName(version)}-${BooleanName(binary)}-${shape}.dxf`),output.ToArray());
    output.Position=0;const read=DxfDocument.Load(output);Check(read!==null,'Authored HELIX load failed.');
    for(const actual of read.Entities.Helices){
      Check(sameGeometry(helix.ControlPoints,actual.ControlPoints)&&sameGeometry(helix.Knots,actual.Knots),'Authored HELIX changed in transport.');
      for(let i=0;i<=100;i++)nearVector(helix.EvaluateDefinition(i/100),actual.EvaluateDefinition(i/100),'Transported analytic definition');
    }
  }finally{output.Dispose();}
}
export function HelixAuthoringRegenerate(){
  const h=NewHelixFixture(),original=Array.from(h.ControlPoints);h.Layer=new Layer('Coil');const doc=new DxfDocument(DxfVersion.AutoCad2018);doc.Entities.Add(h);
  const regenerated=h.WithRegeneratedSpline(1e-6);Check(sameGeometry(h.ControlPoints,original),'Regeneration mutated source curve.');Check(!sameGeometry(regenerated.ControlPoints,original),'Regeneration did not replace edited source curve.');
  Check(regenerated.Handle===null&&regenerated.Owner===null,'Regeneration retained database identity.');
  Equal(h.Radius,regenerated.Radius,'Regenerated radius');Equal(h.Constraint,regenerated.Constraint,'Regenerated constraint');Check(h.AxisVector.Equals(regenerated.AxisVector),'Regenerated axis magnitude');Equal(h.MajorReleaseNumber,regenerated.MajorReleaseNumber,'Regenerated class version');Equal(h.Layer.Name,regenerated.Layer.Name,'Regenerated layer');
  regenerated.XData.get_Item('HELIX_TEST').XDataRecord.Clear();Check(h.XData.get_Item('HELIX_TEST').XDataRecord.Count===1,'Regeneration aliases XData.');Check(regenerated.GetApproximationErrorBound(regenerated.ControlPoints.length/4)<=1e-6,'Regeneration error bound');
  const straight=Helix.Create(Vector3.Zero,Vector3.Zero,Vector3.UnitZ,0,3,2);Equal(4,straight.ControlPoints.length,'Zero-radius linear definition should require one cubic');nearVector(new Vector3(0,0,3),straight.EvaluateDefinition(.5),'Zero-radius definition');
}
export function HelixZeroRadiusPhase(version,binary){
  const source=AuthoredHelix(4,false,2);source.TransformBy(Matrix3.Multiply(Matrix3.Reflection(Vector3.UnitX),Matrix3.RotationZ(.8)),new Vector3(11,19,-23));const regenerated=source.WithRegeneratedSpline(1e-6);
  for(let i=0;i<=100;i++){const t=i/100;nearVector(source.EvaluateDefinition(t),regenerated.EvaluateDefinition(t),'Zero-radius regeneration phase');Check(Vector3.Subtract(CubicSample(regenerated,t),source.EvaluateDefinition(t)).Modulus()<1e-6+1e-10,'Zero-radius regenerated curve lost phase.');}
  const doc=new DxfDocument(version);doc.Entities.Add(source);doc.Entities.Add(regenerated);const stream=new MemoryStream();
  try{
    Check(doc.Save(stream,binary),'Zero-radius phase save failed.');stream.Position=0;const loaded=DxfDocument.Load(stream);Check(loaded!==null,'Zero-radius phase load failed.');Equal(2,Array.from(loaded.Entities.Helices).length,'Zero-radius phase entities lost');
    for(const h of loaded.Entities.Helices)for(const t of [0,.125,.375,.75,1])nearVector(source.EvaluateDefinition(t),h.EvaluateDefinition(t),'Transported zero-radius phase');
    const absent=source.Clone();absent.StartTangent=null;nearVector(absent.EvaluateDefinition(.2),absent.WithRegeneratedSpline().EvaluateDefinition(.2),'Absent phase convention');
  }finally{stream.Dispose();}
}
export function HelixBoundExtremes(){
  const h=NewHelixFixture();h.AxisBasePoint=Vector3.Zero;h.StartPoint=new Vector3(1e308,0,0);h.AxisVector=Vector3.UnitZ;h.Radius=1e308;h.Turns=1e-100;
  const tiny=h.GetApproximationErrorBound(1);Check(Number.isFinite(tiny)&&tiny>0&&tiny<1e-80,'Large-radius small-angle bound overflowed or underflowed.');
  h.StartPoint=Vector3.Zero;const taper=h.GetApproximationErrorBound(1);Check(Number.isFinite(taper)&&taper>1e7&&taper<1e10,'Taper term was lost or overflowed.');h.Turns=1e100;Check(h.GetApproximationErrorBound(1)===Infinity,'Unrepresentable error bound should be positive infinity, not NaN.');
  for(const axisScale of [Number.MIN_VALUE,1e-300,1e308]){const scaled=Helix.Create(Vector3.Zero,Vector3.UnitX,new Vector3(0,0,axisScale),1,1,1);nearVector(new Vector3(-1,0,.5),scaled.EvaluateDefinition(.5),'Finite extreme axis scale');SameDoubleBits(axisScale,scaled.AxisVector.Z,'Extreme axis magnitude retained');}
  const usual=AuthoredHelix(1,true,0);Near(usual.GetApproximationErrorBound(10)/16,usual.GetApproximationErrorBound(20),'Fourth-order error bound scaling');
}
