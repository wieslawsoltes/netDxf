// Port of pinned HatchAffineTests.cs; original identities/assertions and tolerances retained.
import { DxfDocument, DxfVersion, Hatch, HatchPattern, HatchGradientPattern, HatchBoundaryPath, HatchType, Line, Matrix3, MathHelper, Vector2, Vector3 } from '../../index.js';
import { ArgumentException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
import { Run, Check, Equal, SameDoubleBits, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { HatchRelationsLoad, HatchRelationsRaw, HatchRelationsRoundTrip, HatchRelationsEqual, HatchRelationEdge, HatchRelationName } from './HatchSplineRelationTests.js';
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const sameReferences=(a,b)=>{b=Array.from(b);return a.length===b.length&&a.every((x,i)=>x===b[i]);};
const edge=(Type,values)=>Object.assign(new Type(),values);
const transformed=(matrix,point,translation)=>Vector3.Add(Matrix3.Multiply(matrix,point),translation);
export function RegisterHatchAffineTests(){
  for(const v of SupportedVersions)for(const b of [false,true])for(let op=0;op<5;op++)for(let plane=0;plane<2;plane++)
    Run(`hatch-affine/geometry/${VersionName(v)}/${BooleanName(b)}/${op}/${plane}`,()=>HatchAffineGeometry(v,b,op,plane));
  for(const associative of [false,true])for(const defect of ['nan-matrix','infinite-matrix','nan-translation','collapsed-plane','overflow','invalid-spline','invalid-mixed-line'])
    Run(`hatch-affine/atomic/${BooleanName(associative)}/${defect}`,()=>HatchAffineAtomic(associative,defect));
  Run('hatch-affine/association/success',HatchAffineAssociation);Run('hatch-affine/exact-identity',HatchAffineIdentity);
  for(const b of [false,true])for(let op=0;op<5;op++)for(let plane=0;plane<2;plane++)Run(`hatch-affine/closed-polyline/${BooleanName(b)}/${op}/${plane}`,()=>HatchAffinePolyline(b,op,plane));
  for(const kind of ['pattern','gradient'])Run(`hatch-affine/unsupported/${kind}`,()=>HatchAffineUnsupported(kind));
  for(const b of [false,true])Run(`hatch-affine/mixed-spline/${BooleanName(b)}`,()=>HatchAffineMixedSpline(b));
}
export function HatchAffineMatrix(op){
  switch(op){case 0:return Matrix3.Identity;case 1:return Matrix3.Multiply(Matrix3.RotationX(.4),Matrix3.RotationY(-.3));case 2:return Matrix3.Scale(2,3,.5);case 3:return new Matrix3(1,.75,-.2,0,1,.5,.3,0,1);default:return Matrix3.Reflection(Vector3.UnitX);}
}
export function HatchAffineWorld(h,p,vector=false){return Matrix3.Multiply(MathHelper.ArbitraryAxis(h.Normal),new Vector3(p.X,p.Y,vector?0:h.Elevation));}
export function HatchAffineNear(expected,actual,message){
  const scale=Math.max(1,Math.max(expected.Modulus(),actual.Modulus()));
  Check(Vector3.Subtract(expected,actual).Modulus()<=2e-10*scale,`${message}: expected ${expected}, actual ${actual}`);
}
export function HatchAffineGeometry(version,binary,operation,plane){
  let document=HatchRelationsLoad(HatchRelationsRaw(version,binary));const matrix=HatchAffineMatrix(operation),translation=new Vector3(7,-11,13);
  for(const h of document.Entities.Hatches){
    h.Normal=plane===0?Vector3.UnitZ:new Vector3(1,2,3);h.Elevation=4;
    const original=HatchRelationEdge(h);if(!original.IsRational)original.ControlPoints[0]=new Vector3(original.ControlPoints[0].X,original.ControlPoints[0].Y,-2.5);
    const snapshot=original.Clone(),controls=Array.from(original.ControlPoints,p=>HatchAffineWorld(h,new Vector2(p.X,p.Y))),fits=Array.from(original.FitPoints,p=>HatchAffineWorld(h,p));
    const start=original.StartTangent!==null?HatchAffineWorld(h,original.StartTangent,true):null,end=original.EndTangent!==null?HatchAffineWorld(h,original.EndTangent,true):null;
    const line=single(Array.from(single(h.BoundaryPaths).Edges).filter(e=>e instanceof HatchBoundaryPath.Line)),ls=HatchAffineWorld(h,line.Start),le=HatchAffineWorld(h,line.End);
    const seeds=Array.from(h.SeedPoints,p=>HatchAffineWorld(h,p)),flags=single(h.BoundaryPaths).PathType;
    h.TransformBy(matrix,translation);const actual=HatchRelationEdge(h);
    for(const key of ['Degree','IsRational','IsPeriodic'])Equal(snapshot[key],actual[key],`Affine ${key}`);
    Equal(snapshot.Knots.length,actual.Knots.length,'Affine knot count');Equal(snapshot.ControlPoints.length,actual.ControlPoints.length,'Affine control count');
    for(let i=0;i<snapshot.Knots.length;i++)SameDoubleBits(snapshot.Knots[i],actual.Knots[i],'Affine knot value');
    for(let i=0;i<controls.length;i++){SameDoubleBits(snapshot.ControlPoints[i].Z,actual.ControlPoints[i].Z,'Affine stored weight');HatchAffineNear(transformed(matrix,controls[i],translation),HatchAffineWorld(h,new Vector2(actual.ControlPoints[i].X,actual.ControlPoints[i].Y)),'Affine world control');}
    for(let i=0;i<fits.length;i++)HatchAffineNear(transformed(matrix,fits[i],translation),HatchAffineWorld(h,actual.FitPoints.get_Item(i)),'Affine world fit');
    Equal(start!==null,actual.StartTangent!==null,'Affine start presence');Equal(end!==null,actual.EndTangent!==null,'Affine end presence');
    if(start!==null)HatchAffineNear(Matrix3.Multiply(matrix,start),HatchAffineWorld(h,actual.StartTangent,true),'Affine start tangent');
    if(end!==null)HatchAffineNear(Matrix3.Multiply(matrix,end),HatchAffineWorld(h,actual.EndTangent,true),'Affine end tangent');
    const al=single(Array.from(single(h.BoundaryPaths).Edges).filter(e=>e instanceof HatchBoundaryPath.Line));
    HatchAffineNear(transformed(matrix,ls,translation),HatchAffineWorld(h,al.Start),'Affine line start');HatchAffineNear(transformed(matrix,le,translation),HatchAffineWorld(h,al.End),'Affine line end');
    for(let i=0;i<seeds.length;i++)HatchAffineNear(transformed(matrix,seeds[i],translation),HatchAffineWorld(h,h.SeedPoints.get_Item(i)),'Affine seed');
    Equal(flags,single(h.BoundaryPaths).PathType,'Affine path classification');HatchRelationsEqual(snapshot,original);
  }
  const expected=new Map(Array.from(document.Entities.Hatches,h=>[HatchRelationName(h),HatchRelationEdge(h).Clone()]));
  for(let cycle=0;cycle<3;cycle++){
    document=HatchRelationsRoundTrip(document,cycle===1?!binary:binary,cycle===2?`hatch-affine-${VersionName(version)}-${BooleanName(binary)}-${operation}-${plane}.dxf`:null);
    for(const h of document.Entities.Hatches)HatchRelationsEqual(expected.get(HatchRelationName(h)),HatchRelationEdge(h));
  }
}
export function HatchAffineAtomic(associative,defect){
  const document=new DxfDocument(),source=new Line(Vector3.Zero,new Vector3(2,3,0)),hatch=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([source])],associative);document.Entities.Add(hatch);
  const path=single(hatch.BoundaryPaths),original=single(path.Edges);let matrix=Matrix3.Identity,translation=Vector3.Zero;
  switch(defect){
    case 'nan-matrix':matrix.M11=NaN;break;case 'infinite-matrix':matrix.M22=Infinity;break;case 'nan-translation':translation.X=NaN;break;
    case 'collapsed-plane':matrix=Matrix3.Scale(0,1,1);break;case 'overflow':translation=new Vector3(Number.MAX_VALUE,Number.MAX_VALUE,Number.MAX_VALUE);matrix=Matrix3.Scale(Number.MAX_VALUE);break;
    case 'invalid-mixed-line':original.End=new Vector2(NaN,0);hatch.BoundaryPaths.Add(new HatchBoundaryPath([edge(HatchBoundaryPath.Arc,{Center:Vector2.Zero,Radius:1,StartAngle:0,EndAngle:180})]));break;
    default:hatch.BoundaryPaths.Add(new HatchBoundaryPath([edge(HatchBoundaryPath.Spline,{Degree:2,Knots:[0,0,0,1,1,1],ControlPoints:[new Vector3(0,0,1),new Vector3(1,1,1)]})]));break;
  }
  const paths=Array.from(hatch.BoundaryPaths),sources=Array.from(path.Entities),reactors=Array.from(source.Reactors),seed=document.DrawingVariables.HandleSeed,members=Array.from(document.Entities.All).length;let events=0;
  hatch.HatchBoundaryPathAdded.Add(()=>events++);hatch.HatchBoundaryPathRemoved.Add(()=>events++);
  let rejected=false;try{hatch.TransformBy(matrix,translation);}catch(error){if(error instanceof ArgumentException||error instanceof InvalidOperationException)rejected=true;else throw error;}
  Check(rejected,'Invalid affine transform rejected');Equal(associative,hatch.Associative,'Failed transform retains associativity');
  Check(sameReferences(paths,hatch.BoundaryPaths)&&original===single(path.Edges),'Failed transform preserves path and edge identities');
  Check(sameReferences(sources,path.Entities)&&sameReferences(reactors,source.Reactors),'Failed transform retains source occurrences/reactors');
  Equal(seed,document.DrawingVariables.HandleSeed,'Failed transform allocates no handles');Equal(members,Array.from(document.Entities.All).length,'Failed transform retains membership');
  Equal(0,events,'Failed transform raises no path events');Equal(Vector3.UnitZ,hatch.Normal,'Failed transform retains normal');SameDoubleBits(0,hatch.Elevation,'Failed transform retains elevation');
}
export function HatchAffineIdentity(){
  const doc=HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018,false));
  for(const hatch of doc.Entities.Hatches){hatch.Normal=new Vector3(1,2,3);hatch.Elevation=4;hatch.PixelSize=.0625;const original=HatchRelationEdge(hatch),expected=original.Clone(),path=single(hatch.BoundaryPaths);
    hatch.TransformBy(Matrix3.Identity,Vector3.Zero);Check(path===single(hatch.BoundaryPaths)&&original===HatchRelationEdge(hatch),'Identity keeps boundary object identity');HatchRelationsEqual(expected,HatchRelationEdge(hatch));SameDoubleBits(.0625,hatch.PixelSize,'Identity preserves pixel size');}
}
export function HatchAffinePolyline(binary,operation,plane){
  const points=[new Vector3(0,0,0),new Vector3(10,0,-0),new Vector3(10,5,0),new Vector3(0,5,0)],original=edge(HatchBoundaryPath.Polyline,{IsClosed:true,Vertexes:points.map(p=>new Vector3(p.X,p.Y,p.Z))});
  const hatch=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([original])],false);hatch.Normal=plane===0?Vector3.UnitZ:new Vector3(1,2,3);hatch.Elevation=4;hatch.PixelSize=.0625;
  const matrix=HatchAffineMatrix(operation),translation=new Vector3(7,-11,13),world=points.map(p=>HatchAffineWorld(hatch,new Vector2(p.X,p.Y))),flags=single(hatch.BoundaryPaths).PathType;
  hatch.TransformBy(matrix,translation);const actual=single(single(hatch.BoundaryPaths).Edges);Check(actual instanceof HatchBoundaryPath.Polyline,'Polyline result');Check(actual.IsClosed,'Affine straight polyline closure');Equal(flags,single(hatch.BoundaryPaths).PathType,'Affine polyline flags');
  for(let i=0;i<points.length;i++){HatchAffineNear(transformed(matrix,world[i],translation),HatchAffineWorld(hatch,new Vector2(actual.Vertexes[i].X,actual.Vertexes[i].Y)),'Affine polyline world vertex');SameDoubleBits(points[i].Z,actual.Vertexes[i].Z,'Affine stored zero bulge');}
  let doc=new DxfDocument();doc.Entities.Add(hatch);doc=HatchRelationsRoundTrip(doc,binary,`hatch-affine-polyline-${BooleanName(binary)}-${operation}-${plane}.dxf`);SameDoubleBits(.0625,single(doc.Entities.Hatches).PixelSize,'Affine stored pixel size');
}
export function HatchAffineUnsupported(kind){
  let original=edge(HatchBoundaryPath.Line,{Start:Vector2.Zero,End:Vector2.UnitX});
  if(kind==='arc')original=edge(HatchBoundaryPath.Arc,{Center:Vector2.Zero,Radius:1,StartAngle:0,EndAngle:180,IsCounterclockwise:true});
  if(kind==='ellipse')original=edge(HatchBoundaryPath.Ellipse,{Center:Vector2.Zero,EndMajorAxis:Vector2.UnitX,MinorRatio:.5,StartAngle:0,EndAngle:180,IsCounterclockwise:true});
  if(kind==='bulge')original=edge(HatchBoundaryPath.Polyline,{IsClosed:true,Vertexes:[new Vector3(0,0,1),new Vector3(2,0,0),new Vector3(0,2,0)]});
  const pattern=kind==='pattern'?HatchPattern.Line:kind==='gradient'?new HatchGradientPattern():HatchPattern.Solid;if(kind==='pattern')pattern.Type=HatchType.UserDefined;
  const hatch=new Hatch(pattern,[new HatchBoundaryPath([original])],false),path=single(hatch.BoundaryPaths);
  Throws(NotSupportedException,()=>hatch.TransformBy(HatchAffineMatrix(3),new Vector3(7,-11,13)));
  Check(path===single(hatch.BoundaryPaths)&&original===single(path.Edges),'Unsupported transform preserves boundary objects');SameDoubleBits(1,hatch.Pattern.Scale,'Unsupported pattern scale unchanged');SameDoubleBits(0,hatch.Pattern.Angle,'Unsupported pattern angle unchanged');
}
export function HatchAffineMixedSpline(binary){
  const source=HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018,binary)),spline=HatchRelationEdge(single(Array.from(source.Entities.Hatches).filter(h=>HatchRelationName(h)==='PERIODIC'))).Clone();
  spline.IsRational=false;spline.ControlPoints[0]=new Vector3(spline.ControlPoints[0].X,spline.ControlPoints[0].Y,-2.5);
  const hatch=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([spline,edge(HatchBoundaryPath.Arc,{Center:Vector2.Zero,Radius:3,StartAngle:0,EndAngle:180,IsCounterclockwise:true})])],false),expected=spline.Clone();
  let doc=new DxfDocument();doc.Entities.Add(hatch);hatch.TransformBy(Matrix3.Identity,Vector3.Zero);HatchRelationsEqual(expected,HatchRelationEdge(hatch));doc=HatchRelationsRoundTrip(doc,binary);HatchRelationsEqual(expected,HatchRelationEdge(single(doc.Entities.Hatches)));
}
export function HatchAffineAssociation(){
  const document=new DxfDocument(),source=new Line(Vector3.Zero,new Vector3(2,3,0)),h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([source])],true);document.Entities.Add(h);
  const handle=source.Handle;h.TransformBy(HatchAffineMatrix(3),new Vector3(7,-11,13));Check(!h.Associative&&single(h.BoundaryPaths).Entities.Count===0,'Successful transform follows explicit unlink behavior');
  Check(source.Owner!==null&&document.GetObjectByHandle(handle)===source,'Unlink retains source entity identity');Equal(Vector3.Zero,source.StartPoint,'Source geometry remains unchanged');Check(!Array.from(source.Reactors).includes(h),'Successful unlink releases source use');
}
