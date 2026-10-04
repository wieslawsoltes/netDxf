// Copyright (c) Daniel Carvajal and netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { VertexTypeFlags } from './VertexTypeFlags.js';
import { SubclassMarker } from '../SubclassMarker.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
/** Internal typed VERTEX transport model; reference arrays intentionally remain aliased. */
export class Vertex extends DxfObject {
  #position = Vector3.Zero;
  Flags = VertexTypeFlags.Polyline2DVertex;
  Layer = null; Color = null; Linetype = null;
  Bulge = 0; StartWidth = 0; EndWidth = 0; VertexIndexes = null;
  SubclassMarker = SubclassMarker.Polyline2DVertex;
  constructor() { super(DxfObjectCode.Vertex); }
  get Position() { return Copy(this.#position); }
  set Position(value) { this.#position = Copy(value); }
}
