using System.Globalization;
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
    private static readonly DateTime StandardClock = new(2026, 9, 17, 13, 5, 9, DateTimeKind.Unspecified);
    private static void RegisterStandardFieldEvaluatorTests()
    {
        Run("standard-field/date-tokens", StandardDateTokens);
        Run("standard-field/angular-tokens", StandardAngularTokens);
        Run("standard-field/scalar-formulas", StandardScalarFormulas);
        Run("standard-field/variable-snapshots", StandardVariableSnapshots);
        Run("standard-field/formats-output", StandardFormatOutput);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (string mode in new[] { "variable", "date", "angle", "expression", "child", "unknown" })
            Run($"standard-field/roundtrip/{version}/{binary}/{mode}", () => StandardFieldRoundTrip(version, binary, mode));
        foreach (bool binary in new[] { false, true })
        foreach (string fault in new[] { "missing-variable", "unknown-function", "cell-reference", "date-mask", "unknown-format", "divide-zero", "child-text", "bad-index", "field-id", "trailing", "quote", "unclosed", "overflow", "child-marker" })
            Run($"standard-field/reject/{binary}/{fault}", () => StandardFieldRejected(binary, fault));
    }
    private static DxfStandardFieldEvaluator StandardProvider() => new(new Dictionary<string, DxfFieldVariable>
    {
        ["Amount"] = new(21.0), ["Angle"] = new(Math.PI / 2, 2),
        ["Date"] = DxfFieldVariable.FromDateTime(StandardClock), ["Text"] = new("=1+99")
    });
    private static DxfDocument StandardFieldDocument(DxfVersion version, bool binary, string childId, string childCode,
        string? rootCode = null, string? rootId = null)
    {
        return FieldResultDocument(version, binary, raw =>
        {
            foreach (string handle in new[] { "14F", "14E" })
            {
                if (handle == "14E" && rootCode == null) continue;
                var record = StoredFieldRecord(raw, handle); var tags = record.Tags.ToList();
                int at = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbField"));
                tags[at + 1] = new DxfTag(1, handle == "14F" ? childId : rootId ?? "AcExpr");
                tags[at + 2] = new DxfTag(2, handle == "14F" ? childCode : rootCode!);
                while (at + 3 < tags.Count && tags[at + 3].Code == 3) tags.RemoveAt(at + 3);
                raw = raw.WithRecord(record, tags); // Reacquire each record in the next immutable snapshot.
            }
            return raw;
        });
    }
    private static void StandardFieldRoundTrip(DxfVersion version, bool binary, string mode)
    {
        string id = mode == "expression" ? "AcExpr" : mode == "unknown" ? "PrivateEvaluator" : "AcVar";
        string code = mode switch
        {
            "date" => "\\AcVar Date \\f \"yyyy-MM-dd HH:mm:ss\"",
            "angle" => "\\AcVar Angle \\f \"%au0%pr2\"",
            "expression" => "\\AcExpr (2^3 + 6/4) \\f \"%lu2%pr3\"",
            "unknown" => "private code stays inert", _ => "\\AcVar Amount \\f \"%lu2%pr2\""
        };
        string? rootCode = mode == "child" ? "\\AcExpr (%<\\_FldIdx 0>% * 2) \\f \"%lu2%pr2\"" : null;
        var doc = StandardFieldDocument(version, binary, id, code, rootCode); var root = ResultRoot(doc); var child = root.Children.Single();
        var before = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var refs = before.Keys.ToDictionary(f => f, f => f.References.ToArray()); var provider = StandardProvider();
        string suffix = $"{version}-{binary}-{mode}.dxf";
        SaveFieldResults(doc, binary, "standard-fields-before-" + suffix); long seed = StoredFieldSeed(doc);
        Equal(2, doc.Objects.EvaluateFieldTree(root, provider.Evaluate), "two evaluated outcomes");
        Equal(seed, StoredFieldSeed(doc), "standard evaluator allocated handles");
        if (mode == "unknown")
        {
            Equal(4, child.Evaluation.StoredStatus, "unknown evaluator status");
            Equal(64, root.Evaluation.StoredStatus, "failed child propagated to text root");
            Check(child.Evaluation.Value == null && root.Evaluation.Value == null, "failure invented a cache");
        }
        else
        {
            string display = mode switch { "date" => "2026-09-17 13:05:09", "angle" => "90.00", "expression" => "9.500", "child" => "42.00", _ => "21.00" };
            Equal(display, root.Evaluation.FormattedText, "evaluated parent display");
            Equal(2, root.Evaluation.StoredStatus, "successful outcome");
            if (mode == "date") Equal(DrawingTime.ToJulianCalendar(StandardClock), child.Evaluation.Value, "date serial without timezone conversion");
            if (mode == "child") Equal(42.0, root.Evaluation.Value, "numeric child uses scalar not display");
        }
        Check(before.Keys.All(f => refs[f].SequenceEqual(f.References)), "FIELD dependencies changed");
        Check(before.Where(p => p.Key != root && p.Key != child).All(p => ReferenceEquals(p.Value, p.Key.Payload)), "unselected forest changed");
        var oldRoot = root.Payload;
        Equal(0, doc.Objects.EvaluateFieldTree(root, provider.Evaluate), "repeated evaluation is not a no-op");
        Check(ReferenceEquals(oldRoot, root.Payload), "no-op replaced root snapshot");
        SaveFieldResults(doc, binary, "standard-fields-after-" + suffix);
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "opposite transport"); output.Position = 0;
        var loaded = ResultRoot(DxfDocument.Load(output)!);
        Equal(root.Evaluation.Value, loaded.Evaluation.Value, "root scalar reload");
        Equal(child.Evaluation.Value, loaded.Children.Single().Evaluation.Value, "child scalar reload");
        Equal(root.Evaluation.StoredStatus, loaded.Evaluation.StoredStatus, "status reload");
        Equal(root.Evaluation.FormattedText, loaded.Evaluation.FormattedText, "display reload");
    }
    private static void StandardDateTokens()
    {
        Equal("2026-09-17 13:05:09", DxfDateTimeFormat.Parse("yyyy-MM-dd HH:mm:ss").Format(StandardClock), "date/time mask");
        Equal("17 17 Thu Thursday", DxfDateTimeFormat.Parse("d dd ddd dddd").Format(StandardClock), "day tokens");
        Equal("9 09 Sep September", DxfDateTimeFormat.Parse("M MM MMM MMMM").Format(StandardClock), "month tokens");
        Equal("26 26 2026 2026", DxfDateTimeFormat.Parse("y yy yyy yyyy").Format(StandardClock), "year tokens");
        Equal("1 01 13 13 5 05 9 09 P PM", DxfDateTimeFormat.Parse("h hh H HH m mm s ss t tt").Format(StandardClock), "time tokens");
        Equal("12 AM 00", DxfDateTimeFormat.Parse("h tt HH").Format(StandardClock.Date), "midnight");
        Equal("12 PM 12", DxfDateTimeFormat.Parse("h tt HH").Format(StandardClock.Date.AddHours(12)), "noon");
        Equal("0001", DxfDateTimeFormat.Parse("yyy").Format(DateTime.MinValue), "year one");
        Equal("day: 17 don't 'd' Ω", DxfDateTimeFormat.Parse("'day:' d 'don''t' ''\\d'' Ω").Format(StandardClock), "literals");
        var culture = new CultureInfo("en-US"); culture.DateTimeFormat.ShortDatePattern = "yyyy/MM/dd";
        var frozen = DxfDateTimeFormat.Parse("%x", culture); culture.DateTimeFormat.ShortDatePattern = "dd";
        Equal("2026/09/17", frozen.Format(StandardClock), "culture snapshot");
        Equal("2026-09-17", DxfDateTimeFormat.Parse("yyyy-MM-dd").FormatJulian(DrawingTime.ToJulianCalendar(StandardClock)), "DXF calendar adapter");
        Equal(frozen.Format(StandardClock), frozen.Format(DateTime.SpecifyKind(StandardClock, DateTimeKind.Utc)), "Kind caused implicit conversion");
        foreach (string mask in new[] { "ddddd", "MMMMM", "HHH", "%Q", "%", "'open", "bad\0mask", "\ud800", "\\", new string('x', 4097) })
        {
            Exception? error = null; try { DxfDateTimeFormat.Parse(mask); } catch (Exception e) { error = e; }
            Check(error is ArgumentException or FormatException or NotSupportedException, "bad date mask accepted");
        }
        Throws<ArgumentNullException>(() => DxfDateTimeFormat.Parse(null!));
        Throws<ArgumentOutOfRangeException>(() => frozen.FormatJulian(double.NaN));
    }
    private static void StandardAngularTokens()
    {
        Equal("90.00", DxfAngularValueFormat.Parse("%au0%pr2").FormatRadians(Math.PI / 2), "degrees");
        Equal("90°0'0\"", DxfAngularValueFormat.Parse("%au1%pr4").FormatRadians(Math.PI / 2), "DMS");
        Equal("100.00", DxfAngularValueFormat.Parse("%au2%pr2").FormatRadians(Math.PI / 2), "gradians");
        Equal("1.57", DxfAngularValueFormat.Parse("%au3%pr2").FormatRadians(Math.PI / 2), "radians");
        Equal("N 0°0'0\" E", DxfAngularValueFormat.Parse("%au4%pr4").FormatRadians(Math.PI / 2), "bearing");
        Equal("angle=1,5 rad", DxfAngularValueFormat.Parse("%ps[angle=, rad]%au3%pr2%ds44%zs8").FormatRadians(1.5), "prefix and decimal policies");
        Equal("0.12", DxfAngularValueFormat.Parse("%au3%pr2").FormatRadians(0.125), "exact ties-to-even");
        Equal(".5", DxfAngularValueFormat.Parse("%au3%pr2%zs12").FormatRadians(0.5), "suppression");
        foreach (string expression in new[] { "", "%au0", "%pr2", "%au5%pr2", "%au0%pr9", "%au0%pr2%au1", "%lu2%pr2", "%au-1%pr2", "%au0%pr2%ds32", "%au0%pr2%ps[[,]]", "%au1%pr4%zs8", "%au4%pr4%zs12" })
        {
            Exception? error = null; try { DxfAngularValueFormat.Parse(expression); } catch (Exception e) { error = e; }
            Check(error is ArgumentException or FormatException or NotSupportedException, "bad angular mask accepted");
        }
        Throws<ArgumentOutOfRangeException>(() => DxfAngularValueFormat.Parse("%au3%pr2").FormatRadians(double.NaN));
    }
    private static void StandardScalarFormulas()
    {
        Equal(14.0, DxfTableFormula.ParseScalar("=2+3*4").EvaluateScalar(), "scalar precedence");
        Equal(-4.0, DxfTableFormula.ParseScalar("=-2^2").EvaluateScalar(), "unary precedence");
        Equal(6.0, DxfTableFormula.ParseScalar("=SUM(1,2,3)").EvaluateScalar(), "scalar aggregate");
        foreach (string expression in new[] { "=A1", "=SUM(A1:B2)", "=$A$1+1", "=IF(1,2,3)" })
            Throws<NotSupportedException>(() => DxfTableFormula.ParseScalar(expression));
        Throws<InvalidOperationException>(() => DxfTableFormula.Parse("=2").EvaluateScalar());
        Throws<DivideByZeroException>(() => DxfTableFormula.ParseScalar("=1/0").EvaluateScalar());
    }
    private static void StandardVariableSnapshots()
    {
        var pairs = new Dictionary<string, DxfFieldVariable> { ["Amount"] = new(21.0) };
        var evaluator = new DxfStandardFieldEvaluator(pairs); pairs["Amount"] = new(99.0);
        var doc = StandardFieldDocument(DxfVersion.AutoCad2018, false, "AcVar", "\\AcVar Amount \\f \"%lu2%pr2\"");
        doc.Objects.EvaluateFieldTree(ResultRoot(doc), evaluator.EvaluateOrThrow);
        Equal("21.00", ResultRoot(doc).Evaluation.Value, "mutable variable source retained");
        var wrapped = StandardFieldDocument(DxfVersion.AutoCad2018, false, "AcVar", "%<\\AcVar Amount \\f \"%lu2%pr2\">%");
        wrapped.Objects.EvaluateFieldTree(ResultRoot(wrapped), evaluator.EvaluateOrThrow);
        Equal("21.00", ResultRoot(wrapped).Evaluation.Value, "complete FIELD wrapper");
        var negative = StandardFieldDocument(DxfVersion.AutoCad2018, false, "AcVar",
            "\\AcVar Amount \\f \"%lu2%pr2%ps[=99 , units]\"", "\\AcExpr (%<\\_FldIdx 0>% ^ 2) \\f \"%lu2%pr2\"");
        var negativeProvider = new DxfStandardFieldEvaluator(new Dictionary<string, DxfFieldVariable> { ["Amount"] = new(-2.0) });
        negative.Objects.EvaluateFieldTree(ResultRoot(negative), negativeProvider.EvaluateOrThrow);
        Equal(4.0, ResultRoot(negative).Evaluation.Value, "negative child must be parenthesized; display text is not code");
        var literal = StandardFieldDocument(DxfVersion.AutoCad2018, false, "AcVar", "\\AcVar Text \\f \"\"");
        literal.Objects.EvaluateFieldTree(ResultRoot(literal), StandardProvider().EvaluateOrThrow);
        Equal("=1+99", ResultRoot(literal).Evaluation.Value, "explicit text variable is not evaluated as a formula");
        foreach (object v in new object[] { true, DateTime.MinValue, double.NaN, double.PositiveInfinity, new byte[] { 1 } })
            Throws<ArgumentException>(() => new DxfFieldVariable(v));
        Throws<ArgumentException>(() => new DxfFieldVariable("12", 2));
        Throws<ArgumentOutOfRangeException>(() => new DxfFieldVariable(1.0, 64));
        Throws<ArgumentException>(() => new DxfStandardFieldEvaluator(new[] { new KeyValuePair<string, DxfFieldVariable>("Bad.Name", new(1)) }));
        var pair = new KeyValuePair<string, DxfFieldVariable>("Value", new(1));
        Throws<ArgumentException>(() => new DxfStandardFieldEvaluator(new[] { pair, pair }));
        bool disposed = false;
        IEnumerable<KeyValuePair<string, DxfFieldVariable>> Broken()
        { try { yield return pair; } finally { disposed = true; throw new InvalidOperationException("disposal"); } }
        Throws<InvalidOperationException>(() => new DxfStandardFieldEvaluator(Broken()));
        Check(disposed, "variable disposal skipped");
    }
    private static void StandardFieldRejected(bool binary, string fault)
    {
        string id = fault is "missing-variable" or "date-mask" ? "AcVar" : "AcExpr";
        string code = fault switch
        {
            "missing-variable" => "\\AcVar Missing \\f \"%lu2%pr2\"", "date-mask" => "\\AcVar Date \\f \"kkkk\"",
            "unknown-function" => "\\AcExpr SECRET(1)", "cell-reference" => "\\AcExpr A1", "unknown-format" => "\\AcExpr 1 \\f \"%zz9\"",
            "divide-zero" => "\\AcExpr (1/0)", "bad-index" => "\\AcExpr %<\\_FldIdx 999>%",
            "field-id" => "\\AcVar Amount", "trailing" => "\\AcExpr 1 \\f \"%lu2%pr2\" garbage",
            "quote" => "\\AcExpr 1 \\f %lu2%pr2", "unclosed" => "%<\\AcExpr (1+2)",
            "overflow" => "\\AcExpr (1e308*1e308)", "child-marker" => "\\AcExpr %<\\AcVar Amount>%", _ => "\\AcVar Text \\f \"\""
        };
        string? parent = fault == "child-text" ? "\\AcExpr (%<\\_FldIdx 0>% + 1)" : null;
        if (fault == "child-text") id = "AcVar";
        var doc = StandardFieldDocument(DxfVersion.AutoCad2018, binary, id, code, parent);
        var root = ResultRoot(doc); var originals = doc.Objects.Items.OfType<DxfStoredField>().ToDictionary(f => f, f => f.Payload);
        var provider = StandardProvider(); Exception? error = null;
        try { doc.Objects.EvaluateFieldTree(root, provider.EvaluateOrThrow); } catch (Exception e) { error = e; }
        Check(error is ArgumentException or FormatException or NotSupportedException or ArithmeticException or KeyNotFoundException or InvalidOperationException, "unsupported FIELD accepted");
        Check(originals.All(p => ReferenceEquals(p.Value, p.Key.Payload)), "failure partially published");
        doc.Objects.EvaluateFieldTree(root, provider.Evaluate);
        Check(root.Evaluation.StoredStatus != 2, "failure became successful parent");
        Equal(0, doc.Objects.Validate().Count, "failed result broke graph");
        using var persisted = new MemoryStream();
        Check(doc.Save(persisted, binary), "provider error text must remain transport-safe");
        persisted.Position = 0;
        Check(ResultRoot(DxfDocument.Load(persisted)!).Evaluation.StoredStatus != 2, "failed outcome lost on reload");
    }
    private static void StandardFormatOutput()
    {
        var rows = new List<object>();
        DateTime[] dates = { DateTime.MinValue, new(1900,3,1,0,0,0), new(2000,2,29,12,30,59), StandardClock, new(2026,12,31,23,59,59), DateTime.MaxValue };
        string[] masks = { "yyyy-MM-dd HH:mm:ss", "d dd ddd dddd", "M MM MMM MMMM", "y yy yyy yyyy", "h hh H HH m mm s ss t tt", "'clock' HH:mm:ss", "''\\d''", "dd/MM/yyyy" };
        foreach (DateTime date in dates)
        foreach (string mask in masks)
            rows.Add(new { kind = "date", ticks = date.Ticks.ToString(CultureInfo.InvariantCulture), expression = mask, result = DxfDateTimeFormat.Parse(mask).Format(date) });
        foreach (int mode in Enumerable.Range(0,5))
        foreach (int precision in Enumerable.Range(0,9))
        foreach (double radians in new[] { -2*Math.PI, -Math.PI/4, -0.0, 0.125, Math.PI/4, Math.PI/2, Math.PI, 10.0 })
        {
            string expression = $"%au{mode}%pr{precision}";
            rows.Add(new { kind = "angle", bits = BitConverter.DoubleToInt64Bits(radians).ToString("X16",CultureInfo.InvariantCulture), expression, result = DxfAngularValueFormat.Parse(expression).FormatRadians(radians) });
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory, "standard-field-formats.json"), JsonSerializer.Serialize(rows));
    }
}
