// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { ArgumentException, NotSupportedException } from '../../runtime/Errors.js';
const bits = new DataView(new ArrayBuffer(16));
/** Internal validation shared by periodic evaluation and future typed spline conversions. */
export class PeriodicSplineData {
  static Same(a,b) { bits.setFloat64(0,a);bits.setFloat64(8,b);return bits.getBigUint64(0)===bits.getBigUint64(8); }
  static Finite(value) {
    if(value instanceof Vector3) { this.Finite(value.X);this.Finite(value.Y);this.Finite(value.Z);return; }
    if(!Number.isFinite(value))throw new ArgumentException('Periodic SPLINE values must be finite and representable.');
  }
  static Validate(controls,weights,knots,degree) {
    if(arguments.length===1) {
      const s=controls;this.Validate(s.ControlPoints,s.Weights,s.Knots,s.Degree);
      for(const p of s.FitPoints)this.Finite(p);
      if(s.StartTangent!==null)this.Finite(s.StartTangent);if(s.EndTangent!==null)this.Finite(s.EndTangent);return;
    }
    if(degree<1||degree>10||controls==null||weights==null||knots==null||controls.length<degree+1||weights.length!==controls.length||knots.length!==controls.length+2*degree+1)
      throw new NotSupportedException('Unsupported compact periodic control and extended knot layout.');
    let max=0,min=Number.MAX_VALUE;
    for(let i=0;i<controls.length;i++) {
      this.Finite(controls[i]);this.Finite(weights[i]);
      if(weights[i]<=0)throw new NotSupportedException('Periodic SPLINE weights must be positive.');
      max=Math.max(max,weights[i]);min=Math.min(min,weights[i]);
    }
    if(min/max===0)throw new NotSupportedException('Periodic SPLINE weight range is not representable.');
    for(const knot of knots)this.Finite(knot);
    for(let i=0;i+1<knots.length;i++) {const span=knots[i+1]-knots[i];this.Finite(span);if(span<=0)throw new NotSupportedException('Periodic knots must strictly increase.');}
    const period=knots[controls.length+degree]-knots[degree];this.Finite(period);
    if(period<=0)throw new NotSupportedException('Periodic domain must have positive length.');
    for(let i=0;i<2*degree;i++) {
      const left=knots[i+1]-knots[i],right=knots[i+controls.length+1]-knots[i+controls.length];
      if(Math.abs(left-right)>1e-12*Math.max(left,right))throw new NotSupportedException('Exterior knot spans do not continue the active domain cyclically.');
    }
  }
}
