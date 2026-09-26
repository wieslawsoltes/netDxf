// netDxf library. Copyright (c) Daniel Carvajal. Licensed under the MIT License.
import { DxfObjectText } from './DxfRawObjectStore.js';
import { ReadOnlyList, OrdinalIgnoreCaseKey } from '../../runtime/Collections.js';
import { ArgumentNullException, RequireInteger } from '../../runtime/Errors.js';

export const DxfDuplicateRecordCloning = Object.freeze({
  NotApplicable: 0, KeepExisting: 1, UseClone: 2, XrefMangleName: 3, MangleName: 4, UnmangleName: 5,
});
export class DxfRawDictionaryEntry {
  constructor(name, handle, hardOwner = false) {
    DxfObjectText.ValidateName(name);
    this.Name = name; this.Handle = DxfObjectText.Handle(handle, false); this.IsHardOwner = hardOwner;
    Object.freeze(this);
  }
}
export class DxfRawSortOrderEntry {
  constructor(entityHandle, sortHandle) {
    this.EntityHandle = DxfObjectText.Handle(entityHandle, false);
    this.SortHandle = DxfObjectText.Handle(sortHandle, true);
    Object.freeze(this);
  }
}
export class DxfRawStoredObject {
  constructor(type, handle, owner, source, tags, bodyStart, bodyEnd) {
    this.TypeName = type; this.Handle = handle; this.OwnerHandle = owner;
    this.SourceRecord = source; this.Tags = tags;
    // Internal schema offsets; not part of the supported public migration surface.
    this.BodyStart = bodyStart; this.BodyEnd = bodyEnd;
  }
}
export class DxfRawOpaqueStoredObject extends DxfRawStoredObject {
  constructor(type, handle, owner, source, tags, reason) {
    super(type, handle, owner, source, tags, 0, 0); this.Reason = reason; Object.freeze(this);
  }
}
export class DxfRawDictionary extends DxfRawStoredObject {
  #byName;
  constructor(type, handle, owner, source, tags, start, end, hardOwner, cloning, defaultHandle, entries) {
    super(type, handle, owner, source, tags, start, end);
    this.HardOwnerFlag = hardOwner; this.CloningFlag = cloning; this.DefaultHandle = defaultHandle;
    this.Entries = ReadOnlyList(entries);
    this.#byName = new Map(this.Entries.map(e => [OrdinalIgnoreCaseKey(e.Name), e])); Object.freeze(this);
  }
  get HasDefault() { return this.TypeName === 'ACDBDICTIONARYWDFLT'; }
  Find(name) {
    if (name == null) throw new ArgumentNullException('name');
    return this.#byName.get(OrdinalIgnoreCaseKey(name)) ?? null;
  }
}
export class DxfRawXRecord extends DxfRawStoredObject {
  constructor(handle, owner, source, tags, start, end, cloning, data) {
    super('XRECORD', handle, owner, source, tags, start, end);
    this.CloningFlag = cloning; this.Data = ReadOnlyList(data); Object.freeze(this);
  }
}
export class DxfRawPlaceholder extends DxfRawStoredObject {
  constructor(handle, owner, source, tags, end) { super('ACDBPLACEHOLDER', handle, owner, source, tags, end, end); Object.freeze(this); }
}
export class DxfRawDictionaryVariable extends DxfRawStoredObject {
  constructor(handle, owner, source, tags, start, end, schema, value) {
    super('DICTIONARYVAR', handle, owner, source, tags, start, end);
    this.SchemaNumber = schema; this.Value = value; Object.freeze(this);
  }
}
export class DxfRawIdBuffer extends DxfRawStoredObject {
  constructor(handle, owner, source, tags, start, end, handles) {
    super('IDBUFFER', handle, owner, source, tags, start, end); this.Handles = ReadOnlyList(handles); Object.freeze(this);
  }
}
export class DxfRawSortentsTable extends DxfRawStoredObject {
  constructor(handle, owner, source, tags, start, end, block, entries) {
    super('SORTENTSTABLE', handle, owner, source, tags, start, end);
    this.BlockRecordHandle = block; this.Entries = ReadOnlyList(entries); Object.freeze(this);
  }
}
export class DxfRawObjectStoreOptions {
  constructor(maximumObjects = 100000, maximumPayloadTags = 1000000, maximumChanges = 100000) {
    this.MaximumObjects = RequireInteger(maximumObjects, 1, 2147483647, 'maximumObjects');
    this.MaximumPayloadTags = RequireInteger(maximumPayloadTags, 1, 2147483647, 'maximumPayloadTags');
    this.MaximumChanges = RequireInteger(maximumChanges, 1, 2147483647, 'maximumChanges');
    Object.freeze(this);
  }
}
