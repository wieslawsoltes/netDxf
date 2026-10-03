// Port of tests/netDxf.Conformance/RawTagTests.cs. See doc/LANGUAGE_ADAPTATIONS.md.
import { DxfTag, DxfGroupCode, DxfTagValueType as T, DxfHandleKind as K, MemoryStream } from '../../index.js';
import { TextCodeValueReader } from '../../netDxf/IO/TextCodeValueReader.js';
import { TextCodeValueWriter } from '../../netDxf/IO/TextCodeValueWriter.js';
import { BinaryCodeValueReader } from '../../netDxf/IO/BinaryCodeValueReader.js';
import { BinaryCodeValueWriter } from '../../netDxf/IO/BinaryCodeValueWriter.js';
import { Encoding } from '../../runtime/Encoding.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { Run, Check, Equal, Throws, SameDoubleBits, BooleanName } from './TestHarness.js';

export function RegisterRawTagTests() {
  Run('raw-tags/exhaustive-code-space', RawTagCodeSpace);
  Run('raw-tags/reference-kinds', RawTagReferenceKinds);
  Run('raw-tags/binary-storage-isolation', RawTagBinaryIsolation);
  Run('raw-tags/invalid-values', RawTagInvalidValues);
  Run('raw-tags/negative-zero-and-extremes', RawTagExtremes);
  Run('raw-tags/culture-independent-handles', RawTagCultures);
  for (const [code, type] of ExpectedTagTypes()) {
    for (const binary of code === 999 ? [false] : [false, true])
      Run(`raw-tags/codecs/${code}/${BooleanName(binary)}`, () => RawTagCodec(code, type, binary));
  }
}
export function ExpectedTagTypes() {
  const types = new Map();
  const Add = (first, last, type) => { for (let code = first; code <= last; code++) types.set(code, type); };
  Add(0, 9, T.String); Add(10, 59, T.Double); Add(60, 79, T.Int16); Add(90, 99, T.Int32);
  Add(100, 102, T.String); Add(105, 105, T.Handle); Add(110, 149, T.Double); Add(160, 169, T.Int64);
  Add(170, 179, T.Int16); Add(210, 239, T.Double); Add(270, 289, T.Int16); Add(290, 299, T.Boolean);
  Add(300, 309, T.String); Add(310, 319, T.BinaryData); Add(320, 369, T.Handle); Add(370, 389, T.Int16);
  Add(390, 399, T.Handle); Add(400, 409, T.Int16); Add(410, 419, T.String); Add(420, 429, T.Int32);
  Add(430, 439, T.String); Add(440, 459, T.Int32); Add(460, 469, T.Double); Add(470, 479, T.String);
  Add(480, 481, T.Handle); Add(999, 1003, T.String); Add(1004, 1004, T.BinaryData); Add(1005, 1005, T.Handle);
  Add(1006, 1009, T.String); Add(1010, 1059, T.Double); Add(1060, 1070, T.Int16); Add(1071, 1071, T.Int32);
  types.set(5, T.Handle);
  return types;
}
export function RawTagCodeSpace() {
  for (const code of [-32768, -1, 80, 89, 103, 104, 106, 159, 180, 209, 240, 269, 482, 998, 1072, 32767])
    Throws(ArgumentOutOfRangeException, () => DxfGroupCode.GetValueType(code));
  const expected = ExpectedTagTypes();
  for (let code = -32768; code <= 32767; code++) {
    const result = {};
    Equal(expected.has(code), DxfGroupCode.TryGetValueType(code, result), 'Code admission ' + code);
    if (expected.has(code)) {
      Equal(expected.get(code), result.value, 'Type classification ' + code);
      Equal(expected.get(code), DxfGroupCode.GetValueType(code), 'Throwing classifier ' + code);
    } else Equal(K.None, DxfGroupCode.GetHandleKind(code), 'Unknown handle code');
  }
}
export function RawTagReferenceKinds() {
  Equal(K.ObjectIdentity, DxfGroupCode.GetHandleKind(5));
  Equal(K.ObjectIdentity, DxfGroupCode.GetHandleKind(105));
  for (let code = 320; code <= 399; code++) {
    const expected = code < 330 ? K.Arbitrary : code < 340 ? K.SoftPointer : code < 350 ? K.HardPointer :
      code < 360 ? K.SoftOwner : code < 370 ? K.HardOwner : code < 390 ? K.None : K.HardPointer;
    Equal(expected, DxfGroupCode.GetHandleKind(code), 'Reference class ' + code);
  }
  Equal(K.HardPointer, DxfGroupCode.GetHandleKind(480));
  Equal(K.HardPointer, DxfGroupCode.GetHandleKind(481));
  Equal(K.XData, DxfGroupCode.GetHandleKind(1005));
  Equal(K.None, new DxfTag(1, 'FFFFFFFF').HandleKind);
}
export function RawTagSample(type) {
  switch (type) {
    case T.String: return 'Zażółć 東京 \\U+0041';
    case T.Handle: return 'FFFFFFFFFFFFFFFF';
    case T.Double: return -1234.56789012345;
    case T.Int16: return -32768;
    case T.Int32: return -2147483648;
    case T.Int64: return -9223372036854775808n;
    case T.Boolean: return true;
    default: return Uint8Array.of(0, 255, 127, 128, 1);
  }
}
export function RawTagCodec(code, type, binary) {
  const value = RawTagSample(type), original = new DxfTag(code, value), stream = new MemoryStream();
  Equal(type, original.ValueType);
  const writer = binary ? new BinaryCodeValueWriter(stream) : new TextCodeValueWriter(stream);
  writer.Write(original.Code, original.Value); writer.Write(0, 'EOF'); writer.Flush();
  const reader = binary ? new BinaryCodeValueReader(stream.ToArray()) : new TextCodeValueReader(Encoding.UTF8.GetString(stream.ToArray()));
  reader.Next();
  const loaded = new DxfTag(reader.Code, reader.Value);
  Equal(code, loaded.Code); Equal(original.ValueType, loaded.ValueType); Equal(value, loaded.Value);
  reader.Next(); Equal('EOF', reader.ReadString());
}
export function RawTagBinaryIsolation() {
  for (const length of [0, 1, 127, 128, 255, 256, 4096]) {
    const source = Uint8Array.from({ length }, (_, i) => i), expected = source.slice();
    const tag = new DxfTag(310, source); source.fill(99);
    const first = tag.Value; Equal(expected, first); first.fill(88);
    Equal(expected, tag.Value); Check(first !== tag.Value);
  }
}
export function RawTagInvalidValues() {
  for (const [code] of ExpectedTagTypes()) {
    Throws(ArgumentNullException, () => new DxfTag(code, null));
    Throws(ArgumentException, () => new DxfTag(code, {}));
    // Decimal/Single/boxed integral distinctions have no JS Number equivalent.
    // Boxed Number is rejected, and mapped primitive range checks replace CLR boxing checks.
    Throws(ArgumentException, () => new DxfTag(code, new Number(1)));
  }
  for (const bad of ['', ' 1', '1 ', '+1', '-1', '0x1', 'G', 'FFFFFFFFFFFFFFFFF', 'Ｆ', '1\0'])
    for (const code of [5, 105, 320, 330, 340, 350, 360, 390, 480, 1005])
      Throws(ArgumentException, () => new DxfTag(code, bad));
  Throws(ArgumentException, () => new DxfTag(1, 'a\0b'));
  for (const text of ['a\rb', 'a\nb', 'a\r\nb']) Equal(text, new DxfTag(300, text).Value);
  for (const bad of [NaN, Infinity, -Infinity]) Throws(ArgumentOutOfRangeException, () => new DxfTag(10, bad));
  for (const bad of [-32769, 32768, 1.5]) Throws(ArgumentException, () => new DxfTag(70, bad));
  Throws(ArgumentException, () => new DxfTag(90, 2147483648));
  Throws(ArgumentException, () => new DxfTag(160, 1));
  Throws(ArgumentException, () => new DxfTag(290, 1));
  Throws(ArgumentException, () => new DxfTag(10, 1n));
}
export function RawTagExtremes() {
  for (const value of [0, -0, Number.MIN_VALUE, -Number.MIN_VALUE, Number.MAX_VALUE, -Number.MAX_VALUE])
    SameDoubleBits(value, new DxfTag(10, value).Value);
  Equal(9223372036854775807n, new DxfTag(160, 9223372036854775807n).Value);
  Equal(2147483647, new DxfTag(450, 2147483647).Value);
  Equal(32767, new DxfTag(70, 32767).Value);
  Equal('', new DxfTag(1, '').Value);
  Equal('00abcdef', new DxfTag(5, '00abcdef').Value);
}
export function RawTagCultures() {
  // JavaScript primitives have no ambient CurrentCulture. Localized formatting is explicit.
  for (const culture of ['en-US', 'pl-PL', 'tr-TR', 'ar-SA']) {
    new Intl.NumberFormat(culture).format(1234.5);
    Equal('abcdef', new DxfTag(5, 'abcdef').Value);
    Equal('Zażółć 東京', new DxfTag(1, 'Zażółć 東京').Value);
    Throws(ArgumentException, () => new DxfTag(5, '١'));
  }
}
