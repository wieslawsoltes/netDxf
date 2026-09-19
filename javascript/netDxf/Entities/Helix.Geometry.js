// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Spline } from './Spline.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { MathHelper } from '../MathHelper.js';
import { BezierCurveCubic } from '../BezierCurveCubic.js';
import { DotNetMath as M, MultiplyDouble as mul } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
const bits=new DataView(new ArrayBuffer(8));
const divide=(v,s)=>new Vector3(v.X/s,v.Y/s,v.Z/s);
export function InstallHelixGeometry(Helix) {
  const valid=(v,p)=>Helix.ValidateFinite(v,p),length=v=>Helix.StableLength(v);
  function frame(axisBase,start,axis) {
    valid(axisBase,'axisBase');valid(start,'start');valid(axis,'axis');
    const magnitude=length(axis);if(!Number.isFinite(magnitude)||magnitude===0)throw new ArgumentOutOfRangeException('axis');
    const n=divide(axis,magnitude);let radial=Vector3.Subtract(start,axisBase);valid(radial,'start');
    let r=length(radial);const axial=Vector3.DotProduct(radial,n);
    if(!Number.isFinite(r)||Math.abs(axial)>mul(1e-10,r))throw new ArgumentException('HELIX start must lie in the perpendicular plane through the axis base.','start');
    radial=Vector3.Subtract(radial,Vector3.Multiply(n,axial));r=length(radial);
    const u=r===0?Matrix3.Multiply(MathHelper.ArbitraryAxis(n),Vector3.UnitX):divide(radial,r);
    return {Axis:n,Radial:u,Perpendicular:Vector3.CrossProduct(n,u),Radius:r};
  }
  function phase(f,hint) {
    valid(hint,'hint');const r=Vector3.Subtract(hint,Vector3.Multiply(f.Axis,Vector3.DotProduct(hint,f.Axis))),l=length(r);
    if(!Number.isFinite(l))throw new ArgumentOutOfRangeException('hint');if(l===0)return;
    f.Radial=divide(r,l);f.Perpendicular=Vector3.CrossProduct(f.Axis,f.Radial);
  }
  function evaluate(f,base,endRadius,angle,height,t) {
    const delta=endRadius-f.Radius,radius=mul(f.Radius,1-t)+mul(endRadius,t),a=mul(angle,t),cos=M.Cos(a),sin=M.Sin(a);
    const radial=Vector3.Add(Vector3.Multiply(f.Radial,cos),Vector3.Multiply(f.Perpendicular,sin));
    const angular=Vector3.Add(Vector3.Multiply(Vector3.Negate(f.Radial),sin),Vector3.Multiply(f.Perpendicular,cos));
    const point=Vector3.Add(Vector3.Add(base,Vector3.Multiply(f.Axis,mul(height,t))),Vector3.Multiply(radial,radius));
    const derivative=Vector3.Add(Vector3.Add(Vector3.Multiply(f.Axis,height),Vector3.Multiply(radial,delta)),Vector3.Multiply(angular,mul(radius,angle)));
    valid(point,'parameter');valid(derivative,'parameter');return {point,derivative};
  }
  function errorBound(startRadius,endRadius,turns,segments) {
    if(startRadius===0&&endRadius===0)return 0;
    const step=M.Log(mul(2,Math.PI))+M.Log(turns)-M.Log(segments),radial=M.Log(Math.max(startRadius,endRadius))+mul(4,step);
    const delta=Math.abs(endRadius-startRadius),taper=delta===0?-Infinity:M.Log(4)+M.Log(delta)-M.Log(segments)+mul(3,step),max=Math.max(radial,taper);
    const sum=max+M.Log(M.Exp(radial-max)+M.Exp(taper-max));
    const bound=mul(M.Exp(sum+M.Log(Math.sqrt(3)/384)),1+1e-12);if(bound===Infinity)return bound;
    bits.setFloat64(0,bound);bits.setBigUint64(0,bits.getBigUint64(0)+1n);return bits.getFloat64(0);
  }
  function segments(startRadius,endRadius,turns,tolerance,maximumSegments) {
    valid(tolerance,'tolerance');if(tolerance<=0)throw new ArgumentOutOfRangeException('tolerance');RequireInteger(maximumSegments,1,1048576,'maximumSegments');
    const minimum=startRadius===0&&endRadius===0?1:Math.max(1,Math.ceil(mul(4,turns)));
    if(minimum>maximumSegments)throw new ArgumentException('HELIX turns exceed the cubic segment budget.','maximumSegments');
    let n=minimum;while(errorBound(startRadius,endRadius,turns,n)>tolerance){
      if(n===maximumSegments)throw new ArgumentException('HELIX tolerance cannot be met within the cubic segment budget.','maximumSegments');
      n=Math.min(maximumSegments,n*2);
    }return n;
  }
  function create(base,start,axis,radius,turns,turnHeight,right,tolerance,budget,hint) {
    valid(radius,'radius');valid(turns,'turns');valid(turnHeight,'turnHeight');
    if(radius<0)throw new ArgumentOutOfRangeException('radius');if(turns<=0)throw new ArgumentOutOfRangeException('turns');
    const f=frame(base,start,axis);if(f.Radius===0&&radius!==0&&hint!==null)phase(f,hint);
    const angle=mul(mul(right?2:-2,Math.PI),turns),height=mul(turns,turnHeight);valid(angle,'turns');valid(height,'turnHeight');
    const count=segments(f.Radius,radius,turns,tolerance,budget),curves=[];
    let previous=evaluate(f,base,radius,angle,height,0);const first=previous.derivative;
    for(let i=0;i<count;i++){
      const next=evaluate(f,base,radius,angle,height,(i+1)/count);
      const c1=Vector3.Add(previous.point,Vector3.Divide(previous.derivative,mul(3,count))),c2=Vector3.Subtract(next.point,Vector3.Divide(next.derivative,mul(3,count)));
      valid(c1,'tolerance');valid(c2,'tolerance');curves.push(new BezierCurveCubic(previous.point,c1,c2,next.point));previous=next;
    }
    const spline=new Spline(curves);spline.StartTangent=first;spline.EndTangent=previous.derivative;
    const result=new Helix(spline);Object.assign(result,{AxisBasePoint:base,StartPoint:start,AxisVector:axis,Radius:radius,Turns:turns,TurnHeight:turnHeight,IsRightHanded:right,Normal:f.Axis});return result;
  }
  function definition(h,t) {
    valid(t,'parameter');if(t<0||t>1)throw new ArgumentOutOfRangeException('parameter');
    const f=frame(h.AxisBasePoint,h.StartPoint,h.AxisVector);if(f.Radius===0&&h.Radius!==0&&h.StartTangent!==null)phase(f,h.StartTangent);
    const angle=mul(mul(h.IsRightHanded?2:-2,Math.PI),h.Turns),height=mul(h.Turns,h.TurnHeight);valid(angle,'Turns');valid(height,'TurnHeight');
    return evaluate(f,h.AxisBasePoint,h.Radius,angle,height,t);
  }
  Helix.Create=(base,start,axis,radius,turns,turnHeight,right=true,tolerance=1e-5,maximumSegments=65536)=>create(base,start,axis,radius,turns,turnHeight,right,tolerance,maximumSegments,null);
  Helix.prototype.EvaluateDefinition=function(t){return definition(this,t).point;};
  Helix.prototype.EvaluateDefinitionDerivative=function(t){return definition(this,t).derivative;};
  Helix.prototype.GetApproximationErrorBound=function(count){RequireInteger(count,1,2147483647,'segments');return errorBound(frame(this.AxisBasePoint,this.StartPoint,this.AxisVector).Radius,this.Radius,this.Turns,count);};
  Helix.prototype.WithRegeneratedSpline=function(tolerance=1e-5,maximumSegments=65536){
    const r=create(this.AxisBasePoint,this.StartPoint,this.AxisVector,this.Radius,this.Turns,this.TurnHeight,this.IsRightHanded,tolerance,maximumSegments,this.StartTangent);
    for(const key of ['MajorReleaseNumber','MaintenanceReleaseNumber','Constraint'])r[key]=this[key];
    for(const key of ['Layer','Linetype','Color','Transparency'])r[key]=this[key].Clone();
    for(const key of ['Lineweight','LinetypeScale','IsVisible','Normal','KnotTolerance','CtrlPointTolerance','FitTolerance'])r[key]=this[key];
    for(const data of this.XData.Values)r.XData.Add(data.Clone());return r;
  };
}
