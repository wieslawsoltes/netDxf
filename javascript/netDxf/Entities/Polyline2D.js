// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { PolylineSmoothType } from './PolylineSmoothType.js';
import { Line } from './Line.js';
import { Arc } from './Arc.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ValueList } from '../../runtime/ValueList.js';
import { CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { NurbsEvaluator } from '../../runtime/NurbsEvaluator.js';
import { DotNetMath as M, MultiplyDouble as mul, Int32 } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, NullReferenceException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
import { InstallPolyline2DFidelity } from './Polyline2D.Fidelity.js';
import { InstallPolyline2DStoredRecords } from './Polyline2D.StoredRecords.js';
const nonnull=v=>{if(v==null)throw new NullReferenceException();return v;};
export class Polyline2D extends EntityObject {
  StoredHeaderTags=null;StoredHeaderIndices=new Map();StoredNormal=Vector3.Zero;StoredHeaderPublicEnd=0;HasPrivateHeader=false;
  #vertices;#flags=0;#smooth=0;#scalars=new DataView(new ArrayBuffer(16));static #splineSegs=8;
  constructor(vertices=[],isClosed=false){
    super(EntityType.Polyline2D,DxfObjectCode.LwPolyline);if(arguments.length>2)throw new ArgumentException('No matching Polyline2D constructor.');
    if(vertices==null)throw new ArgumentNullException('vertexes');this.#vertices=new ReferenceList();
    for(const v of vertices)this.#vertices.Add(v instanceof Vector2?new Polyline2DVertex(v):v);
    this.#flags=isClosed?1:0;
  }
  static get DefaultSplineSegs(){return this.#splineSegs;}static set DefaultSplineSegs(value){this.#splineSegs=RequireInteger(value,1,32767);}
  get Vertexes(){return this.#vertices;}
  get IsClosed(){return (this.#flags&1)!==0;}set IsClosed(value){this.#flags=value?this.#flags|1:this.#flags&~1;}
  get Flags(){return this.#flags;}set Flags(value){this.#flags=value;}
  get LinetypeGeneration(){return (this.#flags&128)!==0;}set LinetypeGeneration(value){this.#flags=value?this.#flags|128:this.#flags&~128;}
  get Thickness(){return this.#scalars.getFloat64(0);}set Thickness(value){this.#scalars.setFloat64(0,value);}
  get Elevation(){return this.#scalars.getFloat64(8);}set Elevation(value){this.#scalars.setFloat64(8,value);}
  get SmoothType(){return this.#smooth;}set SmoothType(value){
    if(this.HasStoredRecords&&value!==PolylineSmoothType.NoSmooth)throw new NotSupportedException('Smoothing retained records requires regeneration.');
    if(value===PolylineSmoothType.NoSmooth){this.CodeName=this.HasStoredRecords?DxfObjectCode.Polyline:DxfObjectCode.LwPolyline;this.#flags&=~4;}
    else {this.CodeName=DxfObjectCode.Polyline;this.#flags|=4;}this.#smooth=value;
  }
  Reverse(){
    this.ValidateStoredRecordGeometry();if(this.Vertexes.Count<2)return;this.Vertexes.Reverse();this.ReverseStoredRecords();
    const first=nonnull(this.Vertexes.get_Item(0)),bulge=first.Bulge,start=first.StartWidthOverride,end=first.EndWidthOverride;
    for(let i=0;i<this.Vertexes.Count-1;i++){const v=nonnull(this.Vertexes.get_Item(i)),next=nonnull(this.Vertexes.get_Item(i+1));v.Bulge=-next.Bulge;v.StartWidthOverride=next.EndWidthOverride;v.EndWidthOverride=next.StartWidthOverride;}
    const last=nonnull(this.Vertexes.get_Item(this.Vertexes.Count-1));last.Bulge=-bulge;last.StartWidthOverride=end;last.EndWidthOverride=start;
  }
  SetConstantWidth(width){
    Polyline2D.ValidateWidth(width,'width');this.ValidateStoredRecordGeometry();this.ValidateVertexFidelity();this.ConstantWidth=null;
    for(const v of this.Vertexes){v.StartWidth=width;v.EndWidth=width;}
  }
  #line(start,end){const e=CopyCurveAppearance(this,new Line());e.StartPoint=start;e.EndPoint=end;e.Thickness=this.Thickness;return e;}
  Explode(){
    const entities=new ReferenceList();
    if(this.SmoothType===PolylineSmoothType.NoSmooth){
      let i=0;for(const item of this.Vertexes){const v=nonnull(item),bulge=v.Bulge;
        if(i===this.Vertexes.Count-1&&!this.IsClosed)break;
        const p1=v.Position,p2=nonnull(this.Vertexes.get_Item((i+1)%this.Vertexes.Count)).Position;
        const world=p=>MathHelper.Transform(new Vector3(p.X,p.Y,this.Elevation),this.Normal,CoordinateSystem.Object,CoordinateSystem.World);
        if(MathHelper.IsZero(bulge))entities.Add(this.#line(world(p1),world(p2)));
        else {const data=MathHelper.ArcFromBulge(p1,p2,bulge);if(MathHelper.IsZero(data.Item2))entities.Add(this.#line(world(p1),world(p2)));
          else {const e=CopyCurveAppearance(this,new Arc());e.Center=world(data.Item1);e.Radius=data.Item2;e.StartAngle=data.Item3;e.EndAngle=data.Item4;e.Thickness=this.Thickness;entities.Add(e);}}
        i++;
      }return entities;
    }
    const matrix=MathHelper.ArbitraryAxis(this.Normal),controls=Array.from(this.Vertexes,v=>{const p=nonnull(v).Position;return Matrix3.Multiply(matrix,new Vector3(p.X,p.Y,this.Elevation));});
    const degree=this.SmoothType===PolylineSmoothType.Quadratic?2:3,segments=this.Owner===null?Polyline2D.DefaultSplineSegs:this.Owner.Record.Owner.Owner.DrawingVariables.SplineSegs;
    const precision=segments*(this.IsClosed?this.Vertexes.Count:this.Vertexes.Count-1),points=NurbsEvaluator(controls,null,null,degree,false,this.IsClosed,precision);
    for(let i=1;i<points.Count;i++)entities.Add(this.#line(points.get_Item(i-1),points.get_Item(i)));
    if(this.IsClosed)entities.Add(this.#line(points.get_Item(points.Count-1),points.get_Item(0)));return entities;
  }
  PolygonalVertexes(precision,weldThreshold=MathHelper.Epsilon,bulgeThreshold=MathHelper.Epsilon){
    RequireInteger(precision,0,2147483647,'precision');const points=new ValueList();let degree;
    if(this.SmoothType===PolylineSmoothType.Quadratic)degree=2;else if(this.SmoothType===PolylineSmoothType.Cubic)degree=3;
    else {
      let index=0;for(const item of this.Vertexes){const vertex=nonnull(item),bulge=vertex.Bulge,p1=vertex.Position;
        if(index===this.Vertexes.Count-1&&!this.IsClosed){points.Add(p1);continue;}
        const p2=nonnull(this.Vertexes.get_Item((index+1)%this.Vertexes.Count)).Position;
        if(!p1.Equals(p2,weldThreshold)){
          if(MathHelper.IsZero(bulge)||precision===0)points.Add(p1);
          else {const dist=mul(.5,Vector2.Distance(p1,p2)),data=MathHelper.ArcFromBulge(p1,p2,bulge),center=data.Item1,radius=data.Item2;
            if(dist>=bulgeThreshold||!MathHelper.IsZero(radius)){
              const arcAngle=mul(MathHelper.NormalizeAngle(data.Item4-data.Item3),MathHelper.DegToRad),arcPrecision=Int32(mul(precision,arcAngle)/MathHelper.TwoPI),angle=mul(M.Sign(bulge),arcAngle)/(arcPrecision+1);
              points.Add(p1);let previous=p1;const start=Vector2.Subtract(p1,center);
              for(let i=1;i<=arcPrecision;i++){const p=Vector2.Add(center,Vector2.Rotate(start,mul(i,angle)));if(!p.Equals(previous,weldThreshold)&&!p.Equals(p2,weldThreshold)){points.Add(p);previous=p;}}
            }else points.Add(p1);
          }
        }index++;
      }return points;
    }
    precision=Math.max(2,precision);const controls=Array.from(this.Vertexes,v=>{const p=nonnull(v).Position;return new Vector3(p.X,p.Y,0);});
    for(const p of NurbsEvaluator(controls,null,null,degree,false,this.IsClosed,precision))points.Add(new Vector2(p.X,p.Y));return points;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);if(this.HasStoredRecords){this.TransformStoredRecords(matrix,translation);return;}
    const scale=this.GetWidthTransformScale(matrix);let elevation=this.Elevation;const normal=TransformedNormal(matrix,this.Normal),ow=MathHelper.ArbitraryAxis(this.Normal),wo=MathHelper.ArbitraryAxis(normal).Transpose();
    for(const vertex of this.Vertexes){const p=vertex.Position;let v=Matrix3.Multiply(ow,new Vector3(p.X,p.Y,this.Elevation));v=Vector3.Add(Matrix3.Multiply(matrix,v),translation);v=Matrix3.Multiply(wo,v);vertex.Position=new Vector2(v.X,v.Y);elevation=v.Z;}
    this.Elevation=elevation;this.Normal=normal;if(this.ConstantWidth!==null)this.ConstantWidth=mul(this.ConstantWidth,scale);
    for(const v of this.Vertexes){if(v.StartWidthOverride!==null)v.StartWidthOverride=mul(v.StartWidthOverride,scale);if(v.EndWidthOverride!==null)v.EndWidthOverride=mul(v.EndWidthOverride,scale);}
  }
  Clone(){
    this.RejectStoredRecordClone();const p=this.$copyEntityAttributes(new Polyline2D());p.Elevation=this.Elevation;p.Thickness=this.Thickness;p.ConstantWidth=this.ConstantWidth;p.SmoothType=this.SmoothType;p.Flags=this.Flags;
    for(const v of this.Vertexes)p.Vertexes.Add(nonnull(v).Clone());for(const data of this.XData.Values)p.XData.Add(data.Clone());
    this.CopyStoredRecordsTo(p);this.CopyCommonDataTo(p);return p;
  }
}
InstallPolyline2DStoredRecords(Polyline2D);InstallPolyline2DFidelity(Polyline2D);
