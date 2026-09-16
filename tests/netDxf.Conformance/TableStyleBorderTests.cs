using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DxfTableStyleRowBorders ChangedStyleBorders(int row) => new(
        Enumerable.Range(0, 6).Select(i => new DxfTableStyleBorderValues(
            (short)(i % 2 == 0 ? 25 : 50), (i + row) % 2 == 0, (short)(10 + row * 6 + i))));

    private static void RegisterTableStyleBorderTests()
    {
        Run("table-style-borders/constructors", TableStyleBorderConstructors);
        foreach (bool binary in new[] { false, true })
        {
            foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            {
                foreach (bool combined in new[] { false, true })
                    Run($"table-style-borders/roundtrip/{version}/{binary}/{combined}", () => TableStyleBorderRoundTrip(version, binary, combined));
                foreach (string variant in new[] { "zero", "one", "negative", "duplicate", "reordered", "private", "private-subclass" })
                    Run($"table-style-borders/header/{version}/{binary}/{variant}", () => TableStyleBorderHeader(version, binary, variant));
            }
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
                Run($"table-style-borders/native/{file}/{binary}", () => TableStyleBorderNative(file, binary, false));
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
                Run($"table-style-borders/full-native/{file}/{binary}", () => TableStyleBorderNative(file, binary, true));
            foreach (int code in Enumerable.Range(274, 6).Concat(Enumerable.Range(284, 6)).Concat(Enumerable.Range(64, 6)))
                foreach (string variant in new[] { "missing", "duplicate", "private-only" })
                    Run($"table-style-borders/projection/{code}/{binary}/{variant}", () => TableStyleBorderProjection((short)code, binary, variant));
            foreach (int code in Enumerable.Range(284, 6))
                foreach (string variant in new[] { "negative", "two" })
                    Run($"table-style-borders/projection/{code}/{binary}/{variant}", () => TableStyleBorderProjection((short)code, binary, variant));
            foreach (string scenario in new[] { "stale", "foreign", "duplicate", "reentry", "dispose", "profile", "private", "no-scalars", "signed", "reordered" })
                Run($"table-style-borders/boundary/{binary}/{scenario}", () => TableStyleBorderBoundary(binary, scenario));
        }
    }

    private static List<DxfTag> TableStyleBorderPacket(bool versioned = false)
    {
        var packet = TableStylePacket();
        packet.RemoveAll(t => t.Code == 284);
        for (int row = 2; row >= 0; row--)
        {
            int start = packet.Select((tag, index) => (tag, index)).Where(p => p.tag.Code == 7).ElementAt(row).index + 1;
            var fields = new List<DxfTag>();
            for (int i = 0; i < 6; i++)
            {
                fields.Add(new DxfTag((short)(274 + i), (short)-2));
                fields.Add(new DxfTag((short)(284 + i), (short)1));
                fields.Add(new DxfTag((short)(64 + i), (short)0));
            }
            packet.InsertRange(start, fields);
        }
        if (versioned) packet.Insert(1, new DxfTag(280, (short)0));
        return packet;
    }

    private static void TableStyleBorderConstructors()
    {
        var one = new DxfTableStyleBorderValues(-2, true, 256);
        Throws<ArgumentNullException>(() => new DxfTableStyleRowBorders(null!));
        foreach (int count in new[] { 0, 5, 7 })
            Throws<ArgumentException>(() => new DxfTableStyleRowBorders(Enumerable.Repeat(one, count)));
        var source = Enumerable.Repeat(one, 6).ToArray();
        var borders = new DxfTableStyleRowBorders(source); source[0] = null!;
        Check(ReferenceEquals(one, borders.Values[0]), "border values snapshot must not alias caller array");
        Throws<ArgumentException>(() => new DxfTableStyleRowBorders(source));
        Throws<ArgumentOutOfRangeException>(() => borders.WithBorder(-1, one));
        Throws<ArgumentOutOfRangeException>(() => borders.WithBorder(6, one));
        Throws<ArgumentNullException>(() => borders.WithBorder(0, null!));
        Throws<NotSupportedException>(() => ((IList<DxfTableStyleBorderValues>)borders.Values)[0] = one);
        bool disposed = false; int consumed = 0;
        IEnumerable<DxfTableStyleBorderValues> Endless()
        { try { while (true) { consumed++; yield return one; } } finally { disposed = true; } }
        Throws<ArgumentException>(() => new DxfTableStyleRowBorders(Endless()));
        Check(disposed && consumed == 7, "constructor must bound and dispose enumeration");
        IEnumerable<DxfTableStyleBorderValues> Broken()
        { try { foreach (var value in borders.Values) yield return value; } finally { throw new InvalidOperationException("dispose"); } }
        Throws<InvalidOperationException>(() => new DxfTableStyleRowBorders(Broken()));
        var replacement = borders.WithBorder(3, new DxfTableStyleBorderValues(short.MinValue, false, short.MaxValue));
        Equal((short)-2, borders.Values[3].StoredLineweight, "WithBorder retains earlier snapshot");
        Equal(short.MinValue, replacement.Values[3].StoredLineweight, "signed stored values are not normalized");
    }

    private static void TableStyleBorderRoundTrip(DxfVersion version, bool binary, bool combined)
    {
        var doc = TableStyleLoad(TableStyleBorderPacket(version >= DxfVersion.AutoCad2010), binary, version);
        var style = TableStyleObject(doc); var oldTags = style.Tags; var oldRows = style.Rows;
        var oldHeader = style.Header; var oldRefs = style.References.ToArray();
        long seed = OwnershipSeed(doc);
        style.ReplaceStyle(style.Header, style.Rows.Select(row => row.WithBorders(row.Borders)));
        Check(ReferenceEquals(oldTags, style.Tags) && ReferenceEquals(oldRows, style.Rows) && ReferenceEquals(oldHeader, style.Header), "equivalent border edits retain snapshots");
        Equal(seed, OwnershipSeed(doc), "equivalent border edit allocated handles");
        string suffix = $"{version}-{binary}-{combined}.dxf";
        TableStyleSave(doc, binary, "table-style-borders-before-" + suffix);
        seed = OwnershipSeed(doc); // Saving regenerates the empty layer-state dictionary.
        var first = combined ? style.Rows[0].WithValues(EditedStyleRow).WithBorders(ChangedStyleBorders(0)) : style.Rows[0].WithBorders(ChangedStyleBorders(0));
        var third = combined ? style.Rows[2].WithValues(EditedStyleRow).WithBorders(ChangedStyleBorders(2)) : style.Rows[2].WithBorders(ChangedStyleBorders(2));
        style.ReplaceStyle(combined ? EditedStyleHeader() : null!, new[] { first, third });
        Check(!ReferenceEquals(oldTags, style.Tags) && !ReferenceEquals(oldRows, style.Rows), "changes publish new snapshots");
        Check(oldRows.All(row => row.Borders.Values.All(value => value.StoredLineweight == -2 && value.IsVisible && value.StoredColor == 0)), "old border snapshots changed");
        Check(OwnershipTagValues(oldRows[1].Tags).SequenceEqual(OwnershipTagValues(style.Rows[1].Tags)), "untouched middle row changed");
        Check(oldRefs.SequenceEqual(style.References), "border editing changed exact dependencies");
        Check(style.Rows.All(row => ReferenceEquals(row.TextStyle, doc.TextStyles["STYLE_REF"])), "STYLE binding changed");
        Equal(seed, OwnershipSeed(doc), "border editing allocated handles");
        Equal(version >= DxfVersion.AutoCad2010 ? (short?)0 : null, style.Header!.StoredVersion, "leading version changed");
        for (int i = 0; i < oldTags.Count; i++)
            if (oldTags[i].Code == 7 || i == 1 && oldHeader.StoredVersion.HasValue)
                Check(ReferenceEquals(oldTags[i], style.Tags[i]), "version or STYLE tag identity replaced");
        var saved = TableStyleSave(doc, binary, "table-style-borders-after-" + suffix);
        // Raw binary output deliberately rejects ASCII comments; explicitly remove them for this transport conversion.
        var converted = !binary ? saved.WithTags(saved.Tags.Where(t => t.Code != 999)) : saved;
        using var output = new MemoryStream(); converted.Save(output, !binary); output.Position = 0;
        var reloaded = TableStyleObject(DxfDocument.Load(output)!);
        foreach (int row in new[] { 0, 2 }) for (int i = 0; i < 6; i++)
        {
            var expected = ChangedStyleBorders(row).Values[i]; var actual = reloaded.Rows[row].Borders.Values[i];
            Equal(expected.StoredLineweight, actual.StoredLineweight, "reloaded lineweight");
            Equal(expected.IsVisible, actual.IsVisible, "reloaded visibility");
            Equal(expected.StoredColor, actual.StoredColor, "reloaded color");
        }
        Equal(combined ? EditedStyleHeader().Description : oldHeader.Description, reloaded.Header.Description, "header edit roundtrip");
        doc.TextStyles["STYLE_REF"].Name = "BORDER_STYLE_Ω";
        style.ReplaceStyle(null!, new[] { style.Rows[1].WithBorders(ChangedStyleBorders(1)) });
        Check(!doc.TextStyles.Remove(doc.TextStyles["BORDER_STYLE_Ω"]), "border edit released a referenced STYLE");
        using var renamed = new MemoryStream(); Check(doc.Save(renamed, binary), "rename save"); renamed.Position = 0;
        Check(TableStyleObject(DxfDocument.Load(renamed)!).Rows.All(row => row.TextStyle.Name == "BORDER_STYLE_Ω"), "STYLE rename after border edit failed");
    }

    private static void TableStyleBorderNative(string file, bool binary, bool full)
    {
        var source = StoredTableSource(file); var raw = full ? source : TableStyleNativeCarrier(source);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        var doc = DxfDocument.Load(input)!; var style = TableStyleObject(doc);
        Check(style.Rows.All(row => row.Borders != null), "native complete border triples not projected");
        var map = style.CellStyleMap; var references = style.References.ToArray();
        string prefix = "table-style-borders-" + (full ? "full-native" : "native");
        TableStyleSave(doc, binary, $"{prefix}-before-{file}-{binary}.dxf");
        style.ReplaceStyle(null!, new[] { style.Rows[0].WithBorders(ChangedStyleBorders(0)), style.Rows[2].WithBorders(ChangedStyleBorders(2)) });
        Check(ReferenceEquals(map, style.CellStyleMap) && references.SequenceEqual(style.References), "native dependency identity changed");
        TableStyleSave(doc, binary, $"{prefix}-after-{file}-{binary}.dxf");
    }

    private static void TableStyleBorderProjection(short code, bool binary, string variant)
    {
        var packet = TableStyleBorderPacket(); int index = packet.FindIndex(t => t.Code == code); var tag = packet[index];
        if (variant is "missing" or "private-only") packet.RemoveAt(index);
        if (variant == "duplicate") packet.Insert(index, new DxfTag(code, tag.Value));
        if (variant == "private-only") packet.InsertRange(index, new[] { new DxfTag(102, "{PRIVATE_BORDER"), tag, new DxfTag(102, "}") });
        if (variant is "negative" or "two") packet[index] = new DxfTag(code, (short)(variant == "negative" ? -1 : 2));
        var doc = TableStyleLoad(packet, binary); var style = TableStyleObject(doc);
        var original = style.Rows[0];
        Check(original.Borders == null && original.Values != null, "unqualified borders disable only the border projection");
        Throws<NotSupportedException>(() => original.WithBorders(ChangedStyleBorders(0)));
        Throws<NotSupportedException>(() => original.WithValues(EditedStyleRow).WithBorders(ChangedStyleBorders(0)));
        style.ReplaceStyle(null!, new[] { original.WithValues(EditedStyleRow), style.Rows[2].WithBorders(ChangedStyleBorders(2)) });
        Check(style.Rows[0].Borders == null, "other edits must not invent missing border fields");
        var nonScalars = new HashSet<short> { 140, 170, 62, 63, 283 };
        Check(OwnershipTagValues(original.Tags.Where(t => !nonScalars.Contains(t.Code))).SequenceEqual(
            OwnershipTagValues(style.Rows[0].Tags.Where(t => !nonScalars.Contains(t.Code)))), "unqualified border packet changed");
    }

    private static void TableStyleBorderHeader(DxfVersion version, bool binary, string variant)
    {
        var packet = TableStyleBorderPacket();
        if (variant is "zero" or "one" or "negative" or "duplicate" or "reordered")
            packet.Insert(variant == "reordered" ? 2 : 1, new DxfTag(280, (short)(variant == "one" ? 1 : variant == "negative" ? -1 : 0)));
        if (variant == "duplicate") packet.Insert(1, new DxfTag(280, (short)0));
        if (variant == "private") packet.InsertRange(1, new[] { new DxfTag(102, "{PRIVATE_VERSION"), new DxfTag(280, (short)0), new DxfTag(102, "}") });
        if (variant == "private-subclass") packet.AddRange(new[] { new DxfTag(100, "PrivateStyle"), new DxfTag(280, (short)0) });
        bool recognized = variant is "private" or "private-subclass" || variant == "zero" && version >= DxfVersion.AutoCad2010;
        var doc = TableStyleLoad(packet, binary, version); var style = TableStyleObject(doc);
        Equal(recognized, style.Header != null, "versioned header admission");
        var tags = style.Tags; var rows = style.Rows;
        if (!recognized)
        {
            Throws<NotSupportedException>(() => style.ReplaceStyle(EditedStyleHeader(), new[] { rows[0].WithBorders(ChangedStyleBorders(0)) }));
            Check(ReferenceEquals(tags, style.Tags) && ReferenceEquals(rows, style.Rows), "rejected header edit changed borders");
            style.ReplaceStyle(null!, new[] { rows[0].WithBorders(ChangedStyleBorders(0)) });
            Check(style.Header == null, "border-only edit invented a header projection");
        }
        else
        {
            style.ReplaceStyle(EditedStyleHeader(), new[] { rows[0].WithBorders(ChangedStyleBorders(0)) });
            Equal(variant == "zero" ? (short?)0 : null, style.Header!.StoredVersion, "fixed format prefix");
            Equal(true, style.Header!.SuppressTitle, "title suppression not confused with format version");
            if (variant == "zero") Check(ReferenceEquals(tags[1], style.Tags[1]), "leading format tag changed");
        }
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "versioned header save"); output.Position = 0;
        var loaded = TableStyleObject(DxfDocument.Load(output)!);
        Equal(recognized, loaded.Header != null, "versioned header reloaded projection");
        if (recognized) Equal(EditedStyleHeader().Description, loaded.Header!.Description, "versioned header decoded roundtrip");
    }

    private static void TableStyleBorderBoundary(bool binary, string scenario)
    {
        var packet = TableStyleBorderPacket();
        if (scenario == "private") packet.InsertRange(2, new[] { new DxfTag(102, "{PRIVATE_BORDER"), new DxfTag(274, (short)79), new DxfTag(284, (short)2), new DxfTag(64, (short)91), new DxfTag(102, "}") });
        if (scenario == "no-scalars") packet.RemoveAt(packet.FindIndex(t => t.Code == 140));
        if (scenario == "reordered")
        {
            int start = packet.FindIndex(t => t.Code == 7) + 1; var fields = packet.GetRange(start, 18); fields.Reverse();
            packet.RemoveRange(start, 18); packet.InsertRange(start, fields);
        }
        var doc = TableStyleLoad(packet, binary); var style = TableStyleObject(doc);
        var edit = style.Rows[0].WithBorders(ChangedStyleBorders(0));
        Throws<ArgumentNullException>(() => style.Rows[0].WithBorders(null!));
        Throws<ArgumentNullException>(() => edit.WithBorders(null!));
        var tags = style.Tags; var rows = style.Rows; long seed = OwnershipSeed(doc);
        void Apply(IEnumerable<DxfTableStyleRowEdit> edits) => style.ReplaceStyle(null!, edits);
        if (scenario == "stale")
        { Apply(new[] { edit }); tags = style.Tags; rows = style.Rows; Throws<ArgumentException>(() => Apply(new[] { edit })); }
        else if (scenario == "foreign")
        { var foreign = TableStyleObject(TableStyleLoad(TableStyleBorderPacket(), binary)); Throws<ArgumentException>(() => Apply(new[] { foreign.Rows[0].WithBorders(ChangedStyleBorders(0)) })); }
        else if (scenario == "duplicate") Throws<ArgumentException>(() => Apply(new[] { edit, edit }));
        else if (scenario == "reentry")
        {
            IEnumerable<DxfTableStyleRowEdit> Reenter()
            { Throws<InvalidOperationException>(() => Apply(Array.Empty<DxfTableStyleRowEdit>())); yield return edit; }
            Throws<InvalidOperationException>(() => Apply(Reenter()));
        }
        else if (scenario == "dispose")
        {
            IEnumerable<DxfTableStyleRowEdit> Broken()
            { try { yield return edit; } finally { throw new InvalidOperationException("dispose"); } }
            Throws<InvalidOperationException>(() => Apply(Broken()));
        }
        else if (scenario == "profile")
        {
            IEnumerable<DxfTableStyleRowEdit> ChangeProfile()
            { yield return edit; doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013; }
            Throws<InvalidOperationException>(() => Apply(ChangeProfile())); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        }
        else
        {
            if (scenario == "signed") edit = style.Rows[0].WithBorders(style.Rows[0].Borders.WithBorder(0, new DxfTableStyleBorderValues(short.MinValue, false, short.MaxValue)));
            Apply(new[] { edit });
            if (scenario == "private") Check(style.Tags.Any(t => t.Code == 274 && Equals(t.Value, (short)79)) && style.Tags.Any(t => t.Code == 284 && Equals(t.Value, (short)2)) && style.Tags.Any(t => t.Code == 64 && Equals(t.Value, (short)91)), "private lookalike fields changed");
            if (scenario == "no-scalars") Check(style.Rows[0].Values == null, "border-only edit invented row scalars");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "boundary save"); output.Position = 0;
            var reloaded = TableStyleObject(DxfDocument.Load(output)!);
            Equal(scenario == "signed" ? short.MinValue : (short)25, reloaded.Rows[0].Borders.Values[0].StoredLineweight, "boundary border roundtrip");
            return;
        }
        Check(ReferenceEquals(tags, style.Tags) && ReferenceEquals(rows, style.Rows), "failed border request changed snapshots");
        Equal(seed, OwnershipSeed(doc), "failed border request allocated handles");
        Apply(new[] { style.Rows[0].WithBorders(ChangedStyleBorders(2)) });
        Equal((short)22, style.Rows[0].Borders.Values[0].StoredColor, "guard did not reset after rejection");
    }
}
