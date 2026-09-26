// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { EntityObject } from './EntityObject.js';
import { EntityType } from './EntityType.js';
import { DxfObjectCode } from '../DxfObjectCode.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { RequireInertIdentity, CopyInertEntity } from '../../runtime/InertEntity.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, RequireInteger } from '../../runtime/Errors.js';
export const OleObjectType = Object.freeze({ Link: 1, Embedded: 2, Static: 3 });
export const Ole2FrameMetadataFields = Object.freeze({ None: 0, OleVersion: 1, Description: 2, UpperLeftCorner: 4, LowerRightCorner: 8, ObjectType: 16, TileMode: 32, All: 63 });
function point(value, parameter) {
  if (![value.X, value.Y, value.Z].every(Number.isFinite)) throw new ArgumentOutOfRangeException(parameter);
}
/** Immutable informational metadata and inert bytes, never COM activation or link resolution. */
export class Ole2Frame extends EntityObject {
  #bytes; #upper; #lower; #description; #version; #type; #tile; #fields;
  constructor(binaryData, upperLeftCorner, lowerRightCorner, description = '', oleVersion = 2, objectType = OleObjectType.Embedded, tileMode = 0, copyData = true, metadataFields = Ole2FrameMetadataFields.All) {
    super(EntityType.Ole2Frame, DxfObjectCode.Ole2Frame);
    if (binaryData == null) throw new ArgumentNullException('binaryData');
    if (description == null) throw new ArgumentNullException('description');
    if (/[\r\n\0]/.test(description)) throw new ArgumentException('OLE description cannot contain transport delimiters.', 'description');
    point(upperLeftCorner, 'upperLeftCorner'); point(lowerRightCorner, 'lowerRightCorner');
    if (oleVersion < 0) throw new ArgumentOutOfRangeException('oleVersion');
    if (objectType < 1 || objectType > 3) throw new ArgumentOutOfRangeException('objectType');
    if (tileMode !== 0 && tileMode !== 1) throw new ArgumentOutOfRangeException('tileMode');
    if ((metadataFields & ~63) !== 0) throw new ArgumentOutOfRangeException('metadataFields');
    if (!(binaryData instanceof Uint8Array)) throw new ArgumentException('Binary data must be Uint8Array.', 'binaryData');
    RequireInteger(oleVersion, 0, 32767, 'oleVersion'); RequireInteger(objectType, 1, 3, 'objectType'); RequireInteger(metadataFields, 0, 63, 'metadataFields');
    this.#bytes = copyData ? new Uint8Array(binaryData) : binaryData;
    this.#upper = Copy(upperLeftCorner); this.#lower = Copy(lowerRightCorner);
    this.#description = description; this.#version = oleVersion; this.#type = objectType; this.#tile = tileMode; this.#fields = metadataFields;
  }
  get MetadataFields() { return this.#fields; }
  get UpperLeftCorner() { return Copy(this.#upper); }
  get LowerRightCorner() { return Copy(this.#lower); }
  get Description() { return this.#description; }
  get OleVersion() { return this.#version; }
  get ObjectType() { return this.#type; }
  get TileMode() { return this.#tile; }
  get BinaryDataLength() { return this.#bytes.length; }
  GetBinaryData() { return new Uint8Array(this.#bytes); }
  /** Internal borrowed buffer, not the defensive public GetBinaryData snapshot. */
  get BinaryData() { return this.#bytes; }
  WithMetadataFields(metadataFields) {
    return CopyInertEntity(this, new Ole2Frame(this.#bytes, this.#upper, this.#lower, this.#description, this.#version, this.#type, this.#tile, true, metadataFields));
  }
  TransformBy(transformation, translation) { RequireInertIdentity(this, transformation, translation); }
  Clone() { return this.WithMetadataFields(this.#fields); }
}
