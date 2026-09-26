// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { Hatch } from '../Entities/Hatch.js';
import { InvalidDataException } from '../../runtime/Errors.js';
export function RequireHatchBoundary(hatch) {
  if (hatch.BoundaryPaths.Count === 0)
    throw new InvalidDataException('HATCH has no boundary paths. Typed export requires at least one boundary; '
      + 'add a boundary, explicitly remove the entity, or use DxfRawDocument for original-data preservation.');
}
export function ValidateHatchBoundaryPresence(document) {
  for (const block of document.Blocks) for (const entity of block.Entities)
    if (entity instanceof Hatch) RequireHatchBoundary(entity);
}
