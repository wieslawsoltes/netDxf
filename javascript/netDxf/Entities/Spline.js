// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { SplineCreationMethod } from './SplineCreationMethod.js';
import { Polyline2D } from './Polyline2D.js';
import { Polyline3D } from './Polyline3D.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { BezierCurveQuadratic } from '../BezierCurveQuadratic.js';
import { BezierCurveCubic } from '../BezierCurveCubic.js';
import { Copy, MultiplyDouble } from '../../runtime/GeometryRuntime.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { NurbsEvaluator, CreateKnotVector } from '../../runtime/NurbsEvaluator.js';
import { CopyCurveAppearance } from '../../runtime/CurveGeometry.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidOperationException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
const select=Symbol('Spline overload');
const vectors=values=>FixedArray(values,Copy);
const numbers=values=>FixedArray(values,value=>value);
function sequence(values){if(values==null)throw new ArgumentNullException('source');return Array.from(values,Copy);}
function bezierKnots(count,degree){const size=count+degree+1,group=degree+1,factor=1/(size/group-1),knots=Array(size);let index=1;for(let i=0;i<size;){const value=i<group?0:i>=size-group?1:MultiplyDouble(factor,index++);for(let j=0;j<group;j++)knots[i++]=value;}return numbers(knots);}
export class Spline extends EntityObject {
  #controls;#weights=null;#knots=null;#fit;#degree;#method;#periodic;#start=null;#end=null;#tolerances=new DataView(new ArrayBuffer(24));
  KnotParameterization=32;
  static get MaxDegree(){return 10;}
  constructor(...args){
    const selected=args[0]===select?args[1]:null;
    super(selected?.type??EntityType.Spline,selected?.code??DxfObjectCode.Spline);
    this.#tolerances.setFloat64(0,1e-7);this.#tolerances.setFloat64(8,1e-7);this.#tolerances.setFloat64(16,1e-10);
    if(selected?.mode==='copy'){this.#copy(selected.source);return;}
    if(selected?.mode==='bezier'){this.#bezier(selected.curves,selected.degree);return;}
    if(args.length===1){
      const points=args[0];
      // Array overloads can be inferred without consuming caller iterators. Null/empty
      // and custom Bezier enumerables should select an exact signature explicitly.
      if(Array.isArray(points)&&points[0] instanceof BezierCurveQuadratic){this.#bezier(points,2);return;}
      if(Array.isArray(points)&&points[0] instanceof BezierCurveCubic){this.#bezier(points,3);return;}
      this.#bezier(BezierCurveCubic.CreateFromFitPoints(points),3);this.#method=SplineCreationMethod.FitPoints;this.#fit=vectors(sequence(points));return;
    }
    if(![2,3,4,5,7].includes(args.length))throw new ArgumentException('No matching Spline constructor.');
    const explicit=args.length===5||args.length===7;
    const [controls,weights]=args;
    let degree,periodic,knots,fit=null,method=SplineCreationMethod.ControlPoints;
    if(explicit){knots=args[2];degree=args[3];if(args.length===7){fit=args[4];method=args[5];periodic=args[6];}else periodic=args[4];}
    else {degree=typeof args[2]==='boolean'?3:args[2]??3;periodic=typeof args[2]==='boolean'?args[2]:args[3]??false;}
    this.#degree=RequireInteger(degree,1,10,'degree');
    if(controls==null){if(!explicit||method===SplineCreationMethod.ControlPoints)throw new ArgumentNullException('controlPoints');this.#controls=vectors([]);}
    else {
      this.#controls=vectors(controls);const n=this.#controls.length;
      if(n<2||n<degree+1){if(explicit)throw new ArgumentOutOfRangeException('controlPoints',n);throw new ArgumentException('Insufficient control points for the degree.');}
      this.#weights=weights==null?numbers(Array(n).fill(1)):numbers(weights);
      if(this.#weights.length!==n)throw new ArgumentException('Control and weight counts differ.','weights');
      if(explicit){if(knots==null)throw new ArgumentNullException('knots');this.#knots=numbers(knots);if(this.#knots.length!==n+(periodic?2*degree:degree)+1)throw new ArgumentException('Invalid number of knots.');}
    }
    if(!explicit)this.#knots=numbers(CreateKnotVector(this.#controls.length,this.#degree,periodic));
    if(fit==null){if(explicit&&method===SplineCreationMethod.FitPoints)throw new ArgumentNullException('fitPoints');this.#fit=vectors([]);}else this.#fit=vectors(fit);
    this.#method=method;this.#periodic=periodic;
  }
  #bezier(curves,degree){
    if(curves==null)throw new ArgumentNullException('curves');const controls=[];
    for(const curve of curves){if(curve==null)throw new ArgumentException('Curve sequences cannot contain null elements.','curves');for(const p of curve.ControlPoints)controls.push(Copy(p));}
    if(controls.length===0)throw new ArgumentException('At least one Bezier curve is required.','curves');
    this.#controls=vectors(controls);this.#weights=numbers(Array(controls.length).fill(1));this.#degree=degree;this.#periodic=false;this.#fit=vectors([]);this.#method=SplineCreationMethod.ControlPoints;this.#knots=bezierKnots(controls.length,degree);
  }
  #copy(s){
    if(s==null)throw new ArgumentNullException('source');
    this.#controls=s.#controls.Clone();this.#weights=s.#weights===null?null:s.#weights.Clone();this.#knots=s.#knots===null?null:s.#knots.Clone();this.#fit=s.#fit.Clone();
    this.#degree=s.#degree;this.#method=s.#method;this.#periodic=s.#periodic;this.KnotParameterization=s.KnotParameterization;
    for(let i=0;i<24;i+=8)this.#tolerances.setFloat64(i,s.#tolerances.getFloat64(i));this.#start=Copy(s.#start);this.#end=Copy(s.#end);
    CopyCurveAppearance(s,this);this.IsVisible=s.IsVisible;s.CopyCommonDataTo(this);for(const d of s.XData.Values)this.XData.Add(d.Clone());
  }
  static CreateOverload(signature,...args){
    if(signature==='System.Collections.Generic.IEnumerable<netDxf.BezierCurveQuadratic>')return new Spline(select,{mode:'bezier',degree:2,curves:args[0]});
    if(signature==='System.Collections.Generic.IEnumerable<netDxf.BezierCurveCubic>')return new Spline(select,{mode:'bezier',degree:3,curves:args[0]});
    if(signature==='netDxf.Entities.Spline')return new Spline(select,{mode:'copy',source:args[0]});
    if(signature==='netDxf.Entities.Spline,netDxf.Entities.EntityType,string')return new Spline(select,{mode:'copy',source:args[0],type:args[1],code:args[2]});
    const seq='System.Collections.Generic.IEnumerable<';
    const valid=[seq+'netDxf.Vector3>',seq+'netDxf.Vector3>,'+seq+'double>',seq+'netDxf.Vector3>,'+seq+'double>,short',seq+'netDxf.Vector3>,'+seq+'double>,bool',seq+'netDxf.Vector3>,'+seq+'double>,short,bool',seq+'netDxf.Vector3>,'+seq+'double>,'+seq+'double>,short,bool',seq+'netDxf.Vector3>,'+seq+'double>,'+seq+'double>,short,'+seq+'netDxf.Vector3>,netDxf.Entities.SplineCreationMethod,bool'];
    if(!valid.includes(signature))throw new ArgumentException('Unknown constructor signature.','signature');return new Spline(...args);
  }
  // Reusable protected-constructor arguments for derived spline entities such as HELIX.
  static CopyConstructor(source,type=EntityType.Spline,code=DxfObjectCode.Spline){return [select,{mode:'copy',source,type,code}];}
  get FitPoints(){return this.#fit;}get ControlPoints(){return this.#controls;}get Weights(){return this.#weights;}get Knots(){return this.#knots;}
  get CreationMethod(){return this.#method;}get Degree(){return this.#degree;}
  get StartTangent(){return Copy(this.#start);}set StartTangent(v){this.#start=Copy(v);}get EndTangent(){return Copy(this.#end);}set EndTangent(v){this.#end=Copy(v);}
  get KnotTolerance(){return this.#tolerances.getFloat64(0);}set KnotTolerance(v){this.#setTolerance(0,v);}
  get CtrlPointTolerance(){return this.#tolerances.getFloat64(8);}set CtrlPointTolerance(v){this.#setTolerance(8,v);}
  get FitTolerance(){return this.#tolerances.getFloat64(16);}set FitTolerance(v){this.#setTolerance(16,v);}
  #setTolerance(offset,v){if(v<=0)throw new ArgumentOutOfRangeException('value',v);this.#tolerances.setFloat64(offset,v);}
  get IsClosed(){return this.#controls[0].Equals(this.#controls[this.#controls.length-1]);}
  get IsClosedPeriodic(){return this.#periodic;}set IsClosedPeriodic(v){const knots=numbers(CreateKnotVector(this.#controls.length,this.#degree,v));this.#knots=knots;this.#periodic=v;}
  Reverse(){
    let reversed=null;const knots=this.#knots;
    if(knots!==null){const a=knots[this.#degree],b=knots[knots.length-this.#degree-1];reversed=new Array(knots.length);
      for(let i=0;i<knots.length;i++){const k=knots[i];if(!Number.isFinite(k)||(i!==0&&k<knots[i-1]))throw new InvalidOperationException('Spline reversal requires finite nondecreasing knots.');
        const value=k===a?b:k===b?a:(a>=0)!==(b>=0)?(a+b)-k:(a>=0)===(k>=0)?(a-k)+b:(b>=0)===(k>=0)?(b-k)+a:(a+b)-k;
        if(!Number.isFinite(value))throw new InvalidOperationException('Reversed spline knots exceed the finite double range.');reversed[knots.length-1-i]=value;}
    }
    this.#fit.reverse();this.#controls.reverse();if(this.#weights!==null)this.#weights.reverse();
    if(this.#periodic){const rotate=a=>{const p=this.#degree;if(p>a.length)throw new ArgumentException('Invalid offset and length.');const values=Array.from(a);for(let i=0;i<a.length;i++)a[i]=values[(i+p)%a.length];};rotate(this.#controls);if(this.#weights!==null)rotate(this.#weights);}
    if(reversed!==null)for(let i=0;i<knots.length;i++)knots[i]=reversed[i];
    const start=this.#start;this.#start=this.#end===null?null:Vector3.Negate(this.#end);this.#end=start===null?null:Vector3.Negate(start);
  }
  SetUniformWeights(v){if(this.#weights===null)throw new NullReferenceException();for(let i=0;i<this.#weights.length;i++)this.#weights[i]=v;}
  PolygonalVertexes(precision){return NurbsEvaluator(sequence(this.#controls),sequence(this.#weights),this.#knots,this.#degree,this.IsClosed,this.#periodic,precision);}
  ToPolyline3D(precision){const points=this.PolygonalVertexes(precision),closed=this.IsClosed||this.IsClosedPeriodic,p=CopyCurveAppearance(this,new Polyline3D(points));p.IsClosed=closed;return p;}
  ToPolyline2D(precision){const points=MathHelper.Transform(this.PolygonalVertexes(precision),this.Normal,{value:0}),closed=this.IsClosed||this.IsClosedPeriodic,p=CopyCurveAppearance(this,new Polyline2D(points));p.IsClosed=closed;return p;}
  TransformBy(matrix,translation){
    [matrix,translation]=this.$transformArguments(matrix,translation);
    for(let i=0;i<this.#controls.length;i++)this.#controls[i]=Vector3.Add(Matrix3.Multiply(matrix,this.#controls[i]),translation);
    for(let i=0;i<this.#fit.length;i++)this.#fit[i]=Vector3.Add(Matrix3.Multiply(matrix,this.#fit[i]),translation);
    if(this.#start!==null)this.#start=Matrix3.Multiply(matrix,this.#start);if(this.#end!==null)this.#end=Matrix3.Multiply(matrix,this.#end);this.Normal=TransformedNormal(matrix,this.Normal);
  }
  Clone(){return new Spline(...Spline.CopyConstructor(this));}
  static NurbsEvaluator(...args){return NurbsEvaluator(...args);}static CreateKnotVector(...args){return numbers(CreateKnotVector(...args));}
  static ÇreateBezierKnotVector(count,degree){return bezierKnots(count,degree);}
}
