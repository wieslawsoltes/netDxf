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
export class Circle extends EntityObject {
  #center;#scalars=new DataView(new ArrayBuffer(16));
  constructor(center=Vector3.Zero,radius=1){
    super(EntityType.Circle,DxfObjectCode.Circle);
    if(arguments.length!==0&&arguments.length!==2)throw new ArgumentException('No matching Circle constructor.');
    this.#center=EntityVector3(center);if(radius<=0)throw new ArgumentOutOfRangeException('radius',radius);this.#scalars.setFloat64(0,radius);
  }
  get Center(){return Copy(this.#center);}set Center(value){this.#center=Copy(value);}
  get Radius(){return this.#scalars.getFloat64(0);}set Radius(value){if(value<=0)throw new ArgumentOutOfRangeException('value',value);this.#scalars.setFloat64(0,value);}
  get Thickness(){return this.#scalars.getFloat64(8);}set Thickness(value){this.#scalars.setFloat64(8,value);}
  PolygonalVertexes(precision){
    RequireInteger(precision,2,2147483647,'precision');const points=new ValueList(),delta=MathHelper.TwoPI/precision;
    for(let i=0;i<precision;i++){const angle=mul(delta,i),sine=mul(this.Radius,M.Sin(angle)),cosine=mul(this.Radius,M.Cos(angle));points.Add(new Vector2(cosine,sine));}return points;
  }
  ToPolyline2D(precision){
    const vertices=this.PolygonalVertexes(precision),center=MathHelper.Transform(this.Center,this.Normal,CoordinateSystem.World,CoordinateSystem.Object);
    const p=CopyCurveAppearance(this,new Polyline2D());p.Elevation=center.Z;p.Thickness=this.Thickness;p.IsClosed=true;
    for(const v of vertices)p.Vertexes.Add(new Polyline2DVertex(v.X+center.X,v.Y+center.Y));return p;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);const frame=CurveTransformFrame(this,matrix,translation),axis=frame.direction(new Vector3(this.Radius,0,0));
    let radius=new Vector2(axis.X,axis.Y).Modulus();if(MathHelper.IsZero(radius))radius=MathHelper.Epsilon;
    this.Normal=frame.normal;this.Center=frame.center;this.Radius=radius;
  }
  Clone(){const p=this.$copyEntityAttributes(new Circle());p.Center=this.#center;p.Radius=this.Radius;p.Thickness=this.Thickness;return this.$finishEntityClone(p);}
}
