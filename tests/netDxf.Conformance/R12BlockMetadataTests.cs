// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12BlockMetadataTests()
    {
        foreach (bool binary in new[] { false, true })
        for (int fault = -1; fault < 12; fault++)
        {
            int f = fault;
            Run($"r12-block-metadata/neutral-design-center/{binary}/{f}", () => RbmNeutral(binary, f));
        }
    }

    private static void RbmNeutral(bool binary, int fault)
    {
        var root = RbSeeds()[0]; var record = root.Block.Record;
        var data = new XData(new ApplicationRegistry(fault == 0 ? "OTHER" : "ACAD"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "DesignCenter Data"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.ControlString, "{"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)0));
        data.XDataRecord.Add(new XDataRecord(XDataCode.ControlString, "}"));
        record.XData.Add(data);
        switch (fault)
        {
            case 1: data.XDataRecord[0] = new XDataRecord(XDataCode.String, "Private design data"); break;
            case 2: data.XDataRecord[1] = new XDataRecord(XDataCode.ControlString, "}"); break;
            case 3: data.XDataRecord[2] = new XDataRecord(XDataCode.Int16, (short)2); break;
            case 4: data.XDataRecord[3] = new XDataRecord(XDataCode.Int16, (short)1); break;
            case 5: data.XDataRecord[4] = new XDataRecord(XDataCode.ControlString, "{"); break;
            case 6: data.XDataRecord.Add(new XDataRecord(XDataCode.String, "private suffix")); break;
            case 7: data.XDataRecord.RemoveAt(4); break;
            case 8: data.XDataRecord[2] = new XDataRecord(XDataCode.Int32, 1); break;
            case 9: data.XDataRecord[3] = new XDataRecord(XDataCode.Int32, 0); break;
            case 10: record.XData.Add(new XData(new ApplicationRegistry("PRIVATE"))); break;
            case 11: record.Units = DrawingUnits.Millimeters; break;
        }
        var before = data.XDataRecord.ToArray();
        using var output = new MemoryStream(); output.WriteByte(97); output.Position = 0;
        if (fault < 0)
        {
            DxfR12Codec.Save(output, new[] { root }, binary); output.Position = 0;
            var decoded = (Insert)DxfR12Codec.ReadEntities(DxfRawDocument.Load(output)).Single();
            RbSameInsert(root, decoded);
            Equal(DrawingUnits.Unitless, decoded.Block.Record.Units, "Neutral block units");
            Equal(0, decoded.Block.Record.XData.Count, "R12 has no BLOCK_RECORD metadata carrier");
        }
        else
        {
            Throws<NotSupportedException>(() => DxfR12Codec.Save(output, new[] { root }, binary));
            Check(output.Position == 0 && output.Length == 1 && output.ToArray()[0] == 97, "Unsupported metadata touched output");
        }
        Check(before.SequenceEqual(data.XDataRecord) && ReferenceEquals(data, record.XData[data.ApplicationRegistry.Name]),
            "Projection changed caller metadata");
        Check(root.Handle == null && record.Handle == null && record.Owner == null, "Projection adopted caller graph");
    }
}
