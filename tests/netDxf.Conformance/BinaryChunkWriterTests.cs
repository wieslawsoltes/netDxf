using netDxf;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBinaryChunkWriterTests()
    {
        foreach (short code in Enumerable.Range(310, 10).Append(1004).Select(c => (short)c))
        {
            short c = code;
            Run($"binary-output/chunk/all-byte-lengths/{c}", () => BinaryChunkWriterLengths(c));
            foreach (int length in new[] { 256, 257, 511, 512, 65535 })
            {
                int n = length;
                Run($"binary-output/chunk/oversize/{c}/{n}", () => BinaryChunkWriterRejected<ArgumentOutOfRangeException>(c, new byte[n]));
            }
            Run($"binary-output/chunk/null/{c}", () => BinaryChunkWriterRejected<ArgumentNullException>(c, null));
            Run($"binary-output/chunk/wrong-type/{c}", () => BinaryChunkWriterRejected<ArgumentException>(c, "00FF"));
        }
        Run("binary-output/chunk/direct-helper", BinaryChunkWriterDirect);
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            Run($"binary-output/chunk/xdata/{v}", () => BinaryChunkWriterXData(v));
        }
    }

    private static void BinaryChunkWriterLengths(short code)
    {
        for (int length = 0; length <= 255; ++length)
        {
            byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            using var stream = new MemoryStream();
            object writer = NewCodeWriter(stream, true);
            Invoke(writer, "Write", code, bytes);
            Invoke(writer, "Write", (short)0, "EOF"); Invoke(writer, "Flush");
            byte[] raw = stream.ToArray();
            Equal((byte)length, raw[24], "One-byte chunk length prefix");
            Check(bytes.SequenceEqual(raw.Skip(25).Take(length)), "Writer changed chunk bytes.");
            stream.Position = 0; object reader = NewCodeReader(stream, true);
            Invoke(reader, "Next"); Equal(code, TagCode(reader), "Chunk group code");
            Check(bytes.SequenceEqual((byte[])Invoke(reader, "ReadBytes")!), "Chunk did not round trip.");
            Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Chunk misaligned the following record");
        }
    }

    private static void BinaryChunkWriterRejected<T>(short code, object? value) where T : Exception
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 12, 34, 56 });
        object writer = NewCodeWriter(stream, true);
        Invoke(writer, "Write", (short)1, "prior value"); Invoke(writer, "Flush");
        byte[] before = stream.ToArray(); long position = stream.Position;
        Throws<T>(() => Invoke(writer, "Write", code, value!));
        Equal(position, stream.Position, "Invalid chunk advanced the stream");
        Check(before.SequenceEqual(stream.ToArray()), "Invalid chunk wrote a partial group code or payload.");
        Equal((short)1, (short)writer.GetType().GetProperty("Code")!.GetValue(writer)!, "Invalid chunk changed current code");
        Equal("prior value", (string)writer.GetType().GetProperty("Value")!.GetValue(writer)!, "Invalid chunk changed current value");
        Invoke(writer, "Write", code, new byte[] { 0, 255 });
        Invoke(writer, "Write", (short)0, "EOF"); Invoke(writer, "Flush");
        stream.Position = 3; object reader = NewCodeReader(stream, true);
        Invoke(reader, "Next"); Equal("prior value", (string)Invoke(reader, "ReadString")!, "Prior value lost");
        Invoke(reader, "Next"); Check(new byte[] { 0, 255 }.SequenceEqual((byte[])Invoke(reader, "ReadBytes")!), "Writer could not recover after rejection.");
        Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Recovery boundary");
        Check(stream.CanWrite, "Rejected chunk closed caller stream.");
    }

    private static void BinaryChunkWriterDirect()
    {
        using var stream = new MemoryStream(); object writer = NewCodeWriter(stream, true);
        byte[] before = stream.ToArray();
        Throws<ArgumentNullException>(() => Invoke(writer, "WriteBytes", new object[] { null! }));
        Throws<ArgumentOutOfRangeException>(() => Invoke(writer, "WriteBytes", new byte[256]));
        Check(before.SequenceEqual(stream.ToArray()), "Direct rejected binary data changed the stream.");
        foreach (int length in new[] { 0, 1, 127, 128, 255 })
        {
            stream.SetLength(0); stream.Position = 0;
            byte[] value = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            Invoke(writer, "WriteBytes", value); Invoke(writer, "WriteByte", (byte)42); Invoke(writer, "Flush");
            byte[] bytes = stream.ToArray(); Equal(length + 2, bytes.Length, "Direct chunk size");
            Equal((byte)length, bytes[0], "Direct chunk prefix");
            Check(value.SequenceEqual(bytes.Skip(1).Take(length)), "Direct chunk content");
            Equal((byte)42, bytes[bytes.Length - 1], "Direct chunk terminator");
        }
    }

    private static void BinaryChunkWriterXData(DxfVersion version)
    {
        var document = new DxfDocument(version);
        var point = new netDxf.Entities.Point(Vector3.Zero);
        var data = new XData(new ApplicationRegistry("CHUNK_WRITE"));
        foreach (int size in new[] { 0, 1, 126, 127 })
            data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, Enumerable.Range(0, size).Select(i => (byte)i).ToArray()));
        point.XData.Add(data); document.Entities.Add(point);
        using var output = new MemoryStream(); Check(document.Save(output, true), "Valid binary XData failed to save.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Valid binary XData failed to load.");
        var records = loaded.Entities.Points.Single().XData["CHUNK_WRITE"].XDataRecord;
        Equal(4, records.Count, "XData count");
        for (int i = 0; i < records.Count; i++)
            Check(((byte[])data.XDataRecord[i].Value).SequenceEqual((byte[])records[i].Value), "Valid XData payload changed.");
    }
}
