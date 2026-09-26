// Complete original detached numeric/conversion cases. Original document loading,
// CreateBoundary registration, storage and round-trip bodies are not shortened or registered.
import fs from 'node:fs';
import path from 'node:path';
import { HatchBoundaryPath, Spline, Vector3 } from '../../index.js';
import { ArgumentException } from '../../runtime/Errors.js';
import { Culture, NumberText } from '../../runtime/GeometryRuntime.js';
import { sourceRoot, javascriptRoot } from '../../tools/dotnet.mjs';
import { Run, Check, Equal, Throws, SameDoubleBits, BooleanName as B } from './TestHarness.js';
const N = value => NumberText(value, Culture.Invariant);
const range = count => Array.from({ length: count }, (_, i) => i);
const ones = count => Array(count).fill(1);
const xyz = p => [p.X, p.Y, p.Z];
const samples = (s, count) => Array.from(s.PolygonalVertexes(count));
const same = (a, b) => a.length === b.length && a.every((v, i) => v instanceof Vector3 ? v.Equals(b[i]) : v === b[i]);
function output(name, data) {
  const directory = path.resolve(process.env.DXF_JS_TEST_ARTIFACTS || path.join(javascriptRoot, 'artifacts/conformance'));
  fs.mkdirSync(directory, { recursive: true });
  fs.writeFileSync(path.join(directory, name + '.json'), JSON.stringify(data));
}
// Original HatchAffineNear helper/tolerance, not used by exact differential comparison.
function HatchAffineNear(expected, actual, message) {
  const scale = Math.max(1, Math.max(expected.Modulus(), actual.Modulus()));
  Check(Vector3.Subtract(expected, actual).Modulus() <= 2e-10 * scale, message);
}
// Independent rational de Boor test reference from SplineReversalTests.cs.
// This does not use Spline.PolygonalVertexes as its own expected-value oracle.
function ReversalPoint(spline, parameter) {
  const p = spline.Degree;
  const cp = spline.IsClosedPeriodic ? [...spline.ControlPoints.slice(-p), ...spline.ControlPoints] : spline.ControlPoints;
  const w = spline.IsClosedPeriodic ? [...spline.Weights.slice(-p), ...spline.Weights] : spline.Weights;
  const u = spline.Knots;
  let span = p;
  while (span < cp.length - 1 && parameter >= u[span + 1]) span++;
  const points = [], weights = [];
  for (let j = 0; j <= p; j++) { const i = span - p + j; points[j] = Vector3.Multiply(cp[i], w[i]); weights[j] = w[i]; }
  for (let r = 1; r <= p; r++) for (let j = p; j >= r; j--) {
    const i = span - p + j, alpha = (parameter - u[i]) / (u[i + p - r + 1] - u[i]);
    points[j] = Vector3.Add(Vector3.Multiply(1 - alpha, points[j - 1]), Vector3.Multiply(alpha, points[j]));
    weights[j] = (1 - alpha) * weights[j - 1] + alpha * weights[j];
  }
  return Vector3.Divide(points[p], weights[p]);
}
export function RegisterHatchPeriodicConversionTests() {
  for (const degree of range(10).map(i => i + 1)) for (const compact of [false, true]) for (const coincident of [false, true])
    Run(`hatch-periodic/degree/${degree}/${B(compact)}/${B(coincident)}`, () => HatchPeriodicDegree(degree, compact, coincident));
  for (const offset of [false, true]) Run(`hatch-periodic/parameter-grid/${B(offset)}`, () => HatchPeriodicParameterGrid(offset));
  for (const weight of [Number.MIN_VALUE, 1e-200, 1e200]) for (const coincident of [false, true])
    Run(`hatch-periodic/weight-scale/${N(weight)}/${B(coincident)}`, () => HatchPeriodicWeightScale(weight, coincident));
  for (const weight of [Number.MIN_VALUE, 1e-310]) for (const extreme of [false, true])
    Run(`hatch-periodic/local-weight-scale/${N(weight)}/${B(extreme)}`, () => HatchPeriodicLocalWeightScale(weight, extreme));
  for (const degree of [1, 2, 3, 5, 10]) for (const coordinate of [Number.MIN_VALUE, 1e-320, 1e-100, 1e100, Number.MAX_VALUE]) for (const mixed of [false, true])
    Run(`hatch-periodic/constant-coordinate/${degree}/${N(coordinate)}/${B(mixed)}`, () => HatchPeriodicConstantCoordinate(degree, coordinate, mixed));
  for (const degree of [1, 2, 3, 5, 10]) for (const scale of [Number.MIN_VALUE, 1e-320]) for (const mixed of [false, true])
    Run(`hatch-periodic/subnormal-shape/${degree}/${N(scale)}/${B(mixed)}`, () => HatchPeriodicSubnormalShape(degree, scale, mixed));
  for (const magnitude of [1e100, 1e200, 1e308]) for (const middle of [1, 1e-100, 1e100])
    Run(`hatch-periodic/cancellation/${N(magnitude)}/${N(middle)}`, () => HatchPeriodicCancellation(magnitude, middle));
  for (const [small, large] of [[1e-310, 1e13], [5e-300, 3e22], [3e-307, 7e14]])
    Run(`hatch-periodic/weight-compensation/${N(small)}/${N(large)}`, () => HatchPeriodicWeightCompensation(small, large));
  for (let index = 0; index < 9; index++) Run(`hatch-periodic/weighted-cancellation/${index}`, () => HatchPeriodicWeightedCancellation(index));
  for (const offset of [-1e-15, -1e-14]) Run(`hatch-periodic/subnormal-basis/${N(offset)}`, () => HatchPeriodicSubnormalBasis(offset));
}
export function HatchPeriodicPacket() {
  return Object.assign(new HatchBoundaryPath.Spline(), { Degree: 2, IsPeriodic: true, IsRational: true,
    ControlPoints: [[0, 0, 1], [4, 7, 1], [10, 1, 1], [7, -5, 1], [-3, -2, 1], [0, 0, 1], [4, 7, 1]].map(p => new Vector3(...p)),
    Knots: range(10).map(i => i * .25 - 3) });
}
export function HatchPeriodicDegree(degree, compact, coincident) {
  const controls = range(degree + 3).map(i => new Vector3(i * 2, i % 3 * 4, 0)); if (coincident) controls[controls.length - 1] = controls[0];
  const source = new Spline(controls, ones(controls.length), degree, true), edge = new HatchBoundaryPath.Spline(source);
  if (compact) edge.ControlPoints = edge.ControlPoints.slice(degree);
  const result = edge.ConvertTo(), first = source.Knots[degree], last = source.Knots[source.Knots.length - degree - 1], points = samples(result, 65);
  for (let i = 0; i < points.length; i++) HatchAffineNear(ReversalPoint(source, first + (last - first) * i / 65), points[i], 'High-degree/coincident periodic active domain');
  Check(same(source.Knots, result.Knots) && same(source.Weights, result.Weights) && same(source.ControlPoints, result.ControlPoints), 'Adapter preserves compact source exactly');
}
export function HatchPeriodicParameterGrid(offset) {
  const controls = HatchPeriodicPacket().ControlPoints.slice(2).map(p => new Vector3(p.X, p.Y, 0));
  const knots = range(10).map(i => offset ? 1e16 + i * 2 : i * Number.MIN_VALUE);
  const spline = new Spline(controls, ones(controls.length), knots, 2, true), snapshot = Array.from(spline.Knots);
  Throws(ArgumentException, () => spline.PolygonalVertexes(64)); Check(same(snapshot, spline.Knots), 'Unrepresentable sample request retains stored knots');
}
export function HatchPeriodicWeightScale(weight, coincident) {
  const points = [[0, 0, 0], [4, 7, 0], [10, 1, 0], [7, -5, 0], [-3, -2, 0]].map(p => new Vector3(...p)); if (coincident) points[4] = points[0];
  const unit = new Spline(points, ones(points.length), 2, true), source = new Spline(points, Array(points.length).fill(weight), 2, true);
  const result = new HatchBoundaryPath.Spline(source).ConvertTo(), expected = samples(unit, 65), actual = samples(result, 65);
  for (let i = 0; i < actual.length; i++) HatchAffineNear(expected[i], actual[i], 'Temporary weight normalization');
  for (const actualWeight of result.Weights) SameDoubleBits(weight, actualWeight, 'Stored weight remains unchanged by evaluation');
}
export function HatchPeriodicLocalWeightScale(weight, extreme) {
  const points = range(6).map(i => new Vector3(2 * i + 1, 3 * i - 1, 0)), weights = Array(6).fill(weight); weights[3] = 1;
  if (extreme) { for (let i = 0; i < points.length; i++) points[i] = Vector3.Zero; points[0] = new Vector3(1e308, -1e308, 0); weights.fill(1); weights[0] = weight; }
  const source = new Spline(points, weights, 1, true), converted = new HatchBoundaryPath.Spline(source).ConvertTo(), values = samples(converted, 12);
  if (extreme) {
    const expected = weight * 1e308 / (1 + weight);
    Check(Math.abs(values[1].X - expected) <= expected * 1e-12 && Math.abs(values[1].Y + expected) <= expected * 1e-12, 'Subnormal basis coefficient retains its representable coordinate contribution');
  } else HatchAffineNear(Vector3.Multiply(Vector3.Add(points.at(-1), points[0]), .5), values[1], 'Local subnormal weights retain their relative scale');
  HatchAffineNear(points.at(-1), values[0], 'Local subnormal endpoint remains finite');
  Check(same(source.Weights, converted.Weights), 'Evaluation leaves mixed-scale stored weights unchanged');
  output(`hatch-periodic-local-weight-${N(weight)}-${B(extreme)}`, { weight, extreme, controls: points.map(xyz), weights, knots: Array.from(source.Knots), samples: values.map(xyz) });
}
export function HatchPeriodicConstantCoordinate(degree, coordinate, mixed) {
  const controls = range(degree + 3).map(() => new Vector3(coordinate, -coordinate, 0)), weights = ones(controls.length);
  if (mixed) weights.fill(1e-310, 0, weights.length - 1);
  const source = new Spline(controls, weights, degree, true), converted = new HatchBoundaryPath.Spline(source).ConvertTo(), values = samples(converted, 17);
  for (const sample of values) {
    SameDoubleBits(coordinate, sample.X, 'A constant rational curve retains its positive coordinate exactly');
    SameDoubleBits(-coordinate, sample.Y, 'A constant rational curve retains its negative coordinate exactly'); Equal(0, sample.Z, 'A constant zero coordinate remains zero');
  }
  const reverse = converted.Clone(); reverse.Reverse();
  for (const sample of samples(reverse, 17)) { SameDoubleBits(coordinate, sample.X, 'Reversal retains the constant positive coordinate'); SameDoubleBits(-coordinate, sample.Y, 'Reversal retains the constant negative coordinate'); }
  Check(same(source.ControlPoints, converted.ControlPoints) && same(source.Weights, converted.Weights) && same(source.Knots, converted.Knots), 'Constant evaluation leaves the packet unchanged');
  output(`hatch-periodic-constant-${degree}-${N(coordinate)}-${B(mixed)}`, { degree, coordinate, mixed, samples: values.map(xyz) });
}
export function HatchPeriodicNumericOutput(source, name, count) {
  output(`hatch-periodic-numeric-${name}`, { degree: source.Degree, controls: Array.from(source.ControlPoints, xyz), weights: Array.from(source.Weights), knots: Array.from(source.Knots), samples: samples(source, count).map(xyz) });
}
export function HatchPeriodicSubnormalShape(degree, scale, mixed) {
  const controls = range(degree + 3).map(i => new Vector3(i * 3 - 5, i % 4 * 2 - 3, 0)), weights = ones(controls.length);
  if (mixed) weights.fill(1e-310, 0, weights.length - 1);
  const unit = new Spline(controls, weights, degree, true), source = new Spline(controls.map(p => Vector3.Multiply(p, scale)), weights, degree, true);
  const actual = samples(source, 17), expected = samples(unit, 17);
  for (let i = 0; i < actual.length; i++) {
    Check(Math.abs(actual[i].X - expected[i].X * scale) <= 4 * Number.MIN_VALUE, 'Nonconstant subnormal X respects curve scaling');
    Check(Math.abs(actual[i].Y - expected[i].Y * scale) <= 4 * Number.MIN_VALUE, 'Nonconstant subnormal Y respects curve scaling');
  }
  HatchPeriodicNumericOutput(source, `subnormal-${degree}-${N(scale)}-${B(mixed)}`, 17);
}
export function HatchPeriodicCancellation(magnitude, middle) {
  const source = new Spline([new Vector3(-magnitude, 0, 0), Vector3.Zero, Vector3.Zero, new Vector3(magnitude, 0, 0), new Vector3(middle, 0, 0)], ones(5), range(10), 2, true);
  const actual = samples(source, 10)[1].X, expected = .75 * middle;
  Check(Math.abs(actual - expected) <= Math.abs(expected) * 1e-12, 'Opposite large contributions retain the smaller middle contribution');
  HatchPeriodicNumericOutput(source, `cancellation-${N(magnitude)}-${N(middle)}`, 10);
}
export function HatchPeriodicWeightCompensation(small, large) {
  const source = new Spline([new Vector3(1e308, -1e308, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero], [small, large, large, large], range(7), 1, true);
  const expected = small * 1e308 / large, actual = samples(source, 8)[1];
  Check(Math.abs(actual.X - expected) <= expected * 1e-12 && Math.abs(actual.Y + expected) <= expected * 1e-12, 'Weight division does not quantize a later representable coordinate contribution');
  HatchPeriodicNumericOutput(source, `weight-compensation-${N(small)}-${N(large)}`, 8);
}
export function HatchPeriodicWeightedCancellation(index) {
  const fixture = JSON.parse(fs.readFileSync(path.join(sourceRoot, 'tests/fixtures/hatch-periodic-conversion/weighted-cancellation.json'), 'utf8')), item = fixture.cases[index];
  const source = new Spline(item.controls.map(p => new Vector3(...p)), item.weights, item.knots, item.degree, true), count = item.precision, values = samples(source, count);
  for (let i = 0; i < count; i++) { const value = item.expected[i][0]; Check(Math.abs(values[i].X - value) <= Math.abs(value) * 2e-12 + 4 * Number.MIN_VALUE, 'Weighted cancellation matches independent exact-rational input fixture'); }
  HatchPeriodicNumericOutput(source, `weighted-cancellation-${index}`, count);
}
export function HatchPeriodicSubnormalBasis(offset) {
  const source = new Spline([Vector3.Zero, Vector3.Zero, new Vector3(1e308, 0, 0), Vector3.Zero], ones(4), [-1.2e308, -8e307, -4e307, offset, 4e307, 8e307, 1.2e308], 1, true);
  const expected = -offset * 2.5, actual = samples(source, 4)[2].X;
  Check(Math.abs(actual - expected) <= expected * 2e-12, 'Positive subnormal basis retains a representable coordinate contribution');
  const reverse = source.Clone(); reverse.Reverse();
  SameDoubleBits(-offset, reverse.Knots[3], 'Opposite-sign active endpoints retain the reflected small knot');
  Check(Math.abs(samples(reverse, 4)[2].X - expected) <= expected * 2e-12, 'Reversal preserves the same exactly reflected parameter');
  reverse.Reverse(); for (let i = 0; i < source.Knots.length; i++) SameDoubleBits(source.Knots[i], reverse.Knots[i], 'Double reversal preserves large-domain knots');
  HatchPeriodicNumericOutput(source, `subnormal-basis-${N(offset)}`, 4);
}
