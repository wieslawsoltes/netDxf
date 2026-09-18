// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { RequireInertIdentity, CopyInertEntity } from '../../runtime/InertEntity.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
/** Uninterpreted legacy OLE bytes. This class never activates or resolves embedded content. */
export class OleFrame extends EntityObject {
  #bytes; #version; #present;
  constructor(binaryData, oleVersion = 1, copyData = true, hasOleVersion = true) {
    super(EntityType.OleFrame, DxfObjectCode.OleFrame);
    if (binaryData == null) throw new ArgumentNullException('binaryData');
    if (!(binaryData instanceof Uint8Array)) throw new ArgumentException('Binary data must be Uint8Array.', 'binaryData');
    if (oleVersion < 0) throw new ArgumentOutOfRangeException('oleVersion');
    RequireInteger(oleVersion, 0, 32767, 'oleVersion');
    this.#bytes = copyData ? new Uint8Array(binaryData) : binaryData;
    this.#version = oleVersion; this.#present = hasOleVersion;
  }
  get OleVersion() { return this.#version; }
  get HasOleVersion() { return this.#present; }
  get BinaryDataLength() { return this.#bytes.length; }
  GetBinaryData() { return new Uint8Array(this.#bytes); }
  /** Internal adapter, as in C#: only GetBinaryData provides a public defensive snapshot. */
  get BinaryData() { return this.#bytes; }
  WithOleVersionPresence(present) { return CopyInertEntity(this, new OleFrame(this.#bytes, this.#version, true, present)); }
  // Matrix4 follows the inherited affine adapter; unlike ACIS, the source does not override it.
  TransformBy(transformation, translation) { RequireInertIdentity(this, transformation, translation); }
  Clone() { return this.WithOleVersionPresence(this.#present); }
}
