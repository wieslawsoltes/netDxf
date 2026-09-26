// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { Mesh } from './Mesh.js';
import { Face3D } from './Face3D.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { BasisFunctionInput } from '../GTE/BasisFunction.js';
import { BSplineSurface } from '../GTE/BSplineSurface.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ValueList } from '../../runtime/ValueList.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,RequireInteger } from '../../runtime/Errors.js';
import { InstallPolygonMeshStoredRecords } from './PolygonMesh.StoredRecords.js';
/** U-major grid. Generated samples and face conversion retain the pinned source's
 * distinct default-density behavior and last-row/last-column seam rules. */
export class PolygonMesh extends EntityObject {
  #u;#v;#vertices;#densityU=0;#densityV=0;#smooth=0;#flags=16;
  static #surfU=6;static #surfV=6;
  constructor(u,v,vertexes){
    super(EntityType.PolygonMesh,DxfObjectCode.Polyline);
    if(u<2||u>256)throw new ArgumentOutOfRangeException('u',0);this.#u=RequireInteger(u,2,256,'u');
    if(v<2||v>256)throw new ArgumentOutOfRangeException('v',0);this.#v=RequireInteger(v,2,256,'v');
    if(vertexes==null)throw new ArgumentNullException('vertexes');this.#vertices=FixedArray(vertexes);
    if(this.#vertices.length!==u*v)throw new ArgumentException('The number of vertexes must equal UxV.','vertexes');
  }
  get Vertexes(){return this.#vertices;}get U(){return this.#u;}get V(){return this.#v;}
  #index(i0,i1){return i0>=0&&i0<this.#u&&i1>=0&&i1<this.#v?i0+this.#u*i1:-1;}
  GetVertex(i0,i1){return this.#vertices.get_Item(Math.max(0,this.#index(i0,i1)));}
  SetVertex(i0,i1,vertex){const i=this.#index(i0,i1);if(i>=0)this.#vertices[i]=vertex;}
  get DensityU(){return this.#densityU;}set DensityU(v){this.#densityU=RequireInteger(v,3,201);}
  get DensityV(){return this.#densityV;}set DensityV(v){this.#densityV=RequireInteger(v,3,201);}
  get IsClosedInU(){return (this.#flags&1)!==0;}set IsClosedInU(v){this.#flags=v?this.#flags|1:this.#flags&~1;}
  get IsClosedInV(){return (this.#flags&32)!==0;}set IsClosedInV(v){this.#flags=v?this.#flags|32:this.#flags&~32;}
  get Flags(){return this.#flags;}set Flags(v){this.#flags=v;}
  get SmoothType(){return this.#smooth;}set SmoothType(v){
    if(v!==0&&v!==5&&v!==6)throw new ArgumentOutOfRangeException('value',v);
    this.#flags=v===0?this.#flags&~4:this.#flags|4;this.#smooth=v;
  }
  static get DefaultSurfU(){return this.#surfU;}static set DefaultSurfU(v){this.#surfU=RequireInteger(v,0,200);}
  static get DefaultSurfV(){return this.#surfV;}static set DefaultSurfV(v){this.#surfV=RequireInteger(v,0,200);}
  ValidateSurface(){
    const d=this.#smooth===5?2:this.#smooth===6?3:0;
    if(d===0&&this.#smooth!==0)throw new InvalidOperationException('Unsupported POLYGONMESH surface type.');
    if(d!==0&&(this.#u<d+(this.IsClosedInU?0:1)||this.#v<d+(this.IsClosedInV?0:1)))throw new InvalidOperationException('Too few polygon mesh control vertices for degree and closure.');
    for(const p of this.#vertices)if(![p.X,p.Y,p.Z].every(Number.isFinite))throw new InvalidOperationException('POLYGONMESH requires finite vertex coordinates.');
  }
  #precision(){return [this.#densityU===0?(this.Owner===null?PolygonMesh.DefaultSurfU:this.Owner.Record.Owner.Owner.DrawingVariables.SurfU)+1:this.#densityU,
    this.#densityV===0?(this.Owner===null?PolygonMesh.DefaultSurfV:this.Owner.Record.Owner.Owner.DrawingVariables.SurfV)+1:this.#densityV];}
  MeshVertexes(precisionU,precisionV){
    if(arguments.length===0){[precisionU,precisionV]=this.#precision();precisionU=Math.max(3,precisionU);precisionV=Math.max(3,precisionV);}
    RequireInteger(precisionU,3,2147483647,'precisionU');RequireInteger(precisionV,3,2147483647,'precisionV');
    this.ValidateSurface();const d=this.#smooth===5?2:this.#smooth===6?3:0;if(d===0)return new ValueList(this.#vertices);
    const nu=this.#u+(this.IsClosedInU?d:0),nv=this.#v+(this.IsClosedInV?d:0),controls=[];
    for(let row=0;row<nv;row++)for(let col=0;col<nu;col++)controls.push(this.#vertices[(col%this.#u)+this.#u*(row%this.#v)]);
    const surface=new BSplineSurface(new BasisFunctionInput(nu,d),new BasisFunctionInput(nv,d),controls);
    if(this.IsClosedInU){const b=surface.BasisFunction(0),factor=1/this.#u;for(let i=0;i<b.NumKnots;i++)b.Knots[i]=mul(i-b.Degree,factor);}
    if(this.IsClosedInV){const b=surface.BasisFunction(1),factor=1/this.#v;for(let i=0;i<b.NumKnots;i++)b.Knots[i]=mul(i-b.Degree,factor);}
    const stepU=1/(this.IsClosedInU?precisionU:precisionU-1),stepV=1/(this.IsClosedInV?precisionV:precisionV-1),capacity=Math.imul(precisionU,precisionV);
    if(capacity<0)throw new ArgumentOutOfRangeException('capacity',capacity);
    const points=new ValueList();let u=0,v=0;
    for(let row=0;row<precisionV;row++){for(let col=0;col<precisionU;col++){points.Add(surface.GetPosition(u,v));u+=stepU;}v+=stepV;u=0;}
    return points;
  }
  #faces(u,v){
    const faces=[];
    for(let row=0;row<v;row++)for(let col=0;col<u;col++){
      if(col===u-1){if(this.IsClosedInU&&row<v-1)faces.push([row*u+col,row*u,(row+1)*u,(row+1)*u+col]);continue;}
      if(row===v-1){if(this.IsClosedInV&&col<u-1)faces.push([row*u+col,row*u+col+1,col+1,col]);continue;}
      const a=row*u+col,b=a+1,c=(row+1)*u+col;faces.push([a,b,c+1,c]);
    }return faces;
  }
  ToMesh(precisionU,precisionV){
    if(arguments.length===0)[precisionU,precisionV]=this.#precision();
    const points=this.MeshVertexes(precisionU,precisionV),u=this.#smooth===0?this.#u:precisionU,v=this.#smooth===0?this.#v:precisionV;
    return new Mesh(points,this.#faces(u,v));
  }
  Explode(){
    const points=this.MeshVertexes(),u=this.#smooth===0?this.#u:this.#densityU,v=this.#smooth===0?this.#v:this.#densityV;
    return new ReferenceList(this.#faces(u,v).map(f=>new Face3D(...f.map(i=>points.get_Item(i)))));
  }
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    for(let i=0;i<this.#vertices.length;i++)this.#vertices[i]=Vector3.Add(Matrix3.Multiply(matrix,this.#vertices[i]),translation);
    this.Normal=TransformedNormal(matrix,this.Normal);
  }
  Clone(){
    this.RejectStoredRecordClone();const p=CopyCurveAppearance(this,new PolygonMesh(this.#u,this.#v,this.#vertices));p.IsVisible=this.IsVisible;
    p.#densityU=this.#densityU;p.#densityV=this.#densityV;p.#smooth=this.#smooth;p.#flags=this.#flags;
    for(const data of this.XData.Values)p.XData.Add(data.Clone());this.CopyCommonDataTo(p);this.CopyStoredRecordsTo(p);return p;
  }
}
InstallPolygonMeshStoredRecords(PolygonMesh);
