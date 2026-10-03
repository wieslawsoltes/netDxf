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

// ReadEntity common-field phase and resource lookup recovery from the pinned source.
for(const binary of [false,true]){
  test(`known entity common scalars retain last-value and true-color precedence / ${binary}`,()=>{
    const raw=lineFixture(binary,(t,c,b)=>{t.splice(c+1,b-c-1,...[[8,'FIRST_COMMON_LAYER'],[8,'LAST_COMMON_LAYER'],[6,'FIRST_COMMON_TYPE'],[6,'LAST_COMMON_TYPE'],[62,1],[420,0x10203],[62,2],[420,0x40506],[370,25],[370,30],[48,2],[48,3],[60,1],[60,0],[440,0x0200007f],[440,0x020000ff]].map(([c,v])=>tag(c,v)));return t;});
    const doc=load(raw),line=Array.from(doc.Entities.Lines)[0];assert.equal(line.Layer.Name,'LAST_COMMON_LAYER');assert.equal(line.Linetype.Name,'LAST_COMMON_TYPE');assert.ok(doc.Layers.get_Item('FIRST_COMMON_LAYER'));assert.ok(doc.Linetypes.get_Item('FIRST_COMMON_TYPE'));assert.deepEqual([line.Color.R,line.Color.G,line.Color.B],[4,5,6]);assert.equal(line.Lineweight,30);assert.equal(line.LinetypeScale,3);assert.equal(line.IsVisible,true);assert.equal(line.Transparency.Value,0);
  });
  for(const scale of [0,-1,-1e-200])test(`known entity nonpositive linetype scale recovers ${scale} / ${binary}`,()=>{
    const raw=lineFixture(binary,(t,c,b)=>{t.splice(c+1,b-c-1,tag(8,'0'),tag(48,scale));return t;});assert.equal(Array.from(load(raw).Entities.Lines)[0].LinetypeScale,1);
  });
  test(`known entity small positive linetype scale is not rounded to one / ${binary}`,()=>{
    const raw=lineFixture(binary,(t,c,b)=>{t.splice(c+1,b-c-1,tag(8,'0'),tag(48,1e-200));return t;});assert.equal(Array.from(load(raw).Entities.Lines)[0].LinetypeScale,1e-200);
  });
  test(`common resources are processed before malformed body data / ${binary}`,()=>{
    const reader=new DxfReader(),failure=new Error('body decoding reached'),raw=lineFixture(binary,(t,c,b)=>{t.splice(b+1,0,tag(1001,'INVALID_BODY_APPID'),tag(1000,'payload'));t.splice(c+1,0,tag(8,'EARLY_LAYER'),tag(6,'EARLY_TYPE'));return t;});
    const original=reader.Cursor;reader.Cursor=function(record,marker){if(record.Name==='LINE'){assert.ok(this.doc.Layers.get_Item('EARLY_LAYER'));assert.ok(this.doc.Linetypes.get_Item('EARLY_TYPE'));throw failure;}return original.call(this,record,marker);};assert.throws(()=>load(raw,reader),e=>e===failure);
  });
  test(`common proxy completion fails before body cursor and preserves preceding resources / ${binary}`,()=>{
    const reader=new DxfReader(),raw=lineFixture(binary,(t,c,b)=>{t.splice(b,0,tag(8,'PROXY_PREFIX_LAYER'),tag(92,2),tag(310,Uint8Array.of(1)));return t;});let body=false;const original=reader.Cursor;reader.Cursor=function(r,m){if(r.Name==='LINE')body=true;return original.call(this,r,m);};
    assert.throws(()=>load(raw,reader),e=>e instanceof InvalidDataException&&e.message.includes('byte count does not match'));assert.equal(body,false);assert.ok(reader.doc.Layers.get_Item('PROXY_PREFIX_LAYER'));
  });
  test(`common resource callback wins over later proxy and duplicate marker errors / ${binary}`,()=>{
    const reader=new DxfReader(),failure=new Error('common resource callback'),original=reader.Resource,raw=lineFixture(binary,(t,c,b)=>{t.splice(b,0,tag(8,'THROW_COMMON'),tag(92,2),tag(310,Uint8Array.of(1)),tag(100,'AcDbEntity'));return t;});
    reader.Resource=function(p,n){if(p==='Layers'&&n==='THROW_COMMON')throw failure;return original.call(this,p,n);};assert.throws(()=>load(raw,reader),e=>e===failure);
  });
  test(`common incomplete header retains resources consumed before record end / ${binary}`,()=>{
    const reader=new DxfReader(),raw=lineFixture(binary,(t,c)=>[...t.slice(0,c+1),tag(8,'INCOMPLETE_COMMON'),tag(6,'INCOMPLETE_TYPE')]);assert.throws(()=>load(raw,reader),e=>e.constructor===Exception&&e.message==='Premature end of entity LINE definition.');assert.ok(reader.doc.Layers.get_Item('INCOMPLETE_COMMON'));assert.ok(reader.doc.Linetypes.get_Item('INCOMPLETE_TYPE'));
  });
  test(`common setters observe recorded source identity and Layer-Color-Linetype order / ${binary}`,()=>{
    const reader=new DxfReader(),events=[],apply=reader.ApplyCommon,raw=lineFixture(binary,(t,c,b)=>{t.splice(c+1,b-c-1,tag(8,'SETTER_LAYER'),tag(62,1),tag(6,'SETTER_TYPE'));return t;});
    reader.ApplyCommon=function(entity,fields){if(entity instanceof Line){entity.LayerChanged.Add(()=>events.push(['layer',entity.Color.Index,entity.Handle!==null&&this.Context.acceptedSourceObjects.get(BigInt('0x'+entity.Handle))===entity]));entity.LinetypeChanged.Add(()=>events.push(['linetype',entity.Color.Index,entity.Handle!==null&&this.Context.acceptedSourceObjects.get(BigInt('0x'+entity.Handle))===entity]));}return apply.call(this,entity,fields);};
    load(raw,reader);assert.deepEqual(events.slice(0,2),[['layer',256,true],['linetype',1,true]]);
  });
  for(const name of ['', 'invalid/name', 'external|name'])test(`common invalid resource reference ${JSON.stringify(name)} recovers to table defaults / ${binary}`,()=>{
    const raw=lineFixture(binary,(t,c,b)=>{t.splice(c+1,b-c-1,tag(8,name),tag(6,name));return t;}),doc=load(raw),line=Array.from(doc.Entities.Lines)[0];assert.equal(line.Layer.Name,'0');assert.equal(line.Linetype.Name,'Continuous');assert.equal(doc.Layers.Contains(name),false);assert.equal(doc.Linetypes.Contains(name),false);
  });
  test(`omitted common resources retain ByLayer rather than reference-name recovery / ${binary}`,()=>{
    const raw=lineFixture(binary,(t,c,b)=>[...t.slice(0,c+1),...t.slice(b)]),line=Array.from(load(raw).Entities.Lines)[0];assert.equal(line.Layer.Name,'0');assert.equal(line.Linetype.Name,'ByLayer');assert.equal(line.Color.Index,256);
  });
}
