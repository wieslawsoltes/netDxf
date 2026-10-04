// Port of pinned tests/netDxf.Conformance/CommonEntityDataTests.cs.
// The twelve allocation cases and assembly-reflection clone inventory remain unregistered.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, EntityShadowMode, EntityObject,
  DxfVersionNotSupportedException, Line, Vector2, Vector3, Matrix3, Polyline2D, Hatch, HatchPattern, HatchBoundaryPath,
  AttributeDefinition, Block, Circle, Insert, MemoryStream } from '../../index.js';
import { InvalidDataException, ArgumentOutOfRangeException } from '../../runtime/Errors.js';
import { GetTypedIOConfiguration } from '../../runtime/TypedDocumentIO.js';
import { Run, Check, Equal, Throws, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { LegacyOleTags } from './OleFrameTests.js';
import { OlePayload, OleSingle, OleRecords, OleWriteArtifact } from './Ole2FrameTests.js';
import fs from 'node:fs';

export function RegisterCommonEntityDataTests() {
  for (const v of SupportedVersions) for (const b of [false, true]) {
    const id = `${VersionName(v)}/${BooleanName(b)}`;
    for (const n of [-1, 0, 1, 127, 128, 129, 1025]) Run(`common-data/wire/${id}/${n}`, () => CommonDataWire(v, b, n));
    for (let f = 0; f < 15; f++) if (f !== 11) Run(`common-data/malformed/${id}/${f}`, () => CommonDataMalformed(v, b, f));
    Run(`common-data/reordered/${id}`, () => CommonDataReordered(v, b));
    if (v >= DxfVersion.AutoCad2004) Run(`common-data/literal-name/${id}`, () => CommonDataLiteral(v, b));
    Run(`common-data/attributes/${id}`, () => CommonDataAttributes(v, b));
    Run(`common-data/producer/${id}`, () => CommonDataProducer(v, b));
    Run(`common-data/attribute-subclass/${id}`, () => CommonAttributeSubclass(v, b));
    Run(`common-data/payload-boundary/${id}`, () => CommonDataScope(v, b));
    for (let p = 0; p < 3; p++) Run(`common-data/downgrade/${id}/${p}`, () => CommonDataDowngrade(v, b, p));
  }
  Run('common-data/api/isolation-geometry', CommonDataApi);
}
export const CommonDataBytes = count => Uint8Array.from({ length: count }, (_, i) => (i * 37 + 19) & 255);
export const CommonColor = v => v < DxfVersion.AutoCad2004 ? null : 'ACME$青';
export const CommonShadow = v => v < DxfVersion.AutoCad2007 ? null : EntityShadowMode.CastAndReceive;
export function CommonDataTags(v, size) {
  const tags = LegacyOleTags(v, 17, false), at = tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbOleFrame'), common = [];
  if (CommonColor(v) !== null) common.push(new DxfTag(430, v < DxfVersion.AutoCad2007 ? 'ACME$\\U+9752' : CommonColor(v)));
  if (CommonShadow(v) !== null) common.push(new DxfTag(284, 0));
  if (size >= 0) {
    common.push(v < DxfVersion.AutoCad2013 ? new DxfTag(92, size) : new DxfTag(160, BigInt(size)));
    const bytes = CommonDataBytes(size);
    for (let i = 0; i < bytes.length; i += 128) common.push(new DxfTag(310, bytes.slice(i, i + 128)), new DxfTag(60, 0));
  }
  tags.splice(at, 0, ...common); return tags;
}
export function AssertCommonData(entity, v, size) {
  Equal(CommonColor(v), entity.ColorName, 'Common color name');
  Equal(CommonShadow(v), entity.ShadowMode, 'Common shadow presence/value');
  if (size < 0) Check(entity.ProxyGraphics === null, 'Absent proxy became present');
  else Equal(CommonDataBytes(size), entity.ProxyGraphics, 'Common proxy bytes changed');
}
export function CommonSubclass(record) {
  const tags = Array.from(record.Tags), at = tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbEntity');
  const tail = tags.slice(at + 1), end = tail.findIndex(t => t.Code === 100 || t.Code === 0);
  return end < 0 ? tail : tail.slice(0, end);
}
export function CommonDataWire(v, b, size) {
  const input = new MemoryStream(RawFixtureBytes(CommonDataTags(v, size), b));
  try {
    let doc = DxfDocument.Load(input); Check(doc !== null, 'Common packet rejected');
    const frame = OleSingle(doc.Entities.OleFrames); AssertCommonData(frame, v, size);
    const clone = frame.Clone(); AssertCommonData(clone, v, size); doc.Entities.Add(clone);
    for (let cycle = 0; cycle < 2; cycle++) {
      const output = new MemoryStream(); try {
        Check(doc.Save(output, cycle === 0 ? b : !b), 'Common packet save failed');
        if (cycle === 0) OleWriteArtifact(`common-data-wire-${VersionName(v)}-${BooleanName(b)}-${size}.dxf`, output.ToArray());
        output.Position = 0;
        for (const raw of OleRecords(DxfRawDocument.Load(output)).filter(r => r.Name === 'OLEFRAME')) {
          const common = CommonSubclass(raw), counts = common.filter(t => t.Code === 92 || t.Code === 160);
          Equal(size < 0 ? 0 : 1, counts.length, 'Proxy count presence');
          if (size >= 0) {
            const length = OleSingle(counts); Equal(v < DxfVersion.AutoCad2013 ? 92 : 160, length.Code, 'Profile byte-count code');
            Equal(BigInt(size), BigInt(length.Value), 'Proxy wire length');
            Check(common.filter(t => t.Code === 310).every(t => t.Value.length <= 127), 'Writer proxy chunk limit');
            Check(size === 0 || common.findIndex(t => t.Code === 92 || t.Code === 160) < common.findIndex(t => t.Code === 310), 'Proxy count follows bytes');
          }
          Equal(17, OleSingle(Array.from(raw.Tags).filter(t => t.Code === 90)).Value, 'OLE payload count was consumed');
        }
        output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Common packet reload failed');
        for (const f of doc.Entities.OleFrames) {
          AssertCommonData(f, v, size); Equal(OlePayload(17), f.GetBinaryData(), 'Native OLE data merged with proxy graphics');
          Equal('after legacy bytes', OleSingle(f.XData.get_Item('LEGACY_OLE').XDataRecord).Value, 'Following XData');
        }
        Check(new Vector3(10, 20, 30).Equals(OleSingle(doc.Entities.Lines).StartPoint), 'Following entity');
      } finally { output.Dispose(); }
    }
  } finally { input.Dispose(); }
}
export function CommonDataReordered(v, b) {
  const tags = CommonDataTags(v, 129), index = tags.findIndex(t => t.Code === 92 || t.Code === 160);
  let count = tags.splice(index, 1)[0]; if (v >= DxfVersion.AutoCad2013) count = new DxfTag(92, 129);
  tags.splice(tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbOleFrame'), 0, count);
  const input = new MemoryStream(RawFixtureBytes(tags, b)); try {
    const doc = DxfDocument.Load(input); Check(doc !== null, 'Reordered common packet rejected'); AssertCommonData(OleSingle(doc.Entities.OleFrames), v, 129);
  } finally { input.Dispose(); }
}
export function CommonAttributeDocument(v) {
  const doc = new DxfDocument(v), definition = new AttributeDefinition('TAG');
  Object.assign(definition, { Value: 'defined', ColorName: CommonColor(v), ShadowMode: CommonShadow(v), ProxyGraphics: CommonDataBytes(129) });
  const block = new Block('COMMON_BLOCK'); block.AttributeDefinitions.Add(definition); block.Entities.Add(new Circle(Vector3.Zero, 2));
  doc.Entities.Add(new Insert(block, new Vector3(10, 20, 0))); return doc;
}
export function CommonDataLiteral(v, b) {
  for (const value of ['', 'Book$青\\U+0041\\u+FFFF\\name', 'Book$\0\r\n青']) {
    let doc = CommonAttributeDocument(v), insert = OleSingle(doc.Entities.Inserts);
    insert.ColorName = value; OleSingle(insert.Attributes).ColorName = value;
    doc.Blocks.get_Item('COMMON_BLOCK').AttributeDefinitions.get_Item('TAG').ColorName = value;
    for (let cycle = 0; cycle < 2; cycle++) {
      const output = new MemoryStream(); try {
        Check(doc.Save(output, b), 'Literal name save'); output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Literal name reload');
        insert = OleSingle(doc.Entities.Inserts); Equal(value, insert.ColorName, 'Entity literal color name');
        Equal(value, OleSingle(insert.Attributes).ColorName, 'Attribute literal color name');
        Equal(value, doc.Blocks.get_Item('COMMON_BLOCK').AttributeDefinitions.get_Item('TAG').ColorName, 'Definition literal color name');
      } finally { output.Dispose(); }
    }
  }
}
export function CommonDataReject(tags, b) {
  const stream = new MemoryStream(RawFixtureBytes(tags, b)); try {
    if (GetTypedIOConfiguration() === 'Debug') {
      let caught = null; try { DxfDocument.Load(stream); } catch (error) { if (!(error instanceof InvalidDataException)) throw error; caught = error; }
      Check(caught !== null, 'Malformed common data accepted'); Check(caught.message.includes('AcDbEntity'), 'Missing common subclass diagnostic');
    } else Check(DxfDocument.Load(stream) === null, 'Malformed common data accepted');
    Check(stream.CanRead, 'Reader closed caller stream');
  } finally { stream.Dispose(); }
}
export function CommonDataMalformed(v, b, fault) {
  const tags = CommonDataTags(v, 129), at = tags.findIndex(t => t.Code === 92 || t.Code === 160), code = tags[at].Code;
  const length = n => new DxfTag(code, code === 92 ? n : BigInt(n));
  switch (fault) {
    case 0: tags[at] = length(-1); break; case 1: tags[at] = length(128); break; case 2: tags[at] = length(130); break;
    case 3: tags[at] = length(EntityObject.MaximumProxyGraphicsBytes + 1); break; case 4: tags[at] = length(2147483647); break;
    case 5: tags.splice(at, 0, tags[at]); break; case 6: tags.splice(at, 1); break;
    case 7: tags[at + 1] = new DxfTag(310, CommonDataBytes(129)); break;
    case 8: tags.splice(at, 0, new DxfTag(284, 4)); break;
    case 9: tags.splice(at, 0, new DxfTag(430, 'A'), new DxfTag(430, 'B')); break;
    case 10: tags.splice(at, 0, new DxfTag(284, 0), new DxfTag(284, 1)); break;
    case 12: tags[tags.findIndex(t => t.Code === 100)] = new DxfTag(100, 'AcDbNotEntity'); break;
    case 13: if (v < DxfVersion.AutoCad2010) tags[at] = new DxfTag(160, 129n); else tags.splice(at + 1, 0, new DxfTag(92, 129)); break;
    case 14: tags.splice(tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbOleFrame'), 0, new DxfTag(100, 'AcDbEntity')); break;
    default: throw new Error('The allocation-sensitive case is deliberately unregistered.');
  }
  CommonDataReject(tags, b);
}
export function CommonDataAttributes(v, b) {
  let doc = CommonAttributeDocument(v); const source = OleSingle(doc.Entities.Inserts), copy = source.Clone();
  Equal(CommonColor(v), OleSingle(copy.Attributes).ColorName, 'Attribute clone name');
  Equal(CommonDataBytes(129), OleSingle(copy.Attributes).ProxyGraphics, 'Attribute clone proxy');
  Equal(CommonDataBytes(129), copy.Block.AttributeDefinitions.get_Item('TAG').ProxyGraphics, 'Definition clone proxy');
  OleSingle(source.Attributes).Value = 'instance'; const output = new MemoryStream(); try {
    Check(doc.Save(output, b), 'Attribute common save'); OleWriteArtifact(`common-data-attributes-${VersionName(v)}-${BooleanName(b)}.dxf`, output.ToArray());
    output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Attribute common reload');
    const att = OleSingle(OleSingle(doc.Entities.Inserts).Attributes), def = doc.Blocks.get_Item('COMMON_BLOCK').AttributeDefinitions.get_Item('TAG');
    Equal('instance', att.Value, 'Attribute value after common data'); Equal('defined', def.Value, 'Definition value after common data');
    Equal(CommonColor(v), att.ColorName, 'Attribute name'); Equal(CommonColor(v), def.ColorName, 'Definition name');
    Equal(CommonShadow(v), att.ShadowMode, 'Attribute shadow'); Equal(CommonShadow(v), def.ShadowMode, 'Definition shadow');
    Equal(CommonDataBytes(129), att.ProxyGraphics, 'Attribute proxy packet'); Equal(CommonDataBytes(129), def.ProxyGraphics, 'Definition proxy packet');
  } finally { output.Dispose(); }
}
export const CommonDataFixture = (v, b) => `tests/fixtures/common-entity-data/ezdxf-common-R${VersionName(v).slice(7)}-${BooleanName(b)}.dxf`;
export function CommonDataProducer(v, b) {
  let doc = DxfDocument.Load(CommonDataFixture(v, b)); Check(doc !== null, 'Independent common input rejected');
  const proxy = OleSingle(doc.Entities.Lines).ProxyGraphics; Check(proxy !== null, 'Independent proxy absent'); Equal(164, proxy.length, 'Independent valid polyline proxy size');
  for (let cycle = 0; cycle < 2; cycle++) {
    const line = OleSingle(doc.Entities.Lines); Equal(CommonColor(v), line.ColorName, 'Independent color name');
    Equal(v >= DxfVersion.AutoCad2007 ? EntityShadowMode.Ignore : null, line.ShadowMode, 'Independent shadow');
    Equal(proxy, line.ProxyGraphics, 'Independent proxy changed');
    Equal(proxy, OleSingle(OleSingle(doc.Entities.Inserts).Attributes).ProxyGraphics, 'Independent ATTRIB proxy changed');
    Equal(proxy, doc.Blocks.get_Item('COMMON_BLOCK').AttributeDefinitions.get_Item('TAG').ProxyGraphics, 'Independent ATTDEF proxy changed');
    Check(new Vector3(123, 456, 789).Equals(OleSingle(doc.Entities.Points).Position), 'Independent following entity');
    Equal('after common data', line.XData.get_Item('COMMON_DATA_QA').XDataRecord.get_Item(0).Value, 'Independent XData');
    const output = new MemoryStream(); try {
      Check(doc.Save(output, b), 'Independent common save'); if (cycle === 1) OleWriteArtifact(`common-data-producer-${VersionName(v)}-${BooleanName(b)}.dxf`, output.ToArray());
      output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Independent common reload');
    } finally { output.Dispose(); }
  }
}
export function CommonAttributeSubclass(v, b) {
  const input = new MemoryStream(fs.readFileSync(CommonDataFixture(v, b))); let raw;
  try { raw = DxfRawDocument.Load(input); } finally { input.Dispose(); }
  for (const duplicate of [false, true]) {
    const tags = Array.from(raw.Tags), start = tags.findIndex(t => t.Code === 0 && t.Value === 'ATTRIB');
    const common = tags.findIndex((t, i) => i >= start && t.Code === 100);
    if (duplicate) tags.splice(tags.findIndex((t, i) => i > common && t.Code === 100), 0, new DxfTag(100, 'AcDbEntity'));
    else tags[common] = new DxfTag(100, 'AcDbNotEntity');
    CommonDataReject(tags, b);
  }
}
export function CommonDataScope(v, b) {
  let doc = new DxfDocument(v); const boundary = new Polyline2D([Vector2.Zero, new Vector2(4, 0), new Vector2(4, 3), new Vector2(0, 3)], true);
  const hatch = new Hatch(HatchPattern.Solid, [new HatchBoundaryPath([boundary])], false); hatch.ProxyGraphics = CommonDataBytes(128); doc.Entities.Add(hatch);
  const output = new MemoryStream(); try {
    Check(doc.Save(output, b), 'Hatch common save'); output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Hatch common reload');
    Equal(CommonDataBytes(128), OleSingle(doc.Entities.Hatches).ProxyGraphics, 'Hatch proxy bytes');
    Equal(1, OleSingle(doc.Entities.Hatches).BoundaryPaths.Count, 'Hatch92 boundary count consumed by proxy reader');
  } finally { output.Dispose(); }
}
export function CommonDataDowngrade(v, b, location) {
  const doc = CommonAttributeDocument(DxfVersion.AutoCad2007), insert = OleSingle(doc.Entities.Inserts);
  const definition = doc.Blocks.get_Item('COMMON_BLOCK').AttributeDefinitions.get_Item('TAG'), attribute = OleSingle(insert.Attributes);
  definition.ColorName = null; definition.ShadowMode = null; attribute.ColorName = null; attribute.ShadowMode = null;
  const selected = [insert, definition, attribute][location]; selected.ColorName = ''; selected.ShadowMode = EntityShadowMode.CastAndReceive;
  doc.DrawingVariables.AcadVer = v; const output = new MemoryStream(); try {
    output.Write(Uint8Array.of(7, 8, 9)); const position = output.Position;
    if (v < DxfVersion.AutoCad2007) {
      Throws(DxfVersionNotSupportedException, () => doc.Save(output, b)); Equal(position, output.Position, 'Downgrade changed stream position');
      Equal(Uint8Array.of(7, 8, 9), output.ToArray(), 'Downgrade wrote partial output');
    } else Check(doc.Save(output, b), 'Admitted common metadata save');
  } finally { output.Dispose(); }
}
export function CommonDataApi() {
  const bytes = CommonDataBytes(129), line = new Line(Vector3.Zero, Vector3.UnitX);
  Object.assign(line, { ProxyGraphics: bytes, ColorName: '', ShadowMode: EntityShadowMode.CastAndReceive });
  bytes[0] ^= 255; Check(line.ProxyGraphics[0] !== bytes[0], 'Proxy setter retained caller array');
  const exported = line.ProxyGraphics; exported[0] ^= 255; Check(line.ProxyGraphics[0] !== exported[0], 'Proxy getter exposed storage');
  const copy = line.Clone(), copyBytes = copy.ProxyGraphics; copyBytes[0] = 200; copy.ProxyGraphics = copyBytes;
  Check(line.ProxyGraphics[0] !== 200, 'Proxy clone shares storage'); line.TransformBy(Matrix3.Scale(2), new Vector3(3, 4, 5));
  Check(new Vector3(5, 4, 5).Equals(line.EndPoint), 'Stored proxy disabled geometry transformation');
  Equal(CommonDataBytes(129), line.ProxyGraphics, 'Transform altered opaque cache');
  const polyline = new Polyline2D([Vector2.Zero, Vector2.UnitX, Vector2.UnitY]); polyline.ProxyGraphics = CommonDataBytes(129);
  polyline.Reverse(); polyline.Reverse(); Equal(CommonDataBytes(129), polyline.ProxyGraphics, 'Reverse altered opaque cache');
  Throws(ArgumentOutOfRangeException, () => { line.ShadowMode = 4; });
  Throws(ArgumentOutOfRangeException, () => { line.ProxyGraphics = new Uint8Array(EntityObject.MaximumProxyGraphicsBytes + 1); });
  Equal(CommonDataBytes(129), line.ProxyGraphics, 'Failed proxy setter mutated cache'); line.ClearProxyGraphics(); Check(line.ProxyGraphics === null, 'Clear kept an empty packet');
}
