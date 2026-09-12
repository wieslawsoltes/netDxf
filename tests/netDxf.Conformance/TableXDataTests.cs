using netDxf;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunTableXDataTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion capturedVersion = version;
                bool capturedBinary = binary;
                Run($"tables/ucs-xdata-isolation/{version}/{(binary ? "binary" : "text")}",
                    () => UcsTableXDataRoundTrip(capturedVersion, capturedBinary));
            }
        }
    }

    private static void UcsTableXDataRoundTrip(DxfVersion version, bool binary)
    {
        const string ucsApplication = "DXF_UCS_TABLE";
        const string blockApplication = "DXF_BLOCK_TABLE";
        var document = new DxfDocument(version);
        var ucsData = new XData(new ApplicationRegistry(ucsApplication));
        ucsData.XDataRecord.Add(new XDataRecord(XDataCode.String, "UCS table payload"));
        var blockData = new XData(new ApplicationRegistry(blockApplication));
        blockData.XDataRecord.Add(new XDataRecord(XDataCode.String, "BLOCK_RECORD table payload"));
        document.UCSs.XData.Add(ucsData);
        document.Blocks.XData.Add(blockData);
        using var stream = new MemoryStream();
        Check(document.Save(stream, binary), "Table XData fixture failed to save.");
        stream.Position = 0;
        DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Table XData fixture failed to load.");
        Check(loaded.UCSs.XData.ContainsAppId(ucsApplication), "UCS table lost its own XData application.");
        Check(!loaded.UCSs.XData.ContainsAppId(blockApplication), "UCS table acquired BLOCK_RECORD XData.");
        Check(loaded.Blocks.XData.ContainsAppId(blockApplication), "BLOCK_RECORD table lost its own XData application.");
        Check(!loaded.Blocks.XData.ContainsAppId(ucsApplication), "BLOCK_RECORD table acquired UCS XData.");
        Equal("UCS table payload", (string)loaded.UCSs.XData[ucsApplication].XDataRecord.Single().Value, "UCS payload");
        Equal("BLOCK_RECORD table payload", (string)loaded.Blocks.XData[blockApplication].XDataRecord.Single().Value, "BLOCK_RECORD payload");
    }
}
