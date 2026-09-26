// Shared storage adapter for the original-path document partials.
// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Layer } from '../netDxf/Tables/Layer.js';
import { Linetype } from '../netDxf/Tables/Linetype.js';
import { Polyline3D } from '../netDxf/Entities/Polyline3D.js';
import { PolygonMesh } from '../netDxf/Entities/PolygonMesh.js';
import { PolyfaceMesh } from '../netDxf/Entities/PolyfaceMesh.js';
import { Polyline2D } from '../netDxf/Entities/Polyline2D.js';
import { Polyline3DRecord } from '../netDxf/Entities/Polyline3DRecord.js';
import { PolygonMeshRecord } from '../netDxf/Entities/PolygonMeshRecord.js';
import { PolyfaceMeshRecord } from '../netDxf/Entities/PolyfaceMeshRecord.js';
import { Polyline2DRecord } from '../netDxf/Entities/Polyline2DRecord.js';

export const RetainedParentTypes = Object.freeze([Polyline3D, PolygonMesh, PolyfaceMesh, Polyline2D]);
export const RetainedRecordTypes = Object.freeze([Polyline3DRecord, PolygonMeshRecord, PolyfaceMeshRecord, Polyline2DRecord]);
export const IsRetainedParent = item => RetainedParentTypes.some(Type => item instanceof Type);
export const IsRetainedRecord = item => RetainedRecordTypes.some(Type => item instanceof Type);

// NumberStyles.AllowHexSpecifier, with no whitespace and no precision loss.
// Leading zeroes do not count against UInt64's significant width.
export function RetainedHandleValue(handle) {
  if (typeof handle !== 'string' || !/^[0-9a-f]+$/i.test(handle)) return null;
  const significant = handle.replace(/^0+/, '');
  return significant.length > 16 ? null : significant === '' ? 0n : BigInt('0x' + significant);
}

export function RegisterRetainedRecords(document, parent, polyface = false) {
  if (parent == null || !parent.HasStoredRecords) return;
  for (const record of parent.StoredRecords) {
    // Source snapshots keys: the face-layer path may remove a resource slot.
    for (const key of Array.from(record.Resources.keys())) {
      const resource = record.Resources.get(key);
      if (resource instanceof Layer) {
        if (polyface && record.IsFaceRecord) {
          if (record.Face.Layer === null) record.Resources.delete(key);
          else record.Resources.set(key, document.Layers.Add(record.Face.Layer));
        } else record.Resources.set(key, document.Layers.Add(resource));
      } else if (resource instanceof Linetype) record.Resources.set(key, document.Linetypes.Add(resource));
    }
    if (record.Handle === null) document.NumHandles = record.AssignHandle(document.NumHandles);
    record.SourceDocument = document;
    document.AddedObjects.Add(record.Handle, record);
  }
  parent.BindStoredRecordDocument(document);
}

export function UnregisterRetainedRecords(document, parent) {
  if (parent == null) return;
  // Unregistering a chain is not retiring each child: same-document re-adoption
  // keeps the child's handle and its structural relationship to the parent.
  for (const record of parent.StoredRecords) document.AddedObjects.Remove(record.Handle);
}
