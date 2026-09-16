using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCellFormatRequestTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            Run($"cell-format/requests/{binary}", () => CellFormatRequests(binary));
            foreach (string action in new[] { "empty", "wrong-type-clear", "foreign-format", "stale-grid", "preserve-extended", "format-name-identity" })
                Run($"cell-format/request-boundary/{binary}/{action}", () => CellFormatRequestBoundary(binary, action));
        }
    }

    private static void CellFormatRequests(bool binary)
    {
        var doc = CellStyleMapLoad(CellFormatRaw(DxfVersion.AutoCad2018), binary);
        var map = CellStyleMapObject(doc); var entry = map.Entries[0]; var format = entry.Format;
        var packet = map.Payload; long seed = OwnershipSeed(doc);
        var first = format.Edit(); var grid = format.Borders[0]; var scalar = grid.Edit().WithValues(CellGridValues(0, 0));
        Throws<ArgumentNullException>(() => first.WithValues(null!));
        Throws<ArgumentNullException>(() => first.WithContent(null!));
        Throws<ArgumentNullException>(() => first.WithMargins(null!));
        Throws<ArgumentNullException>(() => first.WithBorders(null!));
        Throws<ArgumentNullException>(() => entry.WithName(null!));
        Throws<ArgumentNullException>(() => entry.WithFormat(null!));
        Throws<ArgumentNullException>(() => scalar.WithValues(null!));
        Throws<ArgumentException>(() => first.WithBorders(new DxfCellGridFormatEdit[] { null! }));
        Throws<ArgumentException>(() => first.WithBorders(new[] { scalar, scalar }));
        Throws<ArgumentException>(() => first.WithBorders(new[] { map.Entries[1].Format.Borders[0].Edit() }));
        bool disposed = false; int consumed = 0;
        IEnumerable<DxfCellGridFormatEdit> TooMany()
        {
            try
            {
                foreach (var border in format.Borders) { consumed++; yield return border.Edit(); }
                while (true) { consumed++; yield return scalar; }
            }
            finally { disposed = true; }
        }
        Throws<ArgumentException>(() => first.WithBorders(TooMany()));
        Check(disposed && consumed == 7, "grid builder must bound and dispose enumeration");
        IEnumerable<DxfCellGridFormatEdit> Broken()
        { try { yield return scalar; } finally { throw new InvalidOperationException("dispose grid list"); } }
        Throws<InvalidOperationException>(() => first.WithBorders(Broken()));
        var caller = new[] { scalar };
        var selected = first.WithBorders(caller); caller[0] = grid.Edit();
        Check(ReferenceEquals(selected.Borders[0], scalar), "grid request list aliases the caller's array");
        Throws<NotSupportedException>(() => ((IList<DxfCellGridFormatEdit>)selected.Borders).Clear());
        Throws<NotSupportedException>(() => ((IList<DxfCellGridFormat>)format.Borders).Clear());
        Check(first.Borders.Count == 0 && first.Content == null && !first.ChangesTextStyle, "original request mutated by a failed or successful builder");
        var target = doc.TextStyles.Add(new TextStyle("REQUEST_STYLE", "txt.shx"));
        var line = doc.Linetypes.Add(new Linetype("REQUEST_LINE"));
        seed = OwnershipSeed(doc);
        var composed = selected.WithValues(CellFormatValues(0)).WithMargins(CellMargins(0))
            .WithTextStyle(target).WithContent(CellContentValues(0));
        var request = entry.WithFormat(composed).WithName("composed");
        Check(ReferenceEquals(request.Format, composed) && composed.Borders.Count == 1 && composed.ChangesTextStyle && ReferenceEquals(composed.TextStyle, target), "composition discarded requested components");
        var gridRequest = scalar.WithLinetype(line);
        Check(ReferenceEquals(gridRequest.Values, scalar.Values) && !scalar.ChangesLinetype && gridRequest.ChangesLinetype, "grid composition mutated the previous request");
        var replacement = composed.WithBorders(new[] { gridRequest });
        Check(composed.Borders[0] == scalar, "replacing selected grids mutated a previous request");
        map.ReplaceEntries(new[] { request.WithFormat(replacement) });
        Equal(seed, OwnershipSeed(doc), "composed request allocated handles");
        Check(!ReferenceEquals(packet, map.Payload), "composed changes were not published");
        Equal("composed", map.Entries[0].Name, "composed name");
        Check(ReferenceEquals(map.Entries[0].Format.Content.TextStyle, target) && ReferenceEquals(map.Entries[0].Format.Borders[0].Linetype, line), "composed registered resources");
        Equal(CellFormatValues(0).StoredMergeFlags, map.Entries[0].Format.Values.StoredMergeFlags, "composed table values");
        Equal(CellMargins(0).RightMargin, map.Entries[0].Format.Margins.RightMargin, "composed margins");
    }

    private static void CellFormatRequestBoundary(bool binary, string action)
    {
        var raw = action == "empty"
            ? CellStyleMapRaw(DxfVersion.AutoCad2018, (_, tags) => { tags.Clear(); tags.Add(new DxfTag(100, "AcDbCellStyleMap")); tags.Add(new DxfTag(90, 0)); })
            : CellFormatRaw(DxfVersion.AutoCad2018, (doc, f) =>
            { if (action == "wrong-type-clear") f[18] = new DxfTag(340, doc.Linetypes["SOURCE_MAP_LINE"].Handle); });
        if (action == "preserve-extended")
        {
            var record = TableStyleRaw(raw, "CELLSTYLEMAP"); var tags = record.Tags.ToList();
            int content = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "CONTENTFORMAT_BEGIN"));
            tags.Insert(content + 1, new DxfTag(420, 0x123456)); raw = raw.WithRecord(record, tags);
        }
        var doc = CellStyleMapLoad(raw, binary); var map = CellStyleMapObject(doc);
        var original = map.Payload; var refs = map.References.ToArray(); long seed = OwnershipSeed(doc);
        if (action == "empty")
        {
            map.ReplaceEntries(Array.Empty<DxfStoredCellStyleMapEntryEdit>());
            Check(ReferenceEquals(original, map.Payload) && map.Entries.Count == 0, "empty map no-op changed shape");
            var other = CellStyleMapObject(CellStyleMapLoad(CellFormatRaw(DxfVersion.AutoCad2018), binary));
            Throws<ArgumentException>(() => map.ReplaceEntries(new[] { other.Entries[0].WithName("bad") }));
            Equal(seed, OwnershipSeed(doc), "empty map request allocated handles"); return;
        }
        var entry = map.Entries[0];
        if (action == "wrong-type-clear")
        {
            Check(entry.Format.Content.TextStyle == null, "wrong resource type projected as STYLE");
            map.ReplaceEntries(new[] { entry.WithFormat(entry.Format.Edit().WithTextStyle(null!)) });
            Equal("0", map.Entries[0].Format.Content.StoredTextStyleHandle, "explicit null must clear actual wrong-type handle");
            Equal(refs.Length - 1, map.References.Count, "clearing wrong-type handle changed other repeated references");
            Check(!doc.Linetypes.Remove(doc.Linetypes["SOURCE_MAP_LINE"]), "remaining grid references must still protect the LTYPE");
        }
        else if (action == "foreign-format")
            Throws<ArgumentException>(() => entry.WithFormat(map.Entries[1].Format.Edit()));
        else if (action == "stale-grid")
        {
            var oldGrid = entry.Format.Borders[0];
            map.ReplaceEntries(new[] { entry.WithFormat(entry.Format.Edit().WithContent(CellContentValues(0))) });
            original = map.Payload;
            Throws<ArgumentException>(() => map.Entries[0].Format.Edit().WithBorders(new[] { oldGrid.Edit() }));
            Check(ReferenceEquals(original, map.Payload), "stale grid rejection changed payload");
        }
        else if (action == "preserve-extended")
        {
            Check(entry.Format == null && map.Entries[2].Format != null, "extended format must remain unprojected without disabling other entries");
            var untouched = OwnershipTagValues(entry.FormatPayload).ToArray();
            map.ReplaceEntries(new[] { entry.WithName("opaque format"), map.Entries[2].WithFormat(map.Entries[2].Format.Edit().WithContent(CellContentValues(2))) });
            Check(untouched.SequenceEqual(OwnershipTagValues(map.Entries[0].FormatPayload)), "unqualified packet modified during another entry edit");
        }
        else if (action == "format-name-identity")
        {
            var edit = entry.WithFormat(entry.Format.Edit().WithMargins(CellMargins(0)));
            map.ReplaceEntryNames(map.Entries.Select(e => e.Name + " renamed")); original = map.Payload;
            Throws<ArgumentException>(() => map.ReplaceEntries(new[] { edit }));
            Check(ReferenceEquals(original, map.Payload), "stale entry request survived name replacement");
            // Unchanged format snapshot may be reused only with the actual new entry.
            map.ReplaceEntries(new[] { map.Entries[0].WithFormat(edit.Format) });
        }
        Equal(seed, OwnershipSeed(doc), "boundary format edit allocated handles");
        var loaded = TableContentLoad(TableContentSave(doc, binary));
        Equal(0, loaded.Objects.Validate().Count, "boundary graph invalid after reload");
    }
}
