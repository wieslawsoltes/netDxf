using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawEmbeddedHandleTests()
    {
        foreach (DxfVersion version in HandleProfiles)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            foreach (string type in new[] { "MTEXT", "ATTRIB", "ATTDEF", "VENDOR_ENTITY" })
            foreach (bool controls in new[] { false, true })
            {
                string t = type; bool c = controls;
                Run($"handles/embedded/context/{v}/{b}/{t}/{c}", () => EmbeddedHandleContext(v, b, t, c));
            }
            Run($"handles/embedded/unsafe-remap/{v}/{b}", () => EmbeddedHandleUnsafeRemap(v, b));
            foreach (int context in Enumerable.Range(0, 4))
            {
                int c = context;
                Run($"handles/embedded/context-boundary/{v}/{b}/{c}", () => EmbeddedHandleBoundary(v, b, c));
            }
        }
    }

    private static DxfRawDocument EmbeddedHandleLoad(DxfVersion version, bool binary, List<DxfTag> tags)
    {
        // Independent wire encoders, not a production writer, construct the input.
        byte[] bytes = version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary);
        return LoadRaw(bytes);
    }

    private static List<DxfTag> EmbeddedHandleTags(DxfVersion version, string type, bool controls)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HandleProfileName(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(9, "$HANDSEED"), new(5, "1000"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "OBJECTS"),
            new(0, "DICTIONARY"), new(5, "10"), new(330, "0"), new(100, "AcDbDictionary"),
            new(0, "DICTIONARY"), new(5, "20"), new(330, "10"), new(100, "AcDbDictionary"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, type), new(5, "A"),
            new(102, "{ACAD_REACTORS"), new(330, "20"), new(102, "}"),
            new(330, "10"), new(100, "AcDbEntity"), new(100, "AcDbMText"), new(340, "20"),
            new(101, "Embedded Object"), new(70, (short)1), new(330, "FA"), new(340, "FE"),
            new(360, "20"), new(5, "30"), new(320, "40"),
            new(1001, "EMBEDDED_APP"), new(1005, "20")
        };
        if (controls) tags.AddRange(new DxfTag[]
        {
            new(102, "{ACAD_REACTORS"), new(330, "20"), new(102, "}"),
            new(102, "application payload, not a control"), new(100, "AcDbEntity"),
            new(101, "Embedded Object"), new(5, "A"), new(360, "FE")
        });
        tags.AddRange(new DxfTag[]
        {
            new(0, "POINT"), new(5, "B"), new(330, "10"), new(100, "AcDbEntity"), new(100, "AcDbPoint"),
            new(10, 1.0), new(20, 2.0), new(30, 3.0), new(1001, "REAL_XDATA"), new(1005, "20"),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void EmbeddedHandleContext(DxfVersion version, bool binary, string type, bool controls)
    {
        var raw = EmbeddedHandleLoad(version, binary, EmbeddedHandleTags(version, type, controls));
        byte[] before = SaveRaw(raw);
        var index = DxfRawHandleIndex.Create(raw);
        var record = index.FindDefinitions("A").Single().Record;
        int start = raw.Tags.ToList().FindIndex(t => t.Code == 101);
        var expected = Enumerable.Range(start + 1, record.EndTagIndex - start - 1)
            .Where(i => raw.Tags[i].ValueType == DxfTagValueType.Handle).ToArray();
        var opaque = index.GetOccurrences(record).Where(x => x.Role == DxfRawHandleRole.Opaque).ToArray();
        Check(expected.SequenceEqual(opaque.Select(x => x.TagIndex)), "Embedded handles were interpreted as outer references.");
        Check(opaque.All(x => x.Context == "Embedded Object"), "Missing embedded-object context.");
        Equal(0, index.Diagnostics.Count, "Opaque data manufactured structural diagnostics");
        Equal(1, index.GetOccurrences(record).Count(x => x.Role == DxfRawHandleRole.Owner), "Embedded owner collision");
        Equal(1, index.GetOccurrences(record).Count(x => x.Role == DxfRawHandleRole.Reactor), "Embedded reactor collision");
        Equal(0, index.FindDefinitions("30").Count, "Embedded identity was indexed");
        Equal(0, index.FindReferences("FA").Count, "Embedded data became a dangling reference");
        var point = index.FindDefinitions("B").Single().Record;
        Equal("REAL_XDATA", index.GetOccurrences(point).Single(x => x.Role == DxfRawHandleRole.XData).Context,
            "Embedded state leaked to the following record");
        var closure = index.GetDependencyClosure(new[] { record }, DxfRawReferenceTraversal.All);
        Equal(3, closure.Records.Count, "Closure followed embedded data");
        Check(closure.AreSelectedReferencesResolved, "Opaque data polluted selected-reference resolution");
        Check(expected.SequenceEqual(closure.UninterpretedHandles.Select(x => x.TagIndex)), "Closure hid opaque evidence");
        var changed = index.RemapHandles(new Dictionary<string, string> { ["B"] = "B1" });
        foreach (int position in expected) Check(ReferenceEquals(raw.Tags[position], changed.Tags[position]), "Unrelated remap copied embedded tags.");
        Check(before.SequenceEqual(SaveRaw(raw)), "Index or remap mutated original bytes.");
        SameRawTags(changed.Tags, LoadRaw(SaveRaw(changed, !binary)).Tags);
        if (version == DxfVersion.AutoCad2018 && type == "MTEXT")
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"handles-embedded-{binary}-{controls}.dxf"), SaveRaw(changed));
    }

    private static void EmbeddedHandleUnsafeRemap(DxfVersion version, bool binary)
    {
        var raw = EmbeddedHandleLoad(version, binary, EmbeddedHandleTags(version, "MTEXT", false));
        var index = DxfRawHandleIndex.Create(raw);
        byte[] original = SaveRaw(raw);
        try
        {
            index.RemapHandles(new Dictionary<string, string> { ["20"] = "200" });
            throw new InvalidDataException("An affected embedded-object slot was rewritten without its application schema.");
        }
        catch (InvalidOperationException error)
        {
            Check(error.Message.Contains("opaque", StringComparison.OrdinalIgnoreCase), "Wrong remap rejection reason.");
        }
        Check(original.SequenceEqual(SaveRaw(raw)), "Failed embedded remapping mutated source bytes.");
        Check(ReferenceEquals(raw, index.RemapHandles(new Dictionary<string, string> { ["20"] = "0020" })), "Numeric no-op rejected unnecessarily.");
    }

    private static void EmbeddedHandleBoundary(DxfVersion version, bool binary, int context)
    {
        var tags = EmbeddedHandleTags(version, "MTEXT", false);
        int start = tags.FindIndex(t => t.Code == 101), end = tags.FindIndex(start, t => t.Code == 0);
        tags.RemoveRange(start, end - start);
        if (context == 0)
        {
            tags.InsertRange(start, new DxfTag[] { new(102, "{VENDOR"), new(101, "Embedded Object"), new(330, "FA"),
                new(102, "}"), new(340, "20") });
        }
        else if (context == 1)
        {
            int at = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "DICTIONARY"));
            tags[at] = new(0, "XRECORD");
            tags[at + 3] = new(100, "AcDbXrecord");
            tags.InsertRange(at + 4, new DxfTag[] { new(101, "Embedded Object"), new(330, "FA"), new(1001, "XREC"), new(1005, "20") });
        }
        else if (context == 2)
        {
            tags.InsertRange(start, new DxfTag[] { new(101, "not an embedded object"), new(340, "20") });
        }
        else
        {
            tags.InsertRange(start, new DxfTag[] { new(102, "}"), new(101, "Embedded Object"), new(330, "FA") });
        }
        var index = DxfRawHandleIndex.Create(EmbeddedHandleLoad(version, binary, tags));
        if (context == 3)
        {
            Equal(1, index.Diagnostics.Count(x => x.Kind == DxfRawHandleDiagnosticKind.InvalidControlGroup), "Invalid outer control was hidden");
        }
        else
        {
            Equal(0, index.Diagnostics.Count, "Nested/non-marker payload changed outer framing");
            Equal(1, index.FindDefinitions("A").Count, "Outer identity lost");
            if (context is 0 or 2)
                Equal(2, index.GetOccurrences(index.FindDefinitions("A").Single().Record).Count(x => x.Role == DxfRawHandleRole.HardPointer), "Embedded-state escape from nested/non-marker value");
            if (context == 1)
                Equal("XREC", index.GetOccurrences(index.FindDefinitions("10").Single().Record).Single(x => x.Role == DxfRawHandleRole.XData).Context,
                    "Embedded-like XRECORD payload altered the prior XData policy");
        }
    }
}
