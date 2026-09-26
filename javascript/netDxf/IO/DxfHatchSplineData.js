// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Hatch } from '../Entities/Hatch.js';
import { HatchBoundaryPath } from '../Entities/HatchBoundaryPath.js';
import { HatchSplineData } from '../Entities/HatchSplineData.js';
export function ValidateHatchSplineData(document) {
  for (const block of document.Blocks) for (const entity of block.Entities)
    if (entity instanceof Hatch) for (const path of entity.BoundaryPaths) for (const edge of path.Edges)
      if (edge instanceof HatchBoundaryPath.Spline) HatchSplineData.Validate(edge);
}
