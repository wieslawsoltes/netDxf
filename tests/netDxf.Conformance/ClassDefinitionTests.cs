using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterClassDefinitionTests()
    {
        Run("classes/model", ClassDefinitionModel);
        Run("classes/collection", ClassDefinitionCollection);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"classes/independent-input/{v}/{b}", () => ClassDefinitionInput(v, b));
                Run($"classes/defaults/{v}/{b}", () => ClassDefinitionDefaults(v, b));
                Run($"classes/edit-clone-roundtrip/{v}/{b}", () => ClassDefinitionRoundTrip(v, b));
                Run($"classes/generated-raster/{v}/{b}", () => ClassDefinitionGenerated(v, b));
                Run($"classes/unused-declarations/{v}/{b}", () => ClassDefinitionUnused(v, b));
                for (int scenario = 0; scenario < 9; scenario++)
                {
                    int s = scenario;
                    Run($"classes/malformed/{v}/{b}/{s}", () => ClassDefinitionMalformed(v, b, s));
                }
                for (int scenario = 0; scenario < 3; scenario++)
                {
                    int s = scenario;
                    Run($"classes/conflict-preflight/{v}/{b}/{s}", () => ClassDefinitionConflict(v, b, s));
                }
            }
    }

    private static void ClassDefinitionModel()
    {
        var c = new DxfClass("CUSTOM", "AcDbCustom", "");
        Equal(0, c.ProxyFlags, "Default proxy flags"); Check(c.InstanceCount == null && !c.WasProxy && !c.IsEntity, "Default CLASS metadata");
        foreach (string invalid in new[] { "", " ", "bad\0name", "bad\rname", "bad\nname" })
        {
            Throws<ArgumentException>(() => new DxfClass(invalid, "Cpp", "App"));
            Throws<ArgumentException>(() => new DxfClass("Name", invalid, "App"));
        }
        Throws<ArgumentNullException>(() => new DxfClass(null!, "Cpp", "App"));
        Throws<ArgumentNullException>(() => new DxfClass("Name", null!, "App"));
        Throws<ArgumentNullException>(() => c.ApplicationName = null!);
        Throws<ArgumentException>(() => c.ApplicationName = "bad\0app");
        foreach (int invalid in new[] { -1, int.MinValue }) Throws<ArgumentOutOfRangeException>(() => c.InstanceCount = invalid);
        c.InstanceCount = int.MaxValue; c.ProxyFlags = int.MinValue; c.WasProxy = true; c.IsEntity = true;
        var clone = (DxfClass)c.Clone(); CheckClass(c, clone);
        clone.ApplicationName = "changed"; clone.InstanceCount = null; clone.ProxyFlags = -1; clone.WasProxy = false;
        Equal("", c.ApplicationName, "Clone shared mutable state"); Equal((int?)int.MaxValue, c.InstanceCount, "Clone count isolation");
        Check(c.WasProxy, "Clone flag isolation"); Equal(int.MinValue, c.ProxyFlags, "Clone flags isolation");
        Check(!typeof(DxfObject).IsAssignableFrom(typeof(DxfClass)), "CLASS must not participate in handle allocation.");
    }

    private static void ClassDefinitionCollection()
    {
        var classes = new DxfClassCollection();
        var first = new DxfClass("FIRST", "CppFirst", "App"); var second = new DxfClass("SECOND", "CppSecond", "App");
        classes.Add(first); classes.Add(second);
        Check(ReferenceEquals(first, classes["FIRST"]), "DXF-name lookup");
        Throws<ArgumentException>(() => classes.Add(new DxfClass("FIRST", "DifferentCpp", "App")));
        Throws<ArgumentException>(() => classes.Add(new DxfClass("Different", "CppFirst", "App")));
        Throws<ArgumentNullException>(() => classes.Add(null!));
        Throws<ArgumentException>(() => classes[1] = new DxfClass("THIRD", "CppFirst", "App"));
        Throws<ArgumentException>(() => classes[1] = new DxfClass("FIRST", "CppThird", "App"));
        Equal(2, classes.Count, "Failed additions changed collection"); Check(ReferenceEquals(second, classes[1]), "Failed replacement changed collection");
        classes[0] = new DxfClass("REPLACED", "CppFirst", "New app");
        Check(!classes.Contains("FIRST") && classes.Contains("REPLACED"), "Replacement did not update keys");
        Check(classes.Remove("REPLACED"), "Remove by name"); classes.Add(first);
        Check(ReferenceEquals(first, classes[1]), "Re-add order"); classes.Clear();
        classes.Add(new DxfClass("SECOND", "CppSecond", "Again")); Equal(1, classes.Count, "Clear left stale identity state");
    }

    private static DxfClass ClassSample(string name = "VENDOR_ENTITY", string cpp = "AcDbVendorEntity") => new(name, cpp, "Plugin Zażółć 東京")
    {
        ProxyFlags = unchecked((int)0x800083FF), InstanceCount = 17, WasProxy = true, IsEntity = true
    };

    private static IEnumerable<(short Code, object Value)> ClassSampleTags(DxfClass c)
    {
        // Required identity fields deliberately follow metadata; no fixed ordering is assumed.
        yield return (281, (short)(c.IsEntity ? 1 : 0)); yield return (90, c.ProxyFlags);
        yield return (3, c.ApplicationName); yield return (280, (short)(c.WasProxy ? 1 : 0));
        if (c.InstanceCount.HasValue) yield return (91, c.InstanceCount.Value);
        yield return (2, c.CppClassName); yield return (1, c.Name);
    }

    private static MemoryStream ClassFixture(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> tags, bool terminated = true)
    {
        var stream = new MemoryStream(); object writer = NewCodeWriter(stream, binary);
        void T(short code, object value) => Invoke(writer, "Write", code, value);
        T(0, "SECTION"); T(2, "HEADER"); T(9, "$ACADVER"); T(1, HeaderVersion(version));
        T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252"); T(0, "ENDSEC"); T(0, "SECTION"); T(2, "CLASSES");
        if (!binary) T(999, "ENDSEC");
        T(0, "CLASS");
        foreach (var (code, value) in tags)
        {
            T(code, version < DxfVersion.AutoCad2007 && value is string s ? HeaderEscape(s) : value);
            if (!binary) T(999, "EOF");
        }
        if (terminated) { T(0, "ENDSEC"); T(0, "SECTION"); T(2, "ENTITIES"); T(0, "ENDSEC"); }
        T(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0; return stream;
    }

    private static void CheckClass(DxfClass expected, DxfClass actual)
    {
        Equal(expected.Name, actual.Name, "Class DXF name"); Equal(expected.CppClassName, actual.CppClassName, "Class C++ name");
        Equal(expected.ApplicationName, actual.ApplicationName, "Class application"); Equal(expected.ProxyFlags, actual.ProxyFlags, "Class proxy flags");
        Equal(expected.InstanceCount, actual.InstanceCount, "Class count metadata"); Equal(expected.WasProxy, actual.WasProxy, "Class proxy state");
        Equal(expected.IsEntity, actual.IsEntity, "Class entity classification");
    }

    private static void ClassDefinitionInput(DxfVersion version, bool binary)
    {
        foreach (string name in new[] { "VENDOR_ENTITY", "ENDSEC", "EOF" })
        {
            DxfClass expected = ClassSample(name);
            using var input = ClassFixture(version, binary, ClassSampleTags(expected));
            var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("CLASS fixture failed to load.");
            Equal(1, loaded.Classes.Count, "CLASS input discarded"); CheckClass(expected, loaded.Classes[0]);
            Check(input.CanRead, "CLASS input closed caller stream.");
        }
    }

    private static void ClassDefinitionDefaults(DxfVersion version, bool binary)
    {
        using var input = ClassFixture(version, binary, new (short, object)[] { (1, "MINIMAL"), (2, "CppMinimal") });
        var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Minimal CLASS input failed.");
        Equal(1, loaded.Classes.Count, "Minimal CLASS missing"); CheckClass(new DxfClass("MINIMAL", "CppMinimal", ""), loaded.Classes[0]);
    }

    private static List<Dictionary<short, object>> RawClasses(byte[] bytes, bool binary)
    {
        using var input = new MemoryStream(bytes); object reader = NewCodeReader(input, binary);
        var records = new List<Dictionary<short, object>>(); Dictionary<short, object>? current = null;
        bool classes = false, section = false;
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader); object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0)
            {
                if (current != null) { records.Add(current); current = null; }
                if (Equals(value, "EOF")) return records;
                if (Equals(value, "SECTION")) { section = true; continue; }
                if (Equals(value, "ENDSEC")) { classes = false; continue; }
                if (classes) { Equal("CLASS", (string)value, "CLASSES record type"); current = new Dictionary<short, object>(); }
            }
            if (section && code == 2) { classes = Equals(value, "CLASSES"); section = false; }
            if (current != null && code != 999) current.Add(code, value);
        }
    }

    private static void ClassDefinitionRoundTrip(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); DxfClass first = ClassSample();
        DxfClass second = (DxfClass)new DxfClass("VENDOR_OBJECT", "CppVendorObject", "").Clone();
        second.ProxyFlags = 1024; second.InstanceCount = null;
        doc.Classes.Add(first); doc.Classes.Add(second);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            bool transport = cycle == 1 ? !binary : binary;
            using var output = new MemoryStream(); Check(doc.Save(output, transport), "CLASS save failed.");
            var wire = RawClasses(output.ToArray(), transport);
            Equal(3, wire.Count, "Custom and generated CLASS reconciliation");
            for (int i = 0; i < 2; i++)
            {
                DxfClass c = doc.Classes[i]; var tags = wire[i];
                Equal(c.Name, (string)tags[1], "CLASS output ordering"); Equal(c.CppClassName, (string)tags[2], "CLASS output C++");
                Equal(version < DxfVersion.AutoCad2007 ? HeaderEscape(c.ApplicationName) : c.ApplicationName, (string)tags[3], "CLASS wire encoding");
                Equal(c.ProxyFlags, (int)tags[90], "CLASS raw bit mask"); Equal((short)(c.WasProxy ? 1 : 0), (short)tags[280], "CLASS proxy flag type");
                Equal((short)(c.IsEntity ? 1 : 0), (short)tags[281], "CLASS entity flag type");
                Equal(version > DxfVersion.AutoCad2000 && c.InstanceCount.HasValue, tags.ContainsKey(91), "CLASS count version/absence");
                Check(!tags.ContainsKey(5) && !tags.ContainsKey(330), "CLASS acquired handle/owner fields.");
            }
            CheckClass(first, doc.Classes[0]);
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"classes-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("CLASS reload failed.");
            if (version == DxfVersion.AutoCad2000) first.InstanceCount = null;
            Equal(3, doc.Classes.Count, "Reload lost CLASS records"); CheckClass(first, doc.Classes[0]); CheckClass(second, doc.Classes[1]);
            doc.Classes[0].ApplicationName = "Edited Ł 東京"; first.ApplicationName = "Edited Ł 東京";
        }
        Check(doc.Classes.Remove("VENDOR_ENTITY"), "Remove custom definition");
        using var removed = new MemoryStream(); Check(doc.Save(removed, binary), "Save after CLASS removal failed.");
        Check(RawClasses(removed.ToArray(), binary).All(r => !Equals(r[1], "VENDOR_ENTITY")), "Removed CLASS returned.");
    }

    private static DxfDocument ClassRasterDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        var definition = new ImageDefinition("ClassImage", "not-opened.png", 16, 96, 8, 96, ImageResolutionUnits.Inches);
        doc.Entities.Add(new Image(definition, Vector3.Zero, 16, 8));
        var block = new Block("ImageBlock"); block.Entities.Add(new Image(definition, Vector3.Zero, 16, 8));
        doc.Entities.Add(new Insert(block)); doc.Entities.Add(new Insert(block, new Vector3(5, 5, 0)));
        return doc;
    }

    private static void ClassDefinitionGenerated(DxfVersion version, bool binary)
    {
        DxfDocument doc = ClassRasterDocument(version);
        doc.Classes.Add(new DxfClass("IMAGE", "AcDbRasterImage", "Producer metadata") { ProxyFlags = 1023, WasProxy = true, IsEntity = true, InstanceCount = 999 });
        for (int cycle = 0; cycle < 2; cycle++)
        {
            int before = doc.Classes.Count;
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Raster CLASS reconciliation failed.");
            Equal(before, doc.Classes.Count, "Saving mutated source CLASS collection");
            var raw = RawClasses(stream.ToArray(), binary);
            Equal(4, raw.Count, "Generated CLASS count");
            var image = raw.Single(r => Equals(r[1], "IMAGE")); Equal(1023, (int)image[90], "Generated class overwrote source proxy permissions");
            Equal("Producer metadata", (string)image[3], "Generated class overwrote application metadata");
            foreach (var (name, count) in new[] { ("IMAGE", 2), ("IMAGEDEF", 1), ("IMAGEDEF_REACTOR", 2), ("RASTERVARIABLES", 1) })
            {
                var tags = raw.Single(r => Equals(r[1], name));
                if (version > DxfVersion.AutoCad2000) Equal(count, (int)tags[91], "Generated class instance count " + name);
                else Check(!tags.ContainsKey(91), "2000 generated CLASS emitted count tag.");
            }
            if (cycle == 0) Equal((int?)999, doc.Classes["IMAGE"].InstanceCount, "Count reconciliation mutated source metadata");
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Raster CLASS reload failed.");
        }
    }

    private static void ClassDefinitionUnused(DxfVersion version, bool binary)
    {
        DxfDocument doc = ClassRasterDocument(version);
        using var source = new MemoryStream(); Check(doc.Save(source, binary), "Unused CLASS source failed."); source.Position = 0;
        var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Unused CLASS load failed.");
        var empty = new DxfDocument(version); foreach (DxfClass c in loaded.Classes) empty.Classes.Add((DxfClass)c.Clone());
        using var output = new MemoryStream(); Check(empty.Save(output, binary), "Unused CLASS output failed.");
        var raw = RawClasses(output.ToArray(), binary); Equal(4, raw.Count, "Unused definitions discarded.");
        if (version > DxfVersion.AutoCad2000)
            foreach (string name in new[] { "IMAGE", "IMAGEDEF", "IMAGEDEF_REACTOR" })
                Equal(0, (int)raw.Single(r => Equals(r[1], name))[91], "Unused generated type retained stale count");
    }

    private static void ClassDefinitionMalformed(DxfVersion version, bool binary, int scenario)
    {
        var tags = ClassSampleTags(ClassSample()).ToList(); bool terminated = true;
        switch (scenario)
        {
            case 0: tags.RemoveAll(t => t.Code == 1); break;
            case 1: tags.RemoveAll(t => t.Code == 2); break;
            case 2: tags.Add((280, (short)2)); break;
            case 3: tags.Add((281, (short)(-1))); break;
            case 4: tags.Add((91, -1)); break;
            case 5: tags.Add((3, "bad\\U+0000name")); break;
            case 6: tags.Add((0, "CLASS")); tags.AddRange(ClassSampleTags(ClassSample("VENDOR_ENTITY", "OtherCpp"))); break;
            case 7: tags.Add((0, "CLASS")); tags.AddRange(ClassSampleTags(ClassSample("OTHER", "AcDbVendorEntity"))); break;
            default: terminated = false; break;
        }
        using var input = ClassFixture(version, binary, tags, terminated);
#if DEBUG
        if (terminated) Throws<InvalidDataException>(() => DxfDocument.Load(input));
        else Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed CLASS was accepted.");
#endif
        Check(input.CanRead, "Malformed CLASS closed caller input.");
    }

    private static void ClassDefinitionConflict(DxfVersion version, bool binary, int scenario)
    {
        var doc = new DxfDocument(version);
        var definition = scenario switch
        {
            0 => new DxfClass("RASTERVARIABLES", "WrongCpp", "App"),
            1 => new DxfClass("RASTERVARIABLES", "AcDbRasterVariables", "App") { IsEntity = true },
            _ => new DxfClass("CUSTOM", "AcDbRasterVariables", "App")
        };
        doc.Classes.Add(definition); int layouts = doc.Layouts.Count;
        using var stream = new MemoryStream(); byte[] sentinel = { 7, 8, 9 }; stream.Write(sentinel); long position = stream.Position;
#if DEBUG
        if (scenario == 2) Throws<ArgumentException>(() => doc.Save(stream, binary));
        else Throws<InvalidDataException>(() => doc.Save(stream, binary));
#else
        Check(!doc.Save(stream, binary), "Conflicting CLASS allowed invalid export.");
#endif
        Check(sentinel.SequenceEqual(stream.ToArray()), "CLASS preflight wrote partial data.");
        Equal(position, stream.Position, "CLASS preflight changed stream position"); Equal(layouts, doc.Layouts.Count, "CLASS preflight changed layouts");
        Check(ReferenceEquals(definition, doc.Classes.Single()), "CLASS preflight mutated source collection.");
        Check(stream.CanWrite, "CLASS preflight closed caller stream.");
    }
}
