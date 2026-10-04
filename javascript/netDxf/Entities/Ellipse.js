// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline2DVertex } from './Polyline2DVertex.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { CoordinateSystem } from '../CoordinateSystem.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { Copy, DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ValueList } from '../../runtime/ValueList.js';
import { EntityVector3, TransformedNormal } from '../../runtime/EntityGeometry.js';
import { CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';

function line(p,q){return [p.Y-q.Y,q.X-p.X,mul(p.X,q.Y)-mul(q.X,p.Y)];}
function product(a,b){return [mul(a[0],b[0]),mul(a[0],b[1])+mul(a[1],b[0]),mul(a[1],b[1]),mul(a[0],b[2])+mul(a[2],b[0]),mul(a[1],b[2])+mul(a[2],b[1]),mul(a[2],b[2])];}
function polynomial(c,p){return mul(mul(c[0],p.X),p.X)+mul(mul(c[1],p.X),p.Y)+mul(mul(c[2],p.Y),p.Y)+mul(c[3],p.X)+mul(c[4],p.Y)+c[5];}
function properties(p1,p2,p3,p4,p5){
  const ab=product(line(p1,p2),line(p3,p4)),gd=product(line(p1,p3),line(p2,p4)),sum1=polynomial(ab,p5),sum2=polynomial(gd,p5);
  if(MathHelper.IsZero(sum2))return null;
  const lambda=-sum1/sum2;if(Number.isNaN(lambda))return null;
  const [a,b,c,d,e,f]=ab.map((v,i)=>v+mul(lambda,gd[i])),q=mul(b,b)-mul(mul(4,a),c);if(q>=0)return null;
  const m=M.Sqrt(mul(a-c,a-c)+mul(b,b)),n=mul(2,mul(mul(a,e),e)+mul(mul(c,d),d)-mul(mul(b,d),e)+mul(q,f));
  const axis1=-M.Sqrt(mul(n,a+c+m))/q,axis2=-M.Sqrt(mul(n,a+c-m))/q;
  let rotation=MathHelper.IsZero(b)?(MathHelper.IsEqual(a,c)?0:a<c?0:MathHelper.HalfPI):M.Atan((c-a-M.Sqrt(mul(a-c,a-c)+mul(b,b)))/b);
  if(axis1>=axis2)return {major:axis1,minor:axis2,rotation};return {major:axis2,minor:axis1,rotation:rotation+MathHelper.HalfPI};
}
/** Full detached ellipse model; private conic reconstruction keeps source operation order. */
export class Ellipse extends EntityObject {
  #center;#scalars=new DataView(new ArrayBuffer(48));
  constructor(center=Vector3.Zero,majorAxis=1,minorAxis=.5){
    super(EntityType.Ellipse,DxfObjectCode.Ellipse);if(arguments.length!==0&&arguments.length!==3)throw new ArgumentException('No matching Ellipse constructor.');
    this.#center=EntityVector3(center);
    if(majorAxis<=0)throw new ArgumentOutOfRangeException('majorAxis',majorAxis);if(minorAxis<=0)throw new ArgumentOutOfRangeException('minorAxis',minorAxis);
    if(minorAxis>majorAxis)throw new ArgumentException('The major axis must be greater than the minor axis.');
    this.#scalars.setFloat64(0,majorAxis);this.#scalars.setFloat64(8,minorAxis);
  }
  get Center(){return Copy(this.#center);}set Center(value){this.#center=Copy(value);}
  get MajorAxis(){return this.#scalars.getFloat64(0);}get MinorAxis(){return this.#scalars.getFloat64(8);}
  get Rotation(){return this.#scalars.getFloat64(16);}set Rotation(value){this.#scalars.setFloat64(16,MathHelper.NormalizeAngle(value));}
  get StartAngle(){return this.#scalars.getFloat64(24);}set StartAngle(value){this.#scalars.setFloat64(24,MathHelper.NormalizeAngle(value));}
  get EndAngle(){return this.#scalars.getFloat64(32);}set EndAngle(value){this.#scalars.setFloat64(32,MathHelper.NormalizeAngle(value));}
  get Thickness(){return this.#scalars.getFloat64(40);}set Thickness(value){this.#scalars.setFloat64(40,value);}
  get IsFullEllipse(){return MathHelper.IsEqual(this.StartAngle,this.EndAngle);}
  SetAxis(axis1,axis2){
    if(axis1<=0)throw new ArgumentOutOfRangeException('axis1',axis1);if(axis2<=0)throw new ArgumentOutOfRangeException('axis2',axis2);
    if(axis2>axis1){this.#scalars.setFloat64(0,axis2);this.#scalars.setFloat64(8,axis1);}else {this.#scalars.setFloat64(0,axis1);this.#scalars.setFloat64(8,axis2);}
  }
  PolarCoordinateRelativeToCenter(angle){
    const a=mul(this.MajorAxis,.5),b=mul(this.MinorAxis,.5),radians=mul(angle,MathHelper.DegToRad),a1=mul(a,M.Sin(radians)),b1=mul(b,M.Cos(radians));
    const r=mul(a,b)/M.Sqrt(mul(b1,b1)+mul(a1,a1));return new Vector2(mul(r,M.Cos(radians)),mul(r,M.Sin(radians)));
  }
  PolygonalVertexes(precision){
    RequireInteger(precision,2,2147483647,'precision');const points=new ValueList(),beta=mul(this.Rotation,MathHelper.DegToRad),sb=M.Sin(beta),cb=M.Cos(beta);let start,end,steps;
    if(this.IsFullEllipse){start=0;end=MathHelper.TwoPI;steps=precision;}
    else {const sp=this.PolarCoordinateRelativeToCenter(this.StartAngle),ep=this.PolarCoordinateRelativeToCenter(this.EndAngle),a=1/mul(.5,this.MajorAxis),b=1/mul(.5,this.MinorAxis);
      start=M.Atan2(mul(sp.Y,b),mul(sp.X,a));end=M.Atan2(mul(ep.Y,b),mul(ep.X,a));if(end<start)end+=MathHelper.TwoPI;steps=precision-1;}
    const delta=(end-start)/steps;
    for(let i=0;i<precision;i++){
      const angle=start+mul(delta,i),sa=M.Sin(angle),ca=M.Cos(angle);
      points.Add(new Vector2(mul(.5,mul(mul(this.MajorAxis,ca),cb)-mul(mul(this.MinorAxis,sa),sb)),mul(.5,mul(mul(this.MajorAxis,ca),sb)+mul(mul(this.MinorAxis,sa),cb))));
    }return points;
  }
  ToPolyline2D(precision){
    const vertices=this.PolygonalVertexes(precision),center=MathHelper.Transform(this.Center,this.Normal,CoordinateSystem.World,CoordinateSystem.Object),p=CopyCurveAppearance(this,new Polyline2D());
    p.Elevation=center.Z;p.Thickness=this.Thickness;p.IsClosed=this.IsFullEllipse;for(const v of vertices)p.Vertexes.Add(new Polyline2DVertex(v.X+center.X,v.Y+center.Y));return p;
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    const a=mul(this.MajorAxis,.5),b=mul(this.MinorAxis,.5),rotation=mul(this.Rotation,MathHelper.DegToRad);
    const rotated=MathHelper.Transform([new Vector2(-a,b),new Vector2(a,b),new Vector2(-a,-b),new Vector2(a,-b)],rotation,CoordinateSystem.Object,CoordinateSystem.World);
    const world=MathHelper.Transform(Array.from(rotated,p=>new Vector3(p.X,p.Y,0)),this.Normal,CoordinateSystem.Object,CoordinateSystem.World);
    const transformed=Array.from(world,p=>Vector3.Add(Matrix3.Multiply(matrix,Vector3.Add(p,this.Center)),translation)),normal=TransformedNormal(matrix,this.Normal);
    const rect=MathHelper.Transform(transformed,normal,CoordinateSystem.World,CoordinateSystem.Object),[A,B,C,D]=Array.from(rect,p=>new Vector2(p.X,p.Y));
    const P=Vector2.MidPoint(A,B),N=Vector2.MidPoint(C,D),H=Vector2.MidPoint(A,C),K=Vector2.MidPoint(B,D),origin=Vector2.MidPoint(H,K),X=Vector2.MidPoint(H,origin);
    const Y=MathHelper.FindIntersection(A,Vector2.Subtract(C,A),X,Vector2.Subtract(C,B));
    // Debug.Assert diagnostics are not browser UI prompts; the source's return is retained.
    if(Vector2.IsNaN(Y))return;
    const Z=MathHelper.FindIntersection(P,Vector2.Subtract(X,P),N,Vector2.Subtract(Y,N));if(Vector2.IsNaN(Z))return;
    const oldNormal=this.Normal,oldRotation=rotation,conic=properties(P,N,H,K,Z);if(conic===null)return;
    let major=mul(2,conic.major),minor=mul(2,conic.minor);major=MathHelper.IsZero(major)?MathHelper.Epsilon:major;minor=MathHelper.IsZero(minor)?MathHelper.Epsilon:minor;
    this.Center=Vector3.Add(Matrix3.Multiply(matrix,this.Center),translation);this.SetAxis(major,minor);this.Rotation=mul(conic.rotation,MathHelper.RadToDeg);this.Normal=normal;
    if(this.IsFullEllipse)return;
    // Like the source, endpoints are sampled after the axes have been replaced.
    const endpoint=angle=>{
      const p=Vector2.Rotate(this.PolarCoordinateRelativeToCenter(angle),oldRotation);
      const w=MathHelper.Transform(new Vector3(p.X,p.Y,0),oldNormal,CoordinateSystem.Object,CoordinateSystem.World);
      const o=MathHelper.Transform(Matrix3.Multiply(matrix,w),normal,CoordinateSystem.World,CoordinateSystem.Object);
      return Vector2.Rotate(new Vector2(o.X,o.Y),-conic.rotation);
    };
    const start=endpoint(this.StartAngle),end=endpoint(this.EndAngle);
    if(M.Sign(mul(mul(matrix.M11,matrix.M22),matrix.M33))<0){this.EndAngle=mul(Vector2.Angle(start),MathHelper.RadToDeg);this.StartAngle=mul(Vector2.Angle(end),MathHelper.RadToDeg);}
    else {this.StartAngle=mul(Vector2.Angle(start),MathHelper.RadToDeg);this.EndAngle=mul(Vector2.Angle(end),MathHelper.RadToDeg);}
  }
  Clone(){
    const p=this.$copyEntityAttributes(new Ellipse());p.Center=this.#center;p.#scalars.setFloat64(0,this.MajorAxis);p.#scalars.setFloat64(8,this.MinorAxis);
    p.Rotation=this.Rotation;p.StartAngle=this.StartAngle;p.EndAngle=this.EndAngle;p.Thickness=this.Thickness;return this.$finishEntityClone(p);
  }
}
