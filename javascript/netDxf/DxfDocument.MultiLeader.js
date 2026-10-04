// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
const is = (item, name) => typeof api[name] === 'function' && item instanceof api[name];
/** Live retained-reference accounting. Counts repeated references by identity, not names. */
export function InstallDocumentMultiLeader(Type) {
  Type.prototype.StoredTableHandleTarget = function(handle) {
    const registered = this.GetObjectByHandle(handle); if (registered !== null) return registered;
    if (typeof handle !== 'string' || !/^[0-9a-f]{1,16}$/i.test(handle) || BigInt('0x' + handle) === 0n) return null;
    const value = BigInt('0x' + handle);
    for (const item of this.RetainedMetadataObjects())
      if (typeof item.Handle === 'string' && /^[0-9a-f]{1,16}$/i.test(item.Handle) && BigInt('0x' + item.Handle) === value) return item;
    return null;
  };
  Type.prototype.MLeaderReferences = function(target) {
    const result = new ReferenceList(); if (target instanceof api.Block) target = target.Record;
    if (target == null) return result;
    for (const item of this.AddedObjects.Values) {
      let references;
      if (['Polyline3DRecord','DxfOpaqueEntity','DxfStoredSunStudy','PolygonMeshRecord','PolyfaceMeshRecord',
           'Polyline2DRecord','DxfStoredField','StoredTable','DxfStoredTableContent','DxfStoredTableGeometry',
           'DxfStoredCellStyleMap','DxfTableStyle','DxfStoredDimAssoc'].some(name => is(item,name))) references = item.References;
      else if (item instanceof api.Section) references = item.GeometrySettings === null ? [] : [item.GeometrySettings];
      else if (item instanceof api.DxfSectionSettings || item instanceof api.DxfMLeaderStyle) references = item.DatabaseReferences;
      else if (item instanceof api.MultiLeader) references = Array.from(item.Data).flatMap(data => Array.from(data.References));
      else if (item instanceof api.DxfOpaqueObject && ['SUNSTUDY','TABLECONTENT','TABLEGEOMETRY','CELLSTYLEMAP','DIMASSOC'].includes(item.CodeName))
        references = Array.from(item.Tags).filter(api.DxfObjectDatabase.IsReference).map(tag => this.StoredTableHandleTarget(tag.Value));
      else continue;
      let uses = 0; for (const reference of references) if (reference === target) uses++;
      if (uses > 0) result.Add(new api.DxfObjectReference(item,uses));
    }
    return result;
  };
}
