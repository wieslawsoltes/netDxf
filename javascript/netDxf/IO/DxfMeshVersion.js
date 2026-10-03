// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Mesh } from '../Entities/Mesh.js';
import { NotSupportedException } from '../../runtime/Errors.js';
export function ValidateMeshVersions(document) {
  if (document.DrawingVariables.AcadVer >= 16) return;
  // Includes unused definitions and every layout, not just the active layout.
  for (const block of document.Blocks) for (const entity of block.Entities) if (entity instanceof Mesh)
    throw new NotSupportedException('MESH entities require AutoCAD 2010 (AC1024) or later DXF output. Choose that version, remove the MESH, or explicitly convert its geometry to a legacy representation before saving. No automatic lossy conversion is performed.');
}
