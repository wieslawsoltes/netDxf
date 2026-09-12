using System.Text;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBinaryChunkTests()
    {
        foreach (int value in Enumerable.Range(310, 10).Append(1004))
        {
            short code = (short)value;
            foreach (int size in new[] { 0, 1, 2, 127, 128, 254, 255 })
            {
                int length = size;
                Run($"binary/chunk/{code}/complete-{length}", () => CheckCompleteChunk(code, length, false));
            }
            Run($"binary/chunk/{code}/all-truncated-prefixes", () =>
            {
                for (int actual = 0; actual < 255; actual++)
                {
                    byte[] data = CreateChunkBytes(code, 255, actual, false);
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                    object decoder = CreateBinaryReader(reader);
                    Throws<EndOfStreamException>(() => Invoke(decoder, "Next"));
                }
            });
            Run($"binary/chunk/{code}/missing-count", () =>
            {
                using var stream = new MemoryStream();
                stream.Write(BinarySentinel);
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) writer.Write(code);
                stream.Position = 0;
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                object decoder = CreateBinaryReader(reader);
                Throws<EndOfStreamException>(() => Invoke(decoder, "Next"));
            });
            Run($"binary/chunk/{code}/fragmented", () => CheckCompleteChunk(code, 255, true));
        }
    }

    private static void CheckCompleteChunk(short code, int length, bool fragmented)
    {
        byte[] bytes = CreateChunkBytes(code, (byte)length, length, true);
        using MemoryStream stream = fragmented ? new FragmentedReadStream(bytes) : new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        object decoder = CreateBinaryReader(reader);
        Invoke(decoder, "Next");
        byte[] payload = (byte[])Invoke(decoder, "ReadBytes")!;
        Equal(length, payload.Length, "chunk length");
        for (int i = 0; i < length; i++) Equal((byte)i, payload[i], "chunk byte");
        Invoke(decoder, "Next");
        Equal("EOF", (string)Invoke(decoder, "ReadString")!, "following record alignment");
        Equal(stream.Length, stream.Position, "total consumption");
    }

    private static byte[] CreateChunkBytes(short code, byte declaredLength, int actualLength, bool addEof)
    {
        using var stream = new MemoryStream();
        stream.Write(BinarySentinel);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(code);
            writer.Write(declaredLength);
            for (int i = 0; i < actualLength; i++) writer.Write((byte)i);
            if (addEof)
            {
                writer.Write((short)0);
                writer.Write(Encoding.UTF8.GetBytes("EOF\0"));
            }
        }
        return stream.ToArray();
    }
}
