// Test-only snapshots. Algorithms and validation stay in the production models.
import { Hatch, HatchBoundaryPath as H } from '../index.js';
export function hatchEntityWire(value, common, wire) {
  if (!(value instanceof Hatch)) return undefined;
  return { common, pattern: wire(value.Pattern), associative: value.Associative,
    elevation: wire(value.Elevation), pixel: wire(value.PixelSize),
    seeds: Array.from(value.SeedPoints, wire), paths: Array.from(value.BoundaryPaths, wire) };
}
export function hatchBoundaryWire(value, wire) {
  if (value instanceof H) return { type: 'HatchBoundaryPath', flags: value.PathType,
    attached: value.ContainingHatch !== null, edges: Array.from(value.Edges, wire), entities: Array.from(value.Entities, wire) };
  if (!(value instanceof H.Edge)) return undefined;
  const type = value.constructor.name, kind = value.Type;
  if (value instanceof H.Line) return { type, kind, start: wire(value.Start), end: wire(value.End) };
  if (value instanceof H.Arc) return { type, kind, center: wire(value.Center), radius: wire(value.Radius),
    start: wire(value.StartAngle), end: wire(value.EndAngle), ccw: value.IsCounterclockwise };
  if (value instanceof H.Ellipse) return { type, kind, center: wire(value.Center), axis: wire(value.EndMajorAxis),
    ratio: wire(value.MinorRatio), start: wire(value.StartAngle), end: wire(value.EndAngle), ccw: value.IsCounterclockwise };
  if (value instanceof H.Polyline) return { type, kind, closed: value.IsClosed, vertices: wire(value.Vertexes) };
  if (value instanceof H.Spline) return { type, kind, degree: value.Degree, rational: value.IsRational,
    periodic: value.IsPeriodic, knots: wire(value.Knots), controls: wire(value.ControlPoints),
    fits: Array.from(value.FitPoints, wire), start: wire(value.StartTangent), end: wire(value.EndTangent) };
  throw new Error('Unmapped HATCH boundary edge: ' + type);
}
