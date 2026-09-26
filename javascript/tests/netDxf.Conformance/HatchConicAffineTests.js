// Port of pinned HatchConicAffineTests.cs; original cases and geometric tolerances retained.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument,DxfVersion,Hatch,HatchPattern,HatchBoundaryPath,Line,Matrix3,Vector2,Vector3 } from '../../index.js';
import { ArgumentException,NotSupportedException } from '../../runtime/Errors.js';
import { Run,Check,Equal,SameDoubleBits,SupportedVersions,VersionName,BooleanName } from './TestHarness.js';
import { HatchAffineMatrix,HatchAffineWorld,HatchAffineNear } from './HatchAffineTests.js';
import { HatchRelationsLoad,HatchRelationsRaw,HatchRelationsRoundTrip,HatchRelationEdge } from './HatchSplineRelationTests.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..'),artifacts=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../artifacts/conformance/fixtures');
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const make=(Type,values)=>Object.assign(new Type(),values),sameRefs=(a,b)=>{b=Array.from(b);return a.length===b.length&&a.every((x,i)=>x===b[i]);};
const add=Vector2.Add,sub=Vector2.Subtract,mul=Vector2.Multiply;
export function RegisterHatchConicAffineTests(){
  for(const v of SupportedVersions)for(const b of [false,true])for(let op=0;op<7;op++)for(let plane=0;plane<2;plane++)
    Run(`hatch-conic/geometry/${VersionName(v)}/${BooleanName(b)}/${op}/${plane}`,()=>HatchConicGeometry(v,b,op,plane));
  for(const a of [false,true])for(const defect of ['zero-radius','negative-ratio','zero-axis','multiple-turns','tiny-bulge','terminal-bulge','coincident-bulge','extreme-anisotropy'])
    Run(`hatch-conic/atomic/${BooleanName(a)}/${defect}`,()=>HatchConicAtomic(a,defect));
  Run('hatch-conic/identity',HatchConicIdentity);
  for(const b of [false,true])Run(`hatch-conic/mixed-stored-spline/${BooleanName(b)}`,()=>HatchConicMixed(b));
  Run('hatch-conic/open-dormant-bulge',HatchConicDormant);
  for(const ellipse of [false,true])for(const ccw of [false,true])for(const span of [0,360,-360,359.99999999])
    Run(`hatch-conic/repeated-turns/${BooleanName(ellipse)}/${BooleanName(ccw)}/${span}`,()=>HatchConicTurns(ellipse,ccw,span));
  Run('hatch-conic/bulge-conditioning',HatchConicConditioning);
}
export function HatchConicMatrix(op){return op<5?HatchAffineMatrix(op):op===5?Matrix3.Scale(.2,7,1):Matrix3.Scale(100,.1,2);}
export function HatchConicMod(angle){angle%=2*Math.PI;return angle<0?angle+2*Math.PI:angle;}
export function HatchConicPoint(edge,fraction){
  if(edge instanceof HatchBoundaryPath.Line)return add(edge.Start,mul(sub(edge.End,edge.Start),fraction));
  let center,major,ratio,start,end,ccw;
  if(edge instanceof HatchBoundaryPath.Arc){center=edge.Center;major=new Vector2(edge.Radius,0);ratio=1;start=edge.StartAngle;end=edge.EndAngle;ccw=edge.IsCounterclockwise;}
  else{Check(edge instanceof HatchBoundaryPath.Ellipse,'Ellipse edge');center=edge.Center;major=edge.EndMajorAxis;ratio=edge.MinorRatio;start=edge.StartAngle;end=edge.EndAngle;ccw=edge.IsCounterclockwise;}
  const sign=ccw?1:-1,parameter=value=>{const a=sign*value*Math.PI/180;return Math.atan2(Math.sin(a)/ratio,Math.cos(a));};
  const first=parameter(start),last=parameter(end),sweep=start===end?0:Math.abs(end-start)===360?sign*2*Math.PI:sign*HatchConicMod(sign*(last-first)),t=first+sweep*fraction;
  return add(add(center,mul(major,Math.cos(t))),mul(new Vector2(-major.Y,major.X),ratio*Math.sin(t)));
}
export function HatchConicSegment(polyline,index){
  const a=polyline.Vertexes[index],b=polyline.Vertexes[(index+1)%polyline.Vertexes.length],first=new Vector2(a.X,a.Y),last=new Vector2(b.X,b.Y);
  if(a.Z===0)return make(HatchBoundaryPath.Line,{Start:first,End:last});
  const chord=sub(last,first),center=add(Vector2.Divide(add(first,last),2),mul(new Vector2(-chord.Y,chord.X),(1/a.Z-a.Z)/4));
  const radius=sub(first,center).Modulus(),ccw=a.Z>0,angle=p=>HatchConicMod((ccw?1:-1)*Math.atan2(p.Y-center.Y,p.X-center.X))*180/Math.PI;
  return make(HatchBoundaryPath.Arc,{Center:center,Radius:radius,StartAngle:angle(first),EndAngle:angle(last),IsCounterclockwise:ccw});
}
export function HatchConicEdges(hatch){const result=[];for(const edge of single(hatch.BoundaryPaths).Edges){if(!(edge instanceof HatchBoundaryPath.Polyline))result.push(edge);else for(let i=0;i<edge.Vertexes.length-(edge.IsClosed?0:1);i++)result.push(HatchConicSegment(edge,i));}return result;}
export function HatchConicGeometry(version,binary,operation,plane){
  const year=VersionName(version).replace('AutoCad','');let doc=DxfDocument.Load(path.join(root,'tests/fixtures/hatch-conic-affine',`ezdxf-hatch-conic-R${year}-${binary?'binary':'ascii'}.dxf`));Check(doc!==null,'Conic fixture load failed');
  const matrix=HatchConicMatrix(operation),translation=new Vector3(7,-11,13),expected=new Map();
  for(const h of doc.Entities.Hatches){
    h.Normal=plane===0?Vector3.UnitZ:new Vector3(1,2,3);h.Elevation=4;h.PixelSize=.0625;h.SeedPoints.Add(new Vector2(1,2));
    expected.set(h.Layer.Name,HatchConicEdges(h).map(e=>Array.from({length:33},(_,i)=>Vector3.Add(Matrix3.Multiply(matrix,HatchAffineWorld(h,HatchConicPoint(e,i/32))),translation))));
    const oldPath=single(h.BoundaryPaths),oldFlags=oldPath.PathType,sourceEdges=Array.from(oldPath.Edges);h.TransformBy(matrix,translation);
    Equal(oldFlags&~2,single(h.BoundaryPaths).PathType&~2,'Conic classification flags');Check(sameRefs(sourceEdges,oldPath.Edges),'Conic transform changed original edge objects');
  }
  const verify=drawing=>{for(const h of drawing.Entities.Hatches){const edges=HatchConicEdges(h),points=expected.get(h.Layer.Name);Equal(points.length,edges.length,'Conic segment count / open closure');for(let e=0;e<edges.length;e++)for(let i=0;i<33;i++)HatchAffineNear(points[e][i],HatchAffineWorld(h,HatchConicPoint(edges[e],i/32)),'Conic world curve '+h.Layer.Name);SameDoubleBits(.0625,h.PixelSize,'Conic pixel size');}};
  verify(doc);for(let cycle=0;cycle<3;cycle++){doc=HatchRelationsRoundTrip(doc,cycle===1?!binary:binary,cycle===2?`hatch-conic-${VersionName(version)}-${BooleanName(binary)}-${operation}-${plane}.dxf`:null);verify(doc);}
}
export function HatchConicAtomic(association,defect){
  const source=new Line(Vector3.Zero,new Vector3(2,3,0)),h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([source])],association),doc=new DxfDocument();doc.Entities.Add(h);
  let edge=make(HatchBoundaryPath.Ellipse,{Center:new Vector2(2,3),EndMajorAxis:new Vector2(3,4),MinorRatio:.4,StartAngle:30,EndAngle:150,IsCounterclockwise:true}),matrix=HatchConicMatrix(3);
  switch(defect){case 'zero-radius':edge=make(HatchBoundaryPath.Arc,{Radius:0,StartAngle:0,EndAngle:180});break;case 'negative-ratio':edge.MinorRatio=-.4;break;case 'zero-axis':edge.EndMajorAxis=Vector2.Zero;break;case 'multiple-turns':edge.EndAngle=750;break;case 'extreme-anisotropy':matrix=Matrix3.Scale(1e12,1e-12,1);break;default:edge=make(HatchBoundaryPath.Polyline,{IsClosed:false,Vertexes:[new Vector3(0,0,defect==='tiny-bulge'?1e-15:.25),new Vector3(defect==='coincident-bulge'?0:10,0,defect==='terminal-bulge'?.5:0)]});break;}
  h.BoundaryPaths.Add(new HatchBoundaryPath([edge]));const paths=Array.from(h.BoundaryPaths),reactors=Array.from(source.Reactors),entities=Array.from(paths[0].Entities),seed=doc.DrawingVariables.HandleSeed,members=Array.from(doc.Entities.All).length;let events=0;
  h.HatchBoundaryPathAdded.Add(()=>events++);h.HatchBoundaryPathRemoved.Add(()=>events++);
  let rejected=false;try{h.TransformBy(matrix,new Vector3(7,-11,13));}catch(error){if(error instanceof ArgumentException||error instanceof NotSupportedException)rejected=true;else throw error;}
  Check(rejected,'Unrepresentable conic rejected: '+defect);Check(sameRefs(paths,h.BoundaryPaths)&&edge===single(paths[1].Edges),'Rejected conic preserves object identities');
  Equal(association,h.Associative,'Rejected conic preserves association');Check(sameRefs(reactors,source.Reactors)&&sameRefs(entities,paths[0].Entities),'Rejected conic preserves source occurrences/reactors');
  Equal(seed,doc.DrawingVariables.HandleSeed,'Rejected conic allocates no handles');Equal(members,Array.from(doc.Entities.All).length,'Rejected conic retains membership');Equal(0,events,'Rejected conic raises no callbacks');
  Equal(Vector3.UnitZ,h.Normal,'Rejected conic normal unchanged');SameDoubleBits(0,h.Elevation,'Rejected conic elevation unchanged');SameDoubleBits(1,h.Pattern.Scale,'Rejected conic pattern scale unchanged');
}
export function HatchConicIdentity(){const ellipse=make(HatchBoundaryPath.Ellipse,{Center:new Vector2(2,3),EndMajorAxis:new Vector2(3,4),MinorRatio:.4,StartAngle:-30,EndAngle:330,IsCounterclockwise:false}),h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([ellipse])],false),p=single(h.BoundaryPaths);h.TransformBy(Matrix3.Identity,Vector3.Zero);Check(p===single(h.BoundaryPaths)&&ellipse===single(p.Edges),'Conic identity preserves objects');SameDoubleBits(-30,ellipse.StartAngle,'Conic identity preserves stored start');SameDoubleBits(330,ellipse.EndAngle,'Conic identity preserves stored full turn');}
export function HatchConicDormant(){const p=make(HatchBoundaryPath.Polyline,{IsClosed:false,Vertexes:[new Vector3(0,0,.25),new Vector3(10,0,.75)]}),h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([p])],false);h.TransformBy(Matrix3.Reflection(Vector3.UnitX),Vector3.Zero);const result=single(single(h.BoundaryPaths).Edges);Check(result instanceof HatchBoundaryPath.Polyline,'Polyline result');Check(!result.IsClosed,'Similarity preserves open representation');SameDoubleBits(-.25,result.Vertexes[0].Z,'Active reflected bulge');SameDoubleBits(.75,result.Vertexes[1].Z,'Dormant terminal bulge remains stored');}
export function HatchConicTurns(ellipse,ccw,span){
  const edge=ellipse?make(HatchBoundaryPath.Ellipse,{Center:new Vector2(2,3),EndMajorAxis:new Vector2(3,4),MinorRatio:.4,StartAngle:30,EndAngle:30+span,IsCounterclockwise:ccw}):make(HatchBoundaryPath.Arc,{Center:new Vector2(2,3),Radius:5,StartAngle:30,EndAngle:30+span,IsCounterclockwise:ccw});
  const h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([edge])],false);h.Normal=new Vector3(1,2,3);h.Elevation=4;
  let expected=Array.from({length:33},(_,i)=>HatchAffineWorld(h,HatchConicPoint(edge,i/32)));
  for(const operation of [3,4,2,1]){const matrix=HatchConicMatrix(operation),translation=new Vector3(7,-11,13);expected=expected.map(p=>Vector3.Add(Matrix3.Multiply(matrix,p),translation));h.TransformBy(matrix,translation);const result=single(single(h.BoundaryPaths).Edges);Check(result instanceof HatchBoundaryPath.Ellipse,'Ellipse result');Equal(span===0,result.StartAngle===result.EndAngle,'Repeated transform zero interval');Equal(Math.abs(span)===360,Math.abs(result.EndAngle-result.StartAngle)===360,'Repeated transform exact stored full turn');for(let i=0;i<33;i++)HatchAffineNear(expected[i],HatchAffineWorld(h,HatchConicPoint(result,i/32)),'Repeated conic transform world curve');}
}
export function HatchConicConditioning(){
  const records=[];let accepted=0,rejected=0;
  for(const operation of [2,3])for(const sign of [-1,1])for(let exponent=3;exponent<17;exponent++){
    const bulge=sign*Math.pow(10,-exponent),p=make(HatchBoundaryPath.Polyline,{IsClosed:false,Vertexes:[new Vector3(0,0,bulge),new Vector3(10,0,0)]}),h=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath([p])],false),oldPath=single(h.BoundaryPaths);let file=null,success;
    try{h.TransformBy(HatchConicMatrix(operation),new Vector3(7,-11,13));success=true;}catch(error){if(error instanceof ArgumentException)success=false;else throw error;}
    if(success){Check(single(single(h.BoundaryPaths).Edges) instanceof HatchBoundaryPath.Ellipse,'A tiny nonzero bulge was flattened');const doc=new DxfDocument();doc.Entities.Add(h);file=`hatch-conic-conditioning-${operation}-${sign}-${exponent}.dxf`;HatchRelationsRoundTrip(doc,false,file);accepted++;}
    else{Check(oldPath===single(h.BoundaryPaths)&&p===single(oldPath.Edges),'Rejected tiny bulge mutated geometry');rejected++;}
    if(exponent===3)Check(success,'Well-conditioned small bulge should transform');if(exponent===16)Check(!success,'Unrepresentable tiny bulge should reject');records.push({operation,sign,exponent,bulge,accepted:success,file});
  }
  Check(accepted>0&&rejected>0,'Conditioning probe covers both outcomes');fs.mkdirSync(artifacts,{recursive:true});fs.writeFileSync(path.join(artifacts,'hatch-conic-conditioning.json'),JSON.stringify(records,null,2));
}
export function HatchConicMixed(binary){
  const doc=HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018,binary));
  for(const h of doc.Entities.Hatches){const spline=HatchRelationEdge(h);if(!spline.IsRational)spline.ControlPoints[0]=new Vector3(spline.ControlPoints[0].X,spline.ControlPoints[0].Y,-2.5);const snapshot=spline.Clone();
    h.BoundaryPaths.Add(new HatchBoundaryPath([make(HatchBoundaryPath.Arc,{Center:new Vector2(2,3),Radius:5,StartAngle:30,EndAngle:150,IsCounterclockwise:false})]));h.TransformBy(HatchConicMatrix(3),new Vector3(7,-11,13));const result=single(Array.from(h.BoundaryPaths.get_Item(0).Edges).filter(e=>e instanceof HatchBoundaryPath.Spline));
    Equal(snapshot.IsRational,result.IsRational,'Conic mixed rational flag');Equal(snapshot.IsPeriodic,result.IsPeriodic,'Conic mixed periodic flag');Equal(Array.from(snapshot.Knots),Array.from(result.Knots),'Conic mixed knots');Equal(Array.from(snapshot.ControlPoints,p=>p.Z),Array.from(result.ControlPoints,p=>p.Z),'Conic mixed weights');
  }
  HatchRelationsRoundTrip(doc,binary,`hatch-conic-mixed-${BooleanName(binary)}.dxf`);
}
