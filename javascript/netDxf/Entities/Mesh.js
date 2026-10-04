// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Vector3 } from '../Vector3.js';
import { Matrix3 } from '../Matrix3.js';
import { ValueList } from '../../runtime/ValueList.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { TransformedNormal } from '../../runtime/EntityGeometry.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, NullReferenceException, RequireInteger } from '../../runtime/Errors.js';
export class Mesh extends EntityObject {
  #vertexes; #faces; #edges; #subdivisionLevel = 0; BlendCrease = false;
  constructor(vertexes, faces, edges = null) {
    super(EntityType.Mesh, DxfObjectCode.Mesh);
    if (![2, 3].includes(arguments.length)) throw new ArgumentException('No matching Mesh constructor.');
    if (vertexes == null) throw new ArgumentNullException('vertexes');
    this.#vertexes = new ValueList(vertexes);
    if (faces == null) throw new ArgumentNullException('faces');
    this.#faces = new ReferenceList(faces);
    if (this.#faces.Count > 16000000) throw new ArgumentOutOfRangeException('faces', this.#faces.Count);
    this.#edges = new ReferenceList(edges ?? []);
  }
  get Vertexes() { return this.#vertexes; }
  get Faces() { return this.#faces; }
  get Edges() { return this.#edges; }
  get SubdivisionLevel() { return this.#subdivisionLevel; }
  set SubdivisionLevel(value) { this.#subdivisionLevel = RequireInteger(value, 0, 255); }
  TransformBy(transformation, translation) {
    [transformation, translation] = this.$transformArguments(transformation, translation);
    for (let i = 0; i < this.#vertexes.Count; i++) {
      this.#vertexes.set_Item(i, Vector3.Add(Matrix3.Multiply(transformation, this.#vertexes.get_Item(i)), translation));
    }
    this.Normal = TransformedNormal(transformation, this.Normal);
  }
  Clone() {
    const faces = Array.from(this.#faces, face => { if (face == null) throw new NullReferenceException(); return Array.from(face); });
    const edges = Array.from(this.#edges, edge => { if (edge == null) throw new NullReferenceException(); return edge.Clone(); });
    const copy = this.$copyEntityAttributes(new Mesh(this.#vertexes, faces, edges));
    copy.SubdivisionLevel = this.#subdivisionLevel; copy.BlendCrease = this.BlendCrease;
    return this.$finishEntityClone(copy);
  }
}
