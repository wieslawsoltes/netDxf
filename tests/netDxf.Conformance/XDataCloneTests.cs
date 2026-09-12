using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string CloneApplication = "DXF_CLONE_CONFORMANCE";

    private static void RegisterXDataCloneTests()
    {
        foreach (int count in new[] { 0, 1, 127 })
        {
            int length = count;
            Run($"xdata/clone/binary-length-{length}", () => CheckBinaryClone(length));
        }
        Run("xdata/clone/shared-input-records", CheckSharedBinaryClone);
        Run("xdata/clone/scalar-records", CheckScalarClone);
        Run("xdata/clone/entity", () => CheckObjectClone(new Line(Vector3.Zero, Vector3.UnitX)));
        Run("xdata/clone/layer", () => CheckObjectClone(new Layer("CloneLayer")));
        Run("xdata/clone/block-entity", CheckBlockEntityClone);
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion capturedVersion = version;
                bool capturedBinary = binary;
                Run($"xdata/clone/document/{version}/{(binary ? "binary" : "text")}",
                    () => CheckClonedDocument(capturedVersion, capturedBinary));
            }
        }
    }

    private static XData BinaryXData(byte[] bytes)
    {
        var data = new XData(new ApplicationRegistry(CloneApplication));
        data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, bytes));
        return data;
    }

    private static byte[] BinaryValue(XData data, int index = 0) => (byte[])data.XDataRecord[index].Value;

    private static void CheckBinaryClone(int length)
    {
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        XData source = BinaryXData(bytes);
        var copy = (XData)source.Clone();
        var sibling = (XData)source.Clone();
        Equal(source.ApplicationRegistry.Name, copy.ApplicationRegistry.Name, "application name");
        Check(!ReferenceEquals(source.ApplicationRegistry, copy.ApplicationRegistry), "Application registry was shared.");
        Check(!ReferenceEquals(source.XDataRecord[0], copy.XDataRecord[0]), "Record was shared.");
        Check(!ReferenceEquals(bytes, BinaryValue(copy)), "Binary payload was shared with source.");
        Check(!ReferenceEquals(BinaryValue(copy), BinaryValue(sibling)), "Sibling clones share binary data.");
        Check(bytes.SequenceEqual(BinaryValue(copy)), "Cloning changed binary data.");
        if (length > 0)
        {
            bytes[0] = 200;
            Equal((byte)0, BinaryValue(copy)[0], "source mutation must not change clone");
            BinaryValue(copy)[0] = 150;
            Equal((byte)200, bytes[0], "clone mutation must not change source");
            Equal((byte)0, BinaryValue(sibling)[0], "clone mutation must not change sibling");
        }
        copy.XDataRecord.Clear();
        Equal(1, source.XDataRecord.Count, "record list ownership");
    }

    private static void CheckSharedBinaryClone()
    {
        byte[] bytes = { 1, 2, 3 };
        XData source = BinaryXData(bytes);
        source.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, bytes));
        var copy = (XData)source.Clone();
        Check(!ReferenceEquals(BinaryValue(copy, 0), bytes), "First payload aliases source.");
        Check(!ReferenceEquals(BinaryValue(copy, 1), bytes), "Second payload aliases source.");
        BinaryValue(copy, 0)[0] = 77;
        Equal((byte)1, bytes[0], "shared-input clone isolation");
    }

    private static void CheckScalarClone()
    {
        var source = new XData(new ApplicationRegistry(CloneApplication));
        foreach (XDataCode code in Enum.GetValues(typeof(XDataCode)))
        {
            if (code == XDataCode.AppReg || code == XDataCode.BinaryData) continue;
            object value = code switch
            {
                XDataCode.ControlString => "{",
                XDataCode.DatabaseHandle => "ABC123",
                XDataCode.String => "Zażółć gęślą jaźń",
                XDataCode.LayerName => "Geometry",
                XDataCode.Int16 => (object)(short)-42,
                XDataCode.Int32 => -123456,
                _ => 12.75
            };
            source.XDataRecord.Add(new XDataRecord(code, value));
        }
        var copy = (XData)source.Clone();
        Equal(source.XDataRecord.Count, copy.XDataRecord.Count, "scalar record count");
        for (int i = 0; i < source.XDataRecord.Count; i++)
        {
            Equal(source.XDataRecord[i].Code, copy.XDataRecord[i].Code, "scalar code");
            Equal(source.XDataRecord[i].Value.GetType(), copy.XDataRecord[i].Value.GetType(), "scalar type");
            Equal(source.XDataRecord[i].Value, copy.XDataRecord[i].Value, "scalar value");
        }
    }

    private static void CheckObjectClone(DxfObject source)
    {
        source.XData.Add(BinaryXData(new byte[] { 1, 2, 3 }));
        var copy = (DxfObject)((ICloneable)source).Clone();
        BinaryValue(copy.XData[CloneApplication])[0] = 99;
        Equal((byte)1, BinaryValue(source.XData[CloneApplication])[0], "DxfObject clone payload isolation");
    }

    private static void CheckBlockEntityClone()
    {
        var line = new Line(Vector3.Zero, Vector3.UnitX);
        line.XData.Add(BinaryXData(new byte[] { 1, 2, 3 }));
        var block = new Block("CloneBlock");
        block.Entities.Add(line);
        var copy = (Block)block.Clone();
        BinaryValue(copy.Entities.Single().XData[CloneApplication])[0] = 99;
        Equal((byte)1, BinaryValue(line.XData[CloneApplication])[0], "Nested entity payload isolation");
    }

    private static void CheckClonedDocument(DxfVersion version, bool binary)
    {
        var source = new Line(Vector3.Zero, Vector3.UnitX);
        source.XData.Add(BinaryXData(new byte[] { 10, 20, 30 }));
        var copy = (Line)source.Clone();
        copy.StartPoint = new Vector3(5, 0, 0);
        BinaryValue(copy.XData[CloneApplication])[0] = 99;
        BinaryValue(source.XData[CloneApplication])[1] = 77;
        var document = new DxfDocument(version);
        document.Entities.Add(source);
        document.Entities.Add(copy);
        using var stream = new MemoryStream();
        Check(document.Save(stream, binary), "Clone fixture failed to save.");
        stream.Position = 0;
        DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Clone fixture failed to load.");
        byte[] original = BinaryValue(loaded.Entities.Lines.Single(line => line.StartPoint.X == 0).XData[CloneApplication]);
        byte[] cloned = BinaryValue(loaded.Entities.Lines.Single(line => line.StartPoint.X == 5).XData[CloneApplication]);
        Check(original.SequenceEqual(new byte[] { 10, 77, 30 }), "Serialized original contains clone edits.");
        Check(cloned.SequenceEqual(new byte[] { 99, 20, 30 }), "Serialized clone contains original edits.");
    }
}
