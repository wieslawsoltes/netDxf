import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, Layer, Lineweight, Vector3 } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { InvalidDataException, ArgumentException } from '../../runtime/Errors.js';
import { UcsElevationFixture } from '../netDxf.Conformance/UcsElevationTests.js';

const tag = (code, value) => new DxfTag(code, value);
function fixture(binary, edit) {
  const stream = UcsElevationFixture(18, binary, null);
  try {
    const raw = DxfRawDocument.Load(stream);
    const record = raw.Sections.find(s => s.Name === 'TABLES').Records.find(r => r.Name === 'UCS');
    assert.ok(record);
    return raw.WithRecord(record, edit(Array.from(record.Tags)));
  } finally { stream.Dispose(); }
}
function load(raw) {
  const stream = new MemoryStream();
  try { raw.Save(stream, raw.IsBinary); stream.Position = 0; return new DxfReader().Read(stream); }
  finally { stream.Dispose(); }
}
function replace(tags, code, value) {
  const index = tags.findIndex(t => t.Code === code);
  assert.ok(index >= 0, `Missing group ${code}`);
  tags[index] = tag(code, value);
  return tags;
}
function rejected(raw, message) {
  assert.throws(() => load(raw), error => error instanceof InvalidDataException && error.message.includes(message));
}

for (const binary of [false, true]) {
  test(`UCS duplicate flags cannot be hidden by a private packet / ${binary}`, () => {
    const raw = fixture(binary, tags => [...tags, tag(102, '{PRIVATE'), tag(70, 0), tag(102, '}')]);
    rejected(raw, 'Duplicate UCS flags.');
  });
  test(`UCS rejects a duplicate public type even when both values are zero / ${binary}`, () => {
    rejected(fixture(binary, tags => [...tags, tag(79, 0)]), 'Duplicate UCS orthographic view type.');
  });
  test(`UCS duplicate explicit-null base slots are not absent references / ${binary}`, () => {
    rejected(fixture(binary, tags => [...replace(tags, 79, 2), tag(346, '0000'), tag(346, '0')]), 'Duplicate UCS base-reference group 346.');
  });
  test(`UCS explicit numeric-null base is canonical and physically retained / ${binary}`, () => {
    const doc = load(fixture(binary, tags => [...replace(tags, 79, 2), tag(346, '0000')]));
    const ucs = doc.UCSs.get_Item('Fixture');
    assert.equal(ucs.OrthographicViewType, 2); assert.equal(ucs.BaseUcs, null);
    const output = new MemoryStream();
    try {
      assert.equal(doc.Save(output, binary), true);
      const raw = DxfRawDocument.Load(output.ToArray());
      const record = raw.Sections.find(s => s.Name === 'TABLES').Records.find(r => r.Name === 'UCS' && Array.from(r.Tags).some(t => t.Code === 2 && t.Value === 'Fixture'));
      assert.deepEqual(Array.from(record.Tags).filter(t => t.Code === 346).map(t => t.Value), ['0']);
    } finally { output.Dispose(); }
  });
  test(`UCS base relationship scope survives private XData and ignores public XData tails / ${binary}`, () => {
    const raw = fixture(binary, tags => {
      const at = tags.findIndex(t => t.Code === 79); assert.ok(at >= 0);
      tags.splice(at, 0, tag(102, '{PRIVATE'), tag(1001, 'PRIVATE_APP'), tag(1000, 'private'), tag(102, '}'));
      replace(tags, 79, 2);
      return [...tags, tag(346, '0000'), tag(1001, 'PUBLIC_APP'), tag(1000, 'retained'), tag(79, 7), tag(346, 'BAD')];
    });
    const ucs = load(raw).UCSs.get_Item('Fixture');
    assert.equal(ucs.OrthographicViewType, 2); assert.equal(ucs.BaseUcs, null);
    assert.equal(ucs.XData.get_Item('PUBLIC_APP').XDataRecord.get_Item(0).Value, 'retained');
  });
  test(`UCS repeated scalar components use source order independently of origin overrides / ${binary}`, () => {
    const ucs = load(fixture(binary, tags => [...tags, tag(10, 9), tag(30, 7), tag(146, -3), tag(71, 6), tag(33, 6), tag(146, -5), tag(13, 4), tag(20, 8), tag(23, 5)])).UCSs.get_Item('Fixture');
    assert.deepEqual(ucs.Origin, new Vector3(9, 8, 7)); assert.equal(ucs.Elevation, -5);
    assert.deepEqual(ucs.OrthographicOrigins.get_Item(6), new Vector3(4, 5, 6));
  });
  test(`UCS incomplete origin cannot borrow a missing component from the next type / ${binary}`, () => {
    rejected(fixture(binary, tags => [...tags, tag(71, 1), tag(13, 1), tag(23, 2), tag(71, 2), tag(13, 4), tag(23, 5), tag(33, 6)]), 'requires all three coordinate groups');
  });
  test(`UCS discarded invalid names do not weaken relationship admission / ${binary}`, () => {
    const raw = fixture(binary, tags => replace(tags, 2, 'invalid/name'));
    assert.equal(load(raw).UCSs.Count, 0);
    rejected(fixture(binary, tags => replace(replace(tags, 2, 'invalid/name'), 79, 2)), 'requires a retained, valid UCS name');
  });
  for (const lineweight of [Lineweight.ByLayer, Lineweight.ByBlock]) {
    test(`layer import recovers inherited lineweight ${lineweight} without relaxing setters / ${binary}`, () => {
      const doc = new DxfDocument(18), layer = doc.Layers.Add(new Layer('Recovered')), stream = new MemoryStream();
      assert.throws(() => { layer.Lineweight = lineweight; }, ArgumentException);
      try {
        assert.equal(doc.Save(stream, binary), true);
        const raw = DxfRawDocument.Load(stream.ToArray());
        const record = raw.Sections.find(s => s.Name === 'TABLES').Records.find(r => r.Name === 'LAYER' && Array.from(r.Tags).some(t => t.Code === 2 && t.Value === 'Recovered'));
        const recovered = load(raw.WithRecord(record, replace(Array.from(record.Tags), 370, lineweight))).Layers.get_Item('Recovered');
        assert.equal(recovered.Lineweight, Lineweight.Default);
        assert.throws(() => { recovered.Lineweight = lineweight; }, ArgumentException);
      } finally { stream.Dispose(); }
    });
  }
}
