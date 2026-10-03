// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
/** Original internal POLYLINE transport aggregate, not a replacement for Polyline2D/3D. */
export class Polyline extends DxfObject {
  #normal = Vector3.Zero;
  StoredSource = null; StoredMeshSource = null; StoredPolyfaceSource = null;
  SubclassMarker = SubclassMarker.Polyline;
  Layer = null; Color = null; EndSequence = null; Vertexes = null;
  Thickness = 0; Elevation = 0; Flags = 0; SmoothType = 0;
  M = 0; N = 0; DensityM = 0; DensityN = 0;
  constructor() { super(DxfObjectCode.Polyline); }
  get Normal() { return Copy(this.#normal); }
  set Normal(value) { this.#normal = Copy(value); }
}
