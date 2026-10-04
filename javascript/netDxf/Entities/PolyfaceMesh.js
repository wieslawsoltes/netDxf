// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { PolyfaceMeshFace } from './PolyfaceMeshFace.js';
import { PolylineTypeFlags } from './PolylineTypeFlags.js';
import { Point } from './Point.js';
import { Line } from './Line.js';
import { Face3D } from './Face3D.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { TableObjectChangedEventArgs } from '../Tables/TableObjectChangedEventArgs.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { FixedArray } from '../../runtime/FixedArray.js';
import { ReadOnlyArrayView } from '../../runtime/StoredRecord.js';
import { EventHook } from '../../runtime/EventHook.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { InstallPolyfaceStoredRecords } from './PolyfaceMesh.StoredRecords.js';

export class PolyfaceMesh extends EntityObject {
  #vertexes; #faces; #faceView;
  #faceLayerChanged = (sender, event) => { event.NewValue = this.OnPolyfaceMeshFaceLayerChangedEvent(event.OldValue, event.NewValue); };
  Flags = PolylineTypeFlags.PolyfaceMesh; DeclaredVertexCount = null; DeclaredFaceCount = null;
  StoredHeaderTags = null; StoredNormalIndices = new Map(); StoredNormal = Vector3.Zero; HasPrivateHeader = false; StoredHeaderPublicEnd = 0;
  constructor(vertexes, faces, overload = null) {
    super(EntityType.PolyfaceMesh, DxfObjectCode.Polyline);
    Object.defineProperty(this, 'PolyfaceMeshFaceLayerChanged', { value: new EventHook(), enumerable: true });
    if (vertexes == null) throw new ArgumentNullException('vertexes');
    this.#vertexes = FixedArray(vertexes);
    if (this.#vertexes.length < 3) throw new ArgumentOutOfRangeException('vertexes', this.#vertexes.length);
    if (faces == null) throw new ArgumentNullException('faces');
    const values = overload === 'indices' ? null : Array.from(faces);
    const raw = overload === 'indices' || (overload == null && values.some(f => f != null && !(f instanceof PolyfaceMeshFace)));
    if (raw) {
      const collection = values ?? faces;
      const count = collection.Count ?? collection.length ?? Array.from(collection).length;
      const element = index => {
        if (collection.get_Item) return collection.get_Item(index);
        if (Array.isArray(collection)) return collection[index];
        let i = 0; for (const item of collection) if (i++ === index) return item;
        throw new ArgumentOutOfRangeException('index', index);
      };
      this.#faces = Array.from({length:count}, (_, i) => new PolyfaceMeshFace(element(i)));
    } else this.#faces = values;
    this.#faceView = ReadOnlyArrayView(this.#faces);
    if (!this.#faces.length) throw new ArgumentOutOfRangeException('vertexes', 0);
    this.ValidateFaceIndexes();
    for (const face of this.#faces) face.LayerChanged.Add(this.#faceLayerChanged);
  }
  static CreateOverload(signature, ...args) {
    if (signature === 'System.Collections.Generic.IEnumerable<netDxf.Vector3>,System.Collections.Generic.IEnumerable<short[]>') return new PolyfaceMesh(...args, 'indices');
    if (signature === 'System.Collections.Generic.IEnumerable<netDxf.Vector3>,System.Collections.Generic.IEnumerable<netDxf.Entities.PolyfaceMeshFace>') return new PolyfaceMesh(...args, 'faces');
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
  get Vertexes() { return this.#vertexes; }
  get Faces() { return this.#faceView; }
  OnPolyfaceMeshFaceLayerChangedEvent(oldValue, newValue) {
    const event = new TableObjectChangedEventArgs(oldValue, newValue); this.PolyfaceMeshFaceLayerChanged.Invoke(this, event); return event.NewValue;
  }
  ValidateFaceIndexes() {
    for (const face of this.#faces) {
      if (face == null) throw new ArgumentException('A polyface mesh cannot contain a null face.', 'Faces');
      face.ValidateVertexIndexes(this.#vertexes.length);
    }
  }
  Explode() {
    const result = new ReferenceList();
    for (const face of this.#faces) {
      const count = face.ValidateVertexIndexes(this.#vertexes.length), color = face.Color ?? this.Color, layer = face.Layer ?? this.Layer;
      const entity = count === 1 ? new Point() : count === 2 ? new Line() : new Face3D();
      // Source Explode intentionally does not copy visibility, XData or proxy/common metadata.
      Object.assign(entity, { Layer: layer.Clone(), Linetype: this.Linetype.Clone(), Color: color.Clone(), Lineweight: this.Lineweight,
        Transparency: this.Transparency.Clone(), LinetypeScale: this.LinetypeScale, Normal: this.Normal });
      const point = i => this.#vertexes[Math.abs(face.VertexIndexes[i]) - 1];
      if (count === 1) entity.Position = point(0);
      else if (count === 2) { entity.StartPoint = point(0); entity.EndPoint = point(1); }
      else {
        const fourth = count === 3 ? 2 : 3;
        Object.assign(entity, { FirstVertex: point(0), SecondVertex: point(1), ThirdVertex: point(2), FourthVertex: point(fourth),
          EdgeFlags: (face.VertexIndexes[0] < 0 ? 1 : 0) | (face.VertexIndexes[1] < 0 ? 2 : 0) | (face.VertexIndexes[2] < 0 ? 4 : 0) | (face.VertexIndexes[fourth] < 0 ? 8 : 0) });
      }
      result.Add(entity);
    }
    return result;
  }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    for (let i = 0; i < this.#vertexes.length; i++) this.#vertexes[i] = Vector3.Add(Matrix3.Multiply(transformation, this.#vertexes[i]), translation);
    this.Normal = TransformedNormal(transformation, this.Normal);
  }
  Clone() {
    this.RejectStoredRecordClone();
    const copy = this.$copyEntityAttributes(new PolyfaceMesh(this.#vertexes, this.#faces.map(face => face.Clone()), 'faces'));
    copy.Flags = this.Flags; this.$finishEntityClone(copy); this.CopyStoredRecordsTo(copy); return copy;
  }
}
InstallPolyfaceStoredRecords(PolyfaceMesh);
