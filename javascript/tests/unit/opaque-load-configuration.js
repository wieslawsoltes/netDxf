import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfVersion, DxfTag, MemoryStream } from '../../node-entry.js';
import { SetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { NotSupportedException } from '../../runtime/Errors.js';
import { OpaqueFixture } from '../netDxf.Conformance/OpaqueEntityTests.js';

const T = (code, value) => new DxfTag(code, value);
function withAcds(raw, position) {
  const tags = [...raw.Tags];
  const at = position === 'before' ? raw.Sections.find(s => s.Name === 'ENTITIES').StartTagIndex : tags.length - 1;
  return raw.WithTags([...tags.slice(0, at), T(0, 'SECTION'), T(2, 'ACDSDATA'),
    T(0, 'ACDSSCHEMA'), T(90, 1), T(0, 'ACDSRECORD'), T(310, new Uint8Array([0, 255, 37])),
    T(300, 'retained source metadata'), T(0, 'ENDSEC'), ...tags.slice(at)]);
}
function encode(raw, binary) {
  const stream = new MemoryStream();
  try { raw.Save(stream, binary); stream.Position = 0; return stream; }
  catch (error) { stream.Dispose(); throw error; }
}
// DxfReader is already tested directly. These cover the public wrapper's
// configuration-dependent failure result without counting extra original cases.
for (const configuration of ['Release', 'Debug']) for (const binary of [false, true])
  for (const position of ['before', 'after']) {
    test(`public opaque load preserves ${configuration} failure policy / ${binary}/${position}`, () => {
      const previous = SetTypedIOConfiguration(configuration);
      let input;
      try {
        input = encode(withAcds(OpaqueFixture(DxfVersion.AutoCad2018), position), binary);
        if (configuration === 'Debug')
          assert.throws(() => DxfDocument.Load(input), error => error instanceof NotSupportedException && error.message.includes('ACDSDATA'));
        else assert.equal(DxfDocument.Load(input), null);
        assert.equal(input.CanRead, true, 'A rejected typed load must not dispose its caller-owned stream');
      } finally { input?.Dispose(); SetTypedIOConfiguration(previous); }
    });
  }
for (const binary of [false, true]) test(`raw fallback retains complete opaque and ACDSDATA packets / ${binary}`, () => {
  const source = withAcds(OpaqueFixture(DxfVersion.AutoCad2018), 'after'), input = encode(source, binary);
  try {
    const loaded = DxfRawDocument.Load(input);
    assert.deepEqual([...loaded.Tags].map(t => [t.Code, t.Value]), [...source.Tags].map(t => [t.Code, t.Value]));
    assert.equal(input.CanRead, true);
  } finally { input.Dispose(); }
});
