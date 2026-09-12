using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunThumbnailImageTests()
    {
        Run("thumbnail/defensive-copy-and-clear", ThumbnailCopies);
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version;
                bool b = binary;
                Run($"thumbnail/document/{v}/{(b ? "binary" : "text")}", () => ThumbnailDocument(v, b));
            }
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            string prefix = "thumbnail/codec/" + (b ? "binary" : "text") + "/";
            Run(prefix + "chunk-boundaries-and-count", () => ThumbnailCodecRoundTrip(b));
            Run(prefix + "128-byte-input-chunk", () => ThumbnailValid(b, new[] { Tag(90, 128), Tag(310, PreviewBytes(128)), Tag(0, "ENDSEC") }, PreviewBytes(128)));
            Run(prefix + "zero-length-input-chunk", () => ThumbnailValid(b, new[] { Tag(90, 0), Tag(310, Array.Empty<byte>()), Tag(0, "ENDSEC") }, Array.Empty<byte>()));
            Run(prefix + "count-after-data", () => ThumbnailValid(b, new[] { Tag(310, new byte[] { 1, 2 }), Tag(90, 2), Tag(0, "ENDSEC") }, new byte[] { 1, 2 }));
            Run(prefix + "negative-count", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, -1), Tag(0, "ENDSEC")));
            Run(prefix + "duplicate-count", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, 0), Tag(90, 0), Tag(0, "ENDSEC")));
            Run(prefix + "missing-count", () => ThumbnailInvalid<InvalidDataException>(b, Tag(310, new byte[] { 1 }), Tag(0, "ENDSEC")));
            Run(prefix + "count-too-small", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, 1), Tag(310, new byte[] { 1, 2 }), Tag(0, "ENDSEC")));
            Run(prefix + "count-too-large", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, 3), Tag(310, new byte[] { 1, 2 }), Tag(0, "ENDSEC")));
            Run(prefix + "untrusted-large-count", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, int.MaxValue), Tag(0, "ENDSEC")));
            Run(prefix + "physical-eof", () => ThumbnailInvalid<EndOfStreamException>(b, Tag(90, 0)));
            Run(prefix + "explicit-eof", () => ThumbnailInvalid<EndOfStreamException>(b, Tag(90, 0), Tag(0, "EOF")));
            Run(prefix + "unexpected-record", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, 0), Tag(0, "SECTION")));
            Run(prefix + "unexpected-value-code", () => ThumbnailInvalid<InvalidDataException>(b, Tag(90, 0), Tag(1, "not preview data"), Tag(0, "ENDSEC")));
            Run(prefix + "empty-section", () => ThumbnailValid(b, new[] { Tag(90, 0), Tag(0, "ENDSEC") }, Array.Empty<byte>()));
        }
        Run("thumbnail/text/comment", () => ThumbnailValid(false,
            new[] { Tag(999, "comment"), Tag(90, 2), Tag(310, new byte[] { 7, 9 }), Tag(0, "ENDSEC") }, new byte[] { 7, 9 }));
    }

    private static (short Code, object Value) Tag(short code, object value) => (code, value);

    private static byte[] PreviewBytes(int count) => Enumerable.Range(0, count).Select(i => (byte)(i * 73 + 19)).ToArray();

    private static void ThumbnailCopies()
    {
        var document = new DxfDocument();
        Equal(0, document.ThumbnailImage.Length, "default preview");
        byte[] source = { 1, 2, 3 };
        document.ThumbnailImage = source;
        source[0] = 99;
        byte[] copy = document.ThumbnailImage;
        Equal((byte)1, copy[0], "setter must copy");
        copy[1] = 88;
        Equal((byte)2, document.ThumbnailImage[1], "getter must copy");
        Throws<ArgumentNullException>(() => document.ThumbnailImage = null!);
        document.ThumbnailImage = Array.Empty<byte>();
        Equal(0, document.ThumbnailImage.Length, "clear preview");
    }

    private static void ThumbnailDocument(DxfVersion version, bool binary)
    {
        foreach (int length in new[] { 0, 1, 126, 127, 128, 254, 255, 256, 257, 4097 })
        {
            byte[] expected = PreviewBytes(length);
            var document = new DxfDocument(version) { ThumbnailImage = expected };
            document.Entities.Add(new Line(Vector3.Zero, new Vector3(1, 2, 3)));
            using var stream = new MemoryStream();
            Check(document.Save(stream, binary), "Preview fixture failed to save.");
            Check(stream.CanWrite, "Preview writer closed the caller's stream.");
            stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Preview fixture failed to load.");
            Check(stream.CanRead, "Preview reader closed the caller's stream.");
            Equal(version, loaded.DrawingVariables.AcadVer, "preview document version");
            Check(expected.SequenceEqual(loaded.ThumbnailImage), "Preview bytes changed in document round trip.");
            Equal(1, loaded.Entities.Lines.Count(), "following document content");
        }
    }

    private static object? InvokeThumbnail(string name, params object[] arguments)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.DxfThumbnailImage", true)!;
        try { return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public)!.Invoke(null, arguments); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    private static object NewCodeWriter(Stream stream, bool binary)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO." + (binary ? "BinaryCodeValueWriter" : "TextCodeValueWriter"), true)!;
        object writer = binary
            ? new BinaryWriter(stream, Encoding.UTF8, true)
            : new StreamWriter(stream, new UTF8Encoding(false), 1024, true);
        return Activator.CreateInstance(type, writer)!;
    }

    private static object NewCodeReader(Stream stream, bool binary)
    {
        if (binary) return CreateBinaryReader(new BinaryReader(stream, Encoding.UTF8, true));
        return CreateTextHexReader(new StreamReader(stream, Encoding.UTF8, true, 1024, true));
    }

    private static short TagCode(object reader) => (short)reader.GetType().GetProperty("Code")!.GetValue(reader)!;

    private static void ThumbnailCodecRoundTrip(bool binary)
    {
        foreach (int length in new[] { 1, 126, 127, 128, 254, 255, 256, 257, 4097 })
        {
            byte[] expected = PreviewBytes(length);
            using var stream = new MemoryStream();
            object writer = NewCodeWriter(stream, binary);
            InvokeThumbnail("Write", writer, expected);
            Invoke(writer, "Write", (short)0, "EOF");
            Invoke(writer, "Flush");
            stream.Position = 0;
            object reader = NewCodeReader(stream, binary);
            Invoke(reader, "Next");
            Equal("SECTION", (string)Invoke(reader, "ReadString")!, "preview section start");
            Invoke(reader, "Next");
            Equal("THUMBNAILIMAGE", (string)Invoke(reader, "ReadString")!, "preview section name");
            Invoke(reader, "Next");
            Equal((short)90, TagCode(reader), "preview length group code");
            Equal(length, (int)Invoke(reader, "ReadInt")!, "preview declared bytes");
            var actual = new List<byte>();
            int chunks = 0;
            Invoke(reader, "Next");
            while (TagCode(reader) == 310)
            {
                byte[] part = (byte[])Invoke(reader, "ReadBytes")!;
                Check(part.Length is > 0 and <= 127, "Nonconforming preview chunk size.");
                actual.AddRange(part);
                chunks++;
                Invoke(reader, "Next");
            }
            Equal((length + 126) / 127, chunks, "preview chunk count");
            Equal("ENDSEC", (string)Invoke(reader, "ReadString")!, "preview section end");
            Check(expected.SequenceEqual(actual), "Preview chunk data changed.");
            Invoke(reader, "Next");
            Equal("EOF", (string)Invoke(reader, "ReadString")!, "preview writer boundary");
        }
    }

    private static byte[] ReadThumbnailFixture(bool binary, params (short Code, object Value)[] tags)
    {
        using var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        Invoke(writer, "Write", (short)2, "THUMBNAILIMAGE");
        foreach (var tag in tags) Invoke(writer, "Write", tag.Code, tag.Value);
        Invoke(writer, "Flush");
        stream.Position = 0;
        object reader = NewCodeReader(stream, binary);
        Invoke(reader, "Next");
        return (byte[])InvokeThumbnail("Read", reader)!;
    }

    private static void ThumbnailValid(bool binary, (short Code, object Value)[] tags, byte[] expected)
        => Check(expected.SequenceEqual(ReadThumbnailFixture(binary, tags)), "Preview parser changed valid data.");

    private static void ThumbnailInvalid<T>(bool binary, params (short Code, object Value)[] tags) where T : Exception
        => Throws<T>(() => ReadThumbnailFixture(binary, tags));
}
