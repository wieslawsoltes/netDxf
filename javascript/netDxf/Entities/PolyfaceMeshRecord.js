// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfObject } from '../DxfObject.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Layer } from '../Tables/Layer.js';
import { Linetype } from '../Tables/Linetype.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { IsStoredReference, HasNonzeroXDataReference, SameSequence } from '../../runtime/StoredRecord.js';
import { InvalidCastException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';

/** Original internal-constructor record model. Creating this does not register a DXF record. */
export class PolyfaceMeshRecord extends DxfObject {
  Tags; Resources = new Map(); OriginalResourceNames = new Map(); Coordinates = new Map(); MetadataGroups = new Map();
  ReactorHandles = new ReferenceList(); OriginalReactors = new ReferenceList(); ExtensionHandle = null; OriginalExtension = null;
  SourceOwner = null; CommonEnd = 0; XDataStart; Position = Vector3.Zero; SourceDocument = null;
  IdentityIndex = 0; OwnerIndex = 0; HasPrivateData = false; CoordinateIndex = -1; FaceIndex = -1;
  FaceSlots = new Map(); OriginalIndexes = null; StoredFace = null; OriginalColor = null;
  FaceLayerIndex = -1; FaceCommonEnd = -1; FaceColorIndices = new Set();
  IsFaceRecord = false; SourceVersion = 0; UsesBlockRecordOwner = false;
  constructor(codeName, tags) { super(codeName); this.Tags = tags; this.XDataStart = tags.Count ?? tags.length; }
  get Face() { return this.StoredFace; }
  get FaceColorUnchanged() {
    const face = this.Face, original = this.OriginalColor;
    return face == null || (face.Color == null && original == null) ||
      (face.Color != null && original != null && face.Color.Index === original.Index && face.Color.UseTrueColor === original.UseTrueColor && face.Color.Equals(original));
  }
  get FaceIndexesUnchanged() { return this.Face == null || SameSequence(this.Face.VertexIndexes, this.OriginalIndexes); }
  get Layer() { return this.Face != null ? this.Face.Layer : Array.from(this.Resources.values()).find(v => v instanceof Layer) ?? null; }
  get Linetype() { return Array.from(this.Resources.values()).find(v => v instanceof Linetype) ?? null; }
  get IsSequenceEnd() { return this.CodeName === DxfObjectCode.EndSequence; }
  get StoredOwner() {
    if (!this.UsesBlockRecordOwner) return this.Owner;
    const block = this.Owner?.Owner;
    return block?.CodeName === DxfObjectCode.Block ? block.Record ?? null : null;
  }
  get References() {
    const resources = Array.from(this.Resources.values());
    return this.Face == null ? resources : resources.filter(v => !(v instanceof Layer)).concat(this.Face.Layer == null ? [] : [this.Face.Layer]);
  }
  get OpaqueHandleTags() {
    const record = this;
    return { *[Symbol.iterator]() {
      for (let i = 0; i < record.XDataStart; i++) {
        if (record.MetadataGroups.has(i)) { i = record.MetadataGroups.get(i); continue; }
        const tag = record.Tags.get_Item ? record.Tags.get_Item(i) : record.Tags[i];
        if (i !== record.IdentityIndex && i !== record.OwnerIndex && !record.Resources.has(i) && IsStoredReference(tag)) yield tag;
      }
    } };
  }
  Validate(document, owner, registered) {
    if (document.DrawingVariables.AcadVer !== this.SourceVersion) throw new NotSupportedException('Retained record version conversion requires schema regeneration.');
    if ((this.SourceDocument != null && this.SourceDocument !== document) || this.Owner !== owner) throw new InvalidOperationException('Retained polyface source ownership changed.');
    const current = this.Handle == null ? null : document.GetObjectByHandle(this.Handle);
    if (registered ? current !== this : current != null && current !== this) throw new InvalidOperationException('Inconsistent retained record registration.');
    if (registered && (this.StoredOwner == null || document.GetObjectByHandle(this.StoredOwner.Handle) !== this.StoredOwner)) throw new InvalidOperationException('Invalid retained stored owner.');
    if (this.SourceDocument == null && !registered) return;
    for (const target of this.References) if (document.GetObjectByHandle(target.Handle) !== target) throw new InvalidOperationException('Retained resource is outside its source document.');
  }
  CanClone() {
    return !this.HasPrivateData && this.ExtensionDictionary == null && this.PersistentReactors.Count === 0 &&
      Array.from(this.Resources.values()).every(r => r instanceof Layer || r instanceof Linetype) && !HasNonzeroXDataReference(this);
  }
  TopologyTagCount() {
    let count = this.XDataStart;
    if (this.Face != null) {
      count += (this.Face.Layer == null ? 0 : 1) - (this.FaceLayerIndex < 0 ? 0 : 1);
      if (!this.FaceColorUnchanged) count += (this.Face.Color == null ? 0 : this.Face.Color.UseTrueColor ? 2 : 1) - this.FaceColorIndices.size;
    }
    let extension = false, reactors = false;
    for (const [first, last] of this.MetadataGroups) {
      const tag = this.Tags.get_Item ? this.Tags.get_Item(first) : this.Tags[first];
      const isExtension = tag.Value === '{ACAD_XDICTIONARY';
      const unchanged = isExtension ? this.ExtensionDictionary === this.OriginalExtension :
        SameSequence(this.PersistentReactors, this.OriginalReactors) && SameSequence(Array.from(this.ReactorHandles).filter(h => h !== '0'), Array.from(this.PersistentReactors, t => t.Handle));
      if (!unchanged) {
        count -= last - first + 1;
        count += isExtension ? (this.ExtensionDictionary == null ? 0 : 3) : (this.PersistentReactors.Count === 0 ? 0 : this.PersistentReactors.Count + 2);
      }
      if (isExtension) extension = true; else reactors = true;
    }
    if (!extension && this.ExtensionDictionary != null) count += 3;
    if (!reactors && this.PersistentReactors.Count !== 0) count += this.PersistentReactors.Count + 2;
    for (const data of this.XData.Values) count += 1 + data.XDataRecord.Count;
    return count;
  }
  CopyForClone(face = null) {
    const copy = new PolyfaceMeshRecord(this.CodeName, new ReferenceList(this.Tags));
    for (const name of ['SourceOwner','CommonEnd','XDataStart','Position','IdentityIndex','OwnerIndex','ExtensionHandle','UsesBlockRecordOwner','SourceVersion','CoordinateIndex','FaceIndex','IsFaceRecord','FaceLayerIndex','FaceCommonEnd']) copy[name] = Copy(this[name]);
    copy.OriginalIndexes = this.OriginalIndexes == null ? null : FixedArray(this.OriginalIndexes);
    copy.StoredFace = face; copy.OriginalColor = this.OriginalColor == null ? null : this.OriginalColor.Clone();
    for (const index of this.FaceColorIndices) copy.FaceColorIndices.add(index);
    for (const name of ['FaceSlots','Coordinates','MetadataGroups','OriginalResourceNames']) for (const [key, value] of this[name]) copy[name].set(key, value);
    copy.ReactorHandles.AddRange(this.ReactorHandles);
    for (const [key, value] of this.Resources) {
      if (value instanceof Layer) {
        const layer = face == null ? value.Clone() : face.Layer;
        if (layer != null) copy.Resources.set(key, layer);
      } else {
        if (!(value instanceof Linetype)) throw new InvalidCastException();
        copy.Resources.set(key, value.Clone());
      }
    }
    for (const data of this.XData.Values) copy.XData.Add(data.Clone());
    return copy;
  }
}
