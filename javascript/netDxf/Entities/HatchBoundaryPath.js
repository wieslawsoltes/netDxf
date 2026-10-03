// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Line as LineEntity } from './Line.js';
import { Arc as ArcEntity } from './Arc.js';
import { Circle } from './Circle.js';
import { Ellipse as EllipseEntity } from './Ellipse.js';
import { Spline as SplineEntity } from './Spline.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { SplineCreationMethod } from './SplineCreationMethod.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { PeriodicSplineData } from './PeriodicSplineData.js';
import { HatchSplineData } from './HatchSplineData.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyReferenceList } from '../../runtime/EntityRuntime.js';
import { Required, VectorField, DoubleFields, FiniteVector2, FiniteVector2Collection } from '../../runtime/HatchRuntime.js';
import { ArgumentException, ArgumentNullException, NotSupportedException } from '../../runtime/Errors.js';

const EdgeType=Object.freeze({Polyline:0,Line:1,Arc:2,Ellipse:3,Spline:4});
const xy=p=>new Vector2(p.X,p.Y), v3=p=>new Vector3(p.X,p.Y,0);
const project=(entity,p)=>Matrix3.Multiply(MathHelper.ArbitraryAxis(entity.Normal).Transpose(),p);
class Edge {
  constructor(type) { if(new.target===Edge)throw new NotSupportedException('Hatch boundary Edge is abstract.'); Object.defineProperty(this,'Type',{value:type,enumerable:true}); }
}
class Polyline extends Edge {
  Vertexes=null; IsClosed=false;
  constructor(entity) {
    super(0); if(arguments.length===0)return;
    if(entity==null)throw new ArgumentNullException('entity');
    if(entity.Type===EntityType.Polyline2D){
      this.IsClosed=entity.IsClosed;this.Vertexes=FixedArray(Array.from(entity.Vertexes,v=>new Vector3(v.Position.X,v.Position.Y,v.Bulge)));
    }else if(entity.Type===EntityType.Polyline3D){
      const matrix=MathHelper.ArbitraryAxis(entity.Normal).Transpose();this.IsClosed=entity.IsClosed;
      this.Vertexes=FixedArray(Array.from(entity.Vertexes,v=>{const p=Matrix3.Multiply(matrix,v);return new Vector3(p.X,p.Y,0);}));
    }else throw new ArgumentException('The entity is not a Polyline2D or Polyline3D.','entity');
  }
  static ConvertFrom(entity){return new Polyline(entity);}
  Explode(){
    const result=new ReferenceList(),vs=Required(this.Vertexes);
    for(let i=0;i<vs.length;i++){
      if(i===vs.length-1&&!this.IsClosed)break;
      const p=xy(vs[i]),q=xy(vs[(i+1)%vs.length]),bulge=vs[i].Z;
      if(MathHelper.IsZero(bulge))result.Add(Object.assign(new Line(),{Start:p,End:q}));
      else {
        const data=MathHelper.ArcFromBulge(p,q,bulge);
        result.Add(MathHelper.IsZero(data.Item2)?Object.assign(new Line(),{Start:p,End:q}):Object.assign(new Arc(),{Center:data.Item1,Radius:data.Item2,StartAngle:data.Item3,EndAngle:data.Item4}));
      }
    }return result;
  }
  ConvertTo(){return new Polyline2D(Array.from(Required(this.Vertexes),p=>new Polyline2DVertex(p.X,p.Y,p.Z)),this.IsClosed);}
  Clone(){return Object.assign(new Polyline(),{Vertexes:FixedArray(Required(this.Vertexes)),IsClosed:this.IsClosed});}
}
class Line extends Edge {
  constructor(entity){
    super(1);VectorField(this,'Start',Vector2.Zero);VectorField(this,'End',Vector2.Zero);
    if(arguments.length===0)return;if(entity==null)throw new ArgumentNullException('entity');
    if(!(entity instanceof LineEntity))throw new ArgumentException('The entity is not a Line.','entity');
    const t=MathHelper.ArbitraryAxis(entity.Normal).Transpose();this.Start=xy(Matrix3.Multiply(t,entity.StartPoint));this.End=xy(Matrix3.Multiply(t,entity.EndPoint));
  }
  static ConvertFrom(entity){return new Line(entity);}
  ConvertTo(){return new LineEntity(this.Start,this.End);}
  Clone(){return Object.assign(new Line(),{Start:this.Start,End:this.End});}
}
class Arc extends Edge {
  IsCounterclockwise=false;
  constructor(entity){
    super(2);VectorField(this,'Center',Vector2.Zero);DoubleFields(this,['Radius','StartAngle','EndAngle']);
    if(arguments.length===0)return;if(entity==null)throw new ArgumentNullException('entity');
    const t=MathHelper.ArbitraryAxis(entity.Normal).Transpose();
    if(entity.Type!==EntityType.Arc&&entity.Type!==EntityType.Circle)throw new ArgumentException('The entity is not a Circle or Arc.','entity');
    this.Center=xy(Matrix3.Multiply(t,entity.Center));this.Radius=entity.Radius;this.StartAngle=entity.Type===EntityType.Circle?0:entity.StartAngle;
    this.EndAngle=entity.Type===EntityType.Circle?360:entity.EndAngle;this.IsCounterclockwise=true;
  }
  static ConvertFrom(entity){return new Arc(entity);}
  ConvertTo(){
    if(MathHelper.IsEqual(MathHelper.NormalizeAngle(this.StartAngle),MathHelper.NormalizeAngle(this.EndAngle)))return new Circle(this.Center,this.Radius);
    return this.IsCounterclockwise?new ArcEntity(this.Center,this.Radius,this.StartAngle,this.EndAngle):new ArcEntity(this.Center,this.Radius,360-this.EndAngle,360-this.StartAngle);
  }
  Clone(){return Object.assign(new Arc(),{Center:this.Center,Radius:this.Radius,StartAngle:this.StartAngle,EndAngle:this.EndAngle,IsCounterclockwise:this.IsCounterclockwise});}
}
class Ellipse extends Edge {
  IsCounterclockwise=false;
  constructor(entity){
    super(3);VectorField(this,'Center',Vector2.Zero);VectorField(this,'EndMajorAxis',Vector2.Zero);DoubleFields(this,['MinorRatio','StartAngle','EndAngle']);
    if(arguments.length===0)return;if(entity==null)throw new ArgumentNullException('entity');
    if(!(entity instanceof EllipseEntity))throw new ArgumentException('The entity is not an Ellipse.','entity');
    this.Center=xy(project(entity,entity.Center));const sine=mul(mul(.5,entity.MajorAxis),M.Sin(mul(entity.Rotation,MathHelper.DegToRad))),cosine=mul(mul(.5,entity.MajorAxis),M.Cos(mul(entity.Rotation,MathHelper.DegToRad)));
    this.EndMajorAxis=new Vector2(cosine,sine);this.MinorRatio=entity.MinorAxis/entity.MajorAxis;
    this.StartAngle=entity.IsFullEllipse?0:entity.StartAngle;this.EndAngle=entity.IsFullEllipse?360:entity.EndAngle;this.IsCounterclockwise=true;
  }
  static ConvertFrom(entity){return new Ellipse(entity);}
  ConvertTo(){
    const center=v3(this.Center),axis=v3(this.EndMajorAxis),p=MathHelper.Transform(axis,Vector3.UnitZ,CoordinateSystem.World,CoordinateSystem.Object);
    const rotation=mul(Vector2.Angle(xy(p)),MathHelper.RadToDeg),major=mul(2,axis.Modulus());
    return Object.assign(new EllipseEntity(center,major,mul(major,this.MinorRatio)),{Rotation:rotation,StartAngle:this.IsCounterclockwise?this.StartAngle:360-this.EndAngle,EndAngle:this.IsCounterclockwise?this.EndAngle:360-this.StartAngle});
  }
  Clone(){return Object.assign(new Ellipse(),{Center:this.Center,EndMajorAxis:this.EndMajorAxis,MinorRatio:this.MinorRatio,StartAngle:this.StartAngle,EndAngle:this.EndAngle,IsCounterclockwise:this.IsCounterclockwise});}
}
class Spline extends Edge {
  Degree=0;IsRational=false;IsPeriodic=false;Knots=null;ControlPoints=null;
  #fit=new FiniteVector2Collection('value');#start=null;#end=null;
  constructor(entity){
    super(4);if(arguments.length===0)return;if(entity==null)throw new ArgumentNullException('entity');
    if(!(entity instanceof SplineEntity))throw new ArgumentException('The entity is not a Spline.','entity');
    this.Degree=entity.Degree;this.IsRational=true;this.IsPeriodic=entity.IsClosedPeriodic;
    if(entity.ControlPoints.length===0)throw new ArgumentException('A spline edge requires control points.','entity');
    if(this.IsPeriodic)PeriodicSplineData.Validate(entity);
    const matrix=MathHelper.ArbitraryAxis(entity.Normal).Transpose(),prefix=this.IsPeriodic?this.Degree:0,points=[];
    for(let i=0;i<prefix;i++){const at=entity.ControlPoints.length-prefix+i,p=Matrix3.Multiply(matrix,entity.ControlPoints[at]);points.push(new Vector3(p.X,p.Y,entity.Weights[at]));}
    for(let i=0;i<entity.ControlPoints.length;i++){
      const p=Matrix3.Multiply(matrix,entity.ControlPoints[i]);if(this.IsPeriodic){PeriodicSplineData.Finite(p.X);PeriodicSplineData.Finite(p.Y);}points.push(new Vector3(p.X,p.Y,entity.Weights[i]));
    }
    this.ControlPoints=FixedArray(points);
    for(const p of entity.FitPoints)this.#fit.Add(xy(Matrix3.Multiply(matrix,p)));
    if(entity.StartTangent!==null)this.StartTangent=xy(Matrix3.Multiply(matrix,entity.StartTangent));
    if(entity.EndTangent!==null)this.EndTangent=xy(Matrix3.Multiply(matrix,entity.EndTangent));
    this.Knots=FixedArray(entity.Knots,v=>v);
  }
  get FitPoints(){return this.#fit;}
  get StartTangent(){return Copy(this.#start);}set StartTangent(value){if(value!=null)FiniteVector2(value,'value');this.#start=Copy(value);}
  get EndTangent(){return Copy(this.#end);}set EndTangent(value){if(value!=null)FiniteVector2(value,'value');this.#end=Copy(value);}
  static ConvertFrom(entity){return new Spline(entity);}
  ConvertTo(){
    if(this.IsPeriodic)HatchSplineData.Validate(this);
    if(this.Knots==null)throw new ArgumentNullException('collection');const knots=Array.from(this.Knots),control=Required(this.ControlPoints),points=[],weights=[];let first=0;
    if(this.IsPeriodic){
      if(!this.IsRational&&control.some(p=>p.Z!==1))throw new NotSupportedException('Nonrational periodic weights must be unit.');
      if(knots.length===control.length+this.Degree+1){
        if(control.length<2*this.Degree+1)throw new NotSupportedException('Insufficient cyclic overlap.');
        for(let i=0;i<this.Degree;i++){
          const a=control[i],b=control[control.length-this.Degree+i];if(!['X','Y','Z'].every(k=>PeriodicSplineData.Same(a[k],b[k])))throw new NotSupportedException('Periodic edge overlap must match exactly.');
        }first=this.Degree;
      }
    }
    for(let i=first;i<control.length;i++){points.push(v3(control[i]));weights.push(control[i].Z);}
    if(this.IsPeriodic)PeriodicSplineData.Validate(points,weights,this.Knots,this.Degree);
    const s=new SplineEntity(points,weights,knots,this.Degree,Array.from(this.#fit,v3),SplineCreationMethod.ControlPoints,this.IsPeriodic);
    s.StartTangent=this.#start==null?null:v3(this.#start);s.EndTangent=this.#end==null?null:v3(this.#end);return s;
  }
  Clone(){
    const copy=Object.assign(new Spline(),{Degree:this.Degree,IsRational:this.IsRational,IsPeriodic:this.IsPeriodic,Knots:FixedArray(Required(this.Knots),v=>v),ControlPoints:FixedArray(Required(this.ControlPoints)),StartTangent:this.#start,EndTangent:this.#end});
    for(const p of this.#fit)copy.#fit.Add(p);return copy;
  }
}
const fromEdges=Symbol('edge constructor');
/** Entity contours are live references; edge geometry is a detached snapshot until Update. */
export class HatchBoundaryPath {
  static EdgeType=EdgeType;static Edge=Edge;static Polyline=Polyline;static Line=Line;static Arc=Arc;static Ellipse=Ellipse;static Spline=Spline;
  #entities=new ReferenceList();#edges=new ReferenceList();#entityView=ReadOnlyReferenceList(this.#entities);#edgeView=ReadOnlyReferenceList(this.#edges);
  PathType=5;ContainingHatch=null;
  constructor(edges,mode){
    if(edges==null)throw new ArgumentNullException('edges');
    // Explicit signatures disambiguate empty/null and single-pass enumerable overloads.
    const isEdges=mode===fromEdges||(mode===undefined&&Array.isArray(edges)&&edges[0] instanceof Edge);
    if(!isEdges){this.#entities.AddRange(edges);this.Update();return;}
    for(const edge of edges){
      const count=Number.isInteger(edges.Count)?edges.Count:Array.isArray(edges)?edges.length:Array.from(edges).length;
      if(count===1&&Required(edge).Type===0){this.PathType|=2;this.#edges.Add(edge);}
      else if(Required(edge).Type===0)this.#edges.AddRange(edge.Explode());else this.#edges.Add(edge);
    }
  }
  static CreateOverload(signature,...args){
    if(signature==='System.Collections.Generic.IEnumerable<netDxf.Entities.HatchBoundaryPath.Edge>')return new HatchBoundaryPath(args[0],fromEdges);
    if(signature==='System.Collections.Generic.IEnumerable<netDxf.Entities.EntityObject>')return new HatchBoundaryPath(args[0],false);
    throw new ArgumentException('Unknown boundary constructor signature.','signature');
  }
  static FromEdges(edges){return new HatchBoundaryPath(edges,fromEdges);}
  get Edges(){return this.#edgeView;}get Entities(){return this.#entityView;}
  AddContour(entity){this.#entities.Add(entity);}ClearContour(){this.#entities.Clear();}RemoveContour(entity){return this.#entities.Remove(entity);}
  Update(){this.#setInternal(this.#entities,true);}
  #setInternal(contour,clear){
    const sources=Array.from(contour);for(const e of sources)if(e?.constructor.name==='DxfOpaqueEntity')throw new NotSupportedException('Opaque source geometry cannot be regenerated.');
    let contains=false;if(clear){this.#edges.Clear();this.PathType&=~2;}
    for(const e of sources){
      if(contains)throw new ArgumentException('Closed polylines cannot be combined with other entities.');
      switch(Required(e).Type){
        case EntityType.Arc:case EntityType.Circle:this.#edges.Add(new Arc(e));break;
        case EntityType.Line:this.#edges.Add(new Line(e));break;
        case EntityType.Ellipse:this.#edges.Add(new Ellipse(e));break;
        case EntityType.Spline:this.#edges.Add(new Spline(e));break;
        case EntityType.Polyline2D:case EntityType.Polyline3D:
          if(e.IsClosed){if(this.#edges.Count!==0)throw new ArgumentException('Closed polylines cannot be combined with other entities.');this.#edges.Add(new Polyline(e));this.PathType|=2;contains=true;}
          else this.#setInternal(e.Explode(),false);break;
        default:throw new ArgumentException('Unsupported HATCH contour entity type.');
      }
    }
  }
  Clone(){const copy=HatchBoundaryPath.FromEdges(Array.from(this.#edges,e=>e.Clone()));copy.PathType=this.PathType;return copy;}
}
