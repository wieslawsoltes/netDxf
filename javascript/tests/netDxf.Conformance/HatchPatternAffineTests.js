// Port of pinned HatchPatternAffineTests.cs; original case names/assertions retained.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DxfDocument,DxfVersion,Hatch,HatchPattern,HatchGradientPattern,HatchPatternLineDefinition,HatchBoundaryPath,HatchType,Line,Matrix3,Vector2,Vector3 } from '../../index.js';
import { ArgumentException,NotSupportedException } from '../../runtime/Errors.js';
import { Run,Check,Equal,SameDoubleBits,SupportedVersions,VersionName,BooleanName } from './TestHarness.js';
import { HatchAffineWorld,HatchAffineNear } from './HatchAffineTests.js';
import { HatchRelationsLoad,HatchRelationsRaw,HatchRelationsRoundTrip,HatchRelationEdge,HatchRelationName } from './HatchSplineRelationTests.js';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../../..');
const single=items=>{const a=Array.from(items);Equal(1,a.length,'Expected one item');return a[0];};
const sameRefs=(a,b)=>{b=Array.from(b);return a.length===b.length&&a.every((x,i)=>x===b[i]);};
const transformed=(matrix,p,translation)=>Vector3.Add(Matrix3.Multiply(matrix,p),translation);
export function RegisterHatchPatternAffineTests(){
  for(const v of SupportedVersions)for(const b of [false,true])for(let op=0;op<7;op++)for(let plane=0;plane<2;plane++)
    Run(`hatch-pattern-affine/geometry/${VersionName(v)}/${BooleanName(b)}/${op}/${plane}`,()=>HatchPatternAffineGeometry(v,b,op,plane));
  for(const a of [false,true])for(const defect of ['null-line','angle','base','delta','dash-nan','dash-infinite','origin','line-count','dash-count','underflow','dash-overflow','base-overflow','offset-overflow','empty','subclass','user-defined','double','gradient','origin-z','translation-z','tiny-z'])
    Run(`hatch-pattern-affine/atomic/${BooleanName(a)}/${defect}`,()=>HatchPatternAffineAtomic(a,defect));
  for(const b of [false,true])for(let op=0;op<3;op++)Run(`hatch-pattern-affine/origin/${BooleanName(b)}/${op}`,()=>HatchPatternAffineOrigin(b,op));
  for(const b of [false,true])Run(`hatch-pattern-affine/mixed/${BooleanName(b)}`,()=>HatchPatternAffineMixed(b));
  Run('hatch-pattern-affine/shared',HatchPatternAffineShared);Run('hatch-pattern-affine/identity',HatchPatternAffineIdentity);Run('hatch-pattern-affine/repeated',HatchPatternAffineRepeated);
}
export function HatchPatternAffineMatrix(op){switch(op){case 0:return Matrix3.Identity;case 1:return Matrix3.RotationZ(.43);case 2:return Matrix3.Scale(2,3,.5);case 3:return new Matrix3(1,.75,-.2,0,1,.5,0,0,1);case 4:return Matrix3.Reflection(Vector3.UnitX);case 5:return Matrix3.Scale(2,3,0);default:return Matrix3.Scale(100,.1,2);}}
export function HatchPatternRotate(p,angle){const a=angle*Math.PI/180;return new Vector2(p.X*Math.cos(a)-p.Y*Math.sin(a),p.X*Math.sin(a)+p.Y*Math.cos(a));}
export function HatchPatternSamples(pattern,line){
  const b=Vector2.Multiply(pattern.Scale,HatchPatternRotate(line.Origin,pattern.Angle)),offset=Vector2.Multiply(pattern.Scale,HatchPatternRotate(line.Delta,pattern.Angle+line.Angle)),u=HatchPatternRotate(Vector2.UnitX,pattern.Angle+line.Angle),breaks=[0];let length=0;
  for(const dash of line.DashPattern){breaks.push(length+Math.abs(dash)*pattern.Scale/2);length+=Math.abs(dash)*pattern.Scale;breaks.push(length);}
  const result=[];for(const n of [-3,-1,0,2,5])for(const m of [-2,0,3])for(const at of breaks)result.push(Vector2.Add(Vector2.Add(b,Vector2.Multiply(n,offset)),Vector2.Multiply(m*length+at,u)));return result;
}
export function HatchPatternAffineExample(){
  const pattern=new HatchPattern('AFFINE_EXPLICIT','Preserved description');pattern.Type=HatchType.Custom;pattern.Angle=27;pattern.Scale=.75;pattern.Origin=new Vector2(3,4);
  const line=Object.assign(new HatchPatternLineDefinition(),{Angle:15,Origin:new Vector2(1,2),Delta:new Vector2(.5,3)});line.DashPattern.AddRange([2,-1,0,-0,-.5]);pattern.LineDefinitions.Add(line);return pattern;
}
export function HatchPatternAffineHatch(pattern){return new Hatch(pattern,[new HatchBoundaryPath([Object.assign(new HatchBoundaryPath.Line(),{Start:Vector2.Zero,End:new Vector2(2,3)})])],false);}
export function HatchPatternAffineGeometry(version,binary,operation,plane){
  const year=VersionName(version).replace('AutoCad','');let doc=DxfDocument.Load(path.join(root,'tests/fixtures/hatch-pattern-affine',`ezdxf-hatch-pattern-R${year}-${binary?'binary':'ascii'}.dxf`));Check(doc!==null,'Pattern fixture load failed');
  const matrix=HatchPatternAffineMatrix(operation),translation=new Vector3(7,-11,0),expected=new Map(),origins=new Map(),snapshots=new Map(),directions=new Map();
  for(const h of doc.Entities.Hatches){
    h.Normal=plane===0?Vector3.UnitZ:new Vector3(1,2,3);h.Elevation=4;
    expected.set(h.Layer.Name,Array.from(h.Pattern.LineDefinitions,l=>HatchPatternSamples(h.Pattern,l).map(p=>transformed(matrix,HatchAffineWorld(h,p),translation))));
    const origin=transformed(matrix,new Vector3(h.Pattern.Origin.X,h.Pattern.Origin.Y,0),translation);origins.set(h.Layer.Name,new Vector2(origin.X,origin.Y));
    directions.set(h.Layer.Name,Array.from(h.Pattern.LineDefinitions,l=>Vector3.Normalize(Matrix3.Multiply(matrix,HatchAffineWorld(h,HatchPatternRotate(Vector2.UnitX,h.Pattern.Angle+l.Angle),true)))));
    snapshots.set(h.Layer.Name,h.Pattern.Clone());const original=h.Pattern;h.TransformBy(matrix,translation);Check(original!==h.Pattern,'Explicit pattern receives an independent snapshot');HatchPatternAffineEqual(snapshots.get(h.Layer.Name),original);
  }
  const verify=drawing=>{for(const h of drawing.Entities.Hatches){
    const snapshot=snapshots.get(h.Layer.Name);for(const key of ['Name','Type','Style','IsDouble'])Equal(snapshot[key],h.Pattern[key],`Pattern ${key}`);
    const origin=origins.get(h.Layer.Name);HatchAffineNear(new Vector3(origin.X,origin.Y,0),new Vector3(h.Pattern.Origin.X,h.Pattern.Origin.Y,0),'Separate WCS pattern Origin');
    Equal(expected.get(h.Layer.Name).length,h.Pattern.LineDefinitions.Count,'Pattern family count');
    for(let f=0;f<h.Pattern.LineDefinitions.Count;f++){
      const line=h.Pattern.LineDefinitions.get_Item(f),old=snapshot.LineDefinitions.get_Item(f);Equal(old.DashPattern.Count,line.DashPattern.Count,'Pattern dash count');
      for(let d=0;d<old.DashPattern.Count;d++){
        const before=old.DashPattern.get_Item(d),after=line.DashPattern.get_Item(d),sign=x=>x===0?0:Math.sign(x);Equal(sign(before),sign(after),'Dash/dot/gap sign');if(before===0)SameDoubleBits(before,after,'Stored signed-zero dot');
      }
      HatchAffineNear(directions.get(h.Layer.Name)[f],Vector3.Normalize(HatchAffineWorld(h,HatchPatternRotate(Vector2.UnitX,h.Pattern.Angle+line.Angle),true)),'Pattern family direction');
      const points=HatchPatternSamples(h.Pattern,line);Equal(expected.get(h.Layer.Name)[f].length,points.length,'Pattern probe count');for(let i=0;i<points.length;i++)HatchAffineNear(expected.get(h.Layer.Name)[f][i],HatchAffineWorld(h,points[i]),'Pattern world phase/family/dash point');
    }
  }};
  verify(doc);for(let cycle=0;cycle<3;cycle++){doc=HatchRelationsRoundTrip(doc,cycle===1?!binary:binary,cycle===2?`hatch-pattern-affine-${VersionName(version)}-${BooleanName(binary)}-${operation}-${plane}.dxf`:null);verify(doc);}
}
export function HatchPatternAffineEqual(expected,actual){
  for(const key of ['Name','Description','Type','IsDouble'])Equal(expected[key],actual[key],`Unchanged pattern ${key}`);
  Equal(expected.Origin,actual.Origin,'Unchanged pattern Origin');SameDoubleBits(expected.Scale,actual.Scale,'Unchanged pattern scale');SameDoubleBits(expected.Angle,actual.Angle,'Unchanged pattern angle');
  Equal(expected.LineDefinitions.Count,actual.LineDefinitions.Count,'Unchanged family count');
  for(let i=0;i<expected.LineDefinitions.Count;i++){const a=expected.LineDefinitions.get_Item(i),b=actual.LineDefinitions.get_Item(i);SameDoubleBits(a.Angle,b.Angle,'Unchanged line angle');Equal(a.Origin,b.Origin,'Unchanged line base');Equal(a.Delta,b.Delta,'Unchanged line offset');Equal(a.DashPattern.Count,b.DashPattern.Count,'Unchanged dash count');for(let d=0;d<a.DashPattern.Count;d++)SameDoubleBits(a.DashPattern.get_Item(d),b.DashPattern.get_Item(d),'Unchanged signed dash');}
}
export class HatchPatternAffineSubclass extends HatchPattern {
  CloneCalls=0;
  constructor(){super('SUBCLASS');this.Type=HatchType.Custom;this.LineDefinitions.Add(new HatchPatternLineDefinition());}
  Clone(){this.CloneCalls++;throw new Error('Virtual Clone must not run');}
}
export function HatchPatternAffineAtomic(association,defect){
  let pattern=HatchPatternAffineExample(),matrix=HatchPatternAffineMatrix(3),translation=new Vector3(7,-11,0);const line=single(pattern.LineDefinitions);
  switch(defect){
    case 'null-line':pattern.LineDefinitions.Add(null);break;case 'angle':line.Angle=NaN;break;case 'base':line.Origin=new Vector2(Infinity,0);break;case 'delta':line.Delta=new Vector2(0,NaN);break;
    case 'dash-nan':line.DashPattern.Add(NaN);break;case 'dash-infinite':line.DashPattern.Add(Infinity);break;case 'origin':pattern.Origin=new Vector2(NaN,0);break;
    case 'line-count':pattern.LineDefinitions.AddRange(Array(32767).fill(line));break;case 'dash-count':line.DashPattern.AddRange(Array(32767).fill(1));break;
    case 'underflow':pattern.Angle=0;pattern.Scale=1;line.Angle=0;line.DashPattern.Add(Number.MIN_VALUE);matrix=Matrix3.Scale(.1,.2,1);break;
    case 'dash-overflow':line.DashPattern.Add(Number.MAX_VALUE);matrix=Matrix3.Scale(8,3,1);break;case 'base-overflow':line.Origin=new Vector2(Number.MAX_VALUE,Number.MAX_VALUE);matrix=Matrix3.Scale(8,3,1);break;
    case 'offset-overflow':line.Delta=new Vector2(Number.MAX_VALUE,Number.MAX_VALUE);matrix=Matrix3.Scale(8,3,1);break;case 'empty':pattern.LineDefinitions.Clear();break;case 'subclass':pattern=new HatchPatternAffineSubclass();break;
    case 'user-defined':pattern.Type=HatchType.UserDefined;break;case 'double':pattern.IsDouble=true;break;case 'gradient':pattern=new HatchGradientPattern();break;
    case 'origin-z':pattern.Origin=new Vector2(3,5);matrix=new Matrix3(1,.3,0,0,1,0,4,-3,1);break;case 'translation-z':translation.Z=1;break;case 'tiny-z':translation.Z=1e-200;break;
  }
  const source=new Line(Vector3.Zero,new Vector3(2,3,0)),h=new Hatch(pattern,[new HatchBoundaryPath([source])],association),doc=new DxfDocument();doc.Entities.Add(h);
  const boundary=single(h.BoundaryPaths),edge=single(boundary.Edges),sources=Array.from(boundary.Entities),reactors=Array.from(source.Reactors),definitions=Array.from(pattern.LineDefinitions);
  const oldOrigin=pattern.Origin,oldAngle=pattern.Angle,oldScale=pattern.Scale,dashes=Array.from(line.DashPattern),seed=doc.DrawingVariables.HandleSeed,members=Array.from(doc.Entities.All).length;let events=0;
  h.HatchBoundaryPathAdded.Add(()=>events++);h.HatchBoundaryPathRemoved.Add(()=>events++);
  let rejected=false;try{h.TransformBy(matrix,translation);}catch(error){if(error instanceof ArgumentException||error instanceof NotSupportedException)rejected=true;else throw error;}
  Check(rejected,'Unrepresentable explicit pattern rejected: '+defect);Check(pattern===h.Pattern&&sameRefs(definitions,pattern.LineDefinitions),'Refused pattern retains original objects');
  SameDoubleBits(oldOrigin.X,pattern.Origin.X,'Refused origin X');SameDoubleBits(oldOrigin.Y,pattern.Origin.Y,'Refused origin Y');SameDoubleBits(oldAngle,pattern.Angle,'Refused angle');SameDoubleBits(oldScale,pattern.Scale,'Refused scale');
  for(let i=0;i<dashes.length;i++)SameDoubleBits(dashes[i],line.DashPattern.get_Item(i),'Refused dash');
  Check(boundary===single(h.BoundaryPaths)&&edge===single(boundary.Edges),'Refused boundary identities');Equal(association,h.Associative,'Refused association');
  Check(sameRefs(sources,boundary.Entities)&&sameRefs(reactors,source.Reactors),'Refused source occurrences/reactors');Equal(seed,doc.DrawingVariables.HandleSeed,'Refused handle seed');Equal(members,Array.from(doc.Entities.All).length,'Refused membership');Equal(0,events,'Refused callbacks');
  Equal(Vector3.UnitZ,h.Normal,'Refused normal');SameDoubleBits(0,h.Elevation,'Refused elevation');if(pattern instanceof HatchPatternAffineSubclass)Equal(0,pattern.CloneCalls,'No user virtual callback in validation');
}
export function HatchPatternAffineOrigin(binary,operation){
  const h=HatchPatternAffineHatch(HatchPatternAffineExample());h.Normal=new Vector3(1,2,3);h.Elevation=4;let matrix=new Matrix3(1,.3,0,0,1,0,4,-3,1);const translation=Vector3.Zero;
  if(operation===1){h.Pattern.Origin=new Vector2(11,0);matrix=Matrix3.RotationX(.4);}if(operation===2){h.Pattern.Origin=new Vector2(.1,.2);matrix=new Matrix3(1,.3,0,0,1,0,3,-1.5,1);}
  const old=h.Pattern,expected=transformed(matrix,new Vector3(old.Origin.X,old.Origin.Y,0),translation);h.TransformBy(matrix,translation);
  HatchAffineNear(new Vector3(expected.X,expected.Y,0),new Vector3(h.Pattern.Origin.X,h.Pattern.Origin.Y,0),'Origin-specific representable map');
  let doc=new DxfDocument();doc.Entities.Add(h);doc=HatchRelationsRoundTrip(doc,binary,`hatch-pattern-affine-origin-${BooleanName(binary)}-${operation}.dxf`);const actual=single(doc.Entities.Hatches).Pattern.Origin;
  HatchAffineNear(new Vector3(expected.X,expected.Y,0),new Vector3(actual.X,actual.Y,0),'Origin packet survives reload');
}
export function HatchPatternAffineMixed(binary){
  let doc=HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018,binary));const expected=new Map(),matrix=HatchPatternAffineMatrix(3),translation=new Vector3(7,-11,0);
  for(const h of doc.Entities.Hatches){const spline=HatchRelationEdge(h);if(!spline.IsRational)spline.ControlPoints[0]=new Vector3(spline.ControlPoints[0].X,spline.ControlPoints[0].Y,-2.5);expected.set(HatchRelationName(h),spline.Clone());h.Pattern=HatchPatternAffineExample();h.TransformBy(matrix,translation);}
  const verify=drawing=>{for(const h of drawing.Entities.Hatches){const before=expected.get(HatchRelationName(h)),after=HatchRelationEdge(h);for(const key of ['Degree','IsRational','IsPeriodic'])Equal(before[key],after[key],`Pattern mixed ${key}`);
    Equal(Array.from(before.Knots),Array.from(after.Knots),'Pattern mixed stored knots');Equal(Array.from(before.ControlPoints,p=>p.Z),Array.from(after.ControlPoints,p=>p.Z),'Pattern mixed stored weights');
    for(let i=0;i<before.ControlPoints.length;i++)HatchAffineNear(transformed(matrix,new Vector3(before.ControlPoints[i].X,before.ControlPoints[i].Y,0),translation),HatchAffineWorld(h,new Vector2(after.ControlPoints[i].X,after.ControlPoints[i].Y)),'Pattern mixed control');
    for(let i=0;i<before.FitPoints.Count;i++)HatchAffineNear(transformed(matrix,new Vector3(before.FitPoints.get_Item(i).X,before.FitPoints.get_Item(i).Y,0),translation),HatchAffineWorld(h,after.FitPoints.get_Item(i)),'Pattern mixed fit');
  }};
  verify(doc);for(let cycle=0;cycle<3;cycle++){doc=HatchRelationsRoundTrip(doc,cycle===1?!binary:binary,cycle===2?`hatch-pattern-affine-mixed-${BooleanName(binary)}.dxf`:null);verify(doc);}
}
export function HatchPatternAffineShared(){
  const pattern=HatchPatternAffineExample(),snapshot=pattern.Clone(),other=HatchPatternAffineHatch(pattern),source=new Line(Vector3.Zero,new Vector3(2,3,0)),h=new Hatch(pattern,[new HatchBoundaryPath([source])],true),doc=new DxfDocument();doc.Entities.Add(h);doc.Entities.Add(other);
  const definitions=Array.from(pattern.LineDefinitions);h.TransformBy(HatchPatternAffineMatrix(3),new Vector3(7,-11,0));Check(pattern===other.Pattern&&pattern!==h.Pattern,'Shared pattern ownership separated');Check(sameRefs(definitions,pattern.LineDefinitions),'Shared line objects retained');HatchPatternAffineEqual(snapshot,pattern);
  Check(!h.Associative&&!Array.from(source.Reactors).includes(h),'Success unlinks source association');Equal(Vector3.Zero,source.StartPoint,'Source geometry retained');Check(source.Owner!==null,'Source entity remains in document');
  const clone=h.Clone();clone.Pattern.LineDefinitions.get_Item(0).DashPattern.set_Item(0,101);Check(h.Pattern.LineDefinitions.get_Item(0).DashPattern.get_Item(0)!==101,'Cloned transformed pattern remains independent');
}
export function HatchPatternAffineIdentity(){const pattern=HatchPatternAffineExample(),snapshot=pattern.Clone(),h=HatchPatternAffineHatch(pattern),boundary=single(h.BoundaryPaths),definitions=Array.from(pattern.LineDefinitions);h.TransformBy(Matrix3.Identity,Vector3.Zero);Check(pattern===h.Pattern&&boundary===single(h.BoundaryPaths)&&sameRefs(definitions,pattern.LineDefinitions),'Exact identity retains stored objects');HatchPatternAffineEqual(snapshot,pattern);}
export function HatchPatternAffineRepeated(){const h=HatchPatternAffineHatch(HatchPatternAffineExample());h.Normal=new Vector3(1,2,3);h.Elevation=4;let expected=HatchPatternSamples(h.Pattern,single(h.Pattern.LineDefinitions)).map(p=>HatchAffineWorld(h,p));for(const operation of [3,4,2,1]){const matrix=HatchPatternAffineMatrix(operation),translation=new Vector3(7,-11,0);expected=expected.map(p=>transformed(matrix,p,translation));h.TransformBy(matrix,translation);const observed=HatchPatternSamples(h.Pattern,single(h.Pattern.LineDefinitions));for(let i=0;i<observed.length;i++)HatchAffineNear(expected[i],HatchAffineWorld(h,observed[i]),'Repeated phase/dash mapping');}}
