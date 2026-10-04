import * as M from '../netDxf/IO/DxfRawObjectModel.js';
import { DxfRawDocument, DxfRawOptions, DxfRawObjectStore, MemoryStream } from '../index.js';
import { fromWire, wireTag, jsRaw, attempt, base64ToBytes, bytesToBase64 } from './wire.mjs';

function storedObject(value) {
  let data = null;
  if (value instanceof M.DxfRawDictionary) data = { hardOwnerFlag: value.HardOwnerFlag, cloningFlag: value.CloningFlag,
    hasDefault: value.HasDefault, defaultHandle: value.DefaultHandle,
    entries: value.Entries.map(e => ({ name: e.Name, handle: e.Handle, isHardOwner: e.IsHardOwner })) };
  else if (value instanceof M.DxfRawXRecord) data = { cloningFlag: value.CloningFlag, tags: value.Data.map(wireTag) };
  else if (value instanceof M.DxfRawDictionaryVariable) data = { schemaNumber: value.SchemaNumber, value: value.Value };
  else if (value instanceof M.DxfRawIdBuffer) data = { handles: value.Handles };
  else if (value instanceof M.DxfRawSortentsTable) data = { blockRecordHandle: value.BlockRecordHandle,
    entries: value.Entries.map(e => ({ entityHandle: e.EntityHandle, sortHandle: e.SortHandle })) };
  else if (value instanceof M.DxfRawOpaqueStoredObject) data = { opaque: true, reasonAvailable: value.Reason.length > 0 };
  return { kind: value.constructor.name, typeName: value.TypeName, handle: value.Handle,
    ownerHandle: value.OwnerHandle, tags: Array.from(value.Tags, wireTag), data };
}
export function jsObjects(input) {
  return attempt(() => {
    const o = input.options, options = o ? new DxfRawOptions(o.maximumBytes, o.maximumTags, o.maximumStringLength) : null;
    let doc = input.tags ? DxfRawDocument.Create(input.tags.map(fromWire), input.binary ?? false, options)
      : DxfRawDocument.Load(base64ToBytes(input.bytes), options);
    const io = input.storeOptions;
    const storeOptions = io ? new M.DxfRawObjectStoreOptions(io.maximumObjects, io.maximumPayloadTags, io.maximumChanges) : null;
    DxfRawObjectStore.Open(doc, storeOptions);
    const results = [], references = {}; let transaction = null;
    const resolve = arg => arg && typeof arg === 'object' && !Array.isArray(arg) && Object.hasOwn(arg, 'ref') ? references[arg.ref] : arg;
    try {
      for (const step of input.steps ?? []) {
        results.push(attempt(() => {
          if (step.method === 'BeginEdit') { transaction?.Dispose(); transaction = DxfRawObjectStore.Open(doc, storeOptions).BeginEdit(); return null; }
          const args = (step.args ?? []).map(resolve);
          if (step.method === 'CreateXRecord') args[2] = args[2]?.map(([c,t,v]) => fromWire([c,t,resolve(v)])) ?? null;
          if (step.method === 'SetXRecord') args[1] = args[1]?.map(([c,t,v]) => fromWire([c,t,resolve(v)])) ?? null;
          if (step.method === 'CreateIdBuffer') args[2] = args[2]?.map(resolve) ?? null;
          if (step.method === 'SetIdBuffer') args[1] = args[1]?.map(resolve) ?? null;
          if (step.method === 'SetDrawOrder') args[1] = args[1]?.map(e => new M.DxfRawSortOrderEntry(resolve(e[0]), resolve(e[1]))) ?? null;
          const value = transaction[step.method](...args);
          if (step.as) references[step.as] = value;
          if (value instanceof DxfRawDocument) { doc = value; return { committed: true }; }
          if (value instanceof M.DxfRawStoredObject) return storedObject(value);
          return value ?? null;
        }));
      }
      const current = DxfRawObjectStore.Open(doc, storeOptions);
      // jsRaw receives the original input on a no-op, otherwise the exact immutable tags.
      const unchanged = input.bytes && doc.HasOriginalBytes;
      const snapshot = jsRaw(unchanged ? { bytes: input.bytes } : { tags: doc.Tags.map(wireTag), binary: doc.IsBinary });
      if (!snapshot.ok) throw new Error('Object snapshot failed: ' + snapshot.error);
      return { results, references, document: snapshot.value, objects: current.Objects.map(storedObject) };
    } finally { transaction?.Dispose(); }
  });
}
