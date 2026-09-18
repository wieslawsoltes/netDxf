// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { Vector3 } from '../Vector3.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReadOnlyArrayView, HasNonzeroXDataReference, IsStoredReference } from '../../runtime/StoredRecord.js';
import { InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const storage = new WeakMap();
function state(mesh) {
  if (!storage.has(mesh)) storage.set(mesh, { document: null, sequence: ReadOnlyArrayView([]), vertices: ReadOnlyArrayView([]), faces: ReadOnlyArrayView([]), end: null });
  return storage.get(mesh);
}
function validateNormal(mesh) {
  const n = mesh.Normal;
  if (![n.X,n.Y,n.Z].every(Number.isFinite) || Vector3.IsZero(n)) throw new InvalidOperationException('A polyface normal must be finite and nonzero.');
}
function validateGeometry(mesh) {
  mesh.ValidateFaceIndexes();
  for (const point of mesh.Vertexes) if (![point.X,point.Y,point.Z].every(Number.isFinite)) throw new InvalidOperationException('Retained polyface coordinates must be finite.');
  let i = 0;
  for (const record of mesh.FaceRecords) if (mesh.Faces.get_Item(i++) !== record.Face) throw new NotSupportedException('Replacing a retained face requires topology regeneration.');
}
export function InstallPolyfaceStoredRecords(Type) {
  Object.defineProperties(Type.prototype, {
    VertexRecords: { get() { return state(this).vertices; } }, FaceRecords: { get() { return state(this).faces; } },
    RecordSequence: { get() { return state(this).sequence; } }, EndSequenceRecord: { get() { return state(this).end; } },
    HasStoredRecords: { get() { return state(this).end != null; } }, StoredRecords: { get() { return state(this).sequence; } },
    StoredHeaderReferences: { get() { return this.StoredHeaderTags == null ? [] : Array.from(this.StoredHeaderTags).filter(IsStoredReference); } }
  });
  Type.prototype.SetStoredRecords = function(document, sequence) {
    const s = state(this); s.document = document; s.sequence = ReadOnlyArrayView(sequence);
    s.vertices = ReadOnlyArrayView(Array.from(sequence).filter(r => !r.IsFaceRecord && !r.IsSequenceEnd));
    s.faces = ReadOnlyArrayView(Array.from(sequence).filter(r => r.IsFaceRecord));
    if (!sequence.length) throw new InvalidOperationException('Sequence contains no elements.');
    s.end = sequence[sequence.length - 1];
    for (const record of sequence) record.Owner = this;
  };
  Type.prototype.BindStoredRecordDocument = function(document) { state(this).document = document; };
  Type.prototype.ValidateStoredRecords = function(document, registered) {
    validateNormal(this); if (!this.HasStoredRecords) return;
    if (state(this).document != null && document !== state(this).document) throw new NotSupportedException('Retained polyface records cannot change documents.');
    if (this.Vertexes.length !== this.VertexRecords.Count || this.Faces.Count !== this.FaceRecords.Count) throw new NotSupportedException('Retained polyface count change requires topology regeneration.');
    validateGeometry(this);
    for (const record of this.StoredRecords) record.Validate(document, this, registered);
  };
  Type.prototype.RejectStoredRecordClone = function() {
    validateNormal(this); if (!this.HasStoredRecords) return;
    validateGeometry(this);
    if (this.HasPrivateHeader || this.ExtensionDictionary != null || this.PersistentReactors.Count !== 0 || HasNonzeroXDataReference(this) || Array.from(this.StoredRecords).some(r => !r.CanClone()))
      throw new NotSupportedException('Cloning private, owned or external retained polyface dependencies requires complete graph mapping.');
  };
  Type.prototype.CopyStoredRecordsTo = function(clone) {
    if (!this.HasStoredRecords) return;
    this.RejectStoredRecordClone();
    clone.SetStoredRecords(null, Array.from(this.StoredRecords, r => r.CopyForClone(r.IsFaceRecord ? clone.Faces.get_Item(r.FaceIndex) : null)));
    clone.StoredHeaderTags = new ReferenceList(this.StoredHeaderTags); clone.StoredNormal = Copy(this.StoredNormal); clone.StoredHeaderPublicEnd = this.StoredHeaderPublicEnd;
    for (const [key, value] of this.StoredNormalIndices) clone.StoredNormalIndices.set(key, value);
    clone.DeclaredVertexCount = this.DeclaredVertexCount; clone.DeclaredFaceCount = this.DeclaredFaceCount;
  };
}
