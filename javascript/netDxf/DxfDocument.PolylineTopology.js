// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Polyline3DRecord } from './Entities/Polyline3DRecord.js';
import { DxfObjectCode } from './DxfObjectCode.js';
import { DxfTag } from './IO/DxfTag.js';
import { Copy } from '../runtime/GeometryRuntime.js';
import { ReferenceList } from '../runtime/ReferenceList.js';
import { RetainedRecordTypes, RetainedHandleValue } from '../runtime/RetainedPolylineRegistration.js';
import { InvalidOperationException, NotSupportedException } from '../runtime/Errors.js';
const maximum = 9223372036854775807n;

export function InstallDocumentPolylineTopology(Type) {
  Type.prototype.PreparePolylineVertexInsertion = function(parent, position) {
    if (parent.Layer === null || this.GetObjectByHandle(parent.Layer.Handle) !== parent.Layer)
      throw new InvalidOperationException("The inserted VERTEX requires the parent's registered layer.");
    let retainedTags = 10;
    for (const Record of RetainedRecordTypes) for (const record of this.AddedObjects.Values) if (record instanceof Record) {
      const count = record.TopologyTagCount();
      if (count > 4096) throw new NotSupportedException('An existing retained record exceeds its tag admission budget.');
      retainedTags += count;
    }
    if (retainedTags > 1048576) throw new NotSupportedException("The inserted VERTEX exceeds the document's retained tag admission budget.");
    const occupied = new Set();
    for (const item of this.RetainedMetadataObjects()) {
      const value = RetainedHandleValue(item.Handle);
      if (value !== null) occupied.add(value);
    }
    let number = this.NumHandles;
    while (number > 0n && number < maximum && occupied.has(number)) number++;
    if (number <= 0n || number === maximum) throw new InvalidOperationException('No VERTEX identity can be allocated from the current handle seed.');
    const handle = number.toString(16).toUpperCase();
    const tags = new ReferenceList([
      new DxfTag(5, handle), new DxfTag(330, parent.Handle),
      new DxfTag(100, 'AcDbEntity'), new DxfTag(8, parent.Layer.Name),
      new DxfTag(100, 'AcDbVertex'), new DxfTag(100, 'AcDb3dPolylineVertex'),
      new DxfTag(10, position.X), new DxfTag(20, position.Y), new DxfTag(30, position.Z), new DxfTag(70, 32)
    ]);
    const result = Object.assign(new Polyline3DRecord(DxfObjectCode.Vertex, tags), {
      Owner: parent, Handle: handle, SourceOwner: parent.Handle, SourceDocument: this,
      SourceVersion: parent.EndSequenceRecord.SourceVersion, CommonEnd: 2,
      IdentityIndex: 0, OwnerIndex: 1, Position: Copy(position), IsAuthored: true
    });
    result.Resources.set(3, parent.Layer); result.OriginalResourceNames.set(3, parent.Layer.Name);
    result.Coordinates.set(10, 6); result.Coordinates.set(20, 7); result.Coordinates.set(30, 8);
    return result; // Authored record, never an accepted physical source identity.
  };
  Type.prototype.RegisterPolylineVertexInsertion = function(record) {
    this.AddedObjects.Add(record.Handle, record);
    this.NumHandles = RetainedHandleValue(record.Handle) + 1n;
  };
  Type.prototype.ValidatePolylineVertexRemoval = function(record) {
    if (record.IsRemoved || this.GetObjectByHandle(record.Handle) !== record)
      throw new InvalidOperationException('The removed VERTEX must be registered in its source document.');
    if (this.StoredTableReferencesRemoval(record))
      throw new NotSupportedException('Removing this VERTEX requires resolving incoming references or private/owned payload first.');
    for (const data of record.XData.Values)
      if (!this.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name))
        throw new InvalidOperationException('The removed VERTEX has inconsistent APPID reference bookkeeping.');
  };
  Type.prototype.UnregisterPolylineVertexRemoval = function(record) { this.AddedObjects.Remove(record.Handle); };
}
