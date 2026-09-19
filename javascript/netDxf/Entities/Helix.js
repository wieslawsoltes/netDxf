// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Spline } from './Spline.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { Copy, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
import { InstallHelixGeometry } from './Helix.Geometry.js';

export const HelixConstraint = Object.freeze({ TurnHeight:0, Turns:1, Height:2 });
const cloneToken = Symbol('copy HELIX parameters');
/** The spline payload and helix parameters are independent stored representations. */
export class Helix extends Spline {
  #major=29; #maintenance=63; #base=Vector3.Zero; #start=Vector3.UnitX; #axis=Vector3.UnitZ;
  #scalars=new DataView(new ArrayBuffer(24)); #constraint=HelixConstraint.TurnHeight;
  IsRightHanded=true;
  constructor(spline, token) {
    super(...Spline.CopyConstructor(spline,EntityType.Helix,DxfObjectCode.Helix));
    this.#scalars.setFloat64(0,1); this.#scalars.setFloat64(8,1); this.#scalars.setFloat64(16,1);
    if(token===cloneToken) {
      this.#major=spline.#major; this.#maintenance=spline.#maintenance;
      this.#base=Copy(spline.#base); this.#start=Copy(spline.#start); this.#axis=Copy(spline.#axis);
      for(let i=0;i<24;i+=8)this.#scalars.setFloat64(i,spline.#scalars.getFloat64(i));
      this.#constraint=spline.#constraint; this.IsRightHanded=spline.IsRightHanded;
    }
  }
  static CreateOverload(signature,...args) {
    if(signature==='netDxf.Entities.Spline')return new Helix(args[0]);
    throw new ArgumentException('Unknown HELIX constructor signature.','signature');
  }
  get MajorReleaseNumber(){return this.#major;} set MajorReleaseNumber(v){this.#major=RequireInteger(v,0,2147483647);}
  get MaintenanceReleaseNumber(){return this.#maintenance;} set MaintenanceReleaseNumber(v){this.#maintenance=RequireInteger(v,0,2147483647);}
  get AxisBasePoint(){return Copy(this.#base);} set AxisBasePoint(v){Helix.ValidateFinite(v,'value');this.#base=Copy(v);}
  get StartPoint(){return Copy(this.#start);} set StartPoint(v){Helix.ValidateFinite(v,'value');this.#start=Copy(v);}
  get AxisVector(){return Copy(this.#axis);} set AxisVector(v){
    Helix.ValidateFinite(v,'value');if(v.X===0&&v.Y===0&&v.Z===0)throw new ArgumentOutOfRangeException('value');this.#axis=Copy(v);
  }
  get Radius(){return this.#scalars.getFloat64(0);} set Radius(v){Helix.ValidateFinite(v,'value');if(v<0)throw new ArgumentOutOfRangeException('value');this.#scalars.setFloat64(0,v);}
  get Turns(){return this.#scalars.getFloat64(8);} set Turns(v){Helix.ValidateFinite(v,'value');if(v<=0)throw new ArgumentOutOfRangeException('value');this.#scalars.setFloat64(8,v);}
  get TurnHeight(){return this.#scalars.getFloat64(16);} set TurnHeight(v){Helix.ValidateFinite(v,'value');this.#scalars.setFloat64(16,v);}
  get Constraint(){return this.#constraint;} set Constraint(v){this.#constraint=RequireInteger(v,0,2);}
  ToSpline(){return Spline.prototype.Clone.call(this);}
  Clone(){return new Helix(this,cloneToken);}
  TransformBy(matrix,translation) {
    [matrix,translation]=this.$transformArguments(matrix,translation);
    const output={value:0};
    if(!Helix.TryGetSimilarityScale(matrix,output))throw new NotSupportedException('HELIX parameters require a nonsingular similarity; use ToSpline for general affine transformations.');
    const scale=output.value;Helix.ValidateFinite(translation,'translation');
    const axisBase=Vector3.Add(Matrix3.Multiply(matrix,this.#base),translation),start=Vector3.Add(Matrix3.Multiply(matrix,this.#start),translation);
    const axis=Matrix3.Multiply(matrix,this.#axis),normal=Matrix3.Multiply(matrix,this.Normal),normalLength=Helix.StableLength(normal);
    const radius=mul(this.Radius,scale),height=mul(this.TurnHeight,scale);
    for(const v of [axisBase,start,axis,radius,height])Helix.ValidateFinite(v,'transformation');
    if((axis.X===0&&axis.Y===0&&axis.Z===0)||!Number.isFinite(normalLength)||normalLength===0)throw new ArgumentOutOfRangeException('transformation');
    // Validate all transformed fields before the inherited mutable arrays are touched.
    for(const p of this.ControlPoints)Helix.ValidateFinite(Vector3.Add(Matrix3.Multiply(matrix,p),translation),'transformation');
    for(const p of this.FitPoints)Helix.ValidateFinite(Vector3.Add(Matrix3.Multiply(matrix,p),translation),'transformation');
    if(this.StartTangent!==null)Helix.ValidateFinite(Matrix3.Multiply(matrix,this.StartTangent),'transformation');
    if(this.EndTangent!==null)Helix.ValidateFinite(Matrix3.Multiply(matrix,this.EndTangent),'transformation');
    const x=Vector3.Divide(Matrix3.Multiply(matrix,Vector3.UnitX),scale),y=Vector3.Divide(Matrix3.Multiply(matrix,Vector3.UnitY),scale),z=Vector3.Divide(Matrix3.Multiply(matrix,Vector3.UnitZ),scale);
    const reflected=Vector3.DotProduct(Vector3.CrossProduct(x,y),z)<0;
    super.TransformBy(matrix,translation);this.Normal=Vector3.Divide(normal,normalLength);
    this.#base=axisBase;this.#start=start;this.#axis=axis;this.#scalars.setFloat64(0,radius);this.#scalars.setFloat64(16,height);
    if(reflected)this.IsRightHanded=!this.IsRightHanded;
  }
  static TryGetSimilarityScale(matrix,output) {
    let x=Matrix3.Multiply(matrix,Vector3.UnitX),y=Matrix3.Multiply(matrix,Vector3.UnitY),z=Matrix3.Multiply(matrix,Vector3.UnitZ);
    const scale=Helix.StableLength(x),sy=Helix.StableLength(y),sz=Helix.StableLength(z);output.value=scale;
    if(!Number.isFinite(scale)||scale===0||!Number.isFinite(sy)||!Number.isFinite(sz))return false;
    x=Vector3.Divide(x,scale);y=Vector3.Divide(y,scale);z=Vector3.Divide(z,scale);
    return Math.abs(sy/scale-1)<=1e-10&&Math.abs(sz/scale-1)<=1e-10&&Math.abs(Vector3.DotProduct(x,y))<=1e-10&&Math.abs(Vector3.DotProduct(x,z))<=1e-10&&Math.abs(Vector3.DotProduct(y,z))<=1e-10;
  }
  static StableLength(v) {
    const max=Math.max(Math.abs(v.X),Math.max(Math.abs(v.Y),Math.abs(v.Z)));if(max===0)return 0;
    // Direct divisions are necessary for subnormal and extreme axes.
    const x=v.X/max,y=v.Y/max,z=v.Z/max;return mul(max,Math.sqrt(mul(x,x)+mul(y,y)+mul(z,z)));
  }
  static IsFinite(v){return Number.isFinite(v);}
  static ValidateFinite(v,parameter) {
    if(v instanceof Vector3){Helix.ValidateFinite(v.X,parameter);Helix.ValidateFinite(v.Y,parameter);Helix.ValidateFinite(v.Z,parameter);return;}
    if(!Number.isFinite(v))throw new ArgumentOutOfRangeException(parameter);
  }
}
InstallHelixGeometry(Helix);
