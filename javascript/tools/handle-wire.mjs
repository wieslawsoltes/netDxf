import { DxfRawDocument, DxfRawHandleIndex, DxfRawHandleIndexOptions, DxfRawOptions } from '../index.js';
import { fromWire, wireTag, jsRaw, attempt, base64ToBytes } from './wire.mjs';
import { ArgumentException } from '../runtime/Errors.js';
export function jsHandles(input) {
  return attempt(() => {
    const o = input.options, options = o ? new DxfRawOptions(o.maximumBytes, o.maximumTags, o.maximumStringLength) : null;
    const doc = input.tags ? DxfRawDocument.Create(input.tags.map(fromWire), input.binary ?? false, options)
      : DxfRawDocument.Load(base64ToBytes(input.bytes), options);
    const io = input.indexOptions;
    const index = DxfRawHandleIndex.Create(doc, io ? new DxfRawHandleIndexOptions(io.maximumOccurrences, io.maximumDiagnostics) : null);
    let closure = null, remapped = null;
    if (input.roots) closure = attempt(() => {
      const records = new Map(doc.Sections.flatMap(s => s.Records).map(r => [r.StartTagIndex, r]));
      const c = index.GetDependencyClosure(input.roots.map(at => records.get(at)), input.traversal);
      return { records: c.Records.map(r => r.StartTagIndex), unresolved: c.UnresolvedReferences.map(x => x.TagIndex),
        ambiguous: c.AmbiguousReferences.map(x => x.TagIndex), opaque: c.UninterpretedHandles.map(x => x.TagIndex), resolved: c.AreSelectedReferencesResolved };
    });
    if (input.mapping) remapped = attempt(() => {
      const mapping = new Map(input.mapping);
      if (mapping.size !== input.mapping.length) throw new ArgumentException('Duplicate mapping key.');
      const result = index.RemapHandles(mapping);
      // Construct the same raw snapshot shape without losing the original-byte flag on no-ops.
      const snapshot = result === doc ? jsRaw(input) : jsRaw({ tags: result.Tags.map(wireTag), binary: result.IsBinary });
      if (!snapshot.ok) throw new Error('Cannot snapshot remapped document: ' + snapshot.error);
      return { unchanged: result === doc, document: snapshot.value };
    });
    return { occurrences: index.Occurrences.map(x => ({ record: x.Record?.StartTagIndex ?? null, index: x.TagIndex,
      code: x.Code, handle: x.Handle, canonical: x.CanonicalHandle, numeric: x.NumericHandle.toString(), role: x.Role,
      context: x.Context, subclass: x.Subclass, reference: x.IsReference })),
    diagnostics: index.Diagnostics.map(d => ({ kind: d.Kind, record: d.Record?.StartTagIndex ?? null,
      index: d.TagIndex, handle: d.Handle, message: d.Message })), closure, remapped };
  });
}
