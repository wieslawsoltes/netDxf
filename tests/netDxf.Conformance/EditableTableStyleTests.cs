using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly DxfTableStyleRowValues EditedStyleRow = new(4.125, 6, 3, 257, true);
    private static DxfTableStyleHeader EditedStyleHeader(string description = "Edited Żółć \\U+0041 😀")
        => new(description, 1, -7, 2.125, 3.25, true, false);

    private static void RegisterEditableTableStyleTests()
    {
        Run("table-style-edit/constructors", EditableStyleConstructors);
        foreach (bool binary in new[] { false, true })
        {
            foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
                Run($"table-style-edit/scalars/{version}/{binary}", () => EditableStyleScalars(version, binary));
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
                Run($"table-style-edit/native/{file}/{binary}", () => EditableStyleNative(file, binary, false));
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
                Run($"table-style-edit/full-native/{file}/{binary}", () => EditableStyleNative(file, binary, true));
            foreach (string scenario in new[] { "null-enumerable", "null-entry", "duplicate", "foreign", "stale", "enumerator", "dispose", "reentry", "profile-callback", "unknown-header", "unknown-row", "erased", "profile", "invalid-database", "private-fields", "negative-zero", "description-length", "binary-newline" })
                Run($"table-style-edit/boundary/{scenario}/{binary}", () => EditableStyleBoundary(scenario, binary));
        }
    }

    private static void EditableStyleConstructors()
    {
        Throws<ArgumentNullException>(() => EditedStyleHeader(null!));
        foreach (string value in new[] { "nul\0", "\ud800", "\udc00", "A\ud800B", new string('a', 256) })
            Throws<ArgumentException>(() => EditedStyleHeader(value));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0 })
        {
            Throws<ArgumentOutOfRangeException>(() => new DxfTableStyleHeader("", 0, 0, value, 0, false, false));
            Throws<ArgumentOutOfRangeException>(() => new DxfTableStyleHeader("", 0, 0, 0, value, false, false));
            Throws<ArgumentOutOfRangeException>(() => new DxfTableStyleRowValues(value, 0, 0, 0, false));
        }
        Throws<ArgumentOutOfRangeException>(() => new DxfTableStyleHeader("", -1, 0, 0, 0, false, false));
        Throws<ArgumentOutOfRangeException>(() => new DxfTableStyleHeader("", 2, 0, 0, 0, false, false));
    }

    private static void EditableStyleScalars(DxfVersion version, bool binary)
    {
        var doc = TableStyleLoad(TableStylePacket(), binary, version); var style = TableStyleObject(doc);
        var oldTags = style.Tags; var oldRows = style.Rows; var oldHeader = style.Header; var oldReferences = style.References;
        var original = OwnershipTagValues(oldTags).ToArray(); long seed = OwnershipSeed(doc);
        var equivalent = new DxfTableStyleHeader(oldHeader.Description, oldHeader.FlowDirection, oldHeader.StoredFlags,
            oldHeader.HorizontalCellMargin, oldHeader.VerticalCellMargin, oldHeader.SuppressTitle, oldHeader.SuppressColumnHeading);
        style.ReplaceStyle(equivalent, style.Rows.Select(r => r.WithValues(r.Values)));
        Check(ReferenceEquals(oldTags, style.Tags) && ReferenceEquals(oldHeader, style.Header) && ReferenceEquals(oldRows, style.Rows), "equal request retains snapshots");
        style.ReplaceStyle(EditedStyleHeader(), new[] { style.Rows[2].WithValues(EditedStyleRow), style.Rows[0].WithValues(EditedStyleRow) });
        Check(!ReferenceEquals(oldTags, style.Tags) && original.SequenceEqual(OwnershipTagValues(oldTags)), "old packet remains immutable");
        Equal("Independent description", oldHeader.Description, "old header snapshot");
        Equal(1.0, oldRows[0].Values.TextHeight, "old row snapshot");
        Equal(EditedStyleHeader().Description, style.Header.Description, "decoded description");
        Equal(EditedStyleRow.TextHeight, style.Rows[0].Values.TextHeight, "first changed row");
        Equal(2.0, style.Rows[1].Values.TextHeight, "untouched middle row");
        Check(oldReferences.SequenceEqual(style.References), "references retained");
        Check(style.Rows.All(r => ReferenceEquals(r.TextStyle, doc.TextStyles["STYLE_REF"])), "row STYLE identities retained");
        Check(oldTags.Where(t => t.Code == 7).SequenceEqual(style.Tags.Where(t => t.Code == 7)), "source STYLE tags reused");
        Equal(seed, OwnershipSeed(doc), "editing allocates no handles");
        var saved = TableStyleSave(doc, binary, $"edited-table-style-scalars-{version}-{binary}.dxf");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, !binary), "opposite transport save"); bytes.Position = 0;
        var loaded = DxfDocument.Load(bytes)!; var again = TableStyleObject(loaded);
        Equal(style.Header.Description, again.Header.Description, "escaped Unicode and literal escape round trip");
        SameDoubleBits(4.125, again.Rows[2].Values.TextHeight, "row scalar round trip");
        doc.TextStyles["STYLE_REF"].Name = "RENAMED_Ω_STYLE";
        style.ReplaceStyle(null!, new[] { style.Rows[1].WithValues(new DxfTableStyleRowValues(8.5, -15, -1, 256, false)) });
        var renamed = TableStyleSave(doc, !binary, $"edited-table-style-renamed-{version}-{binary}.dxf");
        Check(TableStyleRaw(renamed).Tags.Where(t => t.Code == 7).All(t => ((string)t.Value).StartsWith("RENAMED_", StringComparison.Ordinal)), "resource rename survives a second edit");
        Check(!doc.TextStyles.Remove(doc.TextStyles["RENAMED_Ω_STYLE"]), "edited style protects actual resource");
    }

    private static void EditableStyleNative(string file, bool binary, bool full)
    {
        var source = StoredTableSource(file); var raw = full ? source : TableStyleNativeCarrier(source);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        var doc = DxfDocument.Load(input)!; var style = TableStyleObject(doc);
        var kind = full ? "full-native" : "native";
        TableStyleSave(doc, binary, $"edited-table-style-{kind}-before-{file}-{binary}.dxf");
        var map = style.CellStyleMap; var originalRows = style.Rows;
        var header = style.Header == null ? null : EditedStyleHeader();
        style.ReplaceStyle(header!, new[] { style.Rows[0].WithValues(EditedStyleRow), style.Rows[2].WithValues(EditedStyleRow) });
        Check(ReferenceEquals(map, style.CellStyleMap), "native map identity");
        Check(style.Rows.All(r => ReferenceEquals(r.TextStyle, doc.GetObjectByHandle("11"))), "native exact STYLE bindings");
        Check(originalRows.All(r => r.Values.TextHeight != 4.125), "native row snapshots retained");
        var after = TableStyleSave(doc, binary, $"edited-table-style-{kind}-after-{file}-{binary}.dxf");
        using var output = new MemoryStream(); after.Save(output, binary); output.Position = 0;
        var reloaded = DxfDocument.Load(output)!; var actual = TableStyleObject(reloaded);
        Equal(4.125, actual.Rows[0].Values.TextHeight, "native stored edit reloaded");
        Check(actual.Header != null, "recognized native header remains projected");
        Equal(file.Contains("AC1024") ? (short?)0 : null, actual.Header!.StoredVersion, "native header format version stays fixed");
        if (actual.Header != null) Equal(EditedStyleHeader().Description, actual.Header.Description, "native header edit");
    }

    private static void EditableStyleBoundary(string scenario, bool binary)
    {
        var packet = TableStylePacket();
        if (scenario == "unknown-header") packet.Insert(1, new DxfTag(280, (short)1));
        if (scenario == "unknown-row") packet.RemoveAt(packet.FindIndex(t => t.Code == 140));
        if (scenario == "private-fields")
        {
            packet.InsertRange(2, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(40, 83.0), new DxfTag(140, 91.0), new DxfTag(102, "}") });
            packet.AddRange(new[] { new DxfTag(100, "PrivateStyle"), new DxfTag(140, 92.0), new DxfTag(3, "private header") });
        }
        var doc = TableStyleLoad(packet, binary); var style = TableStyleObject(doc);
        var tags = style.Tags; var rows = style.Rows; var header = style.Header;
        long seed = OwnershipSeed(doc); int objectCount = doc.Objects.Items.Count;
        var edit = scenario == "unknown-row" ? null : rows[0].WithValues(EditedStyleRow);
        var empty = Array.Empty<DxfTableStyleRowEdit>();
        void Apply(IEnumerable<DxfTableStyleRowEdit> values) => style.ReplaceStyle(EditedStyleHeader(), values);
        if (scenario == "null-enumerable") Throws<ArgumentNullException>(() => Apply(null!));
        else if (scenario == "null-entry") Throws<ArgumentException>(() => Apply(new DxfTableStyleRowEdit[] { null! }));
        else if (scenario == "duplicate") Throws<ArgumentException>(() => Apply(new[] { edit!, edit! }));
        else if (scenario == "foreign")
        {
            var foreign = TableStyleObject(TableStyleLoad(TableStylePacket(), binary));
            Throws<ArgumentException>(() => Apply(new[] { foreign.Rows[0].WithValues(EditedStyleRow) }));
        }
        else if (scenario == "stale")
        {
            style.ReplaceStyle(null!, new[] { edit! }); tags = style.Tags; rows = style.Rows; header = style.Header;
            Throws<ArgumentException>(() => Apply(new[] { edit! }));
        }
        else if (scenario == "enumerator" || scenario == "dispose")
        {
            IEnumerable<DxfTableStyleRowEdit> Broken()
            {
                try { yield return edit!; if (scenario == "enumerator") throw new InvalidOperationException("enumeration failed"); }
                finally { if (scenario == "dispose") throw new InvalidOperationException("disposal failed"); }
            }
            Throws<InvalidOperationException>(() => Apply(Broken()));
        }
        else if (scenario == "reentry")
        {
            IEnumerable<DxfTableStyleRowEdit> Reenter()
            { Throws<InvalidOperationException>(() => style.ReplaceStyle(null!, empty)); yield return edit!; }
            Throws<InvalidOperationException>(() => Apply(Reenter()));
        }
        else if (scenario == "profile-callback")
        {
            IEnumerable<DxfTableStyleRowEdit> ChangeProfile()
            { yield return edit!; doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; }
            Throws<InvalidOperationException>(() => Apply(ChangeProfile())); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        }
        else if (scenario == "profile")
        { doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; Throws<InvalidOperationException>(() => Apply(empty)); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018; }
        else if (scenario == "unknown-header")
        {
            Throws<NotSupportedException>(() => Apply(empty));
            style.ReplaceStyle(null!, new[] { edit! });
            Check(style.Header == null && style.Tags[1].Code == 280, "unknown header remains exact during row edit"); return;
        }
        else if (scenario == "unknown-row")
        {
            Throws<NotSupportedException>(() => style.Rows[0].WithValues(EditedStyleRow));
            style.ReplaceStyle(EditedStyleHeader(), new[] { style.Rows[2].WithValues(EditedStyleRow) });
            Check(style.Rows[0].Values == null, "unknown row remains unprojected during another row edit"); return;
        }
        else if (scenario == "erased")
        {
            doc.Objects.EraseOwnedTree(style); seed = OwnershipSeed(doc); objectCount = doc.Objects.Items.Count;
            Throws<InvalidOperationException>(() => Apply(empty));
        }
        else if (scenario == "invalid-database")
        {
            style.PersistentReactors.Add(new netDxf.Entities.Line(Vector3.Zero, Vector3.UnitX));
            Throws<InvalidOperationException>(() => Apply(empty)); style.PersistentReactors.Clear();
        }
        else if (scenario == "private-fields")
        {
            Apply(new[] { edit! });
            Check(style.Tags.Any(t => t.Code == 40 && Equals(t.Value, 83.0)) && style.Tags.Any(t => t.Code == 140 && Equals(t.Value, 91.0))
                && style.Tags.Any(t => t.Code == 140 && Equals(t.Value, 92.0)) && style.Tags.Any(t => t.Code == 3 && Equals(t.Value, "private header")), "private fields remain exact");
            TableStyleSave(doc, binary, $"edited-table-style-private-{binary}.dxf"); return;
        }
        else if (scenario == "negative-zero")
        {
            var zero = new DxfTableStyleRowValues(-0.0, 0, 0, 0, false);
            style.ReplaceStyle(null!, new[] { style.Rows[0].WithValues(zero) });
            SameDoubleBits(-0.0, style.Rows[0].Values.TextHeight, "negative zero edit retained");
            tags = style.Tags; style.ReplaceStyle(null!, new[] { style.Rows[0].WithValues(new DxfTableStyleRowValues(0.0, 0, 0, 0, false)) });
            Check(!ReferenceEquals(tags, style.Tags), "bit-distinct zero is an edit"); return;
        }
        else if (scenario == "description-length")
        {
            var text = new string('Ω', 254) + "\\"; style.ReplaceStyle(EditedStyleHeader(text), empty);
            Equal(text, style.Header.Description, "255 decoded code units accepted");
            TableStyleSave(doc, binary, $"edited-table-style-long-description-{binary}.dxf"); return;
        }
        else if (scenario == "binary-newline")
        {
            style.ReplaceStyle(EditedStyleHeader("line1\r\nline2"), empty);
            using var output = new MemoryStream();
            if (binary) { Check(doc.Save(output, true), "binary retains newline"); output.Position = 0; Equal("line1\r\nline2", TableStyleObject(DxfDocument.Load(output)!).Header.Description, "binary newline reload"); }
            else { bool refused; try { refused = !doc.Save(output, false); } catch (Exception e) when (e is InvalidDataException || e is ArgumentException || e is InvalidOperationException) { refused = true; } Check(refused && output.Length == 0, "ASCII newline rejects before bytes"); }
            return;
        }
        Check(ReferenceEquals(tags, style.Tags) && ReferenceEquals(rows, style.Rows) && ReferenceEquals(header, style.Header), "rejected request preserves snapshots");
        Equal(seed, OwnershipSeed(doc), "rejection retains handle allocation"); Equal(objectCount, doc.Objects.Items.Count, "rejection retains membership");
    }
}
