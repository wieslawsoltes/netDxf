// Complete port of pinned tests/netDxf.Conformance/TableXDataTests.cs.
import { DxfDocument, XData, XDataRecord, ApplicationRegistry, MemoryStream } from '../../index.js';
import { Run, Check, Equal, SupportedVersions, VersionName } from './TestHarness.js';
import { OleSingle } from './Ole2FrameTests.js';
export function RegisterTableXDataTests() {
  for (const v of SupportedVersions) for (const b of [false, true])
    Run(`tables/ucs-xdata-isolation/${VersionName(v)}/${b ? 'binary' : 'text'}`, () => UcsTableXDataRoundTrip(v, b));
}
export function UcsTableXDataRoundTrip(v, b) {
  const ucsApplication = 'DXF_UCS_TABLE', blockApplication = 'DXF_BLOCK_TABLE', document = new DxfDocument(v);
  const ucsData = new XData(new ApplicationRegistry(ucsApplication)), blockData = new XData(new ApplicationRegistry(blockApplication));
  ucsData.XDataRecord.Add(new XDataRecord(1000, 'UCS table payload')); blockData.XDataRecord.Add(new XDataRecord(1000, 'BLOCK_RECORD table payload'));
  document.UCSs.XData.Add(ucsData); document.Blocks.XData.Add(blockData);
  const stream = new MemoryStream(); try {
    Check(document.Save(stream, b), 'Table XData fixture failed to save.'); stream.Position = 0;
    const loaded = DxfDocument.Load(stream); Check(loaded !== null, 'Table XData fixture failed to load.');
    Check(loaded.UCSs.XData.ContainsAppId(ucsApplication), 'UCS table lost its own XData application.');
    Check(!loaded.UCSs.XData.ContainsAppId(blockApplication), 'UCS table acquired BLOCK_RECORD XData.');
    Check(loaded.Blocks.XData.ContainsAppId(blockApplication), 'BLOCK_RECORD table lost its own XData application.');
    Check(!loaded.Blocks.XData.ContainsAppId(ucsApplication), 'BLOCK_RECORD table acquired UCS XData.');
    Equal('UCS table payload', OleSingle(loaded.UCSs.XData.get_Item(ucsApplication).XDataRecord).Value, 'UCS payload');
    Equal('BLOCK_RECORD table payload', OleSingle(loaded.Blocks.XData.get_Item(blockApplication).XDataRecord).Value, 'BLOCK_RECORD payload');
  } finally { stream.Dispose(); }
}
