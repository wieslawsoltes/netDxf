using System.Globalization;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterValueFormatTests()
    {
        Run("value-format/examples", ValueFormatExamples);
        Run("value-format/invalid", ValueFormatInvalid);
        Run("value-format/independent-matrix", ValueFormatMatrix);
        foreach (string culture in new[] { "en-US", "pl-PL", "tr-TR", "ar-SA" })
            Run("value-format/culture/" + culture, () =>
            {
                var old = CultureInfo.CurrentCulture;
                try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); ValueFormatExamples(); }
                finally { CultureInfo.CurrentCulture = old; }
            });
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
        foreach (bool binary in new[] { false, true })
        {
            Run($"value-format/content/{version}/{binary}", () => ValueFormatContent(version, binary));
            foreach (string failure in new[] { "unsupported", "foreign", "stale", "duplicate", "dispose", "reentry", "unit", "profile" })
                Run($"value-format/atomic/{version}/{binary}/{failure}", () => ValueFormatAtomic(version, binary, failure));
        }
        foreach (string file in TableContentFiles.Where(f => !f.Contains("AC1018")))
        foreach (bool binary in new[] { false, true })
            Run($"value-format/native/{file}/{binary}", () => ValueFormatNative(file, binary));
        foreach (bool binary in new[] { false, true })
            Run($"value-format/compact/{binary}", () =>
            {
                var content = EditableContentObject(EditableContentLoad(DxfVersion.AutoCad2004, binary));
                var tags = content.Payload;
                Throws<NotSupportedException>(() => content.StoredValues[0].EvaluateFormattedText());
                Throws<NotSupportedException>(() => content.StoredValues[0].WithEvaluatedValue(1));
                Throws<NotSupportedException>(() => content.RefreshFormattedText(content.StoredValues));
                Check(ReferenceEquals(tags, content.Payload), "compact evaluation changed stored data");
            });
    }

    private static void ValueFormatExamples()
    {
        var examples = new (string Format, object Value, string Display)[] {
            ("%lu1%pr4",17.5,"1.7500E+01"), ("%lu2%pr2",17.5,"17.50"),
            ("%lu3%pr2",17.5,"1'-5.50\""), ("%lu4%pr2",17.5,"1'-5 1/2\""),
            ("%lu5%pr2",17.5,"17 1/2"), ("%lu5%pr8",.5,"1/2"),
            ("%lu2%pr2%ps[,%]",35.0,"35.00%"), ("%lu2%pr2%ps[$,]",100.0,"$100.00"),
            ("%lu2%pr2%ct8[1000]%th44",12.25,"12,250.00"),
            ("%lu2%pr2%ds44%th46",12345.5,"12.345,50"),
            ("%lu2%pr4%zs12",.5,".5"), ("%lu2%pr4%zs4",-.5,"-.5000"),
            ("%lu2%pr4%zs8",12.5,"12.5"), ("%lu2%pr2%zs12",0.0,"0"),
            ("%lu2%pr2",-.001,"0.00"), ("%lu2%pr1",-.25,"-0.3"),
            ("%lu2%pr0",2.5,"3"), ("%lu2%pr0",-2.5,"-3"),
            ("%lu3%pr2",11.999,"1'-0.00\""), ("%lu4%pr2",11.99,"1'-0\""),
            ("%lu4%pr2",-.5,"-0'-0 1/2\""), ("%lu5%pr2",.99,"1"),
            ("%lu1%pr2",9.999,"1.00E+01"), ("%lu1%pr2",.00125,"1.25E-03"),
            ("",int.MinValue,"-2147483648"), ("",-0.0,"0"), ("","literal %lu2","literal %lu2"),
            ("%ps[Ω,% done]","text","Ωtext% done"), ("%pr2%lu2%ct8[-2]",2.5,"-5.00")
        };
        foreach (var example in examples)
            Equal(example.Display, DxfValueFormat.Parse(example.Format).Format(example.Value), "formatted example " + example.Format);
        Equal("35.00%", DxfValueFormat.Parse("%lu2%pr2%ps[,%]").Format(35.0, 32), "percentage is already a scalar, not an implicit x100");
    }

    private static void ValueFormatInvalid()
    {
        foreach (string expression in new[] { "%", "%lu", "%lu2%pr", "%lu2%pr2%lu2", "%lu6%pr2", "%lu2%pr9", "%pr2", "%lu2", "%au0%pr2", "%zz1", "%lu-1%pr2", "%lu2%pr2junk", "%lu2%pr2%ps[a,b,c]", "%ps[a,[b]]", "%ps[a,b", "%lu2%pr2%ct8[NaN]", "%lu2%pr2%ct8[Infinity]", "%lu2%pr2%ct8[1e999]", "%lu2%pr2%ct7[1]", "%lu2%pr2%ct8[ 1]", "%lu2%pr2%ds44%th44", "%lu5%pr2%ds44", "%lu2%pr2%zs1", "%lu2%pr999999", "not a format", "\ud800", "\0", new string('%',4097) })
        {
            Check(!DxfValueFormat.TryParse(expression, out var parsed, out var diagnostic) && parsed == null && !string.IsNullOrEmpty(diagnostic), "invalid format accepted: " + expression);
        }
        Check(!DxfValueFormat.TryParse(null!, out _, out _), "null format accepted");
        var format = DxfValueFormat.Parse("%lu2%pr2");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => format.Format(invalid));
        Throws<NotSupportedException>(() => format.Format("3"));
        Throws<NotSupportedException>(() => format.Format(new object()));
        Throws<NotSupportedException>(() => format.Format(Vector3.Zero));
        foreach (int unit in new[] { -1, 2, 3, 64, 65536 }) Throws<NotSupportedException>(() => format.Format(1.0, unit));
        Throws<OverflowException>(() => DxfValueFormat.Parse("%lu2%pr2%ct8[2]").Format(double.MaxValue));
        Throws<ArgumentNullException>(() => format.Format(null!));
        Throws<ArgumentOutOfRangeException>(() => DxfValueFormat.Parse("%ps[abc,def]").Format(new string('x',4096)));
        var parsedFormat = DxfValueFormat.Parse("%lu2%pr2");
        Parallel.For(0, 128, i => Equal(i + ".00", parsedFormat.Format(i), "shared formatter is immutable"));
    }

    private static void ValueFormatMatrix()
    {
        var values = new List<double> { 0, -0.0, .125, -.125, .25, -.25, .5, -.5, 2.675, 9.999, 11.999, 12, 17.5, -17.5, int.MinValue, int.MaxValue, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue };
        var random = new Random(173);
        for (int i = 0; i < 80; i++)
        {
            var bytes = new byte[8]; random.NextBytes(bytes);
            double value = BitConverter.ToDouble(bytes, 0);
            if (double.IsNaN(value) || double.IsInfinity(value)) { i--; continue; }
            values.Add(value);
        }
        var output = new List<object>();
        foreach (int mode in Enumerable.Range(1, 5))
        foreach (int precision in Enumerable.Range(0, 9))
        foreach (double value in values)
        {
            string expression = $"%lu{mode}%pr{precision}";
            string actual = DxfValueFormat.Parse(expression).Format(value);
            output.Add(new { bits = BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture), expression, actual });
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory, "value-format-matrix.json"), JsonSerializer.Serialize(output));
        Equal(4500, output.Count, "fixed matrix inventory");
    }

    private static DxfDocument ValueFormatDocument(DxfVersion version, bool binary) => EditableContentLoad(version, binary, (_, tags) =>
    {
        int index = tags.FindIndex(t => t.Code == 300 && Equals(t.Value, "VALUE"));
        index = tags.FindIndex(index + 1, t => t.Code == 300);
        tags[index] = new netDxf.IO.DxfTag(300, "%lu2%pr2%ps[$,]");
    });

    private static void ValueFormatContent(DxfVersion version, bool binary)
    {
        var doc = ValueFormatDocument(version, binary); var content = EditableContentObject(doc);
        var old = content.Payload; var values = content.StoredValues; var references = content.References.ToArray();
        TableContentSave(doc, binary, $"value-format-before-{version}-{binary}.dxf"); long seed = OwnershipSeed(doc);
        var selected = values.Take(3).ToArray();
        content.RefreshFormattedText(selected);
        Equal("$-2147483648.00", content.StoredValues[0].FormattedText, "integer display refresh");
        Equal("0", content.StoredValues[1].FormattedText, "zero display refresh");
        Equal("text", content.StoredValues[2].FormattedText, "string display refresh");
        Equal("display", values[0].FormattedText, "previous display snapshot changed");
        Check(!ReferenceEquals(old, content.Payload) && references.SequenceEqual(content.References), "refresh changed dependency membership");
        Equal(seed, OwnershipSeed(doc), "refresh allocated handles");
        var refreshed = content.Payload;
        content.RefreshFormattedText(content.StoredValues.Take(3));
        Check(ReferenceEquals(refreshed, content.Payload), "equal refresh changed snapshots");
        TableContentSave(doc, binary, $"value-format-after-{version}-{binary}.dxf");
        content.ReplaceContent(content.Name, content.Description, content.TableStyle, new[] { content.StoredValues[0].WithEvaluatedValue(123) });
        Equal("$123.00", content.StoredValues[0].FormattedText, "scalar and display not edited together");
        var reloaded = EditableContentObject(TableContentLoad(TableContentSave(doc, !binary)));
        Equal(123, reloaded.StoredValues[0].Value, "evaluated scalar reload");
        Equal("$123.00", reloaded.StoredValues[0].FormattedText, "evaluated display reload");
    }

    private static void ValueFormatAtomic(DxfVersion version, bool binary, string failure)
    {
        var doc = failure == "unit" ? EditableContentLoad(version, binary, (_, body) =>
        {
            int start = body.FindIndex(t => t.Code == 300 && Equals(t.Value, "VALUE"));
            int unit = body.FindIndex(start + 1, t => t.Code == 94);
            body[unit] = new netDxf.IO.DxfTag(94, 2);
        }) : ValueFormatDocument(version, binary);
        var content = EditableContentObject(doc);
        var oldValue = content.StoredValues[0];
        if (failure == "stale") content.ReplaceContent("changed", content.Description, content.TableStyle, Array.Empty<DxfStoredTableContentValueEdit>());
        var tags = content.Payload; var values = content.StoredValues; long seed = OwnershipSeed(doc);
        IEnumerable<DxfStoredTableContentValue> Select()
        {
            try
            {
                if (failure == "reentry") Throws<InvalidOperationException>(() => content.RefreshFormattedText(Array.Empty<DxfStoredTableContentValue>()));
                yield return oldValue;
                if (failure == "unsupported") yield return values[3];
                if (failure == "duplicate") yield return oldValue;
                if (failure == "foreign") yield return EditableContentObject(ValueFormatDocument(version, binary)).StoredValues[0];
                if (failure == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            }
            finally { if (failure == "dispose") throw new InvalidOperationException("disposal"); }
        }
        bool rejected = false;
        try { content.RefreshFormattedText(Select()); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { rejected = true; }
        Check(rejected, "invalid refresh accepted");
        Check(ReferenceEquals(tags, content.Payload) && ReferenceEquals(values, content.StoredValues), "failed refresh published changes");
        Equal(seed, OwnershipSeed(doc), "failed refresh allocated");
        doc.DrawingVariables.AcadVer = version;
        if (failure != "unit") content.RefreshFormattedText(content.StoredValues.Take(1));
        Throws<ArgumentException>(() => content.StoredValues[0].WithEvaluatedValue(1.0));
    }

    private static void ValueFormatNative(string file, bool binary)
    {
        var doc = TableContentLoad(TableContentSourceBytes(file));
        int evaluated = 0;
        foreach (var content in doc.Objects.Items.OfType<DxfStoredTableContent>())
        {
            var values = content.StoredValues.Where(v => !string.IsNullOrEmpty(v.FormatString) && v.FormatString.StartsWith("%lu2%pr2", StringComparison.Ordinal)).ToArray();
            foreach (var value in values)
            { Equal(value.FormattedText, value.EvaluateFormattedText(), "native producer display " + file); evaluated++; }
            var tags = content.Payload; content.RefreshFormattedText(values);
            Check(ReferenceEquals(tags, content.Payload), "matching native producer text changed snapshots");
        }
        // Two native families contain no explicit numeric scalar formats; keep their no-op regression.
        if (file.Contains("AC1021") || file.Contains("AC1024")) Check(evaluated >= 2, "native numeric format inventory missing");
        TableContentSave(doc, binary, $"value-format-native-{file}-{binary}.dxf");
    }
}
