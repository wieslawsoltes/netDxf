// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { RegisterRetainedRecords, UnregisterRetainedRecords } from '../runtime/RetainedPolylineRegistration.js';
export function InstallDocumentPolyfaceMeshRecords(Type) {
  Type.prototype.RegisterStoredPolyfaceMeshRecords = function(parent) { RegisterRetainedRecords(this, parent, true); };
  Type.prototype.UnregisterStoredPolyfaceMeshRecords = function(parent) { UnregisterRetainedRecords(this, parent); };
}
