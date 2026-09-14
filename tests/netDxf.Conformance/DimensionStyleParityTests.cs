using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunDimensionStyleParityTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                Run($"dimstyle-parity/authored/{version}/{binary}", () => DimensionStyleParityAuthored(version, binary));
                Run($"dimstyle-parity/independent/{version}/{binary}", () => DimensionStyleParityIndependent(version, binary));
                Run($"dimstyle-parity/header-presence/{version}/{binary}", () => DimensionStyleParityHeaderPresence(version, binary));
            }
        Run("dimstyle-parity/clone-and-validation", DimensionStyleParityValidation);
        Run("dimstyle-parity/header-save-atomicity", DimensionStyleParityHeaderValidation);
        Run("dimstyle-parity/malformed-wire-values", DimensionStyleParityMalformed);
    }

    private static DimensionStyle ParityStyle() => new("PARITY") { TickSize = 1.375, TextVerticalPosition = -0.625, UserPositionedText = true, DimRoundoff = 0.125 };
    private static void ParityOverrides(Dimension dimension, bool first)
    {
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TickSize, first ? 2.75 : 0.0));
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TextVerticalPosition, first ? -1.125 : 0.0));
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.UserPositionedText, !first));
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.DimRoundoff, first ? 0.25 : 0.5));
        dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.AltUnitsEnabled, first));
    }
    private static void CheckParity(DxfDocument doc)
    {
        var style = doc.DimensionStyles["PARITY"];
        Equal(1.375, style.TickSize, "DIMSTYLE DIMTSZ"); Equal(-0.625, style.TextVerticalPosition, "DIMSTYLE DIMTVP"); Check(style.UserPositionedText, "DIMSTYLE DIMUPT"); Equal(0.125, style.DimRoundoff, "DIMSTYLE DIMRND");
        Dimension[] dimensions = doc.Entities.Dimensions.ToArray(); Equal(2, dimensions.Length, "dimension count");
        for (int index = 0; index < dimensions.Length; index++)
        {
            bool first = index == 0; var values = dimensions[index].StyleOverrides;
            Equal(first ? 2.75 : 0.0, (double)values[DimensionStyleOverrideType.TickSize].Value, "override DIMTSZ");
            Equal(first ? -1.125 : 0.0, (double)values[DimensionStyleOverrideType.TextVerticalPosition].Value, "override DIMTVP");
            Equal(!first, (bool)values[DimensionStyleOverrideType.UserPositionedText].Value, "override DIMUPT");
            Equal(first ? 0.25 : 0.5, (double)values[DimensionStyleOverrideType.DimRoundoff].Value, "override DIMRND real value");
            Equal(first, (bool)values[DimensionStyleOverrideType.AltUnitsEnabled].Value, "override DIMALT polarity");
        }
    }
    private static DxfDocument ParitySave(DxfDocument doc, bool binary, string? artifact = null)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "parity save");
        if (artifact != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, artifact), stream.ToArray());
        stream.Position = 0; var raw = DxfRawDocument.Load(stream);
        foreach (string name in new[] { "$DIMTSZ", "$DIMTVP", "$DIMUPT" })
            Equal(1, raw.Tags.Count(t => t.Code == 9 && Equals(t.Value, name)), "one header emission " + name);
        stream.Position = 0; return DxfDocument.Load(stream) ?? throw new Exception("parity load");
    }
    private static void DimensionStyleParityAuthored(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; var style = ParityStyle(); doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
        for (int i = 0; i < 2; i++)
        {
            var original = new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3 + 4 * i, 0, style); ParityOverrides(original, i == 0);
            // An entity clone must retain all values and clone its style.
            var clone = (LinearDimension)original.Clone(); Check(!ReferenceEquals(original.Style, clone.Style), "cloned style independence");
            doc.Entities.Add(clone);
        }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            CheckParity(doc);
            doc = ParitySave(doc, binary, cycle == 0 ? $"dimstyle-parity-{version}-{(binary ? "binary" : "ascii")}.dxf" : null);
            Check(doc.DrawingVariables.TryGetCustomVariable("$DIMTSZ", out var header), "derived header retained"); Equal(1.375, (double)header.Value, "derived header tick");
        }
        CheckParity(doc);
    }
    private static void DimensionStyleParityIndependent(DxfVersion version, bool binary)
    {
        string year = version.ToString().Substring(7), directory = Path.Combine("tests", "fixtures", "dimstyle-stored-settings");
        string input = Path.Combine(directory, $"independent-dimstyle-R{year}.dxf");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var source = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("file").GetString() == Path.GetFileName(input));
        Equal(source.GetProperty("sha256").GetString()!, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant(), "fixture provenance");
        var doc = DxfDocument.Load(input) ?? throw new Exception("independent dimension input");
        string[] identities = doc.Entities.Dimensions.Select(d => d.Handle).ToArray();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            CheckParity(doc); Equal(identities[0], doc.Entities.Dimensions.First().Handle, "source identity");
            Check(doc.DrawingVariables.TryGetCustomVariable("$DIMTSZ", out var tick), "explicit zero header retained"); Equal(0.0, (double)tick.Value, "header independent of table");
            Check(doc.DrawingVariables.TryGetCustomVariable("$DIMTVP", out var vertical), "vertical header retained"); Equal(0.875, (double)vertical.Value, "different header value");
            Check(doc.DrawingVariables.TryGetCustomVariable("$DIMUPT", out var user), "boolean header retained"); Equal((short)0, (short)user.Value, "explicit false header");
            doc = ParitySave(doc, binary, cycle == 2 ? $"independent-dimstyle-R{year}-{(binary ? "binary" : "ascii")}.dxf" : null);
        }
        CheckParity(doc);
    }
    private static void DimensionStyleParityHeaderPresence(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var style = ParityStyle(); doc.DimensionStyles.Add(style); doc.DrawingVariables.DimStyle = style.Name;
        Check(!doc.DrawingVariables.ContainsCustomVariable("$DIMTSZ"), "new header is absent");
        using (var input = new MemoryStream())
        {
            Check(doc.Save(input, binary), "header presence source"); input.Position = 0; var raw = DxfRawDocument.Load(input);
            var tags = new List<DxfTag>();
            for (int i = 0; i < raw.Tags.Count; i++)
            {
                if (raw.Tags[i].Code == 9 && new[] { "$DIMTSZ", "$DIMTVP", "$DIMUPT" }.Contains((string)raw.Tags[i].Value)) { i++; continue; }
                tags.Add(raw.Tags[i]);
            }
            input.SetLength(0); raw.WithTags(tags).Save(input, binary); input.Position = 0;
            doc = DxfDocument.Load(input) ?? throw new Exception("absent header input");
            Check(!doc.DrawingVariables.ContainsCustomVariable("$DIMTSZ") && !doc.DrawingVariables.ContainsCustomVariable("$DIMTVP") && !doc.DrawingVariables.ContainsCustomVariable("$DIMUPT"), "absent input values remain absent");
        }
        doc = ParitySave(doc, binary); Equal(1.375, doc.DimensionStyles["PARITY"].TickSize, "table value");
        foreach (string name in new[] { "$DIMTSZ", "$DIMTVP", "$DIMUPT" }) doc.DrawingVariables.RemoveCustomVariable(name);
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$dimtsz", 40, 0.0));
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$dimtvp", 40, 0.0));
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$dimupt", 70, (short)0));
        doc = ParitySave(doc, binary); doc = ParitySave(doc, binary);
        Check(doc.DrawingVariables.TryGetCustomVariable("$DIMTSZ", out var explicitZero), "explicit zero presence"); Equal(0.0, (double)explicitZero.Value, "zero is not absence");
        doc.DrawingVariables.RemoveCustomVariable("$DIMTSZ"); doc.DimensionStyles["PARITY"].TickSize = 4.5;
        doc = ParitySave(doc, binary); Check(doc.DrawingVariables.TryGetCustomVariable("$DIMTSZ", out var fallback), "restored fallback"); Equal(4.5, (double)fallback.Value, "absence derives current active style");
    }
    private static void DimensionStyleParityValidation()
    {
        var style = new DimensionStyle("Clone") { ExtLineFixed = true, ExtLineFixedLength = 4.75, TextInsideAlign = true, TextOutsideAlign = true, TextDirection = DimensionStyleTextDirection.RightToLeft, TickSize = 2.5, TextVerticalPosition = -3.25, UserPositionedText = true };
        var clone = (DimensionStyle)style.Clone("Clone2");
        Check(clone.ExtLineFixed && clone.TextInsideAlign && clone.TextOutsideAlign, "stored clone boolean settings"); Equal(4.75, clone.ExtLineFixedLength, "clone fixed extension length"); Equal(style.TextDirection, clone.TextDirection, "clone text direction"); Equal(2.5, clone.TickSize, "clone ticks"); Equal(-3.25, clone.TextVerticalPosition, "clone vertical offset"); Check(clone.UserPositionedText, "clone user text");
        clone.TickSize = 9; Equal(2.5, style.TickSize, "clone editing independence");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => style.TickSize = invalid); Throws<ArgumentOutOfRangeException>(() => style.TextVerticalPosition = invalid);
            Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.TickSize, invalid)); Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.TextVerticalPosition, invalid));
        }
        Throws<ArgumentOutOfRangeException>(() => style.TickSize = -1); Throws<ArgumentOutOfRangeException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.TickSize, -1.0));
        Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.TickSize, 1)); Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.TextVerticalPosition, "1")); Throws<ArgumentException>(() => new DimensionStyleOverride(DimensionStyleOverrideType.UserPositionedText, (short)1));
        var defaults = DimensionStyle.Default; Equal(0.0, defaults.TickSize, "default ticks"); Equal(0.0, defaults.TextVerticalPosition, "default vertical"); Check(!defaults.UserPositionedText, "default user text");
    }
    private static void DimensionStyleParityHeaderValidation()
    {
        foreach (var variable in new[] { new HeaderVariable("$DIMTSZ", 70, (short)0), new HeaderVariable("$DIMTSZ", 40, -1.0), new HeaderVariable("$DIMTVP", 40, double.NaN), new HeaderVariable("$DIMUPT", 70, (short)2), new HeaderVariable("$DIMUPT", 70, true) })
        {
            foreach (bool binary in new[] { false, true })
            {
                var doc = new DxfDocument(); doc.DrawingVariables.AddCustomVariable(variable); string seed = doc.DrawingVariables.HandleSeed; int layouts = doc.Layouts.Count;
                using var output = new MemoryStream(); bool rejected;
                try { rejected = !doc.Save(output, binary); } catch (ArgumentException) { rejected = true; } catch (FormatException) { rejected = true; }
                Check(rejected, "invalid custom stored header rejected"); Equal(0L, output.Length, "preflight output untouched"); Equal(seed, doc.DrawingVariables.HandleSeed, "preflight seed untouched"); Equal(layouts, doc.Layouts.Count, "preflight layouts untouched");
                doc.DrawingVariables.RemoveCustomVariable(variable.Name); Check(doc.Save(output, binary), "corrected header retry");
            }
        }
    }
    private static void DimensionStyleParityMalformed()
    {
        using var input = File.OpenRead(Path.Combine("tests", "fixtures", "dimstyle-stored-settings", "independent-dimstyle-R2018.dxf"));
        var raw = DxfRawDocument.Load(input);
        foreach (short code in new short[] { 142, 145, 288 })
        {
            var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DIMSTYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "PARITY")));
            var tags = record.Tags.Select(t => t.Code == code ? new DxfTag(code, code == 288 ? (object)(short)2 : code == 142 ? -1.0 : -12345.625) : t).ToArray();
            var changed = raw.WithRecord(record, tags);
            foreach (bool binary in new[] { false, true })
            {
                using var output = new MemoryStream(); changed.Save(output, binary);
                if (code == 145)
                {
                    byte[] bytes = output.ToArray();
                    if (binary)
                    {
                        byte[] needle = BitConverter.GetBytes(-12345.625), replacement = BitConverter.GetBytes(double.NaN); int matches = 0;
                        for (int i = 0; i <= bytes.Length - needle.Length; i++)
                            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) { replacement.CopyTo(bytes, i); matches++; }
                        Equal(1, matches, "one scalar mutation");
                    }
                    else bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(bytes).Replace("-12345.625", "NaN"));
                    output.SetLength(0); output.Write(bytes);
                }
                output.Position = 0; bool rejected;
                try { rejected = DxfDocument.Load(output) == null; } catch (ArgumentException) { rejected = true; } catch (FormatException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "malformed DIMSTYLE scalar rejected " + code);
            }
        }
    }
}
