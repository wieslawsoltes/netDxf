using System.Text;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBinarySentinelTests()
    {
        for (int length = 0; length < BinarySentinel.Length; length++)
        {
            int captured = length;
            Run($"binary/sentinel/truncated-{length}", () =>
            {
                using var stream = new MemoryStream(BinarySentinel[..captured]);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                Throws<EndOfStreamException>(() => CreateBinaryReader(reader));
            });
        }
        for (int index = 0; index < BinarySentinel.Length; index++)
        {
            int captured = index;
            Run($"binary/sentinel/corrupt-byte-{index}", () =>
            {
                byte[] bytes = (byte[])BinarySentinel.Clone();
                bytes[captured] ^= 0x01;
                using var stream = new MemoryStream(bytes);
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                Throws<InvalidDataException>(() => CreateBinaryReader(reader));
            });
        }
        Run("binary/sentinel/fragmented-stream", () =>
        {
            using var stream = new FragmentedReadStream(BinarySentinel);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            CreateBinaryReader(reader);
            Equal((long)BinarySentinel.Length, stream.Position, "sentinel consumption");
        });
        Run("binary/sentinel/null-reader", () =>
            Throws<ArgumentNullException>(() => CreateBinaryReader(null!)));
        Run("binary/sentinel/no-overread", () =>
        {
            byte[] bytes = BinarySentinel.Concat(new byte[] { 0x55, 0xAA }).ToArray();
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            CreateBinaryReader(reader);
            Equal((byte)0x55, reader.ReadByte(), "next byte after sentinel");
        });
    }

    private sealed class FragmentedReadStream : MemoryStream
    {
        internal FragmentedReadStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count) =>
            base.Read(buffer, offset, Math.Min(count, 1));
        public override int Read(Span<byte> buffer) =>
            base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }
}
