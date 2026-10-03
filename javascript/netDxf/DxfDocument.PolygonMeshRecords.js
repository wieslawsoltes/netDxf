// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { RegisterRetainedRecords, UnregisterRetainedRecords } from '../runtime/RetainedPolylineRegistration.js';
export function InstallDocumentPolygonMeshRecords(Type) {
  Type.prototype.RegisterStoredPolygonMeshRecords = function(parent) { RegisterRetainedRecords(this, parent); };
  Type.prototype.UnregisterStoredPolygonMeshRecords = function(parent) { UnregisterRetainedRecords(this, parent); };
}
