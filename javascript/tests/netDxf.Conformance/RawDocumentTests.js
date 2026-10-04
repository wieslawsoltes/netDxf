// Port of tests/netDxf.Conformance/RawDocumentTests.cs; typed-control case remains unported.
import { DxfTag, DxfRawDocument, DxfRawOptions, DxfVersion, MemoryStream, DxfTagValueType, DxfVersionNotSupportedException } from '../../index.js';
import { Encoding } from '../../runtime/Encoding.js';
import * as E from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SameDoubleBits, SupportedVersions, VersionName, HeaderVersion, BooleanName } from './TestHarness.js';

export function RegisterRawDocumentTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) {
    const suffix = `${VersionName(v)}/${BooleanName(b)}`;
    Run(`raw-document/exact-source/${suffix}`, () => RawDocumentExact(v, b));
    Run(`raw-document/normalized-tags/${suffix}`, () => RawDocumentNormalize(v, b));
    Run(`raw-document/unknown-edit/${suffix}`, () => RawDocumentEdit(v, b));
    Run(`raw-document/section-indexes/${suffix}`, () => RawDocumentSections(v, b));
    Run(`raw-document/nonseekable/${suffix}`, () => RawDocumentNonseekable(v, b));
    Run(`raw-document/limits/${suffix}`, () => RawDocumentLimits(v, b));
    Run(`raw-document/profile-edits/${suffix}`, () => RawDocumentProfile(v, b));
  }
  for (const v of SupportedVersions) {
    Run(`raw-document/comments/${VersionName(v)}`, () => RawDocumentComments(v));
    Run(`raw-document/binary-multiline-string/${VersionName(v)}`, () => RawDocumentMultiline(v));
    Run(`raw-document/bom-and-newlines/${VersionName(v)}`, () => RawDocumentLexical(v));
  }
  for (const cp of [1250, 1251, 1252, 932]) for (const b of [false, true])
    Run(`raw-document/code-page/${cp}/${BooleanName(b)}`, () => RawDocumentCodePage(cp, b));
  for (let s = 0; s < 20; s++) Run(`raw-document/malformed-structure/${s}`, () => RawDocumentMalformed(s));
  Run('raw-document/encoding-and-binary-rejection', RawDocumentInvalidEncoding);
  Run('raw-document/immutable-snapshots', RawDocumentIsolation);
  Run('raw-document/cancellation-and-stream-errors', RawDocumentCancellation);
  Run('raw-document/options', RawDocumentOptions);
  Run('raw-document/normalized-byte-budgets', RawDocumentOutputBudgets);
  Run('raw-document/bootstrap-unicode-budget', RawDocumentBootstrapBudget);
}
export function RawFixtureTags(version, comments = false) {
  const tags = [], T = (code, value) => tags.push(new DxfTag(code, value));
  const Section = name => { T(0, 'SECTION'); T(2, name); };
  if (comments) T(999, 'leading comment');
  Section('HEADER'); T(9, '$ACADVER'); T(1, HeaderVersion(version));
  T(9, '$DWGCODEPAGE'); T(3, 'ANSI_1252'); T(9, '$HANDSEED'); T(5, 'ABCD');
  T(9, '$PROJECTNAME'); T(1, 'ENDSEC'); T(0, 'ENDSEC');
  Section('CLASSES'); T(0, 'CLASS'); T(1, 'FUTURE_ENTITY'); T(2, 'AcDbFutureEntity');
  T(3, 'Future application'); T(90, 19); T(91, 1); T(280, 0); T(281, 1); T(0, 'ENDSEC');
  Section('TABLES'); T(0, 'TABLE'); T(2, 'FUTURE_TABLE'); T(5, '10');
  T(0, 'FUTURE_ENTRY'); T(5, '11'); T(2, 'entry'); T(49, 1.25); T(49, 2.5); T(0, 'ENDTAB'); T(0, 'ENDSEC');
  Section('BLOCKS'); T(0, 'BLOCK'); T(5, '20'); T(2, 'CustomBlock');
  T(0, 'FUTURE_ENTITY'); T(5, '21'); T(330, '20'); T(1, 'block payload'); T(0, 'ENDBLK'); T(5, '22'); T(0, 'ENDSEC');
  Section('ENTITIES'); T(0, 'LINE'); T(5, '100'); T(100, 'AcDbEntity'); T(8, '0');
  T(100, 'AcDbLine'); T(10, 1e-20); T(20, -0); T(30, 0); T(11, 1); T(21, 2); T(31, 3);
  T(0, 'SECTION'); T(5, '101'); T(100, 'AcDbEntity'); T(100, 'AcDbSection'); T(2, 'Cut plane');
  T(0, 'FUTURE_ENTITY'); T(5, '102'); T(102, '{ACAD_REACTORS'); T(330, '201'); T(102, '}');
  T(330, '200'); T(100, 'AcDbFutureEntity'); T(320, 'FEDCBA9876543210'); T(340, '202'); T(350, '203'); T(360, '204');
  T(160, -9223372036854775808n); T(90, 2147483647); T(70, -32768); T(290, true);
  T(310, Uint8Array.of(0, 255, 128, 127)); T(310, new Uint8Array());
  T(1, version >= DxfVersion.AutoCad2007 ? 'Zażółć 東京' : 'Za\\U+017C\\U+00F3\\U+0142\\U+0107 \\U+6771\\U+4EAC');
  if (comments) { T(999, 'EOF'); T(999, 'ENDSEC'); }
  T(1001, 'APP'); T(1002, '{'); T(1004, Uint8Array.of(0, 255)); T(1005, 'FFFFFFFFFFFFFFFF');
  T(1040, Number.MIN_VALUE); T(1070, -1); T(1071, -2147483648); T(1002, '}'); T(0, 'ENDSEC');
  Section('OBJECTS'); T(0, 'FUTURE_OBJECT'); T(5, '200'); T(102, '{ACAD_XDICTIONARY'); T(360, '204'); T(102, '}');
  T(100, 'FutureObjectClass'); T(1, 'SECTION'); T(3, 'ENDSEC'); T(300, 'EOF'); T(0, 'ENDSEC');
  Section('ACDSDATA'); T(0, 'ACDSRECORD'); T(90, 2); T(101, 'opaque schema'); T(310, Uint8Array.of(1, 2, 3)); T(0, 'ENDSEC');
  Section('VENDOR_SECTION'); T(0, 'VENDOR_RECORD'); T(1, 'untouched'); T(460, Number.MAX_VALUE); T(0, 'ENDSEC');
  T(0, 'EOF'); return tags;
}
/** Independent fixture encoder: deliberately does not call production code-value writers. */
export function RawFixtureBytes(tags, binary, encoding = Encoding.UTF8, newline = '\n', legacy = false) {
  if (!binary) {
    let text = '';
    for (const tag of tags) {
      let value = tag.Value;
      if (value instanceof Uint8Array) value = Buffer.from(value).toString('hex').toUpperCase();
      else if (typeof value === 'boolean') value = value ? '1' : '0';
      else if (typeof value === 'number' && Object.is(value, -0)) value = '-0.0';
      text += String(tag.Code) + newline + String(value) + newline;
    }
    return encoding.GetBytes(text);
  }
  const chunks = [Buffer.from('AutoCAD Binary DXF\r\n\x1a\0', 'ascii')];
  for (const tag of tags) {
    const code = Buffer.alloc(legacy ? (tag.Code < 255 ? 1 : 3) : 2);
    if (!legacy) code.writeInt16LE(tag.Code);
    else if (tag.Code < 255) code[0] = tag.Code;
    else { code[0] = 255; code.writeInt16LE(tag.Code, 1); }
    chunks.push(code);
    const value = tag.Value;
    if (typeof value === 'string') chunks.push(encoding.GetBytes(value), Uint8Array.of(0));
    else if (value instanceof Uint8Array) { Check(value.length <= 255); chunks.push(Uint8Array.of(value.length), value); }
    else {
      const buffer = Buffer.alloc(tag.ValueType === DxfTagValueType.Int16 ? 2 : tag.ValueType === DxfTagValueType.Int32 ? 4 : tag.ValueType === DxfTagValueType.Boolean ? 1 : 8);
      if (typeof value === 'bigint') buffer.writeBigInt64LE(value);
      else if (typeof value === 'boolean') buffer[0] = value ? 1 : 0;
      else if (tag.ValueType === DxfTagValueType.Int16) buffer.writeInt16LE(value);
      else if (tag.ValueType === DxfTagValueType.Int32) buffer.writeInt32LE(value);
      else buffer.writeDoubleLE(value);
      chunks.push(buffer);
    }
  }
  return new Uint8Array(Buffer.concat(chunks));
}
export function LoadRaw(data, options = null) {
  const stream = new MemoryStream(data), raw = DxfRawDocument.Load(stream, options);
  Check(stream.CanRead, 'Raw loader closed caller stream.'); return raw;
}
export function SaveRaw(raw, binary = raw.IsBinary) {
  const stream = new MemoryStream(); raw.Save(stream, binary);
  Check(stream.CanWrite, 'Raw saver closed caller stream.'); return stream.ToArray();
}
export function SameRawTags(expected, actual) {
  expected = Array.from(expected); actual = Array.from(actual);
  Equal(expected.length, actual.length, 'Raw tag count');
  for (let i = 0; i < expected.length; i++) {
    Equal(expected[i].Code, actual[i].Code, 'Raw tag order/code ' + i);
    Equal(expected[i].ValueType, actual[i].ValueType, 'Raw type ' + i);
    Equal(expected[i].Value, actual[i].Value, 'Raw value ' + i);
  }
}
export function RawDocumentExact(v, b) {
  const input = RawFixtureBytes(RawFixtureTags(v, !b), b), expected = input.slice(), raw = LoadRaw(input);
  Equal(v, raw.Version); Equal(b, raw.IsBinary); Check(raw.HasOriginalBytes);
  input.fill(99); Equal(expected, SaveRaw(raw)); Equal(expected, SaveRaw(raw));
}
export function RawDocumentNormalize(v, b) {
  const tags = RawFixtureTags(v), raw = LoadRaw(RawFixtureBytes(tags, b)); SameRawTags(tags, raw.Tags);
  const edited = raw.WithTags(raw.Tags); Check(!edited.HasOriginalBytes);
  for (const transport of [false, true]) {
    SameRawTags(raw.Tags, LoadRaw(SaveRaw(edited, transport)).Tags);
    SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, transport)).Tags);
  }
}
export function RawDocumentEdit(v, b) {
  const raw = LoadRaw(RawFixtureBytes(RawFixtureTags(v), b)), values = Array.from(raw.Tags);
  const index = values.findIndex(t => t.Code === 10); values[index] = new DxfTag(10, -1.2345678901234567);
  const edited = raw.WithTags(values); SameDoubleBits(1e-20, raw.Tags[index].Value);
  SameRawTags(values, LoadRaw(SaveRaw(edited, !b)).Tags);
}
export function RawDocumentSections(v, b) {
  const raw = LoadRaw(RawFixtureBytes(RawFixtureTags(v), b));
  Equal(['HEADER', 'CLASSES', 'TABLES', 'BLOCKS', 'ENTITIES', 'OBJECTS', 'ACDSDATA', 'VENDOR_SECTION'], raw.Sections.map(s => s.Name));
  for (const s of raw.Sections) {
    Equal('SECTION', raw.Tags[s.StartTagIndex].Value); Equal('ENDSEC', raw.Tags[s.EndTagIndex - 1].Value);
    SameRawTags(raw.Tags.slice(s.StartTagIndex + 2, s.EndTagIndex - 1), s.Content);
    Throws(E.ArgumentOutOfRangeException, () => s.Content.get_Item(-1));
    Throws(E.ArgumentOutOfRangeException, () => s.Content.get_Item(s.Content.Count));
  }
  Check(Array.from(raw.Sections.find(s => s.Name === 'ENTITIES').Content).some(t => t.Code === 0 && t.Value === 'SECTION'));
}
export class RawNonseekableStream {
  #stream; #writable; #fragment;
  ReadCount = 0; FailAfter = 2147483647; WasDisposed = false;
  constructor(bytes, writable, fragment) { this.#stream = writable ? new MemoryStream() : new MemoryStream(bytes, false); this.#writable = writable; this.#fragment = fragment; }
  get CanRead() { return !this.#writable; }
  get CanWrite() { return this.#writable; }
  get CanSeek() { return false; }
  get Bytes() { return this.#stream.ToArray(); }
  Read(buffer, offset, count) {
    if (this.ReadCount >= this.FailAfter) throw new E.IOException('Injected input failure.');
    const n = this.#stream.Read(buffer, offset, Math.min(count, this.#fragment)); this.ReadCount += n; return n;
  }
  Write(buffer, offset, count) {
    if (this.#stream.Length >= this.FailAfter) throw new E.IOException('Injected output failure.');
    this.#stream.Write(buffer, offset, count);
  }
  Flush() {}
  Dispose() { this.WasDisposed = true; this.#stream.Dispose(); }
}
export function RawDocumentNonseekable(v, b) {
  const bytes = RawFixtureBytes(RawFixtureTags(v), b), input = new RawNonseekableStream(bytes, false, 3);
  const raw = DxfRawDocument.Load(input); Check(!input.WasDisposed);
  const output = new RawNonseekableStream(new Uint8Array(), true, 3); raw.Save(output);
  Check(!output.WasDisposed); Equal(bytes, output.Bytes);
  const offset = new MemoryStream(Uint8Array.from([1, 2, 3, 4, ...bytes])); offset.Position = 4;
  Equal(bytes, SaveRaw(DxfRawDocument.Load(offset)));
}
export function RawDocumentLimits(v, b) {
  const tags = RawFixtureTags(v), bytes = RawFixtureBytes(tags, b);
  Equal(bytes, SaveRaw(LoadRaw(bytes, new DxfRawOptions(bytes.length, tags.length))));
  Throws(E.InvalidDataException, () => LoadRaw(bytes, new DxfRawOptions(bytes.length - 1)));
  Throws(E.InvalidDataException, () => LoadRaw(bytes, new DxfRawOptions(bytes.length, tags.length - 1)));
  Throws(E.InvalidDataException, () => LoadRaw(bytes, new DxfRawOptions(bytes.length, tags.length, 2)));
  const limited = new RawNonseekableStream(bytes, false, 3);
  Throws(E.InvalidDataException, () => DxfRawDocument.Load(limited, new DxfRawOptions(10)));
  Check(limited.ReadCount <= 11 && !limited.WasDisposed);
}
export function RawDocumentProfile(v, b) {
  const raw = LoadRaw(RawFixtureBytes(RawFixtureTags(v), b)); let tags = Array.from(raw.Tags);
  let index = tags.findIndex(t => t.Code === 9 && t.Value === '$ACADVER') + 1;
  tags[index] = new DxfTag(1, v === DxfVersion.AutoCad2000 ? 'AC1032' : 'AC1015');
  Throws(E.NotSupportedException, () => raw.WithTags(tags));
  tags = Array.from(raw.Tags); index = tags.findIndex(t => t.Code === 9 && t.Value === '$DWGCODEPAGE') + 1;
  tags[index] = new DxfTag(3, 'ANSI_1250'); Throws(E.NotSupportedException, () => raw.WithTags(tags)); Equal(v, raw.Version);
}
export function RawDocumentComments(v) {
  const tags = RawFixtureTags(v, true); tags.splice(2, 0, new DxfTag(999, 'before section name'));
  const raw = LoadRaw(RawFixtureBytes(tags, false)); SameRawTags(tags, raw.Tags);
  SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags))).Tags);
  const output = new MemoryStream(); output.Write(Uint8Array.of(1, 2, 3));
  Throws(E.NotSupportedException, () => raw.Save(output, true)); Equal(Uint8Array.of(1, 2, 3), output.ToArray());
  const stripped = raw.WithTags(raw.Tags.filter(t => t.Code !== 999)); SameRawTags(stripped.Tags, LoadRaw(SaveRaw(stripped, true)).Tags);
}
export function RawDocumentMultiline(v) {
  const tags = RawFixtureTags(v), value = 'a\r\nb\nc\rd'; tags.splice(tags.length - 2, 0, new DxfTag(300, value));
  const input = RawFixtureBytes(tags, true), raw = LoadRaw(input); SameRawTags(tags, raw.Tags);
  Equal(input, SaveRaw(raw)); SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags))).Tags);
  const output = new MemoryStream(); output.WriteByte(42);
  Throws(E.NotSupportedException, () => raw.Save(output, false)); Equal(1, output.Length);
}
export function RawDocumentLexical(v) {
  for (const nl of ['\r', '\n', '\r\n']) {
    let bytes = RawFixtureBytes(RawFixtureTags(v), false, Encoding.UTF8, nl);
    const text = Encoding.UTF8.GetString(bytes).replace('5' + nl + 'ABCD', '  5' + nl + '00abcd').replace(/[\r\n]+$/, '');
    bytes = Encoding.UTF8.GetBytes(text + nl + ' \t' + nl);
    if (v >= DxfVersion.AutoCad2007) bytes = Uint8Array.from([239, 187, 191, ...bytes]);
    const raw = LoadRaw(bytes); Equal(bytes, SaveRaw(raw)); SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);
  }
}
export function RawDocumentCodePage(cp, b) {
  const tags = RawFixtureTags(DxfVersion.AutoCad2004); tags[5] = new DxfTag(3, 'ANSI_' + cp);
  const text = cp === 1250 ? 'Zażółć' : cp === 1251 ? 'Привет' : cp === 932 ? '東京' : 'Résumé €';
  const index = tags.findIndex(t => t.Code === 9 && t.Value === '$PROJECTNAME') + 1; tags[index] = new DxfTag(1, text);
  const raw = LoadRaw(RawFixtureBytes(tags, b, Encoding.GetEncoding(cp)));
  Equal(cp, raw.EncodingCodePage); Equal(text, raw.Tags[index].Value);
  SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !b)).Tags);
}
export function RawDocumentMalformed(s) {
  const tags = RawFixtureTags(DxfVersion.AutoCad2018); let load = false;
  switch (s) {
    case 0: tags.pop(); break;
    case 1: tags.splice(tags.length - 2, 1); break;
    case 2: tags.push(new DxfTag(1, 'after EOF')); break;
    case 3: tags[1] = new DxfTag(1, 'HEADER'); break;
    case 4: tags[1] = new DxfTag(2, ''); break;
    case 5: tags[0] = new DxfTag(0, 'ENDSEC'); break;
    case 6: tags.splice(2, 0, new DxfTag(0, 'EOF')); break;
    case 7: tags[2] = new DxfTag(9, '$MISSING_VERSION'); break;
    case 8: tags[3] = new DxfTag(3, 'AC1032'); break;
    case 9: tags.splice(4, 0, new DxfTag(9, '$ACADVER'), new DxfTag(1, 'AC1032')); break;
    case 10: tags.splice(6, 0, new DxfTag(9, '$DWGCODEPAGE'), new DxfTag(3, 'ANSI_1252')); break;
    case 11: tags[3] = new DxfTag(1, 'AC1006'); break;
    case 12: tags[3] = new DxfTag(1, 'AC9999'); break;
    case 13: tags[5] = new DxfTag(1, 'ANSI_1252'); break;
    case 14: tags.splice(tags.length - 1, 0, ...tags.slice(0, 11)); break;
    case 15: case 16: case 17: load = true; break;
    case 18: tags.splice(4, 0, new DxfTag(1, 'AC1015')); break;
    case 19: tags.splice(6, 0, new DxfTag(3, 'ANSI_1250')); break;
  }
  if (load) {
    let bytes = RawFixtureBytes(tags, s === 17);
    bytes = s === 15 ? bytes.slice(0, -5) : Uint8Array.from([...bytes, ...(s === 17 ? [0] : Encoding.UTF8.GetBytes('999\ntrailing tag\n'))]);
    Throws(s === 15 ? E.EndOfStreamException : E.FormatException, () => LoadRaw(bytes));
  } else if (s === 0) Throws(E.EndOfStreamException, () => DxfRawDocument.Create(tags));
  else if (s === 11 || s === 12) Throws(DxfVersionNotSupportedException, () => DxfRawDocument.Create(tags));
  else Throws(E.FormatException, () => DxfRawDocument.Create(tags));
}
export function RawDocumentInvalidEncoding() {
  let bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), false);
  const index = bytes.indexOf('u'.charCodeAt(0), bytes.indexOf('F'.charCodeAt(0))); bytes[index] = 255;
  Throws(E.DecoderFallbackException, () => LoadRaw(bytes));
  const legacy = DxfRawDocument.Create(RawFixtureTags(DxfVersion.AutoCad2000)); let tags = Array.from(legacy.Tags);
  tags.splice(tags.length - 2, 0, new DxfTag(1, '東京')); const invalid = legacy.WithTags(tags);
  const output = new MemoryStream(); output.WriteByte(42);
  Throws(E.EncoderFallbackException, () => invalid.Save(output)); Equal(1, output.Length);
  tags = RawFixtureTags(DxfVersion.AutoCad2018); tags.splice(tags.length - 2, 0, new DxfTag(310, new Uint8Array(256)));
  Throws(E.NotSupportedException, () => DxfRawDocument.Create(tags).Save(output, true)); Equal(1, output.Length);
  for (const bom of [[255, 254], [254, 255], [0, 0, 254, 255]]) Throws(E.NotSupportedException, () => LoadRaw(Uint8Array.from([...bom, ...bytes])));
  bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2000), false);
  Throws(E.NotSupportedException, () => LoadRaw(Uint8Array.from([239, 187, 191, ...bytes])));
  tags = RawFixtureTags(DxfVersion.AutoCad2000); tags[5] = new DxfTag(3, 'ANSI_1200');
  Throws(E.NotSupportedException, () => DxfRawDocument.Create(tags));
  tags[5] = new DxfTag(3, 'not-a-codepage'); Throws(E.NotSupportedException, () => DxfRawDocument.Create(tags));
}
export function RawDocumentIsolation() {
  const tags = RawFixtureTags(DxfVersion.AutoCad2018), raw = DxfRawDocument.Create(tags), saved = Array.from(raw.Tags);
  tags.length = 0; SameRawTags(saved, raw.Tags);
  const binary = raw.Tags.find(t => t.ValueType === DxfTagValueType.BinaryData), bytes = binary.Value;
  bytes[0] = 99; Equal(0, binary.Value[0]);
  Throws(TypeError, () => raw.Tags.push(new DxfTag(1, 'mutate')));
  Throws(TypeError, () => { raw.Sections.length = 0; });
  Throws(E.ArgumentNullException, () => DxfRawDocument.Create(null));
  Throws(E.ArgumentException, () => raw.WithTags([null]));
}
export function RawDocumentCancellation() {
  const bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), false), input = new RawNonseekableStream(bytes, false, 3);
  const cancelled = { aborted: true };
  Throws(E.OperationCanceledException, () => DxfRawDocument.Load(input, null, cancelled));
  Equal(0, input.ReadCount); Check(!input.WasDisposed);
  const raw = LoadRaw(bytes), output = new MemoryStream(); output.WriteByte(42);
  Throws(E.OperationCanceledException, () => raw.Save(output, true, cancelled)); Equal(1, output.Length);
  const failing = new RawNonseekableStream(bytes, false, 3); failing.FailAfter = 12;
  Throws(E.IOException, () => DxfRawDocument.Load(failing)); Check(!failing.WasDisposed);
  const bad = new RawNonseekableStream(new Uint8Array(), true, 3); bad.FailAfter = 0;
  Throws(E.IOException, () => raw.Save(bad)); Check(!bad.WasDisposed);
  Throws(E.ArgumentNullException, () => DxfRawDocument.Load(null)); Throws(E.ArgumentNullException, () => raw.Save(null));
  const closed = new MemoryStream(); closed.Dispose();
  Throws(E.ArgumentException, () => DxfRawDocument.Load(closed)); Throws(E.ArgumentException, () => raw.Save(closed));
}
export function RawDocumentOptions() {
  Throws(E.ArgumentOutOfRangeException, () => new DxfRawOptions(0));
  Throws(E.ArgumentOutOfRangeException, () => new DxfRawOptions(undefined, 0));
  Throws(E.ArgumentOutOfRangeException, () => new DxfRawOptions(undefined, undefined, 0));
  const tags = RawFixtureTags(DxfVersion.AutoCad2018);
  Throws(E.InvalidDataException, () => DxfRawDocument.Create(tags, false, new DxfRawOptions(undefined, tags.length - 1)));
  function* Infinite() { while (true) yield new DxfTag(999, 'infinite'); }
  Throws(E.InvalidDataException, () => DxfRawDocument.Create(Infinite(), false, new DxfRawOptions(undefined, 3)));
}
export function RawDocumentOutputBudgets() {
  const tags = RawFixtureTags(DxfVersion.AutoCad2018);
  for (const b of [false, true]) {
    const bytes = SaveRaw(DxfRawDocument.Create(tags), b);
    for (const limit of [22, 100, bytes.length - 1]) {
      const raw = DxfRawDocument.Create(tags, false, new DxfRawOptions(limit)), output = new MemoryStream(); output.WriteByte(42);
      Throws(E.InvalidDataException, () => raw.Save(output, b)); Equal(1, output.Length);
    }
    Equal(bytes.length, SaveRaw(DxfRawDocument.Create(tags, false, new DxfRawOptions(bytes.length)), b).length);
  }
}
export function RawDocumentBootstrapBudget() {
  const tags = RawFixtureTags(DxfVersion.AutoCad2018), index = tags.findIndex(t => t.Code === 9 && t.Value === '$PROJECTNAME') + 1;
  tags[index] = new DxfTag(1, '東'.repeat(40));
  const raw = LoadRaw(RawFixtureBytes(tags, false), new DxfRawOptions(undefined, undefined, 50)); Equal('東'.repeat(40), raw.Tags[index].Value);
}
