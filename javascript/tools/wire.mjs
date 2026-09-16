import { DxfTag, DxfTagValueType as T, DxfRawDocument, DxfRawOptions } from '../index.js';
const view = new DataView(new ArrayBuffer(8));
export function doubleBits(value) { view.setFloat64(0, value, false); return view.getBigUint64(0, false).toString(16).padStart(16, '0').toUpperCase(); }
export function fromBits(bits) { view.setBigUint64(0, BigInt('0x' + bits), false); return view.getFloat64(0, false); }
export function wireTag(tag) {
  const value = tag.Value;
  return [tag.Code, tag.ValueType, tag.ValueType === T.Double ? doubleBits(value) :
    typeof value === 'bigint' ? value.toString() : value instanceof Uint8Array ? Buffer.from(value).toString('base64') : value];
}
export function fromWire([code, type, value]) {
  if (code === 5 && type === T.String) return DxfTag.CreateDimensionStyleArrowName(value);
  return new DxfTag(code, type === T.Double ? fromBits(value) : type === T.Int64 ? BigInt(value) :
    type === T.BinaryData ? Uint8Array.from(Buffer.from(value, 'base64')) : value);
}
export function attempt(action) { try { return { ok: true, value: action() }; } catch (error) { return { ok: false, error: error.name }; } }
export function jsRaw(input) {
  return attempt(() => {
    const o = input.options;
    const options = o ? new DxfRawOptions(o.maximumBytes, o.maximumTags, o.maximumStringLength) : null;
    let doc = input.tags ? DxfRawDocument.Create(input.tags.map(fromWire), input.binary ?? false, options)
      : DxfRawDocument.Load(Buffer.from(input.bytes, 'base64'), options);
    if (input.edit) { const tags = Array.from(doc.Tags); tags[input.edit.index] = fromWire(input.edit.tag); doc = doc.WithTags(tags); }
    if (input.normalize) doc = doc.WithTags(doc.Tags);
    return {
      version: doc.Version, binary: doc.IsBinary, original: doc.HasOriginalBytes, codePage: doc.EncodingCodePage,
      tags: doc.Tags.map(wireTag),
      sections: doc.Sections.map(s => ({ name: s.Name, start: s.StartTagIndex, content: s.ContentStartTagIndex, end: s.EndTagIndex,
        preamble: s.Preamble.Count, records: s.Records.map(r => ({ name: r.Name, start: r.StartTagIndex, end: r.EndTagIndex, marker: r.MarkerCode })) })),
      text: attempt(() => Buffer.from(doc.ToBytes(false)).toString('base64')),
      binaryOutput: attempt(() => Buffer.from(doc.ToBytes(true)).toString('base64')),
    };
  });
}
