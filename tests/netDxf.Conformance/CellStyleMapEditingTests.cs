using System.Collections;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCellStyleMapEditingTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
        foreach (bool binary in new[] { false, true })
        {
            Run($"cell-map-edit/roundtrip/{version}/{binary}", () => CellMapEditRoundTrip(version, binary));
            foreach (int fault in Enumerable.Range(0, 12))
                Run($"cell-map-edit/atomic/{version}/{binary}/{fault}", () => CellMapEditAtomic(version, binary, fault));
            Run($"cell-map-edit/empty/{version}/{binary}", () =>
            {
                var map = CellStyleMapObject(CellStyleMapLoad(CellStyleMapRaw(version, (_, tags) =>
                { tags.RemoveRange(2, tags.Count - 2); tags[1] = new DxfTag(90, 0); }), binary));
                var before = map.Payload; map.ReplaceEntryNames(Array.Empty<string>());
                Check(ReferenceEquals(before, map.Payload), "empty name update changed source packet");
            });
        }
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
            Run($"cell-map-edit/native/{file}/{binary}", () => CellMapEditNative(file, binary));
    }

    private static void CellMapEditRoundTrip(DxfVersion version, bool binary)
    {
        var doc = CellStyleMapLoad(CellStyleMapRaw(version), binary);
        var map = CellStyleMapObject(doc);
        var payload = map.Payload; var entries = map.Entries;
        var refs = map.References.ToArray();
        string[] names = { @"Literal \U+0041 — Zażółć 日本語 😀", "CELLSTYLE_END" };
        map.ReplaceEntryNames(names);
        Check(!ReferenceEquals(payload, map.Payload) && !ReferenceEquals(entries, map.Entries), "changed update did not replace snapshots");
        Check(entries.All(e => e.Name == "Custom"), "old entry snapshot changed");
        Check(map.Entries.Select(e => e.Name).SequenceEqual(names), "decoded names differ");
        Check(map.References.SequenceEqual(refs), "name edit changed referenced identities");
        for (int i = 0; i < payload.Count; i++)
            if (i != 10 && i != 20) Equal(payload[i], map.Payload[i], "unselected payload tag changed");
        for (int i = 0; i < entries.Count; i++)
        {
            Equal(entries[i].Id, map.Entries[i].Id, "stored id changed");
            Equal(entries[i].StoredType, map.Entries[i].StoredType, "stored type changed");
            Check(entries[i].FormatPayload.SequenceEqual(map.Entries[i].FormatPayload), "format payload changed");
        }
        var equivalent = map.Payload; var sameEntries = map.Entries;
        map.ReplaceEntryNames(names);
        Check(ReferenceEquals(equivalent, map.Payload) && ReferenceEquals(sameEntries, map.Entries), "equivalent names invalidated snapshots");
        var reload = CellStyleMapObject(TableContentLoad(TableContentSave(doc, binary, $"cell-map-edit-{version}-{binary}.dxf")));
        Check(reload.Entries.Select(e => e.Name).SequenceEqual(names), "Unicode or literal escape names failed roundtrip");
        map.ReplaceEntryNames(new[] { "", "" });
        Check(map.Entries.All(e => e.Name == ""), "empty duplicate names rejected");
    }

    private static void CellMapEditAtomic(DxfVersion version, bool binary, int fault)
    {
        var doc = CellStyleMapLoad(CellStyleMapRaw(version), binary);
        var map = CellStyleMapObject(doc); var payload = map.Payload; var entries = map.Entries;
        Exception? error = null;
        try
        {
            if (fault == 0) map.ReplaceEntryNames(null!);
            else if (fault == 1) map.ReplaceEntryNames(new[] { "one" });
            else if (fault == 2) map.ReplaceEntryNames(new[] { "one", "two", "three" });
            else if (fault == 3) map.ReplaceEntryNames(new[] { "one", null! });
            else if (fault == 4) map.ReplaceEntryNames(new[] { "one", "bad\0name" });
            else if (fault == 5) map.ReplaceEntryNames(new[] { "one", "\uD800" });
            else if (fault == 6) map.ReplaceEntryNames(new[] { "one", new string('x', 1048577) });
            else if (fault == 7) map.ReplaceEntryNames(new[] { "one", new string('\\', 149797) });
            else if (fault == 8) map.ReplaceEntryNames(new CellMapNames(() => throw new InvalidOperationException("enumerator dispose")));
            else if (fault == 9) map.ReplaceEntryNames(new CellMapNames(() =>
            { try { map.ReplaceEntryNames(new[] { "nested", "names" }); } catch (InvalidOperationException) { } }));
            else if (fault == 10) map.ReplaceEntryNames(new CellMapNames(() => doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000));
            else map.ReplaceEntryNames(CellMapThrowingNames());
        }
        catch (Exception caught) { error = caught; }
        Check(error is ArgumentException || error is InvalidOperationException, "invalid name edit accepted or wrong rejection");
        Check(ReferenceEquals(payload, map.Payload) && ReferenceEquals(entries, map.Entries), "failed name edit changed map snapshots");
        doc.DrawingVariables.AcadVer = version;
        map.ReplaceEntryNames(new[] { "recovered", "after failure" });
        Equal("recovered", map.Entries[0].Name, "failure left edit guard engaged");
    }

    private static IEnumerable<string> CellMapThrowingNames()
    { yield return "first"; throw new InvalidOperationException("enumeration failure"); }

    private sealed class CellMapNames : IEnumerable<string>
    {
        private readonly Action dispose;
        internal CellMapNames(Action dispose) { this.dispose = dispose; }
        public IEnumerator<string> GetEnumerator() => new Enumerator(this.dispose);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator : IEnumerator<string>
        {
            private readonly Action dispose; private int index = -1;
            internal Enumerator(Action dispose) { this.dispose = dispose; }
            public string Current => this.index == 0 ? "first" : "second";
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++this.index < 2;
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => this.dispose();
        }
    }

    private static void CellMapEditNative(string file, bool binary)
    {
        var doc = TableContentLoad(TableContentSourceBytes(file)); var map = CellStyleMapObject(doc);
        var original = map.Entries; string owner = map.Owner.Handle; string handle = map.Handle;
        var dependencies = map.References.Select(item => item.Handle).ToArray();
        string[] names = original.Select((_, i) => "Custom style " + i).ToArray();
        map.ReplaceEntryNames(names);
        var reload = CellStyleMapObject(TableContentLoad(TableContentSave(doc, binary, $"cell-map-edit-native-{file}-{binary}.dxf")));
        Equal(handle, reload.Handle, "native map identity changed"); Equal(owner, reload.Owner.Handle, "native owner changed");
        Check(reload.References.Select(item => item.Handle).SequenceEqual(dependencies), "native dependencies changed");
        Check(reload.Entries.Select(item => item.Name).SequenceEqual(names), "native names failed roundtrip");
        for (int i = 0; i < original.Count; i++)
        {
            Equal(original[i].Id, reload.Entries[i].Id, "native id changed");
            Equal(original[i].StoredType, reload.Entries[i].StoredType, "native type changed");
            Check(OwnershipTagValues(original[i].FormatPayload).SequenceEqual(OwnershipTagValues(reload.Entries[i].FormatPayload)), "native format or dependency packet changed");
        }
    }
}
