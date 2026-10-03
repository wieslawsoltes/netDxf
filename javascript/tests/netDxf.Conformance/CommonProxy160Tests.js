// Port of pinned CommonProxy160Tests.cs; twelve native allocation cases remain unregistered.
import { DxfDocument, DxfRawDocument, DxfTag, DxfVersion, MemoryStream, Line, Vector3, XData, XDataRecord, ApplicationRegistry } from '../../index.js';
import { Run, Check, Equal, SupportedVersions, VersionName, BooleanName } from './TestHarness.js';
import { RawFixtureBytes } from './RawDocumentTests.js';
import { OlePayload, OleSingle, OleRecords, OleWriteArtifact } from './Ole2FrameTests.js';
import { CommonDataTags, CommonDataBytes, CommonDataReject, CommonSubclass, AssertCommonData } from './CommonEntityDataTests.js';
import fs from 'node:fs';
import { gunzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
const NativeFile = 'sample_AC1024_ascii.dxf';
const NativeHash = 'c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const proxyBytes = record => Uint8Array.from(CommonSubclass(record).filter(t => t.Code === 310).flatMap(t => Array.from(t.Value)));
export function RegisterCommonProxy160Tests() {
  for (const v of SupportedVersions) for (const b of [false, true]) {
    const id = `${VersionName(v)}/${BooleanName(b)}`;
    for (const size of [0, 1, 127, 128, 129, 1025]) Run(`common-data/proxy160/wire/${id}/${size}`, () => CommonProxy160Wire(v, b, size));
    for (let f = 0; f < 6; f++) if (f !== 4) Run(`common-data/proxy160/malformed/${id}/${f}`, () => CommonProxy160Malformed(v, b, f));
  }
  for (const b of [false, true]) Run(`common-data/proxy160/native-packets/${BooleanName(b)}`, () => CommonProxy160Native(b));
}
export function CommonProxy160Tags(v, size) {
  const tags = CommonDataTags(v, size); tags[tags.findIndex(t => t.Code === 92 || t.Code === 160)] = new DxfTag(160, BigInt(size)); return tags;
}
export function CommonProxy160Wire(v, b, size) {
  for (const reordered of [false, true]) {
    const tags = CommonProxy160Tags(v, size);
    if (reordered) { const at = tags.findIndex(t => t.Code === 160), count = tags.splice(at, 1)[0]; tags.splice(tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbOleFrame'), 0, count); }
    if (v < DxfVersion.AutoCad2010) { CommonDataReject(tags, b); continue; }
    const input = new MemoryStream(RawFixtureBytes(tags, b)); try {
      let doc = DxfDocument.Load(input); Check(doc !== null, 'Valid group 160 packet rejected');
      let frame = OleSingle(doc.Entities.OleFrames); AssertCommonData(frame, v, size); const clone = frame.Clone(); AssertCommonData(clone, v, size);
      clone.ProxyGraphics = Uint8Array.of(255); AssertCommonData(frame, v, size);
      for (let cycle = 0; cycle < 2; cycle++) {
        const output = new MemoryStream(); try {
          Check(doc.Save(output, cycle === 0 ? b : !b), 'Group 160 save failed'); output.Position = 0;
          const record = OleSingle(OleRecords(DxfRawDocument.Load(output)).filter(r => r.Name === 'OLEFRAME'));
          const common = CommonSubclass(record), count = OleSingle(common.filter(t => t.Code === 92 || t.Code === 160));
          Equal(v < DxfVersion.AutoCad2013 ? 92 : 160, count.Code, 'Canonical output count code');
          Equal(BigInt(size), BigInt(count.Value), 'Canonical output count value');
          Check(common.filter(t => t.Code === 310).every(t => t.Value.length <= 127), 'Canonical output chunk size');
          output.Position = 0; doc = DxfDocument.Load(output); Check(doc !== null, 'Group 160 reload failed');
          frame = OleSingle(doc.Entities.OleFrames); AssertCommonData(frame, v, size);
          Equal(OlePayload(17), frame.GetBinaryData(), 'Proxy packet consumed native OLE bytes');
          Equal('after legacy bytes', OleSingle(frame.XData.get_Item('LEGACY_OLE').XDataRecord).Value, 'Following XData changed');
          Check(new Vector3(10, 20, 30).Equals(OleSingle(doc.Entities.Lines).StartPoint), 'Following entity changed');
        } finally { output.Dispose(); }
      }
    } finally { input.Dispose(); }
  }
}
export function CommonProxy160Malformed(v, b, fault) {
  const variants = [], length = value => { const tags = CommonProxy160Tags(v, 129); tags[tags.findIndex(t => t.Code === 160)] = new DxfTag(160, value); variants.push(tags); };
  switch (fault) {
    case 0: length(-1n); length(-9223372036854775808n); break;
    case 1: length(9223372036854775807n); length(4294967425n); length(2147483647n); break;
    case 2: length(128n); length(130n); break;
    case 3:
      for (const code of [92, 160]) { const tags = CommonProxy160Tags(v, 129), at = tags.findIndex(t => t.Code === 160); tags.splice(at, 0, new DxfTag(code, code === 92 ? 129 : 129n)); variants.push(tags); } break;
    case 5: {
      const oversized = CommonProxy160Tags(v, 129); oversized[oversized.findIndex(t => t.Code === 310)] = new DxfTag(310, CommonDataBytes(129)); variants.push(oversized);
      const noCount = CommonProxy160Tags(v, 129); noCount.splice(noCount.findIndex(t => t.Code === 160), 1); variants.push(noCount); break;
    }
    default: throw new Error('The allocation-sensitive case is deliberately unregistered.');
  }
  for (const tags of variants) CommonDataReject(tags, b);
}
export function CommonProxy160Native(b) {
  const originalBytes = gunzipSync(fs.readFileSync(`tests/fixtures/table-oracle/${NativeFile}.gz`));
  Equal(NativeHash, sha256(originalBytes), 'Native proxy source hash');
  const original = new MemoryStream(originalBytes); let source; try { source = DxfRawDocument.Load(original); } finally { original.Dispose(); }
  Equal(DxfVersion.AutoCad2010, source.Version, 'Native proxy source profile');
  const packets = OleRecords(source).filter(r => CommonSubclass(r).some(t => t.Code === 160));
  Equal(22, packets.length, 'Pinned native proxy packet inventory');
  Equal(45416n, packets.reduce((sum, r) => sum + BigInt(OleSingle(CommonSubclass(r).filter(t => t.Code === 160)).Value), 0n), 'Pinned native proxy byte inventory');
  const setup = new DxfDocument(DxfVersion.AutoCad2010);
  for (const packet of packets) {
    const handle = OleSingle(Array.from(packet.Tags).filter(t => t.Code === 5)).Value, bytes = proxyBytes(packet);
    const line = new Line(new Vector3(Array.from(setup.Entities.Lines).length, 0, 0), Vector3.UnitY), data = new XData(new ApplicationRegistry('NATIVE_PROXY_SOURCE'));
    for (const value of [NativeFile, handle, packet.Name, sha256(bytes)]) data.XDataRecord.Add(new XDataRecord(1000, value));
    line.XData.Add(data); setup.Entities.Add(line);
  }
  const saved = new MemoryStream(); let raw; try { Check(setup.Save(saved, b), 'Native carrier setup failed'); saved.Position = 0; raw = DxfRawDocument.Load(saved); } finally { saved.Dispose(); }
  for (let i = 0; i < packets.length; i++) {
    const carrier = Array.from(OleSingle(Array.from(raw.Sections).filter(s => s.Name === 'ENTITIES')).Records).filter(r => r.Name === 'LINE')[i];
    const tags = Array.from(carrier.Tags), boundary = tags.findIndex(t => t.Code === 100 && t.Value === 'AcDbLine');
    tags.splice(boundary, 0, ...CommonSubclass(packets[i]).filter(t => t.Code === 160 || t.Code === 310)); raw = raw.WithRecord(carrier, tags);
  }
  const input = new MemoryStream(); try {
    raw.Save(input, b); input.Position = 0; let loaded = DxfDocument.Load(input); Check(loaded !== null, 'Native R2010 proxy carriers rejected');
    for (let cycle = 0; cycle < 2; cycle++) {
      Equal(22, Array.from(loaded.Entities.Lines).length, 'Native carrier count');
      for (const line of loaded.Entities.Lines) {
        const handle = line.XData.get_Item('NATIVE_PROXY_SOURCE').XDataRecord.get_Item(1).Value;
        const packet = OleSingle(packets.filter(r => OleSingle(Array.from(r.Tags).filter(t => t.Code === 5)).Value === handle)), bytes = proxyBytes(packet);
        Equal(bytes, line.ProxyGraphics, 'Native cache bytes changed'); const clone = line.Clone(); Equal(bytes, clone.ProxyGraphics, 'Native clone bytes changed');
        clone.ClearProxyGraphics(); Equal(bytes, line.ProxyGraphics, 'Native clone shares cache state');
      }
      const output = new MemoryStream(); try {
        Check(loaded.Save(output, cycle === 0 ? b : !b), 'Native carrier save failed');
        if (cycle === 0) OleWriteArtifact(`common-data-native160-AutoCad2010-${BooleanName(b)}.dxf`, output.ToArray());
        output.Position = 0; loaded = DxfDocument.Load(output); Check(loaded !== null, 'Native carrier reload failed');
      } finally { output.Dispose(); }
    }
  } finally { input.Dispose(); }
}
