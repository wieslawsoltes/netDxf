using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterTextFramingTests()
    {
        Run("text/framing/null-reader", () => Throws<ArgumentNullException>(() => FramingReader(null!)));
        Run("text/framing/empty-input", () => FramingError<EndOfStreamException>("", 1));
        Run("text/framing/invalid-group", () =>
        {
            foreach (string code in new[] { "", "abc", "32768", "-32769", "1.5", "0x10", "١٠" })
                FramingError<FormatException>(code + "\nvalue\n", 1);
        });
        Run("text/framing/empty-string-is-not-truncation", () =>
        {
            using var source = new StringReader("1\n\n0\nEOF");
            object reader = FramingReader(source);
            Invoke(reader, "Next");
            Equal("", (string)Invoke(reader, "ReadString")!, "empty string value");
            Invoke(reader, "Next");
            Equal("EOF", (string)Invoke(reader, "ReadString")!, "explicit end marker");
            Throws<EndOfStreamException>(() => Invoke(reader, "Next"));
        });
        Run("text/framing/group-code-whitespace", () =>
        {
            using var source = new StringReader("  +10  \n1.25\n0\nEOF\n");
            object reader = FramingReader(source);
            Invoke(reader, "Next");
            Equal((short)10, TagCode(reader), "space-padded group code");
            Near(1.25, (double)Invoke(reader, "ReadDouble")!, "space-padded group value");
        });
        Run("text/framing/source-error", () =>
        {
            using var source = new FailingTextSource();
            object reader = FramingReader(source);
            Throws<IOException>(() => Invoke(reader, "Next"));
        });
        foreach (string newline in new[] { "\n", "\r\n", "\r" })
        {
            string nl = newline;
            Run("text/framing/newline-" + Convert.ToHexString(Encoding.ASCII.GetBytes(nl)), () =>
            {
                using var source = new StringReader("999" + nl + "comment" + nl + "0" + nl + "EOF");
                object reader = FramingReader(source);
                Invoke(reader, "Next");
                Equal((long)2, (long)reader.GetType().GetProperty("CurrentPosition")!.GetValue(reader)!, "comment line count");
                Invoke(reader, "Next");
                Equal((short)0, TagCode(reader), "end-marker group code");
                Equal("EOF", (string)Invoke(reader, "ReadString")!, "end marker without final newline");
                Equal((long)4, (long)reader.GetType().GetProperty("CurrentPosition")!.GetValue(reader)!, "EOF line count");
                Throws<EndOfStreamException>(() => Invoke(reader, "Next"));
            });
        }
        foreach (short code in new short[] { 0, 1, 9, 10, 40, 60, 90, 100, 101, 102, 105, 110, 140, 160, 170, 210,
                     270, 280, 290, 300, 310, 320, 330, 370, 380, 390, 400, 410, 420, 430, 440, 450, 460, 470, 480,
                     999, 1000, 1004, 1005, 1010, 1060, 1071 })
        {
            short group = code;
            Run($"text/framing/missing-value/{group}", () =>
            {
                FramingError<EndOfStreamException>(group.ToString(CultureInfo.InvariantCulture) + "\n", 2, group);
                FramingError<EndOfStreamException>("999\ncomment\n" + group.ToString(CultureInfo.InvariantCulture) + "\n", 4, group);
            });
        }
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version;
                bool b = binary;
                Run($"framing/document/no-end-marker/{v}/{b}", () => MissingDocumentEndMarker(v, b));
                Run($"framing/document/unterminated-header/{v}/{b}", () => UnterminatedSection(v, b, "HEADER"));
                Run($"framing/document/unterminated-classes/{v}/{b}", () => UnterminatedSection(v, b, "CLASSES"));
            }
        }
    }

    private static object FramingReader(TextReader source)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.TextCodeValueReader", true)!;
        try { return Activator.CreateInstance(type, source)!; }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void FramingError<T>(string input, long line, short? group = null) where T : Exception
    {
        using var source = new StringReader(input);
        object reader = FramingReader(source);
        if (line == 4) Invoke(reader, "Next");
        try { Invoke(reader, "Next"); }
        catch (T exception)
        {
            Check(exception.Message.Contains("line " + line.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Framing error has no physical line.");
            if (group.HasValue)
                Check(exception.Message.Contains("group code " + group.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Missing-value error has no group code.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}; truncated record was accepted.");
    }

    private static void MissingDocumentEndMarker(DxfVersion version, bool binary)
    {
        using var complete = new MemoryStream();
        var document = new DxfDocument(version);
        Check(document.Save(complete, binary), "Complete document failed to save.");
        byte[] data = complete.ToArray();
        byte[] marker = binary ? new byte[] { 0, 0, (byte)'E', (byte)'O', (byte)'F', 0 }
            : Encoding.ASCII.GetBytes("0" + Environment.NewLine + "EOF" + Environment.NewLine);
        Check(data.AsSpan(data.Length - marker.Length).SequenceEqual(marker), "Unexpected writer end-marker framing.");
        ExpectFiniteLoadFailure(data[..^marker.Length]);
    }

    private static void UnterminatedSection(DxfVersion version, bool binary, string section)
    {
        using var output = new MemoryStream();
        object writer = NewCodeWriter(output, binary);
        void Tag(short code, object value) => Invoke(writer, "Write", code, value);
        string acadver = version switch
        {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018",
            DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024",
            DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        Tag(0, "SECTION"); Tag(2, "HEADER"); Tag(9, "$ACADVER"); Tag(1, acadver);
        Tag(9, "$DWGCODEPAGE"); Tag(3, "ANSI_1252");
        if (section != "HEADER")
        {
            Tag(0, "ENDSEC"); Tag(0, "SECTION"); Tag(2, section);
        }
        // An EOF marker cannot replace the required ENDSEC inside a section.
        Tag(0, "EOF"); Invoke(writer, "Flush");
        ExpectFiniteLoadFailure(output.ToArray());
    }

    private static void ExpectFiniteLoadFailure(byte[] bytes)
    {
        using var input = new EofGuardStream(bytes);
        try
        {
#if DEBUG
            Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
            Check(DxfDocument.Load(input) == null, "A truncated document was accepted.");
#endif
        }
        finally
        {
            Check(!input.GuardTripped, "Reader kept consuming physical EOF instead of reporting truncation.");
            Check(input.CanRead, "Failure closed a caller-owned stream.");
        }
    }

    // Stops a broken baseline loop without relying on timeouts or orphaned tasks.
    private sealed class EofGuardStream : MemoryStream
    {
        private int emptyReads;
        public bool GuardTripped { get; private set; }
        public EofGuardStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Guard(count);
            return base.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            Guard(buffer.Length);
            return base.Read(buffer);
        }
        private void Guard(int count)
        {
            if (count > 0 && Position == Length && ++emptyReads > 8)
            {
                GuardTripped = true;
                throw new IOException("Regression guard stopped repeated EOF reads.");
            }
        }
    }

    private sealed class FailingTextSource : TextReader
    {
        public override string? ReadLine() => throw new IOException("Source failed.");
    }
}
