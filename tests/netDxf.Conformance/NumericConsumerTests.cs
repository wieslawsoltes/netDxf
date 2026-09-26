// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterNumericConsumerTests()
    {
        Run("numeric-consumers/rounding-corpus", () =>
        {
            var observations = new List<object>();
            foreach (var item in PortableDoubleCases.NumericConsumerCases())
            {
                ulong[] values = PortableDoubleCases.VerifyNumericConsumer(item);
                observations.Add(new { id = item.Id, token = item.Token,
                    scalarBits = PortableDoubleCases.Hex(values[0]), tableBits = PortableDoubleCases.Hex(values[1]),
                    factorBits = PortableDoubleCases.Hex(values[2]) });
            }
            Equal(12375, observations.Count, "Numeric consumer input inventory");
            File.WriteAllText(Path.Combine(ArtifactDirectory, "numeric-consumers.json"),
                JsonSerializer.Serialize(new { schema = 1, observations }));
        });
        Run("numeric-consumers/grammar-and-bounds", NumericConsumerGrammar);
        var selected = PortableDoubleCases.NumericConsumerCases().Where(item =>
            item.Id == "exponent/0/1/0" || item.Id == "tail/7/1/0" || item.Id == "tail/4/-1/0").ToArray();
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        for (int i = 0; i < selected.Length; i++)
        {
            int index = i;
            Run($"numeric-consumers/field/{version}/{binary}/{i}", () =>
                NumericConsumerField(version, binary, index, selected[index]));
        }
    }

    private static void NumericConsumerGrammar()
    {
        foreach (string token in new[] { "1e309", "1e9999", "1e", "1e+", ".", "1.2.3", "1,5", "1\0", "0x1", "1e2x", "NaN", "Infinity" })
        {
            bool rejected = false;
            try { DxfTableFormula.ParseScalar("=" + token).EvaluateScalar(); }
            catch (Exception error) when (error is FormatException || error is NotSupportedException || error is ArithmeticException) { rejected = true; }
            Check(rejected, "Malformed formula literal accepted: " + token);
            Check(!DxfValueFormat.TryParse("%lu2%pr8%ct8[" + token + "]", out _, out _), "Malformed conversion factor accepted");
        }
        Throws<FormatException>(() => DxfTableFormula.ParseScalar("=" + new string('0', 4096)));
        Check(!DxfValueFormat.TryParse("%lu2%pr8%ct8[" + new string('0', 4096) + "]", out _, out _), "Format length budget weakened");
        Equal(0L, BitConverter.DoubleToInt64Bits(DxfTableFormula.ParseScalar("=-0").EvaluateScalar()), "Formula zero canonicalization changed");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            Equal(1.5, DxfTableFormula.ParseScalar("=1.5").EvaluateScalar(), "Formula used current culture");
            Equal("1.50000000", DxfValueFormat.Parse("%lu2%pr8%ct8[1.5]").Format(1.0), "Factor used current culture");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static void NumericConsumerField(DxfVersion version, bool binary, int index, PortableDoubleCases.Case item)
    {
        string code = "\\AcExpr (" + item.Token + ") \\f \"%lu2%pr8\"";
        var doc = FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int at = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbField"));
            tags[at + 1] = new DxfTag(1, "AcExpr");
            tags.RemoveAt(at + 2);
            while (tags[at + 2].Code == 3) tags.RemoveAt(at + 2);
            var chunks = new List<DxfTag>();
            for (int offset = 0; offset < code.Length; offset += 250)
                chunks.Add(new DxfTag((short)(offset == 0 ? 2 : 3), code.Substring(offset, Math.Min(250, code.Length - offset))));
            tags.InsertRange(at + 2, chunks); return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children.Single();
        string rootHandle = root.Handle, childHandle = child.Handle;
        var provider = new DxfStandardFieldEvaluator();
        string prefix = $"numeric-field-{version}-{binary}-{index}";
        SaveFieldResults(doc, binary, prefix + "-source.dxf");
        long seed = StoredFieldSeed(doc);
        Equal(2, doc.Objects.EvaluateFieldTree(root, provider.EvaluateOrThrow), "FIELD and parent cache update count");
        Equal(seed, StoredFieldSeed(doc), "FIELD evaluation allocated handles");
        double expected = BitConverter.Int64BitsToDouble(unchecked((long)item.Bits!.Value));
        string display = DxfValueFormat.Parse("%lu2%pr8").Format(expected);
        for (int stage = 0; stage < 2; stage++)
        {
            Check(child.FieldCode == code && child.Evaluation.StoredStatus == 2, "FIELD code/status changed");
            Equal(item.Bits.Value, unchecked((ulong)BitConverter.DoubleToInt64Bits((double)child.Evaluation.Value)), "FIELD cache rounded incorrectly");
            Equal(display, child.Evaluation.FormattedText, "FIELD formatted cache differs");
            Equal(display, root.Evaluation.FormattedText, "Parent FIELD text not synchronized");
            seed = StoredFieldSeed(doc);
            var old = child.Payload;
            Equal(0, doc.Objects.EvaluateFieldTree(root, provider.EvaluateOrThrow), "Repeated FIELD evaluation changed state");
            Check(ReferenceEquals(old, child.Payload), "No-op FIELD evaluation replaced payload");
            Equal(seed, StoredFieldSeed(doc), "No-op FIELD evaluation allocated handles");
            bool format = stage == 0 ? binary : !binary;
            SaveFieldResults(doc, format, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf"));
            using var stream = new MemoryStream(); Check(doc.Save(stream, format), "Numeric FIELD save"); stream.Position = 0;
            doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Numeric FIELD reload failed");
            root = (DxfStoredField)doc.GetObjectByHandle(rootHandle); child = (DxfStoredField)doc.GetObjectByHandle(childHandle);
            Check(ReferenceEquals(child.Owner, root) && doc.Objects.Validate().Count == 0, "Numeric FIELD ownership changed");
        }
        Equal(item.Bits.Value, unchecked((ulong)BitConverter.DoubleToInt64Bits((double)child.Evaluation.Value)), "Second resave lost scalar bits");
    }
}
