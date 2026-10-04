// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { Mesh } from '../Entities/Mesh.js';
import { PolygonMesh } from '../Entities/PolygonMesh.js';
import { InvalidOperationException } from '../../runtime/Errors.js';
const invalid = (block, group, message) => new InvalidOperationException(`MESH output in block '${block ?? ''}', group ${group}: ${message}.`);
/** Preflight serialized size before walking shared face arrays or vertex indices. */
export function ValidateMeshOutput(mesh, blockName) {
  let faceListSize = mesh.Faces.Count;
  if (mesh.Faces.Count > 16000000) throw invalid(blockName, 93, 'face count exceeds the typed model limit of 16000000');
  for (let i = 0; i < mesh.Faces.Count; i++) {
    const face = mesh.Faces.get_Item(i);
    if (face === null || face.length < 3) throw invalid(blockName, 93, 'face ' + i + ' must contain at least three vertex indices');
    faceListSize += face.length;
    if (faceListSize > 2147483647) throw invalid(blockName, 93, 'serialized face-list size exceeds Int32.MaxValue');
  }
  for (let i = 0; i < mesh.Vertexes.Count; i++) {
    const vertex = mesh.Vertexes.get_Item(i);
    if (!Number.isFinite(vertex.X) || !Number.isFinite(vertex.Y) || !Number.isFinite(vertex.Z)) throw invalid(blockName, 10, 'vertex ' + i + ' must have finite 10/20/30 coordinates');
  }
  for (let i = 0; i < mesh.Faces.Count; i++) for (const index of mesh.Faces.get_Item(i))
    if (index < 0 || index >= mesh.Vertexes.Count) throw invalid(blockName, 90, 'face ' + i + ' references a missing vertex');
  for (let i = 0; i < mesh.Edges.Count; i++) {
    const edge = mesh.Edges.get_Item(i);
    if (edge === null) throw invalid(blockName, 94, 'edge ' + i + ' is null');
    if (edge.StartVertexIndex < 0 || edge.StartVertexIndex >= mesh.Vertexes.Count || edge.EndVertexIndex < 0 || edge.EndVertexIndex >= mesh.Vertexes.Count)
      throw invalid(blockName, 90, 'edge ' + i + ' references a missing vertex');
    if (!Number.isFinite(edge.Crease)) throw invalid(blockName, 140, 'edge ' + i + ' must have a finite crease value');
  }
}
export function ValidateDocumentMeshOutput(document) {
  for (const block of document.Blocks) for (const entity of block.Entities) {
    if (entity instanceof Mesh) ValidateMeshOutput(entity, block.Name);
    if (entity instanceof PolygonMesh) entity.ValidateSurface();
  }
}
