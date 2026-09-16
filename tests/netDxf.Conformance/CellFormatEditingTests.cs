using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] CellFormatModes = { "table", "content", "margins", "grids", "resources", "combined" };
    private static DxfCellStyleFormatValues CellFormatValues(int row) => new(17, 42, (short)(10 + row), 3);
    private static DxfCellContentFormatValues CellContentValues(int row, string? text = null) => new(
        19, 2, 4, 2, text ?? $"%lu2%pr3 Literal \\U+0041 — Ω 😀 {row}", .25 + row, 2.25, 6, (short)(11 + row), 3.5 + row);
    private static DxfCellMargins CellMargins(int row) => new(.25, .5, .75, 1, 1.25, 1.5 + row);
    private static DxfCellGridFormatValues CellGridValues(int row, int slot) => new(7, 1, (short)(20 + slot), 25 + row, slot % 2, .125 * (slot + 1));

    private static void RegisterCellFormatEditingTests()
    {
        Run("cell-format/constructors", CellFormatConstructors);
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
        foreach (bool binary in new[] { false, true })
        {
            foreach (string mode in CellFormatModes)
                Run($"cell-format/synthetic/{version}/{binary}/{mode}", () => CellFormatExercise(CellStyleMapLoad(CellFormatRaw(version), binary), binary, mode, $"synthetic-{version}-{binary}-{mode}"));
            foreach (string fault in new[] { "stale", "foreign", "duplicate", "null-edit", "null-sequence", "enumeration", "disposal", "nested-entries", "nested-names", "names-outer", "profile", "foreign-style", "foreign-line", "removed-style", "removed-line", "invalid-source", "encoding" })
                Run($"cell-format/atomic/{version}/{binary}/{fault}", () => CellFormatAtomic(version, binary, fault));
            foreach (string mode in new[] { "reassign", "clear", "other-consumer", "partial" })
                Run($"cell-format/references/{version}/{binary}/{mode}", () => CellFormatReferences(version, binary, mode));
            foreach (string text in new[] { "", "CONTENTFORMAT_BEGIN", "TABLEFORMAT_END", "CELLSTYLE_BEGIN", @"\U+0041", "Zażółć 日本語 😀", "a\r\nb" })
                Run($"cell-format/format-string/{version}/{binary}/{text}", () => CellFormatString(version, binary, text));
        }
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
        foreach (string mode in CellFormatModes)
            Run($"cell-format/native/{file}/{binary}/{mode}", () => CellFormatExercise(TableContentLoad(TableContentSourceBytes(file)), binary, mode, $"native-{file}-{binary}-{mode}"));
        foreach (bool binary in new[] { false, true })
        {
            foreach (string shape in new[] { "empty-data", "no-margins", "no-borders", "duplicate-masks", "signed", "negative-zero", "callback-add", "callback-rename" })
                Run($"cell-format/shape/{binary}/{shape}", () => CellFormatShape(binary, shape));
            foreach (int position in new[] { 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 23, 24, 25, 26, 27, 28, 29, 31, 32, 33, 35, 36, 37, 38, 39, 40, 41 })
            foreach (string fault in new[] { "missing", "duplicate" })
                Run($"cell-format/projection/{binary}/{position}/{fault}", () => CellFormatProjection(binary, position, fault));
            foreach (string fault in new[] { "extra-color", "surplus", "bad-count", "zero-mask", "unknown-literal", "bad-decoded-string", "wrong-resource-type" })
                Run($"cell-format/extended/{binary}/{fault}", () => CellFormatExtended(binary, fault));
        }
    }

    private static List<DxfTag> CellFormatPacket(string style, string line)
    {
        var tags = new List<DxfTag> {
            new(1,"TABLEFORMAT_BEGIN"), new(90,5), new(170,(short)1), new(91,0), new(92,32768), new(62,(short)257), new(93,1),
            new(300,"CONTENTFORMAT"), new(1,"CONTENTFORMAT_BEGIN"), new(90,0), new(91,0), new(92,4), new(93,0), new(300,""),
            new(40,0.0), new(140,1.0), new(94,5), new(62,(short)0), new(340,style), new(144,6.0), new(309,"CONTENTFORMAT_END"),
            new(171,(short)1), new(301,"MARGIN"), new(1,"CELLMARGIN_BEGIN"), new(40,1.5), new(40,1.5), new(40,1.5),
            new(40,1.5), new(40,1.5), new(40,1.5), new(309,"CELLMARGIN_END"), new(94,6) };
        for (int i = 0; i < 6; i++) tags.AddRange(new DxfTag[] { new(95,1 << i), new(302,"GRIDFORMAT"), new(1,"GRIDFORMAT_BEGIN"),
            new(90,0), new(91,1), new(62,(short)0), new(92,-2), new(340,line), new(93,0), new(40,.045), new(309,"GRIDFORMAT_END") });
        tags.Add(new DxfTag(309,"TABLEFORMAT_END")); return tags;
    }

    private static DxfRawDocument CellFormatRaw(DxfVersion version, Action<DxfDocument, List<DxfTag>>? mutate = null)
    {
        return CellStyleMapRaw(version, (doc, payload) =>
        {
            var style = doc.TextStyles.Add(new TextStyle("SOURCE_MAP_STYLE", "txt.shx"));
            var line = doc.Linetypes.Add(new Linetype("SOURCE_MAP_LINE"));
            var format = CellFormatPacket(style.Handle, line.Handle); mutate?.Invoke(doc, format);
            payload.Clear(); payload.AddRange(new DxfTag[] { new(100,"AcDbCellStyleMap"), new(90,3) });
            for (int i = 0; i < 3; i++)
            {
                payload.Add(new DxfTag(300,"CELLSTYLE")); payload.AddRange(format.Select(t => new DxfTag(t.Code, t.Value)));
                payload.AddRange(new DxfTag[] { new(1,"CELLSTYLE_BEGIN"), new(90,-7), new(91,i), new(300,"Custom"), new(309,"CELLSTYLE_END") });
            }
        });
    }

    private static DxfStoredCellStyleMapEntryEdit CellFormatRequest(DxfStoredCellStyleMapEntry entry, int index, string mode, TextStyle style, Linetype line)
    {
        var format = entry.Format ?? throw new InvalidOperationException("Expected qualified format"); var edit = format.Edit();
        if (mode is "table" or "combined") edit = edit.WithValues(CellFormatValues(index));
        if (mode is "content" or "combined") edit = edit.WithContent(CellContentValues(index));
        if (mode is "margins" or "combined") edit = edit.WithMargins(CellMargins(index));
        if (mode is "resources" or "combined") edit = edit.WithTextStyle(style);
        if (mode is "grids" or "resources" or "combined") edit = edit.WithBorders(format.Borders.Select((border, slot) =>
        {
            var request = border.Edit();
            if (mode != "resources") request = request.WithValues(CellGridValues(index, slot));
            if (mode != "grids") request = request.WithLinetype(line);
            return request;
        }));
        var result = entry.WithFormat(edit);
        return mode == "combined" ? result.WithName("Edited map " + index) : result;
    }

    private static void CellFormatExercise(DxfDocument doc, bool binary, string mode, string suffix)
    {
        var map = CellStyleMapObject(doc);
        Check(map.Entries.Count == 3 && map.Entries.All(e => e.Format != null), "complete format projections required");
        var targetStyle = doc.TextStyles.Add(new TextStyle("EDIT_MAP_STYLE_Ω", "txt.shx"));
        var targetLine = doc.Linetypes.Add(new Linetype("EDIT_MAP_LTYPE_Ω"));
        var prior = map.Entries; var priorPacket = map.Payload; var priorRefs = map.References; var refs = priorRefs.ToArray();
        var priorWire = OwnershipTagValues(priorPacket).ToArray();
        map.ReplaceEntries(prior.Select(e => e.WithFormat(e.Format.Edit().WithValues(e.Format.Values).WithContent(e.Format.Content.Values)
            .WithMargins(e.Format.Margins).WithTextStyle(e.Format.Content.TextStyle)
            .WithBorders(e.Format.Borders.Select(b => b.Edit().WithValues(b.Values).WithLinetype(b.Linetype))))));
        Check(ReferenceEquals(priorPacket, map.Payload) && ReferenceEquals(prior, map.Entries), "equivalent full request changed snapshots");
        TableContentSave(doc, binary, $"cell-format-before-{suffix}.dxf"); long seed = OwnershipSeed(doc);
        map.ReplaceEntries(new[] { CellFormatRequest(prior[0], 0, mode, targetStyle, targetLine), CellFormatRequest(prior[2], 2, mode, targetStyle, targetLine) });
        Equal(seed, OwnershipSeed(doc), "format editing allocated handles");
        Check(priorWire.SequenceEqual(OwnershipTagValues(priorPacket)) && refs.SequenceEqual(priorRefs), "old source/reference snapshots mutated");
        Check(OwnershipTagValues(prior[1].FormatPayload).SequenceEqual(OwnershipTagValues(map.Entries[1].FormatPayload)), "unselected entry changed");
        Check(map.Entries.Select(e => e.Id).SequenceEqual(prior.Select(e => e.Id)) && map.Entries.Select(e => e.StoredType).SequenceEqual(prior.Select(e => e.StoredType)), "entry IDs/types changed");
        foreach (int index in new[] { 0, 2 })
        {
            var f = map.Entries[index].Format;
            Check(f.Borders.Select(b => b.StoredIndexMask).SequenceEqual(prior[index].Format.Borders.Select(b => b.StoredIndexMask)), "border mask/order changed");
            if (mode is "resources" or "combined") Check(ReferenceEquals(f.Content.TextStyle, targetStyle) && f.Borders.All(b => ReferenceEquals(b.Linetype, targetLine)), "selected resource identity not bound");
            if (mode is "content" or "combined") Equal(CellContentValues(index).FormatString, f.Content.Values.FormatString, "decoded expression changed");
        }
        var current = map.Payload;
        map.ReplaceEntries(new[] { CellFormatRequest(map.Entries[0], 0, mode, targetStyle, targetLine), CellFormatRequest(map.Entries[2], 2, mode, targetStyle, targetLine) });
        Check(ReferenceEquals(current, map.Payload), "identical repeated edit is not a no-op");
        TableContentSave(doc, binary, $"cell-format-after-{suffix}.dxf");
        var again = CellStyleMapObject(TableContentLoad(TableContentSave(doc, !binary)));
        Check(OwnershipTagValues(map.Payload).SequenceEqual(OwnershipTagValues(again.Payload)), "opposite-transport reload changed format packet");
        Check(map.References.Select(o => o.Handle).SequenceEqual(again.References.Select(o => o.Handle)), "ordered dependencies changed on reload");
        if (mode is "resources" or "combined")
        {
            targetStyle.Name = "RENAMED_STYLE_Ż"; targetLine.Name = "RENAMED_LINE_Ż";
            again = CellStyleMapObject(TableContentLoad(TableContentSave(doc, binary)));
            Equal(targetStyle.Name, again.Entries[0].Format.Content.TextStyle.Name, "renamed STYLE lost its binding");
            Equal(targetLine.Name, again.Entries[0].Format.Borders[0].Linetype.Name, "renamed LTYPE lost its binding");
        }
        map.ReplaceEntryNames(map.Entries.Select(e => e.Name + " renamed"));
        Check(map.Entries.All(e => e.Format != null), "old name-edit API dropped format projections");
        Equal(0, doc.Objects.Validate().Count, "edited map graph invalid");
    }

    private static void CellFormatAtomic(DxfVersion version, bool binary, string fault)
    {
        var doc = CellStyleMapLoad(CellFormatRaw(version), binary); var map = CellStyleMapObject(doc);
        var style = doc.TextStyles.Add(new TextStyle("EDIT_MAP_STYLE_Ω", "txt.shx")); var line = doc.Linetypes.Add(new Linetype("EDIT_MAP_LTYPE_Ω"));
        if (fault == "foreign-style") style = new DxfDocument(version).TextStyles.Add(new TextStyle(style.Name, "txt.shx"));
        if (fault == "foreign-line") line = new DxfDocument(version).Linetypes.Add(new Linetype(line.Name));
        if (fault == "removed-style") Check(doc.TextStyles.Remove(style), "setup remove style");
        if (fault == "removed-line") Check(doc.Linetypes.Remove(line), "setup remove line");
        var edit = CellFormatRequest(map.Entries[0], 0, "combined", style, line);
        if (fault == "stale") map.ReplaceEntryNames(map.Entries.Select(e => e.Name + " stale"));
        if (fault == "foreign")
        {
            var foreign = CellStyleMapObject(CellStyleMapLoad(CellFormatRaw(version), binary));
            edit = foreign.Entries[0].WithName("foreign");
        }
        if (fault == "encoding") edit = map.Entries[0].WithFormat(map.Entries[0].Format.Edit().WithContent(CellContentValues(0, new string('\\', 149797))));
        if (fault == "invalid-source") map.PersistentReactors.Add(new Line(Vector3.Zero, Vector3.UnitX));
        var payload = map.Payload; var entries = map.Entries; var refs = map.References.ToArray(); long seed = OwnershipSeed(doc);
        IEnumerable<DxfStoredCellStyleMapEntryEdit> Input()
        {
            try
            {
                if (fault == "nested-entries") Throws<InvalidOperationException>(() => map.ReplaceEntries(Array.Empty<DxfStoredCellStyleMapEntryEdit>()));
                if (fault == "nested-names") Throws<InvalidOperationException>(() => map.ReplaceEntryNames(map.Entries.Select(e => e.Name)));
                yield return fault == "null-edit" ? null! : edit;
                if (fault == "duplicate") yield return edit;
                if (fault == "enumeration") throw new InvalidOperationException("enumeration");
                if (fault == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            }
            finally { if (fault == "disposal") throw new InvalidOperationException("disposal"); }
        }
        if (fault == "names-outer")
        {
            IEnumerable<string> Names()
            { Throws<InvalidOperationException>(() => map.ReplaceEntries(Input())); foreach (var e in map.Entries) yield return e.Name + " outer"; }
            Throws<InvalidOperationException>(() => map.ReplaceEntryNames(Names()));
        }
        else
        {
            Exception? error = null; try { map.ReplaceEntries(fault == "null-sequence" ? null! : Input()); } catch (Exception e) { error = e; }
            Check(error is ArgumentException or InvalidOperationException, "bad format edit accepted or unexpected exception");
        }
        Check(ReferenceEquals(payload, map.Payload) && ReferenceEquals(entries, map.Entries) && refs.SequenceEqual(map.References), "failed edit changed snapshots or references");
        Equal(seed, OwnershipSeed(doc), "rejected edit allocated handles");
        doc.DrawingVariables.AcadVer = version; if (fault == "invalid-source") map.PersistentReactors.Clear();
        map.ReplaceEntries(new[] { map.Entries[0].WithName("recovered") });
        Equal("recovered", map.Entries[0].Name, "format guard did not reset");
    }

    private static void CellFormatReferences(DxfVersion version, bool binary, string mode)
    {
        var doc = CellStyleMapLoad(CellFormatRaw(version), binary); var map = CellStyleMapObject(doc);
        var oldStyle = doc.TextStyles["SOURCE_MAP_STYLE"]; var oldLine = doc.Linetypes["SOURCE_MAP_LINE"];
        var targetStyle = doc.TextStyles.Add(new TextStyle("TARGET_STYLE", "txt.shx")); var targetLine = doc.Linetypes.Add(new Linetype("TARGET_LINE"));
        var other = new Text("other", Vector2.Zero, 1, oldStyle) { Linetype = oldLine };
        if (mode == "other-consumer") doc.Entities.Add(other);
        var refs = map.References; var old = refs.ToArray();
        var chosen = mode == "partial" ? map.Entries.Take(1) : map.Entries;
        map.ReplaceEntries(chosen.Select(e => e.WithFormat(e.Format.Edit().WithTextStyle(mode == "clear" ? null! : targetStyle)
            .WithBorders(e.Format.Borders.Select(b => b.Edit().WithLinetype(mode == "clear" ? null! : targetLine))))));
        Check(refs.SequenceEqual(old), "old reference membership changed");
        int oldStyleUses = doc.TextStyles.GetReferences(oldStyle).Where(r => ReferenceEquals(r.Reference, map)).Sum(r => r.Uses);
        int oldLineUses = doc.Linetypes.GetReferences(oldLine).Where(r => ReferenceEquals(r.Reference, map)).Sum(r => r.Uses);
        Equal(mode == "partial" ? 2 : 0, oldStyleUses, "remaining old STYLE count");
        Equal(mode == "partial" ? 12 : 0, oldLineUses, "remaining repeated LTYPE count");
        if (mode is "partial" or "other-consumer")
        { Check(!doc.TextStyles.Remove(oldStyle) && !doc.Linetypes.Remove(oldLine), "remaining resource use lost its guard"); }
        else Check(doc.TextStyles.Remove(oldStyle) && doc.Linetypes.Remove(oldLine), "last map reference did not release resource");
        if (mode != "clear") Check(!doc.TextStyles.Remove(targetStyle) && !doc.Linetypes.Remove(targetLine), "new references not guarded");
        var loaded = TableContentLoad(TableContentSave(doc, binary)); var again = CellStyleMapObject(loaded);
        Check(map.References.Select(o => o.Handle).SequenceEqual(again.References.Select(o => o.Handle)), "reference order/multiplicity reload");
        if (mode == "clear") Check(again.References.Count == 0 && again.Entries.All(e => e.Format.Content.TextStyle == null && e.Format.Borders.All(b => b.Linetype == null)), "null handles rebound");
    }

    private static void CellFormatString(DxfVersion version, bool binary, string text)
    {
        var doc = CellStyleMapLoad(CellFormatRaw(version), binary); var map = CellStyleMapObject(doc);
        map.ReplaceEntries(new[] { map.Entries[0].WithFormat(map.Entries[0].Format.Edit().WithContent(CellContentValues(0, text))) });
        using var output = new MemoryStream();
        if (!binary && text.Contains('\n')) { CheckSaveRejected(doc, output); Equal(0L, output.Length, "newline refusal must precede bytes"); return; }
        Check(doc.Save(output, binary), "format string save"); output.Position = 0;
        var again = CellStyleMapObject(DxfDocument.Load(output)!);
        Equal(text, again.Entries[0].Format.Content.Values.FormatString, "decoded format string did not roundtrip");
    }

    private static void CellFormatShape(bool binary, string shape)
    {
        var raw = CellFormatRaw(DxfVersion.AutoCad2018, (_, f) =>
        {
            if (shape == "empty-data") { f.RemoveRange(3, f.Count - 4); f[2] = new DxfTag(170, (short)0); }
            if (shape == "no-margins") { f.RemoveRange(22, 9); f[21] = new DxfTag(171, (short)0); }
            if (shape == "no-borders") { f.RemoveRange(32, f.Count - 33); f[31] = new DxfTag(94, 0); }
            if (shape == "duplicate-masks") for (int i = 0; i < f.Count; i++) if (f[i].Code == 95) f[i] = new DxfTag(95, 1);
        });
        var doc = CellStyleMapLoad(raw, binary); var map = CellStyleMapObject(doc); var entry = map.Entries[0]; var format = entry.Format;
        Check(format != null, "qualified optional shape not projected");
        var edit = format!.Edit();
        if (shape == "empty-data")
        {
            Check(format.Values == null && format.Content == null && format.Margins == null && format.Borders.Count == 0, "absent data synthesized");
            Throws<NotSupportedException>(() => edit.WithValues(CellFormatValues(0))); Throws<NotSupportedException>(() => edit.WithContent(CellContentValues(0)));
            Throws<NotSupportedException>(() => edit.WithTextStyle(null!));
        }
        else if (shape == "no-margins") { Check(format.Margins == null, "absent margins synthesized"); Throws<NotSupportedException>(() => edit.WithMargins(CellMargins(0))); }
        else if (shape == "no-borders") Check(format.Borders.Count == 0, "absent borders synthesized");
        else if (shape == "signed") edit = edit.WithValues(new DxfCellStyleFormatValues(int.MinValue, int.MaxValue, short.MinValue, -1))
            .WithContent(new DxfCellContentFormatValues(-1, -2, -3, -4, "", -5, -6, -7, short.MaxValue, -8))
            .WithMargins(new DxfCellMargins(-1, -2, -3, -4, -5, -6));
        else if (shape == "negative-zero") edit = edit.WithMargins(new DxfCellMargins(-0.0, 0, 0, 0, 0, 0));
        else if (shape is "callback-add" or "callback-rename")
        {
            var style = new TextStyle("CALLBACK_STYLE", "txt.shx");
            if (shape == "callback-rename") doc.TextStyles.Add(style);
            IEnumerable<DxfStoredCellStyleMapEntryEdit> Input()
            {
                yield return entry.WithFormat(edit.WithTextStyle(style));
                if (shape == "callback-add") doc.TextStyles.Add(style); else style.Name = "CALLBACK_RENAMED";
            }
            map.ReplaceEntries(Input()); Check(ReferenceEquals(style, map.Entries[0].Format.Content.TextStyle), "post-enumeration exact target not used"); return;
        }
        map.ReplaceEntries(new[] { entry.WithFormat(edit).WithName("shape") });
        var again = CellStyleMapObject(TableContentLoad(TableContentSave(doc, binary)));
        if (shape == "negative-zero") SameDoubleBits(-0.0, again.Entries[0].Format.Margins.VerticalMargin, "negative zero margin");
        Check(again.Entries[0].Format != null, "optional shape lost after reload");
    }

    private static void CellFormatProjection(bool binary, int position, string fault)
    {
        var raw = CellFormatRaw(DxfVersion.AutoCad2018, (_, f) => { if (fault == "missing") f.RemoveAt(position); else f.Insert(position, new DxfTag(f[position].Code, f[position].Value)); });
        // Frame marker mutations are rejected by the existing stored envelope, not made editable.
        bool frame = new[] { 23 }.Contains(position);
        if (frame)
        {
            bool rejected = false; try { using var input = new MemoryStream(TableContentRawBytes(raw, binary)); rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
            Check(rejected, "broken structural frame not rejected"); return;
        }
        var doc = CellStyleMapLoad(raw, binary); var map = CellStyleMapObject(doc);
        Check(map.Entries.All(e => e.Format == null), "unqualified format shape gained an editing projection");
        var prior = OwnershipTagValues(map.Entries[0].FormatPayload).ToArray();
        map.ReplaceEntries(new[] { map.Entries[0].WithName("still stored") });
        Check(prior.SequenceEqual(OwnershipTagValues(map.Entries[0].FormatPayload)), "name edit modified unqualified format");
    }

    private static void CellFormatExtended(bool binary, string fault)
    {
        var raw = CellFormatRaw(DxfVersion.AutoCad2018, (doc, f) =>
        {
            if (fault == "extra-color") f.Insert(18, new DxfTag(420, 0x123456));
            if (fault == "surplus") f.Insert(20, new DxfTag(98, 19));
            if (fault == "bad-count") f[31] = new DxfTag(94, 7);
            if (fault == "zero-mask") f[32] = new DxfTag(95, 0);
            if (fault == "unknown-literal") f[7] = new DxfTag(300, "not-content-format");
            if (fault == "bad-decoded-string") f[13] = new DxfTag(300, @"\U+D800");
            if (fault == "wrong-resource-type") f[18] = new DxfTag(340, doc.Linetypes["SOURCE_MAP_LINE"].Handle);
        });
        var doc = CellStyleMapLoad(raw, binary); var map = CellStyleMapObject(doc);
        if (fault == "wrong-resource-type")
        {
            Check(map.Entries[0].Format != null && map.Entries[0].Format.Content.TextStyle == null, "wrong typed resource incorrectly projected");
            map.ReplaceEntries(new[] { map.Entries[0].WithFormat(map.Entries[0].Format.Edit().WithTextStyle(doc.TextStyles["SOURCE_MAP_STYLE"])) });
            Check(ReferenceEquals(map.Entries[0].Format.Content.TextStyle, doc.TextStyles["SOURCE_MAP_STYLE"]), "explicit repair failed");
        }
        else Check(map.Entries[0].Format == null, "extended or unqualified packet projected");
    }

    private static void CellFormatConstructors()
    {
        foreach (double v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => new DxfCellGridFormatValues(0, 0, 0, 0, 0, v));
            for (int i = 0; i < 6; i++) { var d = new double[6]; d[i] = v; Throws<ArgumentOutOfRangeException>(() => new DxfCellMargins(d[0], d[1], d[2], d[3], d[4], d[5])); }
            for (int i = 0; i < 3; i++) { var d = new double[3]; d[i] = v; Throws<ArgumentOutOfRangeException>(() => new DxfCellContentFormatValues(0, 0, 0, 0, "", d[0], d[1], 0, 0, d[2])); }
        }
        foreach (string? text in new[] { null, "\0", "\ud800", new string('x', 1048577) })
            Throws<ArgumentException>(() => new DxfCellContentFormatValues(0, 0, 0, 0, text!, 0, 0, 0, 0, 0));
    }
}
