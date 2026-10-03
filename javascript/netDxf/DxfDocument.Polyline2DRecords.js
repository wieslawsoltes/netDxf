// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { RegisterRetainedRecords, UnregisterRetainedRecords } from '../runtime/RetainedPolylineRegistration.js';
export function InstallDocumentPolyline2DRecords(Type) {
  Type.prototype.RegisterStoredPolyline2DRecords = function(parent) { RegisterRetainedRecords(this, parent); };
  Type.prototype.UnregisterStoredPolyline2DRecords = function(parent) { UnregisterRetainedRecords(this, parent); };
}
