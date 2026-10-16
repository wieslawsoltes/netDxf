using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterOpaqueObjectCode5Tests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"opaque-object-code5/roundtrip/{version}/{binary}", () => OpaqueCode5RoundTrip(version, binary));
        foreach (bool binary in new[] { false, true })
        {
            Run($"opaque-object-code5/strict-boundaries/{binary}", () => OpaqueCode5StrictBoundaries(binary));
            Run($"opaque-object-code5/graph-boundaries/{binary}", () => OpaqueCode5GraphBoundaries(binary));
            Run($"opaque-object-code5/legacy-codepage/{binary}", () => OpaqueCode5LegacyEncoding(binary));
            Run($"opaque-object-code5/legacy-unrepresentable/{binary}", () => OpaqueCode5Unrepresentable(binary));
        }
        Run("opaque-object-code5/snapshot-placement", OpaqueCode5SnapshotPlacement);
        Run("opaque-object-code5/binary-multiline", OpaqueCode5Multiline);
    }

    private static (DxfRawDocument Raw, string[] Handles, string Parent) OpaqueCode5Fixture(DxfVersion version, params string[] values)
    {
        var document = new DxfDocument(version);
        document.Entities.Add(new Line(Vector3.Zero, new Vector3(7, 3, 0)));
        document.Classes.Add(new DxfClass("SYNTHETIC_OBJECT", "AcmePayload", "SyntheticApplication")
        { ProxyFlags = 1024, WasProxy = true, IsEntity = false, InstanceCount = values.Length });
        var parent = new DxfDictionary(); document.NamedObjects.Add("OPAQUE_TEST", parent);
        var placeholders = values.Select((value, i) =>
        {
            var item = new DxfPlaceholder(); parent.Add("ITEM_" + i, item); return item;
        }).ToArray();
        using var bytes = new MemoryStream(); Check(document.Save(bytes), "Synthetic object scaffold save");
        bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        for (int i = 0; i < placeholders.Length; i++)
        {
            string handle = placeholders[i].Handle;
            var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r =>
                r.Tags.TakeWhile(t => t.Code != 100).Any(t => t.Code == 5 && Equals(t.Value, handle)));
            var tags = record.Tags.ToList(); tags[0] = new DxfTag(0, "SYNTHETIC_OBJECT");
            tags.AddRange(new DxfTag[]
            {
                new(100, "AcDbObject"), new(100, "AcmePayload"), DxfTag.CreateOpaqueObjectCode5(values[i]),
                new(102, "{ACME_REFERENCES"), new(102, "{NESTED"), new(5, "CD"), new(102, "}"),
                new(5, "EF"), new(102, "}"), DxfTag.CreateOpaqueObjectCode5("00aB"),
                new(1001, "ACAD"), new(1005, parent.Handle)
            });
            raw = raw.WithRecord(record, tags);
        }
        return (raw, placeholders.Select(p => p.Handle).ToArray(), parent.Handle);
    }

    private static byte[] OpaqueCode5Bytes(DxfRawDocument raw, bool binary)
    {
        using var bytes = new MemoryStream();
        raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(bytes, binary);
        return bytes.ToArray();
    }

    private static void OpaqueCode5RoundTrip(DxfVersion version, bool binary)
    {
        string[] values = { "", "authored label", "000aB", "  a0  ", "literal\\U+0041", "café" };
        var fixture = OpaqueCode5Fixture(version, values);
        byte[] input = OpaqueCode5Bytes(fixture.Raw, binary);
        using var rawStream = new MemoryStream(input); var raw = DxfRawDocument.Load(rawStream);
        using var exact = new MemoryStream(); raw.Save(exact);
        Check(input.SequenceEqual(exact.ToArray()), "Untouched opaque source bytes changed");
        using var stream = new MemoryStream(input);
        var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Synthetic opaque object load failed.");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var definition = document.Classes["SYNTHETIC_OBJECT"];
            Check(definition.CppClassName == "AcmePayload" && definition.ApplicationName == "SyntheticApplication" &&
                definition.ProxyFlags == 1024 && definition.WasProxy && !definition.IsEntity, "Opaque class declaration changed.");
            var parent = (DxfDictionary)document.NamedObjects["OPAQUE_TEST"];
            for (int i = 0; i < values.Length; i++)
            {
                var item = parent["ITEM_" + i] as DxfOpaqueObject ?? throw new InvalidOperationException("Custom object was interpreted as a known schema.");
                Equal(fixture.Handles[i], item.Handle, "Common object identity changed");
                Check(ReferenceEquals(item.Owner, parent), "Opaque ownership changed");
                var literal = item.Tags.Where(t => t.Code == 5 && t.ValueType == DxfTagValueType.String).ToArray();
                Equal(2, literal.Length, "Opaque group-5 string inventory");
                Equal(values[i], (string)literal[0].Value, "Opaque value spelling or Unicode escape changed");
                Equal("00aB", (string)literal[1].Value, "Hexadecimal-looking literal was normalized");
                Check(literal.All(t => t.HandleKind == DxfHandleKind.None), "A literal acquired handle semantics");
                Check(item.Tags.Where(t => t.Code == 5 && t.ValueType == DxfTagValueType.Handle).Select(t => (string)t.Value)
                    .SequenceEqual(new[] { "CD", "EF" }), "Control-group handles lost strict typing");
                Equal(parent.Handle, (string)item.Tags.Single(t => t.Code == 1005).Value, "XData handle changed");
            }
            Equal(1, document.Entities.Lines.Count(), "Unrelated model geometry changed");
            Equal(0, document.Objects.Validate().Count, "Opaque object database validation");
            using var saved = new MemoryStream(); Check(document.Save(saved, cycle % 2 == 0 ? !binary : binary), "Opaque object roundtrip save");
            saved.Position = 0; var stored = DxfRawDocument.Load(saved);
            foreach (string handle in fixture.Handles)
            {
                var record = stored.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r =>
                    r.Tags.TakeWhile(t => t.Code != 100).Any(t => t.Code == 5 && t.ValueType == DxfTagValueType.Handle && Equals(t.Value, handle)));
                Check(record.Tags.Count(t => t.Code == 5 && t.ValueType == DxfTagValueType.String) == 2, "Raw reload lost literal typing");
            }
            using var rewritten = new MemoryStream(); stored.WithTags(stored.Tags.Where(t => t.Code != 999)).Save(rewritten, binary);
            rewritten.Position = 0; document = DxfDocument.Load(rewritten) ?? throw new InvalidOperationException("Re-encoded opaque object reload failed.");
        }
    }

    // Independent transport encoding permits deliberately invalid group-5 values;
    // these tests do not rely on the validating production tag factories or writers.
    private static byte[] OpaqueCode5Wire(IEnumerable<(short Code, object Value)> pairs, bool binary)
    {
        using var stream = new MemoryStream(); var encoding = new UTF8Encoding(false, true);
        if (binary)
        {
            using var writer = new BinaryWriter(stream, encoding, true); writer.Write(BinarySentinel);
            foreach (var (code, value) in pairs)
            {
                if (code == 999) continue;
                writer.Write(code);
                switch (value)
                {
                    case string text: writer.Write(encoding.GetBytes(text)); writer.Write((byte)0); break;
                    case short number: writer.Write(number); break;
                    case int number: writer.Write(number); break;
                    case long number: writer.Write(number); break;
                    case double number: writer.Write(number); break;
                    case bool flag: writer.Write((byte)(flag ? 1 : 0)); break;
                    case byte[] data: writer.Write((byte)data.Length); writer.Write(data); break;
                    default: throw new InvalidOperationException("Unexpected synthetic transport value.");
                }
            }
        }
        else
        {
            var text = new StringBuilder();
            foreach (var (code, value) in pairs)
            {
                text.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append(value is bool flag ? flag ? "1" : "0" : value is byte[] bytes ? Convert.ToHexString(bytes) :
                    Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
            }
            stream.Write(encoding.GetBytes(text.ToString()));
        }
        return stream.ToArray();
    }

    private static void OpaqueCode5StrictBoundaries(bool binary)
    {
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2018, "valid literal");
        var record = fixture.Raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SYNTHETIC_OBJECT");
        var common = record.Tags.TakeWhile(t => t.Code != 100).Select(t => (t.Code, t.Value)).ToList();
        var variants = new List<(string Name, List<(short Code, object Value)> Tags)>();
        void Add(string name, params (short Code, object Value)[] body) => variants.Add((name, common.Concat(body).ToList()));
        Add("no-subclass", (5, "not a handle"));
        Add("base-subclass", (100, "AcDbObject"), (5, "not a handle"));
        Add("entity-base", (100, "AcDbEntity"), (5, "not a handle"));
        Add("control", (100, "AcmePayload"), (102, "{ACME"), (5, "not a handle"), (102, "}"));
        Add("nested-control", (100, "AcmePayload"), (102, "{ACME"), (102, "{NESTED"), (5, "not a handle"), (102, "}"), (102, "}"));
        Add("xdata", (100, "AcmePayload"), (1001, "ACAD"), (5, "not a handle"));
        Add("xdata-handle", (100, "AcmePayload"), (1001, "ACAD"), (1005, "not a handle"));
        Add("sort-subclass", (100, "AcDbSortentsTable"), (5, "not a handle"));
        Add("light-subclass", (100, "AcDbLightList"), (5, "not a handle"));
        Add("common-identity", (100, "AcmePayload"), (1, "payload"));
        int identity = variants[^1].Tags.FindIndex(t => t.Code == 5);
        variants[^1].Tags[identity] = (5, "not a handle");
        Add("missing-identity", (100, "AcmePayload"), (5, "not a handle"));
        variants[^1].Tags.RemoveAt(identity);
        foreach (string known in new[] { "DICTIONARY", "XRECORD", "SORTENTSTABLE", "LIGHTLIST", "TABLESTYLE", "FIELD", "LAYOUT" })
        {
            Add(known, (100, "AcmePayload"), (5, "not a handle"));
            variants[^1].Tags[0] = (0, known);
        }
        foreach (var variant in variants)
        {
            var pairs = fixture.Raw.Tags.Take(record.StartTagIndex).Select(t => (t.Code, t.Value)).Concat(variant.Tags)
                .Concat(fixture.Raw.Tags.Skip(record.EndTagIndex).Select(t => (t.Code, t.Value)));
            byte[] input = OpaqueCode5Wire(pairs, binary);
            using var raw = new MemoryStream(input);
            if (binary) Throws<InvalidDataException>(() => DxfRawDocument.Load(raw));
            else Throws<FormatException>(() => DxfRawDocument.Load(raw));
            using var typed = new MemoryStream(input); bool rejected;
            try { rejected = DxfDocument.Load(typed) == null; }
            catch (FormatException) { rejected = true; }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Typed loader admitted invalid group-5 context: " + variant.Name);
        }
    }

    private static void OpaqueCode5GraphBoundaries(bool binary)
    {
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2018, "000aB");
        var store = DxfRawObjectStore.Open(fixture.Raw);
        Check(store.Get(fixture.Handles[0]) is DxfRawOpaqueStoredObject, "Raw store interpreted a private object");
        var index = DxfRawHandleIndex.Create(fixture.Raw);
        Throws<InvalidOperationException>(() => index.RemapHandles(new Dictionary<string, string> { [fixture.Parent] = "FFF000" }));
        Check(ReferenceEquals(fixture.Raw, index.RemapHandles(new Dictionary<string, string> { [fixture.Parent] = fixture.Parent })), "Numeric no-op remap changed the snapshot");
        using var edit = store.BeginEdit();
        Throws<NotSupportedException>(() => edit.CloneDictionaryTree(fixture.Parent, store.RootDictionary!.Handle, "COPY"));
        Throws<NotSupportedException>(() => edit.RemoveEntry(store.RootDictionary!.Handle, "OPAQUE_TEST", true));
        edit.CreateVariable(store.RootDictionary!.Handle, "UNRELATED_NOTE", "retained structural edit");
        var edited = edit.Commit();
        SameRawTags(store.Get(fixture.Handles[0]).Tags, DxfRawObjectStore.Open(edited).Get(fixture.Handles[0]).Tags);
        using var bytes = new MemoryStream(OpaqueCode5Bytes(edited, binary));
        var document = DxfDocument.Load(bytes) ?? throw new InvalidOperationException("Opaque structural edit reload failed.");
        var parent = (DxfDictionary)document.NamedObjects["OPAQUE_TEST"];
        var target = new DxfDocument();
        Throws<NotSupportedException>(() => target.Objects.Clone(parent, target.Objects.Root, "COPY"));
        Throws<NotSupportedException>(() => document.Objects.EraseOwnedTree(parent));
        Equal("retained structural edit", ((DxfDictionaryVariable)document.NamedObjects["UNRELATED_NOTE"]).Value, "Unrelated raw edit was lost");
    }

    private static void OpaqueCode5SnapshotPlacement()
    {
        Throws<ArgumentNullException>(() => DxfTag.CreateOpaqueObjectCode5(null!));
        Throws<ArgumentException>(() => DxfTag.CreateOpaqueObjectCode5("a\0b"));
        Throws<ArgumentException>(() => new DxfTag(5, "literal"));
        Equal(DxfTagValueType.Handle, DxfGroupCode.GetValueType(5), "Context-free handle classification changed");
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2018, "00aB");
        var record = fixture.Raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SYNTHETIC_OBJECT");
        var tags = record.Tags.ToList();
        int literal = tags.FindIndex(t => t.Code == 5 && t.ValueType == DxfTagValueType.String);
        tags[literal] = new DxfTag(5, "AB");
        Throws<ArgumentException>(() => fixture.Raw.WithRecord(record, tags));
        tags = record.Tags.ToList(); tags[tags.FindIndex(t => t.Code == 5)] = DxfTag.CreateOpaqueObjectCode5("AB");
        Throws<ArgumentException>(() => fixture.Raw.WithRecord(record, tags));
        tags = record.Tags.ToList(); tags[0] = new DxfTag(0, "SORTENTSTABLE");
        Throws<ArgumentException>(() => fixture.Raw.WithRecord(record, tags));
        tags = record.Tags.ToList(); tags[tags.FindIndex(t => t.Code == 5 && Equals(t.Value, "CD"))] = DxfTag.CreateOpaqueObjectCode5("CD");
        Throws<ArgumentException>(() => fixture.Raw.WithRecord(record, tags));
    }

    private static void OpaqueCode5Multiline()
    {
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2018, "first\r\nsecond");
        using var input = new MemoryStream(OpaqueCode5Bytes(fixture.Raw, true));
        var raw = DxfRawDocument.Load(input);
        using var destination = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Throws<NotSupportedException>(() => raw.Save(destination, false));
        Check(destination.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Text preflight modified the destination");
        input.Position = 0;
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Binary literal load failed.");
        var item = (DxfOpaqueObject)((DxfDictionary)document.NamedObjects["OPAQUE_TEST"])["ITEM_0"];
        Equal("first\r\nsecond", (string)item.Tags.First(t => t.Code == 5).Value, "Binary literal line breaks changed");
        using var output = new MemoryStream(); Check(document.Save(output, true), "Binary literal save failed");
    }

    private static void OpaqueCode5LegacyEncoding(bool binary)
    {
        const string literal = "ěxposé\\U+0041";
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2004, literal);
        var tags = fixture.Raw.Tags.ToList();
        int codepage = tags.FindIndex(tag => tag.Code == 9 && Equals(tag.Value, "$DWGCODEPAGE")) + 1;
        tags[codepage] = new DxfTag(3, "ANSI_1250");
        var raw = DxfRawDocument.Create(tags);
        using var input = new MemoryStream(OpaqueCode5Bytes(raw, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Declared legacy codepage load failed.");
        document.Entities.Add(new Text("漢字 café", Vector3.Zero, 1));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var item = document.Objects.Items.OfType<DxfOpaqueObject>().Single();
            Equal(literal, (string)item.Tags.First(tag => tag.Code == 5 && tag.ValueType == DxfTagValueType.String).Value, "Legacy literal text or escape spelling changed.");
            Equal("漢字 café", document.Entities.Texts.Single().Value, "Known Unicode text was not independently escaped.");
            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Declared legacy codepage save failed.");
            Check(output.CanRead && output.CanWrite, "Legacy writer closed the caller stream.");
            output.Position = 0; var stored = DxfRawDocument.Load(output);
            Equal(1250, stored.EncodingCodePage, "Legacy output does not match its declared codepage.");
            Equal(literal, (string)stored.Tags.First(tag => tag.Code == 5 && tag.ValueType == DxfTagValueType.String).Value, "Raw legacy literal changed after native output.");
            output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Declared legacy codepage reload failed.");
        }
        Check(input.CanRead, "Legacy literal reader closed the caller stream.");
    }

    private static void OpaqueCode5Unrepresentable(bool binary)
    {
        const string literal = "漢字\\U+0041";
        var fixture = OpaqueCode5Fixture(DxfVersion.AutoCad2018, literal);
        using var input = new MemoryStream(OpaqueCode5Bytes(fixture.Raw, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Unicode literal fixture load failed.");
        document.DrawingVariables.AcadVer = DxfVersion.AutoCad2004;
        var identities = document.Objects.Items.Select(item => item.Handle).ToArray();
        string seed = document.DrawingVariables.HandleSeed;
        using var output = new MemoryStream(); output.Write(new byte[] { 1, 2, 3, 4 }); output.Position = 2;
        bool rejected;
        try { rejected = !document.Save(output, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Unrepresentable opaque Unicode was silently rewritten for a legacy codepage.");
        Check(output.ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 }) && output.Position == 2, "Legacy literal preflight changed the destination.");
        Check(identities.SequenceEqual(document.Objects.Items.Select(item => item.Handle)), "Legacy literal preflight allocated object handles.");
        Equal(seed, document.DrawingVariables.HandleSeed, "Legacy literal preflight changed the handle seed.");
        document.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        using var retry = new MemoryStream(); Check(document.Save(retry, binary), "Modern retry of exact literal text failed.");
        retry.Position = 0; var stored = DxfRawDocument.Load(retry);
        Equal(literal, (string)stored.Tags.First(tag => tag.Code == 5 && tag.ValueType == DxfTagValueType.String).Value, "Modern retry changed literal escape spelling.");
    }
}
