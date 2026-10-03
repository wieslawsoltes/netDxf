// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { ArgumentException } from '../../runtime/Errors.js';
export class HatchSplineData {
  static Validate(spline) {
    if (spline.Degree < 1) throw new ArgumentException('HATCH spline degree must be positive.');
    if (spline.Knots == null || spline.ControlPoints == null) throw new ArgumentException('HATCH spline knots and control points must be supplied.');
    if (!spline.IsPeriodic) {
      if (spline.ControlPoints.length < spline.Degree + 1) throw new ArgumentException('Insufficient HATCH control points.');
      if (spline.Knots.length !== spline.ControlPoints.length + spline.Degree + 1) throw new ArgumentException('Invalid HATCH knot count.');
    }
    for (let i=0;i<spline.Knots.length;i++) {
      if (!Number.isFinite(spline.Knots[i])) throw new ArgumentException('HATCH knots must be finite.');
      if (i>0 && spline.Knots[i]<spline.Knots[i-1]) throw new ArgumentException('HATCH knots must be nondecreasing.');
    }
    for (const p of spline.ControlPoints) if (![p.X,p.Y,p.Z].every(Number.isFinite)) throw new ArgumentException('HATCH control coordinates and stored weights must be finite.');
  }
}
