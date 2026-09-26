// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Vector2 } from '../Vector2.js';
import { HatchBoundaryPath } from '../Entities/HatchBoundaryPath.js';
import { ArgumentOutOfRangeException } from '../../runtime/Errors.js';
/** Private-reader partial adapters. The caller supplies the original strict scalar/list
 * helpers; cursor advancement and partial out-parameter updates retain source order. */
export function ReadHatchPolylineHeader(reader, closed, vertexCount) {
  closed.value = false; vertexCount.value = 0; let seen = 0;
  while ([72, 73, 93].includes(reader.chunk.Code)) {
    const code = reader.chunk.Code, bit = code === 72 ? 1 : code === 73 ? 2 : 4;
    if ((seen & bit) !== 0) throw reader.InvalidHatchPolylineData('duplicate polyline header field');
    seen |= bit;
    if (code === 93) vertexCount.value = reader.ReadHatchPolylineCount(93);
    else { const flag = reader.ReadHatchPolylineFlag(code); if (code === 73) closed.value = flag; }
    reader.ReadNextHatchPolylineTag();
  }
  if (seen !== 7) throw reader.InvalidHatchPolylineData('incomplete polyline header');
}
export function ReadHatchSplineHeader(reader, degree, rational, periodic, knotCount, controlCount) {
  degree.value = 0; rational.value = false; periodic.value = false; knotCount.value = 0; controlCount.value = 0;
  let seen = 0;
  while ([94, 73, 74, 95, 96].includes(reader.chunk.Code)) {
    const code = reader.chunk.Code, bit = code === 94 ? 1 : code === 73 ? 2 : code === 74 ? 4 : code === 95 ? 8 : 16;
    if ((seen & bit) !== 0) throw reader.InvalidHatchEdgeData('duplicate spline header field');
    seen |= bit;
    switch (code) {
      case 94:
        degree.value = reader.chunk.ReadInt();
        if (degree.value < 1 || degree.value > 32767)
          throw reader.InvalidHatchEdgeData('spline degree cannot be represented by the positive Int16 typed model');
        reader.ReadNextHatchEdgeTag(); break;
      case 73: rational.value = reader.ReadHatchEdgeFlag(73); break;
      case 74: periodic.value = reader.ReadHatchEdgeFlag(74); break;
      case 95: knotCount.value = reader.ReadHatchEdgeCount(95); break;
      case 96: controlCount.value = reader.ReadHatchEdgeCount(96); break;
    }
  }
  if (seen !== 31) throw reader.InvalidHatchEdgeData('incomplete spline header');
}
export function ReadHatchScalarEdge(reader, type) {
  let required, name;
  switch (type) {
    case HatchBoundaryPath.EdgeType.Line: required = 15; name = 'Line'; break;
    case HatchBoundaryPath.EdgeType.Arc: required = 243; name = 'Arc'; break;
    case HatchBoundaryPath.EdgeType.Ellipse: required = 255; name = 'Ellipse'; break;
    default: throw new ArgumentOutOfRangeException('type');
  }
  const point = Vector2.Zero, axis = Vector2.Zero; let radius = 0, start = 0, end = 0, ccw = false, seen = 0;
  while (![0, 72, 97].includes(reader.chunk.Code)) {
    const code = reader.chunk.Code;
    const bit = ({10: 1, 20: 2, 11: 4, 21: 8, 40: 16, 50: 32, 51: 64, 73: 128})[code] ?? 0;
    if ((required & bit) === 0) throw reader.InvalidHatchEdgeData('unexpected scalar group ' + code + ' in ' + name + ' edge');
    if ((seen & bit) !== 0) throw reader.InvalidHatchEdgeData('duplicate scalar group ' + code + ' in ' + name + ' edge');
    seen |= bit;
    if (code === 73) ccw = reader.ReadHatchEdgeFlag(73);
    else {
      const value = reader.ReadHatchEdgeDouble(code);
      switch (code) {
        case 10: point.X = value; break; case 20: point.Y = value; break;
        case 11: axis.X = value; break; case 21: axis.Y = value; break;
        case 40: radius = value; break; case 50: start = value; break; case 51: end = value; break;
      }
    }
  }
  if (seen !== required) throw reader.InvalidHatchEdgeData('incomplete scalar packet for ' + name + ' edge');
  if (type === HatchBoundaryPath.EdgeType.Line) return Object.assign(new HatchBoundaryPath.Line(), { Start: point, End: axis });
  if (type === HatchBoundaryPath.EdgeType.Arc) return Object.assign(new HatchBoundaryPath.Arc(),
    { Center: point, Radius: radius, StartAngle: start, EndAngle: end, IsCounterclockwise: ccw });
  return Object.assign(new HatchBoundaryPath.Ellipse(),
    { Center: point, EndMajorAxis: axis, MinorRatio: radius, StartAngle: start, EndAngle: end, IsCounterclockwise: ccw });
}
