using System.Globalization;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLegacyUtilityReviewTests()
    {
        Run("legacy-utility/format-regressions", LegacyFormatRegressions);
        Run("legacy-utility/format-validation", LegacyFormatValidation);
        Run("legacy-utility/exact-format-matrix", LegacyFormatMatrix);
        Run("legacy-utility/calendar-matrix", LegacyCalendarMatrix);
        Run("legacy-utility/calendar-validation", LegacyCalendarValidation);
        Run("legacy-utility/color-snapshots", LegacyColorSnapshots);
        Run("legacy-utility/color-validation", LegacyColorValidation);
        Run("legacy-utility/color-packing", LegacyColorPacking);
        foreach (string culture in new[] { "en-US", "pl-PL", "tr-TR", "ar-SA" })
            Run("legacy-utility/culture/" + culture, () =>
            {
                var original = CultureInfo.CurrentCulture;
                try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); LegacyFormatRegressions(); }
                finally { CultureInfo.CurrentCulture = original; }
            });
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"legacy-utility/header-and-color/{version}/{binary}", () => LegacyHeaderAndColor(version, binary));
    }

    private static UnitStyleFormat LegacyFormat(int places) => new()
    {
        AngularDecimalPlaces = (short)places,
        LinearDecimalPlaces = (short)places,
        DegreesSymbol = "d", FractionType = FractionFormatType.NotStacked,
        SuppressZeroFeet = false, SuppressZeroInches = false
    };

    private static void LegacyFormatRegressions()
    {
        Equal("13d0'0\"", AngleUnitFormat.ToDegreesMinutesSeconds(12.999999, LegacyFormat(4)), "DMS seconds carry");
        Equal("13d0'", AngleUnitFormat.ToDegreesMinutesSeconds(12.999999, LegacyFormat(2)), "DMS minutes carry");
        Equal("-0d30'0\"", AngleUnitFormat.ToDegreesMinutesSeconds(-.5, LegacyFormat(4)), "one DMS sign");
        Equal("0d0'0\"", AngleUnitFormat.ToDegreesMinutesSeconds(-1e-12, LegacyFormat(4)), "no negative rounded zero");
        Equal("12d30'0.0000\"", AngleUnitFormat.ToDegreesMinutesSeconds(12.5, LegacyFormat(8)), "fractional seconds");
        Equal("2d", AngleUnitFormat.ToDegreesMinutesSeconds(2.5, LegacyFormat(0)), "historical tie-to-even");
        Equal("N 45d0'0\" E", AngleUnitFormat.ToSurveyor(45, LegacyFormat(4)), "NE bearing");
        Equal("N 45d0'0\" W", AngleUnitFormat.ToSurveyor(135, LegacyFormat(4)), "NW bearing");
        Equal("S 45d0'0\" W", AngleUnitFormat.ToSurveyor(225, LegacyFormat(4)), "SW bearing");
        Equal("S 45d0'0\" E", AngleUnitFormat.ToSurveyor(-45, LegacyFormat(4)), "negative SE bearing");
        Equal("S45d0'0\"E", AngleUnitFormat.ToSurveyor(-45, LegacyFormat(4), false), "compact bearing");
        Equal("315.00d", AngleUnitFormat.Format(-45, AngleUnitType.DecimalDegrees, LegacyFormat(2), true), "explicit normalization");
        Equal("50.0000g", AngleUnitFormat.Format(45, AngleUnitType.Gradians, LegacyFormat(4)), "gradians dispatch");
        Equal("0.7854r", AngleUnitFormat.Format(45, AngleUnitType.Radians, LegacyFormat(4)), "radians dispatch");
        Equal("1'-0.00\"", LinearUnitFormat.ToEngineering(11.999, LegacyFormat(2)), "engineering carry");
        Equal("1'-0\"", LinearUnitFormat.ToArchitectural(11.999, LegacyFormat(2)), "architectural carry");
        Equal("1", LinearUnitFormat.ToFractional(.999, LegacyFormat(2)), "fraction rounds to whole");
        Equal("-1'-5 1/2\"", LinearUnitFormat.ToArchitectural(-17.5, LegacyFormat(2)), "single architectural sign");
        Equal("-0 1/2", LinearUnitFormat.ToFractional(-.5, LegacyFormat(2)), "single fractional sign");
        Equal("2147483648", LinearUnitFormat.ToFractional(2147483648.0, LegacyFormat(8)), "no Int32 truncation");
        var format = LegacyFormat(2); format.FractionType = FractionFormatType.Horizontal; format.FractionHeightScale = .75;
        Equal("\\A1;17{\\H0.75x;\\S1/2;}", LinearUnitFormat.ToFractional(17.5, format), "invariant MTEXT fraction scale");
        format.FractionType = FractionFormatType.Diagonal;
        Equal("\\A1;1'-5{\\H0.75x;\\S1#2;}\"", LinearUnitFormat.ToArchitectural(17.5, format), "diagonal stacked fraction");
        format.DecimalSeparator = ",";
        Equal("1'-5,50\"", LinearUnitFormat.ToEngineering(17.5, format), "explicit decimal separator");
        format.SuppressLinearLeadingZeros = true; format.SuppressLinearTrailingZeros = true;
        Equal("0", LinearUnitFormat.ToDecimal(0, format), "zero is not an empty string");
        Equal(",5", LinearUnitFormat.ToDecimal(.5, format), "leading/trailing suppression");
        format.SuppressZeroFeet = true; format.SuppressZeroInches = true;
        Equal("1'", LinearUnitFormat.ToArchitectural(11.999, format), "suppression applied after carry");
        Equal("0\"", LinearUnitFormat.ToArchitectural(0, format), "all-zero fallback");
    }

    private static void LegacyFormatValidation()
    {
        Func<double, UnitStyleFormat, string>[] methods = {
            AngleUnitFormat.ToDecimal, AngleUnitFormat.ToDegreesMinutesSeconds, AngleUnitFormat.ToGradians, AngleUnitFormat.ToRadians,
            LinearUnitFormat.ToDecimal, LinearUnitFormat.ToScientific, LinearUnitFormat.ToArchitectural, LinearUnitFormat.ToEngineering, LinearUnitFormat.ToFractional };
        foreach (var method in methods)
        {
            Throws<ArgumentNullException>(() => method(1, null!));
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Throws<ArgumentOutOfRangeException>(() => method(bad, LegacyFormat(2)));
            Throws<ArgumentOutOfRangeException>(() => method(1, LegacyFormat(9)));
        }
        Throws<ArgumentOutOfRangeException>(() => AngleUnitFormat.Format(1, (AngleUnitType)99, LegacyFormat(2)));
        Throws<OverflowException>(() => AngleUnitFormat.ToGradians(double.MaxValue, LegacyFormat(2)));
        var format = LegacyFormat(2); format.FractionHeightScale = double.NaN;
        Throws<ArgumentOutOfRangeException>(() => LinearUnitFormat.ToFractional(1.5, format));
        format = LegacyFormat(2); format.DecimalSeparator = "";
        Throws<ArgumentException>(() => AngleUnitFormat.ToDecimal(1, format));
    }

    private static string LegacyRender(string kind, double value, UnitStyleFormat format) => kind switch
    {
        "decimal" => LinearUnitFormat.ToDecimal(value, format),
        "engineering" => LinearUnitFormat.ToEngineering(value, format),
        "architectural" => LinearUnitFormat.ToArchitectural(value, format),
        "fractional" => LinearUnitFormat.ToFractional(value, format),
        "angle" => AngleUnitFormat.ToDecimal(value, format),
        "dms" => AngleUnitFormat.ToDegreesMinutesSeconds(value, format),
        _ => throw new ArgumentException(kind)
    };

    private static void LegacyFormatMatrix()
    {
        var values = new List<double> { 0, -0.0, .125, -.125, .5, -.5, 2.5, -2.5, .999, 11.999, 12.999999, 17.5, -17.5,
            1e-300, -1e-300, 2147483648.0, -2147483649.0, double.MaxValue, -double.MaxValue, double.Epsilon };
        var random = new Random(2051);
        while (values.Count < 80)
        {
            byte[] bytes = new byte[8]; random.NextBytes(bytes); double value = BitConverter.ToDouble(bytes, 0);
            if (!double.IsNaN(value) && !double.IsInfinity(value)) values.Add(value);
        }
        var output = new List<object>();
        foreach (string kind in new[] { "decimal", "engineering", "architectural", "fractional", "angle", "dms" })
        foreach (int places in Enumerable.Range(0, 9))
        foreach (double value in values)
            output.Add(new { kind, places, bits = BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture),
                actual = LegacyRender(kind, value, LegacyFormat(places)) });
        File.WriteAllText(Path.Combine(ArtifactDirectory, "legacy-utility-formats.json"), JsonSerializer.Serialize(output));
        Equal(4320, output.Count, "format oracle matrix inventory");
    }

    private static void LegacyCalendarMatrix()
    {
        var dates = new List<DateTime> { DateTime.MinValue, DateTime.MaxValue, new(1999,12,31,21,58,35), new(1998,1,1,12,0,0), new(1997,7,4,14,29,58) };
        foreach (int year in new[] { 1, 4, 99, 100, 400, 1500, 1582, 1600, 1700, 1900, 2000, 2026, 2400, 9999 })
        foreach (int month in new[] { 1, 2, 3, 10, 12 })
            dates.Add(new DateTime(year, month, DateTime.DaysInMonth(year, month), 23, 59, 59).AddTicks(1234567));
        var output = new List<object>();
        foreach (var date in dates)
        {
            double serial = DrawingTime.ToJulianCalendar(date); DateTime decoded = DrawingTime.FromJulianCalendar(serial);
            Check(Math.Abs(date.Ticks - decoded.Ticks) <= 1000, "calendar rounding exceeded binary64 resolution");
            Equal(DateTimeKind.Unspecified, decoded.Kind, "timezone not inferred");
            output.Add(new { ticks = date.Ticks.ToString(CultureInfo.InvariantCulture), bits = BitConverter.DoubleToInt64Bits(serial).ToString("X16", CultureInfo.InvariantCulture),
                decodedTicks = decoded.Ticks.ToString(CultureInfo.InvariantCulture) });
            Equal(serial, DrawingTime.ToJulianCalendar(DateTime.SpecifyKind(date, DateTimeKind.Utc)), "clock fields changed by Kind");
            Equal(serial, DrawingTime.ToJulianCalendar(DateTime.SpecifyKind(date, DateTimeKind.Local)), "local Kind changed clock fields");
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory, "legacy-utility-calendar.json"), JsonSerializer.Serialize(output));
        Equal(75, output.Count, "calendar oracle matrix inventory");
        var elapsed = new List<object>();
        foreach (double days in new[] { 0, -0.0, .5, -.5, 1.0, -1.0, .000001, -.000001, 12345.123456789, -12345.123456789,
            TimeSpan.MaxValue.TotalDays, TimeSpan.MinValue.TotalDays, double.Epsilon, -double.Epsilon })
            elapsed.Add(new { bits = BitConverter.DoubleToInt64Bits(days).ToString("X16", CultureInfo.InvariantCulture), ticks = DrawingTime.EditingTime(days).Ticks.ToString(CultureInfo.InvariantCulture) });
        File.WriteAllText(Path.Combine(ArtifactDirectory, "legacy-utility-elapsed.json"), JsonSerializer.Serialize(elapsed));
    }

    private static void LegacyCalendarValidation()
    {
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, 1721425.99999, 5373485.0, double.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => DrawingTime.FromJulianCalendar(value));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, -double.MaxValue, 10675200.0, -10675200.0 })
            Throws<ArgumentOutOfRangeException>(() => DrawingTime.EditingTime(value));
        Equal(new DateTime(9999, 12, 31, 12, 0, 0), DrawingTime.FromJulianCalendar(5373484.5), "last supported afternoon");
        Equal(new DateTime(1, 1, 1), DrawingTime.FromJulianCalendar(1721426), "calendar origin");
        Equal(2450815.5, DrawingTime.ToJulianCalendar(new DateTime(1998, 1, 1, 12, 0, 0)), "published DXF noon example");
    }

    private static void LegacyColorSnapshots()
    {
        var palette = AciColor.IndexRgb; var baseline = palette.Select(c => (byte[])c.Clone()).ToArray();
        palette[1][0] = 0; palette[7][1] = 0;
        Check(baseline.Zip(AciColor.IndexRgb).All(pair => pair.First.SequenceEqual(pair.Second)), "public palette mutated global ACI definitions");
        Equal((byte)255, AciColor.Red.R, "red palette construction changed");
        Equal((byte)1, AciColor.RgbToAci(255, 0, 0), "nearest-color palette was corrupted");
        if (palette is IList<byte[]> mutable) Throws<NotSupportedException>(() => mutable[0] = new byte[3]);
        var color = new AciColor(new byte[] { 18, 171, 52 }); var copy = (AciColor)color.Clone(); copy.Index = 1;
        Equal((byte)18, color.R, "clone aliases color");
    }

    private static void LegacyColorValidation()
    {
        Throws<ArgumentNullException>(() => new AciColor((byte[])null!));
        Throws<ArgumentNullException>(() => new AciColor((double[])null!));
        foreach (int length in new[] { 0, 1, 2, 4 })
        { Throws<ArgumentException>(() => new AciColor(new byte[length])); Throws<ArgumentException>(() => new AciColor(new double[length])); }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 2 })
        for (int component = 0; component < 3; component++)
        {
            double[] values = { .5, .5, .5 }; values[component] = invalid;
            Throws<ArgumentOutOfRangeException>(() => new AciColor(values));
            Throws<ArgumentOutOfRangeException>(() => AciColor.FromHsl(values[0], values[1], values[2]));
        }
    }

    private static void LegacyColorPacking()
    {
        foreach (int marker in new[] { 0, 1, 127, 194, 255 })
        foreach (int r in new[] { 0, 1, 128, 255 })
        foreach (int g in new[] { 0, 17, 255 })
        foreach (int b in new[] { 0, 52, 255 })
        {
            int packed = unchecked((int)((uint)marker << 24)) | r << 16 | g << 8 | b;
            var color = AciColor.FromTrueColor(packed);
            Equal((byte)r, color.R, "packed red"); Equal((byte)g, color.G, "packed green"); Equal((byte)b, color.B, "packed blue");
            Equal(unchecked((int)0xC2000000) | r << 16 | g << 8 | b, AciColor.ToTrueColor(color), "retained CAD marker");
        }
    }

    private static void LegacyHeaderAndColor(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version);
        var created = new DateTime(9999, 12, 31, 21, 58, 35).AddTicks(1234567);
        var utcCreated = new DateTime(100, 3, 1, 1, 2, 3).AddTicks(7654321);
        doc.DrawingVariables.TdCreate = created; doc.DrawingVariables.TduCreate = utcCreated;
        doc.DrawingVariables.TdinDwg = TimeSpan.FromTicks(123456789);
        doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { Color = new AciColor(18, 171, 52) });
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "utility output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"legacy-utility-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        Check(Math.Abs(created.Ticks - loaded.DrawingVariables.TdCreate.Ticks) <= 1000, "reader replaced a valid final-day time");
        Check(Math.Abs(utcCreated.Ticks - loaded.DrawingVariables.TduCreate.Ticks) <= 1000, "early-century calendar changed");
        Check(Math.Abs(123456789 - loaded.DrawingVariables.TdinDwg.Ticks) <= 1, "elapsed precision lost");
        var actual = loaded.Entities.Lines.Single().Color;
        if (version >= DxfVersion.AutoCad2004) Check(actual.R == 18 && actual.G == 171 && actual.B == 52, "true-color transport changed");
        else Equal(new AciColor(18,171,52).Index, actual.Index, "legacy palette fallback changed");
    }
}
