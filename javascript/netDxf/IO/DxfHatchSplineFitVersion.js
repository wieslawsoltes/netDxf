// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Hatch } from '../Entities/Hatch.js';
import { HatchBoundaryPath } from '../Entities/HatchBoundaryPath.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export function ValidateHatchSplineFitVersions(document) {
  if (document.DrawingVariables.AcadVer >= DxfVersion.AutoCad2010) return;
  for (const block of document.Blocks) for (const entity of block.Entities) {
    if (!(entity instanceof Hatch)) continue;
    for (const path of entity.BoundaryPaths) for (const edge of path.Edges)
      if (edge instanceof HatchBoundaryPath.Spline && (edge.FitPoints.Count !== 0
        || edge.StartTangent !== null || edge.EndTangent !== null))
        throw new NotSupportedException('HATCH spline fit points and tangents require AutoCAD 2010 (AC1024) or later '
          + 'in this writer profile. Choose that version or explicitly clear the fit '
          + 'metadata before saving. Control points and knots remain independent.');
  }
}
