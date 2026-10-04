// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { EntityVector3 } from '../../runtime/EntityGeometry.js';
import { CurveTransformFrame, CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { ValueList } from '../../runtime/ValueList.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
export class Arc extends EntityObject {
  #center;#scalars=new DataView(new ArrayBuffer(32));
  constructor(...args){
    super(EntityType.Arc,DxfObjectCode.Arc);
    if(args.length===3&&args[0] instanceof Vector2&&args[1] instanceof Vector2){
      const a=MathHelper.ArcFromBulge(...args);if(a.Item2<=0)throw new ArgumentOutOfRangeException('radius',0);
      this.#center=new Vector3(a.Item1.X,a.Item1.Y,0);this.#scalars.setFloat64(0,a.Item2);this.#scalars.setFloat64(8,a.Item3);this.#scalars.setFloat64(16,a.Item4);
    }else{
      if(args.length!==0&&args.length!==4)throw new ArgumentException('No matching Arc constructor.');
      const [center,radius,start,end]=args.length===0?[Vector3.Zero,1,0,180]:args;this.#center=EntityVector3(center);
      if(radius<=0)throw new ArgumentOutOfRangeException('radius',radius);this.#scalars.setFloat64(0,radius);this.StartAngle=start;this.EndAngle=end;
    }
  }
  get Center(){return Copy(this.#center);}set Center(value){this.#center=Copy(value);}
  get Radius(){return this.#scalars.getFloat64(0);}set Radius(value){if(value<=0)throw new ArgumentOutOfRangeException('value',value);this.#scalars.setFloat64(0,value);}
  get StartAngle(){return this.#scalars.getFloat64(8);}set StartAngle(value){this.#scalars.setFloat64(8,MathHelper.NormalizeAngle(value));}
  get EndAngle(){return this.#scalars.getFloat64(16);}set EndAngle(value){this.#scalars.setFloat64(16,MathHelper.NormalizeAngle(value));}
  get Thickness(){return this.#scalars.getFloat64(24);}set Thickness(value){this.#scalars.setFloat64(24,value);}
  PolygonalVertexes(precision){
    RequireInteger(precision,2,2147483647,'precision');const points=new ValueList(),start=mul(this.StartAngle,MathHelper.DegToRad);let end=mul(this.EndAngle,MathHelper.DegToRad);if(end<start)end+=MathHelper.TwoPI;
    const delta=(end-start)/(precision-1);for(let i=0;i<precision;i++){const angle=start+mul(delta,i),sine=mul(this.Radius,M.Sin(angle)),cosine=mul(this.Radius,M.Cos(angle));points.Add(new Vector2(cosine,sine));}return points;
  }
  ToPolyline2D(precision){
    const vertices=this.PolygonalVertexes(precision),center=MathHelper.Transform(this.Center,this.Normal,CoordinateSystem.World,CoordinateSystem.Object);
    const p=CopyCurveAppearance(this,new Polyline2D());p.Elevation=center.Z;p.Thickness=this.Thickness;p.IsClosed=false;
    for(const v of vertices)p.Vertexes.Add(new Polyline2DVertex(v.X+center.X,v.Y+center.Y));return p;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);const frame=CurveTransformFrame(this,matrix,translation),axis=frame.direction(new Vector3(this.Radius,0,0));
    let radius=new Vector2(axis.X,axis.Y).Modulus();if(MathHelper.IsZero(radius))radius=MathHelper.Epsilon;
    const start=Vector2.Rotate(new Vector2(this.Radius,0),mul(this.StartAngle,MathHelper.DegToRad)),end=Vector2.Rotate(new Vector2(this.Radius,0),mul(this.EndAngle,MathHelper.DegToRad));
    const vs=frame.direction(new Vector3(start.X,start.Y,0)),ve=frame.direction(new Vector3(end.X,end.Y,0));
    this.Normal=frame.normal;this.Center=frame.center;this.Radius=radius;
    if(M.Sign(mul(mul(matrix.M11,matrix.M22),matrix.M33))<0){this.EndAngle=mul(Vector2.Angle(new Vector2(vs.X,vs.Y)),MathHelper.RadToDeg);this.StartAngle=mul(Vector2.Angle(new Vector2(ve.X,ve.Y)),MathHelper.RadToDeg);}
    else {this.StartAngle=mul(Vector2.Angle(new Vector2(vs.X,vs.Y)),MathHelper.RadToDeg);this.EndAngle=mul(Vector2.Angle(new Vector2(ve.X,ve.Y)),MathHelper.RadToDeg);}
  }
  Clone(){const p=this.$copyEntityAttributes(new Arc());p.Center=this.#center;p.Radius=this.Radius;p.StartAngle=this.StartAngle;p.EndAngle=this.EndAngle;p.Thickness=this.Thickness;return this.$finishEntityClone(p);}
}
