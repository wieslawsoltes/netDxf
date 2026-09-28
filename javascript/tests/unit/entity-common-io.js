import test from 'node:test';
import assert from 'node:assert/strict';
import { DxfDocument, DxfRawDocument, DxfTag, MemoryStream, AttributeDefinition, Block, Insert, Line } from '../../index.js';
import { DxfReader } from '../../netDxf/IO/DxfReader.js';
import { Exception, InvalidDataException, ArgumentException } from '../../runtime/Errors.js';
const tag = (code, value) => new DxfTag(code, value);
function fixture(binary, edit) {
  const doc = new DxfDocument(), block = new Block('ATTRIBUTE_COMMON_TEST');
  const definition = new AttributeDefinition('TAG'); definition.Value = 'stored'; block.AttributeDefinitions.Add(definition);
  doc.Entities.Add(new Insert(block)); const stream = new MemoryStream();
  try {
    assert.equal(doc.Save(stream, binary), true); stream.Position = 0;
    const raw = DxfRawDocument.Load(stream), record = raw.Sections.flatMap(s => s.Records).find(r => r.Name === 'ATTRIB');
    assert.ok(record); const tags = Array.from(record.Tags), common = tags.findIndex(t => t.Code === 100);
    const body = tags.findIndex((t, i) => i > common && t.Code === 100);
    assert.equal(tags[common].Value, 'AcDbEntity'); assert.ok(body > common);
    return raw.WithRecord(record, edit(tags, common, body));
  } finally { stream.Dispose(); }
}
function load(raw, reader = new DxfReader()) {
  const stream = new MemoryStream();
  try { raw.Save(stream, raw.IsBinary); stream.Position = 0; return reader.Read(stream); }
  finally { stream.Dispose(); }
}
function rejects(raw, message) {
  assert.throws(() => load(raw), e => e instanceof InvalidDataException && e.message.includes(message));
}
const attribute = doc => Array.from(Array.from(doc.Entities.Inserts)[0].Attributes)[0];
for (const binary of [false, true]) {
  test(`ATTRIB requires its first common subclass before text decoding / ${binary}`, () => {
    rejects(fixture(binary, (t, c) => { t[c] = tag(100, 'AcDbNotEntity'); return t; }), 'Expected AcDbEntity common subclass for ATTRIB.');
  });
  test(`ATTRIB rejects consecutive common subclasses / ${binary}`, () => {
    rejects(fixture(binary, (t, c, b) => { t.splice(b, 0, tag(100, 'AcDbEntity')); return t; }), 'Duplicate AcDbEntity common subclass.');
  });
  test(`ATTRIB common completion error precedes duplicate subclass error / ${binary}`, () => {
    rejects(fixture(binary, (t, c, b) => { t[b] = tag(100, 'AcDbEntity'); t.splice(b, 0, tag(92, 2), tag(310, Uint8Array.of(1))); return t; }), 'byte count does not match');
  });
  test(`ATTRIB common failure precedes text-style registration / ${binary}`, () => {
    const reader = new DxfReader(), raw = fixture(binary, (t, c, b) => {
      t.splice(b + 1, 0, tag(7, 'BODY_STYLE_MUST_NOT_REGISTER'));
      t.splice(b, 0, tag(8, 'COMMON_LAYER_BEFORE_FAILURE'), tag(92, 2), tag(310, Uint8Array.of(1)));
      return t;
    });
    assert.throws(() => load(raw, reader), e => e instanceof InvalidDataException && e.message.includes('byte count does not match'));
    assert.ok(reader.doc.Layers.get_Item('COMMON_LAYER_BEFORE_FAILURE'));
    assert.equal(reader.doc.TextStyles.get_Item('BODY_STYLE_MUST_NOT_REGISTER'), null);
  });
  test(`ATTRIB malformed common data wins over an invalid body style / ${binary}`, () => {
    rejects(fixture(binary, (t, c, b) => {
      t.splice(b + 1, 0, tag(7, 'invalid/style'));
      t.splice(b, 0, tag(92, 2), tag(310, Uint8Array.of(1))); return t;
    }), 'byte count does not match');
  });
  for (const retainCommon of [false, true]) {
    test(`ATTRIB incomplete ${retainCommon ? 'common' : 'object'} header stops at the record / ${binary}`, () => {
      const raw = fixture(binary, (t, c) => t.filter((x, i) => x.Code !== 100 || retainCommon && i === c));
      assert.throws(() => load(raw), e => e.constructor === Exception && e.message === 'Premature end of entity ATTRIB definition.');
    });
  }
  test(`ATTRIB common fields are ordered and true color survives later ACI / ${binary}`, () => {
    const raw = fixture(binary, (t, c, b) => {
      t.splice(c + 1, b - c - 1, ...[[8, 'FIRST'], [8, 'LAST'], [6, 'TYPE_FIRST'], [6, 'TYPE_LAST'],
        [62, 1], [420, 0x10203], [62, 2], [370, 25], [370, 30], [48, 2], [48, 3], [60, 1], [60, 0]].map(([c, v]) => tag(c, v)));
      return t;
    });
    const a = attribute(load(raw)); assert.equal(a.Layer.Name, 'LAST'); assert.equal(a.Linetype.Name, 'TYPE_LAST');
    assert.deepEqual([a.Color.R, a.Color.G, a.Color.B], [1, 2, 3]); assert.equal(a.Color.UseTrueColor, true); assert.equal(a.Lineweight, 30); assert.equal(a.LinetypeScale, 3); assert.equal(a.IsVisible, true);
  });
  test(`ATTRIB checks the first physical subclass rather than hiding it in a control packet / ${binary}`, () => {
    rejects(fixture(binary, (t, c) => { t.splice(c, 0, tag(102, '{PRIVATE'), tag(100, 'NotAcDbEntity'), tag(102, '}')); return t; }), 'Expected AcDbEntity common subclass');
  });
  test(`ATTRIB reader discards an empty tag without weakening ATTDEF validation / ${binary}`, () => {
    const raw = fixture(binary, (t, c, b) => t.filter((x, i) => i < b || x.Code !== 2));
    const doc = load(raw); assert.equal(Array.from(doc.Entities.Inserts)[0].Attributes.Count, 0);
    assert.throws(() => new AttributeDefinition(''), ArgumentException);
  });
  test(`ATTRIB accepts a nonduplicate body marker and raw access stays independent / ${binary}`, () => {
    const raw = fixture(binary, (t, c, b) => { t[b] = tag(100, 'ProducerBody'); return t; });
    assert.equal(attribute(load(raw)).Value, 'stored');
    const malformed = fixture(binary, (t, c) => { t[c] = tag(100, 'AcDbNotEntity'); return t; });
    const stream = new MemoryStream(); try {
      malformed.Save(stream, binary); stream.Position = 0;
      assert.ok(DxfRawDocument.Load(stream).Sections.flatMap(s => s.Records).some(r => r.Name === 'ATTRIB'));
    } finally { stream.Dispose(); }
  });
}

function lineFixture(binary, edit) {
  const doc = new DxfDocument(); doc.Entities.Add(new Line()); const stream = new MemoryStream();
  try {
    assert.equal(doc.Save(stream, binary), true); stream.Position = 0;
    const raw = DxfRawDocument.Load(stream), record = raw.Sections.flatMap(s => s.Records).find(r => r.Name === 'LINE');
    const tags = Array.from(record.Tags), common = tags.findIndex(t => t.Code === 100);
    const body = tags.findIndex((t, i) => i > common && t.Code === 100);
    assert.equal(tags[common].Value, 'AcDbEntity'); assert.ok(body > common);
    return raw.WithRecord(record, edit(tags, common, body));
  } finally { stream.Dispose(); }
}
for (const binary of [false, true]) {
  test(`known entity common marker diagnostic precedes body decoding / ${binary}`, () => {
    rejects(lineFixture(binary, (t, c) => { t[c] = tag(100, 'NotAcDbEntity'); return t; }), 'Expected AcDbEntity common subclass.');
  });
  test(`known entity duplicate common marker has its own diagnostic / ${binary}`, () => {
    rejects(lineFixture(binary, (t, c, b) => { t.splice(b, 0, tag(100, 'AcDbEntity')); return t; }), 'Duplicate AcDbEntity common subclass.');
  });
  test(`known entity proxy completion still precedes the duplicate marker / ${binary}`, () => {
    rejects(lineFixture(binary, (t, c, b) => {
      t.splice(b, 0, tag(92, 2), tag(310, Uint8Array.of(1)), tag(100, 'AcDbEntity')); return t;
    }), 'byte count does not match');
  });
  test(`known entity object-level private markers are not common markers / ${binary}`, () => {
    const raw = lineFixture(binary, (t, c) => {
      t.splice(c, 0, tag(102, '{PRIVATE'), tag(100, 'PrivateSubclass'), tag(102, '{NESTED'), tag(100, 'OtherSubclass'), tag(102, '}'), tag(102, '}')); return t;
    });
    assert.equal(Array.from(load(raw).Entities.Lines).length, 1);
  });
}
