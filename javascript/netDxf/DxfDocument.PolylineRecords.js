// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
import { BoxedString } from '../runtime/BoxedString.js';
import { IsRetainedParent, IsRetainedRecord, RetainedHandleValue,
  RegisterRetainedRecords, UnregisterRetainedRecords } from '../runtime/RetainedPolylineRegistration.js';

export function InstallDocumentPolylineRecords(Type) {
  Type.prototype.RegisterStoredPolylineRecords = function(parent) { RegisterRetainedRecords(this, parent); };
  Type.prototype.UnregisterStoredPolylineRecords = function(parent) { UnregisterRetainedRecords(this, parent); };
  Type.prototype.RemovedPolylineHandle = function(handle, removed) {
    const value = RetainedHandleValue(handle instanceof BoxedString ? handle.Value : handle);
    if (value === null || value === 0n) return false;
    const target = this.GetObjectByHandle(value.toString(16).toUpperCase());
    return target !== null && removed.has(target);
  };
  Type.prototype.StoredPolylineReferencesRemoval = function(removed) {
    const removesRecords = Array.from(removed).some(IsRetainedRecord);
    for (const record of removed)
      if (IsRetainedRecord(record) && (record.HasPrivateData || record.ExtensionDictionary !== null)) return true;
    for (const parent of removed)
      if ((parent instanceof api.Polyline2D || parent instanceof api.PolyfaceMesh) && parent.HasPrivateHeader) return true;

    // A collection move changes the parent's handle. Raw XData text cannot follow
    // that change, even when both the referring child and the parent are removed.
    const parents = new Set(Array.from(removed).filter(item => IsRetainedParent(item) && item.HasStoredRecords));
    for (const item of removed) for (const data of item.XData.Values) for (const tag of data.XDataRecord)
      if (tag.Code === api.XDataCode.DatabaseHandle && this.RemovedPolylineHandle(tag.Value, parents)) return true;

    for (const item of this.RetainedMetadataObjects()) {
      if (removed.has(item)) continue;
      if (IsRetainedRecord(item) && (Array.from(item.References).some(target => removed.has(target)) ||
          Array.from(item.OpaqueHandleTags).some(tag => this.RemovedPolylineHandle(tag.Value, removed)))) return true;
      if ((item instanceof api.PolyfaceMesh || item instanceof api.Polyline2D) &&
          Array.from(item.StoredHeaderReferences).some(tag => this.RemovedPolylineHandle(tag.Value, removed))) return true;
      if (!removesRecords && !IsRetainedRecord(item)) continue;
      if (item.Owner !== null && removed.has(item.Owner) || item.ExtensionDictionary !== null && removed.has(item.ExtensionDictionary)) return true;
      if (Array.from(item.PersistentReactors).some(target => removed.has(target))) return true;
      if (item instanceof api.EntityObject && Array.from(item.Reactors).some(target => removed.has(target))) return true;
      for (const data of item.XData.Values) for (const tag of data.XDataRecord)
        if (tag.Code === api.XDataCode.DatabaseHandle && this.RemovedPolylineHandle(tag.Value, removed)) return true;
      if (item instanceof api.DxfDatabaseObject && Array.from(item.DatabaseReferences).some(target => removed.has(target))) return true;
      if (item instanceof api.DxfDictionary && Array.from(item.Entries).some(entry => removed.has(entry.Target))) return true;
      if (item instanceof api.DxfDictionaryWithDefault && item.Default !== null && removed.has(item.Default)) return true;
      const tags = item instanceof api.DxfXRecord ? item.Data : item instanceof api.DxfOpaqueObject ? item.Tags : [];
      for (const tag of tags)
        if (api.DxfObjectDatabase.IsReference(tag) && this.RemovedPolylineHandle(tag.Value, removed)) return true;
    }
    if (removesRecords) for (const variable of this.DrawingVariables.CustomValues()) {
      const kind = api.DxfGroupCode.GetHandleKind(variable.GroupCode);
      const value = variable.Value instanceof BoxedString ? variable.Value.Value : variable.Value;
      if (kind !== api.DxfHandleKind.None && kind !== api.DxfHandleKind.Arbitrary &&
          typeof value === 'string' && this.RemovedPolylineHandle(value, removed)) return true;
    }
    return false;
  };
}
