using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string RowSettingsTarget = "ROW_REPLACEMENT_Ω";
    private static DxfTableStyleRowDataTypes RowDataTypes(int row) => new(4 + row, 2 + row);

    private static void RegisterTableRowSettingsTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
        foreach (bool binary in new[] { false, true })
        {
            foreach (string mode in new[] { "types", "style", "combined" })
                Run($"table-row-settings/roundtrip/{version}/{binary}/{mode}", () => TableRowSettingsRoundTrip(version, binary, mode));
            foreach (string scenario in new[] { "reassign", "handle", "other-consumer", "unresolved" })
                Run($"table-row-settings/references/{version}/{binary}/{scenario}", () => TableRowSettingsReferences(version, binary, scenario));
            foreach (int code in new[] { 90, 91 })
            foreach (string variant in new[] { "missing", "duplicate", "private-only" })
                Run($"table-row-settings/projection/{version}/{binary}/{code}/{variant}", () => TableRowSettingsProjection(version, binary, (short)code, variant));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
            foreach (string mode in new[] { "types", "style", "combined" })
                Run($"table-row-settings/native/{file}/{binary}/{mode}", () => TableRowSettingsNative(file, binary, mode, false));
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
            foreach (string mode in new[] { "types", "style", "combined" })
                Run($"table-row-settings/full-native/{file}/{binary}/{mode}", () => TableRowSettingsNative(file, binary, mode, true));
            foreach (string scenario in new[] { "foreign-target", "unregistered", "removed-target", "stale", "duplicate", "foreign-row", "dispose", "reentry", "profile", "remove-callback", "erased", "invalid-source", "null-edit", "null-rows" })
                Run($"table-row-settings/atomic/{binary}/{scenario}", () => TableRowSettingsAtomic(binary, scenario));
            foreach (string scenario in new[] { "no-scalars", "no-borders", "private", "signed", "composition", "renamed-before", "added-callback", "unresolved-handle" })
                Run($"table-row-settings/boundary/{binary}/{scenario}", () => TableRowSettingsBoundary(binary, scenario));
        }
    }

    private static void ApplyRowSettings(DxfTableStyle style, TextStyle target, string mode)
    {
        var edits = new List<DxfTableStyleRowEdit>();
        foreach (int index in new[] { 0, 2 })
        {
            var row = style.Rows[index];
            if (mode == "types")
            {
                if (row.DataTypes != null) edits.Add(row.WithDataTypes(RowDataTypes(index)));
                continue;
            }
            var edit = row.WithTextStyle(target);
            if (mode == "combined")
            {
                edit = edit.WithValues(EditedStyleRow).WithBorders(ChangedStyleBorders(index));
                if (row.DataTypes != null) edit = edit.WithDataTypes(RowDataTypes(index));
            }
            edits.Add(edit);
        }
        style.ReplaceStyle(mode == "combined" ? EditedStyleHeader() : null!, edits);
    }

    private static void TableRowSettingsRoundTrip(DxfVersion version, bool binary, string mode)
    {
        var doc = TableStyleLoad(TableStyleBorderPacket(version >= DxfVersion.AutoCad2010), binary, version);
        TableRowSettingsExercise(doc, binary, mode, $"synthetic-{version}-{binary}-{mode}");
    }

    private static void TableRowSettingsNative(string file, bool binary, string mode, bool full)
    {
        var raw = StoredTableSource(file);
        if (!full) raw = TableStyleNativeCarrier(raw);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        var doc = DxfDocument.Load(input)!;
        Check(TableStyleObject(doc).Rows.All(row => (row.DataTypes == null) == file.Contains("AC1018")), "native optional data/unit inventory");
        TableRowSettingsExercise(doc, binary, mode, $"{(full ? "full-native" : "native")}-{file}-{binary}-{mode}");
    }

    private static void TableRowSettingsExercise(DxfDocument doc, bool binary, string mode, string suffix)
    {
        var style = TableStyleObject(doc);
        var target = doc.TextStyles.Add(new TextStyle(RowSettingsTarget, "txt.shx"));
        var oldTags = style.Tags; var oldRows = style.Rows; var oldReferences = style.References;
        var referenceValues = oldReferences.ToArray(); var map = style.CellStyleMap;
        style.ReplaceStyle(null!, style.Rows.Select(row => row.WithTextStyle(row.TextStyle)));
        Check(ReferenceEquals(oldTags, style.Tags) && ReferenceEquals(oldRows, style.Rows), "same identity preserves snapshots and wire spelling");
        TableStyleSave(doc, binary, $"table-row-settings-before-{suffix}.dxf");
        long seed = OwnershipSeed(doc);
        ApplyRowSettings(style, target, mode);
        Equal(seed, OwnershipSeed(doc), "row settings allocate no handles");
        Check(referenceValues.SequenceEqual(oldReferences), "previous dependency membership snapshot changed");
        Check(ReferenceEquals(map, style.CellStyleMap), "map ownership changed");
        Check(OwnershipTagValues(oldRows[1].Tags).SequenceEqual(OwnershipTagValues(style.Rows[1].Tags)), "unselected row changed");
        Check(oldRows.SelectMany(row => row.Tags).Where(tag => tag.Code == 1).SequenceEqual(style.Rows.SelectMany(row => row.Tags).Where(tag => tag.Code == 1)), "raw format strings changed");
        foreach (int index in new[] { 0, 2 })
        {
            var row = style.Rows[index];
            Check(ReferenceEquals(row.TextStyle, mode == "types" ? oldRows[index].TextStyle : target), "current row STYLE identity");
            if (mode != "style" && row.DataTypes != null)
            {
                Equal(RowDataTypes(index).StoredDataType, row.DataTypes.StoredDataType, "new stored data type");
                Equal(RowDataTypes(index).StoredUnitType, row.DataTypes.StoredUnitType, "new stored unit type");
            }
        }
        var current = style.Tags; ApplyRowSettings(style, target, mode);
        Check(ReferenceEquals(current, style.Tags), "equivalent composed edit is a no-op");
        TableStyleSave(doc, binary, $"table-row-settings-after-{suffix}.dxf");
        target.Name = "ROW_RENAMED_Żółć_😀";
        current = style.Tags;
        if (mode != "types") style.ReplaceStyle(null!, new[] { style.Rows[0].WithTextStyle(target) });
        Check(ReferenceEquals(current, style.Tags), "same renamed STYLE must not normalize stored tags");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "opposite transport save"); output.Position = 0;
        var again = TableStyleObject(DxfDocument.Load(output)!);
        foreach (int index in new[] { 0, 2 })
        {
            Equal(style.Rows[index].TextStyle.Name, again.Rows[index].TextStyle.Name, "STYLE reassignment/rename reload");
            if (style.Rows[index].DataTypes != null)
            {
                Equal(style.Rows[index].DataTypes.StoredDataType, again.Rows[index].DataTypes.StoredDataType, "data type reload");
                Equal(style.Rows[index].DataTypes.StoredUnitType, again.Rows[index].DataTypes.StoredUnitType, "unit type reload");
            }
        }
        Equal(0, doc.Objects.Validate().Count, "edited source graph remains valid");
    }

    private static int TableRowUses(DxfDocument doc, TextStyle resource, DxfTableStyle style) =>
        doc.TextStyles.GetReferences(resource).Where(r => ReferenceEquals(r.Reference, style)).Sum(r => r.Uses);

    private static void TableRowSettingsReferences(DxfVersion version, bool binary, string scenario)
    {
        var packet = TableStyleBorderPacket();
        if (scenario == "unresolved") packet = packet.Select(tag => tag.Code == 7 ? new DxfTag(7, "EXPLICIT_NEW") : tag).ToList();
        var doc = TableStyleLoad(packet, binary, version, mutate: raw =>
        {
            if (scenario != "handle") return raw;
            var old = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "STYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "STYLE_REF")));
            string handle = (string)old.Tags.Single(t => t.Code == 5).Value;
            var record = TableStyleRaw(raw);
            return raw.WithRecord(record, record.Tags.Concat(new[] { new DxfTag(340, handle), new DxfTag(340, handle) }));
        });
        var style = TableStyleObject(doc); var oldResource = doc.TextStyles["STYLE_REF"];
        var target = doc.TextStyles.Add(new TextStyle(scenario == "unresolved" ? "EXPLICIT_NEW" : RowSettingsTarget, "txt.shx"));
        var oldReferences = style.References; var oldValues = oldReferences.ToArray(); var oldRows = style.Rows;
        var text = new Text("other", Vector2.Zero, 1, oldResource);
        if (scenario == "other-consumer") doc.Entities.Add(text);
        if (scenario == "unresolved") Check(oldRows.All(row => row.TextStyle == null), "adding a same-name resource must not implicitly bind an unresolved source");
        style.ReplaceStyle(null!, new[] { style.Rows[0].WithTextStyle(target) });
        Equal(1, TableRowUses(doc, target, style), "first new named reference count");
        if (scenario != "unresolved") Equal(scenario == "handle" ? 4 : 2, TableRowUses(doc, oldResource, style), "remaining old named/handle uses");
        style.ReplaceStyle(null!, style.Rows.Select(row => row.WithTextStyle(target)));
        Equal(3, TableRowUses(doc, target, style), "three new named uses");
        Equal(scenario == "handle" ? 2 : 0, TableRowUses(doc, oldResource, style), "old explicit handles retained after named reassignment");
        Check(oldReferences.SequenceEqual(oldValues), "old dependency view mutated");
        Check(oldRows.All(row => !ReferenceEquals(row.TextStyle, target)), "old row STYLE identities mutated");
        Check(!doc.TextStyles.Remove(target), "new STYLE must be protected");
        if (scenario is "handle" or "other-consumer") Check(!doc.TextStyles.Remove(oldResource), "unrelated references to former STYLE must stay protected");
        if (scenario == "other-consumer") Check(doc.Entities.Remove(text), "remove other consumer");
        if (scenario != "handle") Check(doc.TextStyles.Remove(oldResource), "last named reassignment must release former STYLE");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "reference graph save"); output.Position = 0;
        var loaded = DxfDocument.Load(output)!; var reloaded = TableStyleObject(loaded);
        Equal(3, TableRowUses(loaded, loaded.TextStyles[target.Name], reloaded), "reference count reload");
        doc.Objects.EraseOwnedTree(style);
        Check(doc.TextStyles.Remove(target), "erasure releases new named references");
        if (scenario == "handle") Check(doc.TextStyles.Remove(oldResource), "erasure releases remaining handle references");
    }

    private static void TableRowSettingsProjection(DxfVersion version, bool binary, short code, string variant)
    {
        var packet = TableStyleBorderPacket(); int index = packet.FindIndex(t => t.Code == code); var original = packet[index];
        if (variant != "duplicate") packet.RemoveAt(index);
        if (variant == "duplicate") packet.Insert(index, new DxfTag(code, original.Value));
        if (variant == "private-only") packet.InsertRange(index, new[] { new DxfTag(102, "{PRIVATE_TYPES"), original, new DxfTag(102, "}") });
        var doc = TableStyleLoad(packet, binary, version); var style = TableStyleObject(doc); var row = style.Rows[0];
        Check(row.DataTypes == null && style.Rows[1].DataTypes != null, "incomplete/ambiguous public pair must not project");
        Throws<NotSupportedException>(() => row.WithDataTypes(RowDataTypes(0)));
        Throws<NotSupportedException>(() => row.WithValues(EditedStyleRow).WithDataTypes(RowDataTypes(0)));
        var before = row.Tags.Where(t => t.Code is 90 or 91).ToArray();
        style.ReplaceStyle(null!, new[] { row.WithTextStyle(doc.TextStyles["PRIVATE_STYLE"]).WithValues(EditedStyleRow), style.Rows[2].WithDataTypes(RowDataTypes(2)) });
        Check(before.SequenceEqual(style.Rows[0].Tags.Where(t => t.Code is 90 or 91)), "other edits changed unprojected data/unit fields");
    }

    private static void TableRowSettingsAtomic(bool binary, string scenario)
    {
        var doc = TableStyleLoad(TableStyleBorderPacket(), binary); var style = TableStyleObject(doc);
        var target = doc.TextStyles.Add(new TextStyle(RowSettingsTarget, "txt.shx"));
        if (scenario == "foreign-target") target = new DxfDocument().TextStyles.Add(new TextStyle(RowSettingsTarget, "txt.shx"));
        if (scenario == "unregistered") target = new TextStyle(RowSettingsTarget, "txt.shx");
        if (scenario == "removed-target") Check(doc.TextStyles.Remove(target), "setup removes unused target");
        if (scenario == "erased") doc.Objects.EraseOwnedTree(style);
        var edit = style.Rows[0].WithTextStyle(target).WithDataTypes(RowDataTypes(0));
        if (scenario == "stale") style.ReplaceStyle(EditedStyleHeader(), Array.Empty<DxfTableStyleRowEdit>());
        if (scenario == "foreign-row") edit = TableStyleObject(TableStyleLoad(TableStyleBorderPacket(), binary)).Rows[0].WithTextStyle(target);
        var tags = style.Tags; var rows = style.Rows; var references = style.References; var values = references.ToArray(); long seed = OwnershipSeed(doc);
        if (scenario == "invalid-source") style.PersistentReactors.Add(new Line(Vector3.Zero, Vector3.UnitX));
        IEnumerable<DxfTableStyleRowEdit> Requests()
        {
            try
            {
                if (scenario == "reentry") Throws<InvalidOperationException>(() => style.ReplaceStyle(null!, Array.Empty<DxfTableStyleRowEdit>()));
                yield return scenario == "null-edit" ? null! : edit;
                if (scenario == "duplicate") yield return edit;
                if (scenario == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
                if (scenario == "remove-callback") Check(doc.TextStyles.Remove(target), "callback removes unused target");
            }
            finally { if (scenario == "dispose") throw new InvalidOperationException("disposal"); }
        }
        Exception? error = null;
        try { style.ReplaceStyle(EditedStyleHeader(), scenario == "null-rows" ? null! : Requests()); }
        catch (Exception caught) { error = caught; }
        Check(error is ArgumentException or InvalidOperationException, "invalid reassignment was accepted or failed unexpectedly");
        Check(ReferenceEquals(tags, style.Tags) && ReferenceEquals(rows, style.Rows) && values.SequenceEqual(references) && values.SequenceEqual(style.References), "rejection changed style or dependency snapshots");
        Equal(seed, OwnershipSeed(doc), "rejection allocated handles");
        if (scenario == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        if (scenario == "invalid-source") style.PersistentReactors.Clear();
        if (scenario != "erased") style.ReplaceStyle(null!, new[] { style.Rows[0].WithTextStyle(doc.TextStyles["PRIVATE_STYLE"]) });
    }

    private static void TableRowSettingsBoundary(bool binary, string scenario)
    {
        var packet = TableStyleBorderPacket();
        if (scenario == "no-scalars") packet.RemoveAt(packet.FindIndex(t => t.Code == 140));
        if (scenario == "no-borders") packet.RemoveAt(packet.FindIndex(t => t.Code == 274));
        if (scenario == "private") packet.InsertRange(10, new[] { new DxfTag(102, "{PRIVATE_ROW"), new DxfTag(7, "PRIVATE_STYLE"), new DxfTag(90, -77), new DxfTag(91, -88), new DxfTag(102, "}") });
        if (scenario == "unresolved-handle") packet.Add(new DxfTag(340, "C0FFEE01"));
        var doc = TableStyleLoad(packet, binary); var style = TableStyleObject(doc);
        var target = new TextStyle(RowSettingsTarget, "txt.shx");
        if (scenario != "added-callback") target = doc.TextStyles.Add(target);
        var row = style.Rows[0]; var types = scenario == "signed" ? new DxfTableStyleRowDataTypes(int.MinValue, int.MaxValue) : RowDataTypes(0);
        var edit = row.WithDataTypes(types).WithTextStyle(target);
        Throws<ArgumentNullException>(() => row.WithDataTypes(null!)); Throws<ArgumentNullException>(() => row.WithTextStyle(null!));
        Throws<ArgumentNullException>(() => edit.WithDataTypes(null!)); Throws<ArgumentNullException>(() => edit.WithTextStyle(null!));
        Throws<ArgumentNullException>(() => edit.WithValues(null!));
        if (scenario == "no-scalars") Throws<NotSupportedException>(() => edit.WithValues(EditedStyleRow));
        if (scenario == "no-borders") Throws<NotSupportedException>(() => edit.WithBorders(ChangedStyleBorders(0)));
        if (scenario == "renamed-before") target.Name = "RENAMED_BEFORE_Ω";
        if (scenario == "composition")
        {
            edit = edit.WithBorders(ChangedStyleBorders(0)).WithValues(EditedStyleRow).WithTextStyle(target).WithDataTypes(types);
            Check(ReferenceEquals(edit.TextStyle, target) && ReferenceEquals(edit.DataTypes, types) && ReferenceEquals(edit.Values, EditedStyleRow), "chaining dropped changes");
        }
        IEnumerable<DxfTableStyleRowEdit> Requests()
        { yield return edit; if (scenario == "added-callback") doc.TextStyles.Add(target); }
        style.ReplaceStyle(null!, Requests());
        Check(ReferenceEquals(target, style.Rows[0].TextStyle), "actual post-enumeration STYLE not bound");
        Equal(types.StoredDataType, style.Rows[0].DataTypes.StoredDataType, "stored data type");
        Equal(types.StoredUnitType, style.Rows[0].DataTypes.StoredUnitType, "stored unit type");
        if (scenario == "private") Check(style.Tags.Any(t => t.Code == 90 && Equals(t.Value, -77)) && style.Tags.Any(t => t.Code == 91 && Equals(t.Value, -88)) && !style.References.Contains(doc.TextStyles["PRIVATE_STYLE"]), "private fields changed or bound");
        if (scenario == "unresolved-handle") Check(style.Tags.Any(t => t.Code == 340 && Equals(t.Value, "C0FFEE01")) && !style.References.Any(r => r.Handle == "C0FFEE01"), "unbound handle gained a target");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "boundary save"); output.Position = 0;
        var again = TableStyleObject(DxfDocument.Load(output)!);
        Equal(types.StoredDataType, again.Rows[0].DataTypes.StoredDataType, "signed data reload");
        Equal(types.StoredUnitType, again.Rows[0].DataTypes.StoredUnitType, "signed unit reload");
        Equal(target.Name, again.Rows[0].TextStyle.Name, "name reload");
    }
}
