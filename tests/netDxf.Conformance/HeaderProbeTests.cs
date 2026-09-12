using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHeaderProbeTests()
    {
        for (int length = 0; length < BinarySentinel.Length; length++)
        {
            int count = length;
            Run($"probe/truncated-signature/{count}", () => CheckProbeBytes(BinarySentinel[..count], DxfVersion.Unknown, false));
        }
        for (int index = 0; index < BinarySentinel.Length; index++)
        {
            int offset = index;
            Run($"probe/corrupt-signature/{offset}", () =>
            {
                using var file = CreateMinimalDocument(DxfVersion.AutoCad2018, true, false, false);
                byte[] bytes = file.ToArray(); bytes[offset] ^= 1;
                CheckProbeBytes(bytes, DxfVersion.Unknown, false);
            });
        }
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"probe/valid-position/{v}/{b}", () =>
                {
                    using var file = CreateMinimalDocument(v, b, true, false);
                    CheckProbeBytes(file.ToArray(), v, b, true);
                });
                Run($"probe/IO-failure/{v}/{b}", () =>
                {
                    using var file = CreateMinimalDocument(v, b, true, false);
                    byte[] prefix = new byte[7];
                    using var stream = new ProbeFailureStream(prefix.Concat(file.ToArray()).ToArray());
                    stream.Position = 7;
                    Equal(DxfVersion.Unknown, DxfDocument.CheckDxfFileVersion(stream, out _), "IO failure version");
                    Equal(7L, stream.Position, "IO failure position");
                    Check(stream.CanRead, "Probe disposed a caller-owned stream after IO failure.");
                });
            }
        }
        Run("probe/null-public-contract", () =>
        {
            Equal(DxfVersion.Unknown, DxfDocument.CheckDxfFileVersion((Stream)null!, out bool binary), "null public probe");
            Equal(false, binary, "null public binary flag");
        });
        Run("probe/nonseekable", () =>
        {
            using var stream = new ProbeNonSeekableStream(Encoding.ASCII.GetBytes("0\nEOF\n"));
            Equal(DxfVersion.Unknown, DxfDocument.CheckDxfFileVersion(stream, out _), "nonseekable result");
            Equal(0, stream.ReadCount, "nonseekable probe must not consume bytes");
            Check(stream.CanRead, "Nonseekable stream was disposed.");
        });
        Run("probe/headerless-short-file", () =>
        {
            foreach (string text in new[] { "0\nEOF", "0\nEOF\n", "999\nHEADER\n0\nEOF\n" })
            {
                CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.Unknown, false);
                using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
                Equal("", ProbeHeaderString(stream, "$ACADVER"), "no header variable");
                Equal(0L, stream.Position, "internal short probe position");
            }
        });
        Run("probe/malformed-text", () =>
        {
            foreach (string text in new[]
            {
                "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n",
                "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n70\n1\n0\nENDSEC\n0\nEOF\n",
                "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n3\nAC1032\n0\nENDSEC\n0\nEOF\n",
                "0\nSECTION\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n",
                "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n9\nAC1032\n0\nENDSEC\n0\nEOF\n",
                "0\nSECTION\n2\nCLASSES\n0\nEOF\n"
            }) CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.Unknown, false);
        });
        Run("probe/ignore-comment-control-words", () =>
        {
            foreach (string word in new[] { "EOF", "HEADER", "ENDSEC", "SECTION", "$ACADVER" })
            {
                string text = "999\n" + word + "\n0\nSECTION\n999\n" + word + "\n2\nHEADER\n999\n" + word +
                              "\n9\n$ACADVER\n999\n" + word + "\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
                CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.AutoCad2018, false);
            }
        });
        Run("probe/ignore-nonheader-payload", () =>
        {
            string text = "0\nSECTION\n2\nENTITIES\n0\nTEXT\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
            CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.Unknown, false);
        });
        Run("probe/skip-multitype-section", () =>
        {
            string text = "0\nSECTION\n2\nCLASSES\n0\nCLASS\n1\nHEADER\n90\n0\n280\n0\n281\n0\n0\nENDSEC\n" +
                          "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
            CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.AutoCad2018, false);
        });
        Run("probe/skip-multivalue-header", () =>
        {
            string text = "0\nSECTION\n2\nHEADER\n9\n$INSBASE\n10\n1\n20\n2\n30\n3\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
            CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.AutoCad2018, false);
        });
        Run("probe/codepage-string", () =>
        {
            using var file = CreateMinimalDocument(DxfVersion.AutoCad2000, false, false, false);
            Equal("ANSI_1252", ProbeHeaderString(file, "$DWGCODEPAGE"), "codepage probe");
            Equal(0L, file.Position, "codepage probe position");
            Check(file.CanRead, "Codepage probe disposed its caller.");
        });
        Run("probe/not-full-file-validation", () =>
        {
            string text = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\ngarbage-after-selected-variable\n";
            CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.AutoCad2018, false);
        });
        Run("probe/unknown-format-string", () =>
        {
            string text = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC9999\n0\nENDSEC\n0\nEOF\n";
            CheckProbeBytes(Encoding.ASCII.GetBytes(text), DxfVersion.Unknown, false);
        });
        Run("probe/internal-error-position", () =>
        {
            byte[] input = new byte[7].Concat(Encoding.ASCII.GetBytes("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n")).ToArray();
            using var stream = new MemoryStream(input); stream.Position = 7;
            Throws<EndOfStreamException>(() => ProbeHeaderString(stream, "$ACADVER"));
            Equal(7L, stream.Position, "internal error position");
            Check(stream.CanRead, "Internal probe closed its caller.");
        });
    }

    private static void CheckProbeBytes(byte[] bytes, DxfVersion expected, bool binary, bool load = false)
    {
        foreach (int offset in new[] { 0, 7, 31 })
        {
            foreach (bool fragmented in new[] { false, true })
            {
                byte[] input = Enumerable.Repeat((byte)0xCC, offset).Concat(bytes).ToArray();
                using MemoryStream stream = fragmented ? new FragmentedReadStream(input) : new MemoryStream(input, false);
                stream.Position = offset;
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Equal(expected, DxfDocument.CheckDxfFileVersion(stream, out bool foundBinary), "probe version");
                    Equal(binary, foundBinary, "probe transport");
                    Equal((long)offset, stream.Position, "probe must restore stream position");
                    Check(stream.CanRead, "Probe closed a caller-owned stream.");
                }
                if (load)
                {
                    DxfDocument document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Probe broke the following load.");
                    Equal(expected, document.DrawingVariables.AcadVer, "load after probe version");
                    Equal(1, document.Entities.Lines.Count(), "load after probe geometry");
                }
            }
        }
    }

    private static string ProbeHeaderString(Stream stream, string variable)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.DxfReader", true)!;
        MethodInfo method = type.GetMethod("CheckHeaderVariable", BindingFlags.Public | BindingFlags.Static)!;
        try { return (string)method.Invoke(null, new object[] { stream, variable, false })!; }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private sealed class ProbeFailureStream : MemoryStream
    {
        private int reads;
        public ProbeFailureStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (++reads > 1) throw new IOException("Injected read failure.");
            return base.Read(buffer, offset, count);
        }
        public override int Read(Span<byte> buffer)
        {
            if (++reads > 1) throw new IOException("Injected read failure.");
            return base.Read(buffer);
        }
    }

    private sealed class ProbeNonSeekableStream : MemoryStream
    {
        public int ReadCount { get; private set; }
        public ProbeNonSeekableStream(byte[] bytes) : base(bytes, false) { }
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            return base.Read(buffer, offset, count);
        }
    }
}
