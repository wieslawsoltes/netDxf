using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunSourceAmbiguityTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (string placement in new[] { "native", "before", "after", "private" })
                Run($"source-ambiguity/section-manager/{placement}/{binary}", () => SourceAmbiguityManager(placement, binary));
            foreach (string placement in new[] { "native", "duplicate-entity", "private", "nested-private", "nested-standard-lookalikes", "unbalanced-private" })
                Run($"source-ambiguity/consumed-layer-dictionary/{placement}/{binary}", () => SourceAmbiguityLayerDictionary(placement, binary));
        }
    }

    private static IEnumerable<DxfTag> SourceAmbiguityDecoy(string handle)
    {
        // This physical record is discarded by the typed entity parser. Its common
        // identity still makes a second declaration ambiguous, regardless of order.
        return new[] { new DxfTag(0, "FUTURE_SOURCE_ENTITY"),
            new DxfTag(5, handle.ToLowerInvariant().PadLeft(12, '0')),
            new DxfTag(100, "AcDbEntity"), new DxfTag(8, "0"),
            new DxfTag(100, "AcDbFutureSourceEntity") };
    }

    private static DxfRawDocument SourceAmbiguityPrivateHandle(DxfRawDocument raw, DxfRawRecord record, string handle, bool nested)
    {
        var tags = record.Tags.ToList();
        var control = new List<DxfTag> { new DxfTag(102, "{PRIVATE_SOURCE") };
        if (nested) control.AddRange(new[] { new DxfTag(102, "{NESTED"), new DxfTag(102, "}") });
        control.Add(new DxfTag(5, handle)); control.Add(new DxfTag(102, "}"));
        tags.InsertRange(tags.FindIndex(tag => tag.Code == 100), control);
        return raw.WithRecord(record, tags);
    }

    private static DxfDocument? SourceAmbiguityLoad(DxfRawDocument raw, bool binary, bool reject)
    {
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        DxfDocument? loaded = null;
        bool rejected = false;
        try { loaded = DxfDocument.Load(input); rejected = loaded == null; }
        catch (Exception error) when (reject && (error is FormatException or InvalidDataException)) { rejected = true; }
        Check(input.CanRead, "Source ambiguity handling closed the caller stream");
        Equal(reject, rejected, "Ambiguous physical source declarations must reject; private handle fields must not");
        return loaded;
    }

    private static void SourceAmbiguityManager(string placement, bool binary)
    {
        using var source = new MemoryStream(SectionManagerNativeBytes());
        var raw = DxfRawDocument.Load(source);
        var section = SourceReferenceRecord(raw, "228");
        if (placement is "before" or "after")
        {
            int index = section.StartTagIndex + (placement == "after" ? section.Tags.Count : 0);
            raw = DxfRawDocument.Create(raw.Tags.Take(index).Concat(SourceAmbiguityDecoy("228")).Concat(raw.Tags.Skip(index)));
        }
        else if (placement == "private") raw = SourceAmbiguityPrivateHandle(raw, section, "228", true);

        var doc = SourceAmbiguityLoad(raw, binary, placement is "before" or "after");
        if (doc == null) return;
        var manager = (DxfStoredSectionManager)doc.GetObjectByHandle("229");
        Check(ReferenceEquals(manager.Sections.Single(), doc.GetObjectByHandle("228")), "Manager lost its actual physical section identity");
        Equal(0, doc.Objects.Validate().Count, "Unambiguous manager graph validation");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Unambiguous manager cross-transport save");
        output.Position = 0; var again = DxfDocument.Load(output) ?? throw new FormatException("Unambiguous manager reload rejected");
        Check(ReferenceEquals(((DxfStoredSectionManager)again.GetObjectByHandle("229")).Sections.Single(), again.GetObjectByHandle("228")),
            "Manager target identity changed after cross-transport save");
    }

    private static void SourceAmbiguityLayerDictionary(string placement, bool binary)
    {
        var seed = new DxfDocument(DxfVersion.AutoCad2018);
        seed.Layers.StateManager.AddNew("AMBIGUITY_SNAPSHOT");
        seed.NamedObjects.Add("AMBIGUITY_APP", new DxfDictionaryVariable { Value = "retained alongside layer-state conversion" });
        using var bytes = new MemoryStream(); Check(seed.Save(bytes, binary), "Layer-state source seed save"); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes);
        var table = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record =>
            record.Name == "TABLE" && record.Tags.Any(tag => tag.Code == 2 && Equals(tag.Value, "LAYER")));
        string extension = (string)table.Tags.Single(tag => tag.Code == 360).Value;
        var dictionary = SourceReferenceRecord(raw, extension);
        Equal("DICTIONARY", dictionary.Name, "Consumed layer extension has a physical dictionary declaration");
        if (placement == "duplicate-entity")
        {
            int index = raw.Sections.Single(section => section.Name == "ENTITIES").ContentStartTagIndex;
            raw = DxfRawDocument.Create(raw.Tags.Take(index).Concat(SourceAmbiguityDecoy(extension)).Concat(raw.Tags.Skip(index)));
        }
        else if (placement == "private") raw = SourceAmbiguityPrivateHandle(raw, dictionary, extension, false);
        else if (placement == "nested-private") raw = SourceAmbiguityPrivateHandle(raw, dictionary, extension, true);
        else if (placement is "nested-standard-lookalikes" or "unbalanced-private")
        {
            var tags = dictionary.Tags.ToList();
            var control = new List<DxfTag> { new DxfTag(102, "{PRIVATE_SOURCE"), new DxfTag(5, extension) };
            if (placement == "nested-standard-lookalikes")
            {
                // These private lookalikes do not declare actual common metadata.
                control.AddRange(new[] { new DxfTag(102, "{ACAD_REACTORS"), new DxfTag(330, "ABCDEF"), new DxfTag(102, "}"),
                    new DxfTag(102, "{ACAD_XDICTIONARY"), new DxfTag(360, "ABCDE0"), new DxfTag(102, "}"), new DxfTag(102, "}") });
            }
            tags.InsertRange(tags.FindIndex(tag => tag.Code == 100), control);
            raw = raw.WithRecord(dictionary, tags);
        }

        var doc = SourceAmbiguityLoad(raw, binary, placement is "duplicate-entity" or "unbalanced-private");
        if (doc == null) return;
        Check(doc.Layers.StateManager.Contains("AMBIGUITY_SNAPSHOT"), "Accepted consumed dictionary lost its layer snapshot");
        Equal("retained alongside layer-state conversion", ((DxfDictionaryVariable)doc.NamedObjects["AMBIGUITY_APP"]).Value,
            "Consumed layer-state mapping changed ordinary source objects");
        Equal(0, doc.Objects.Validate().Count, "Unambiguous consumed dictionary graph validation");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Consumed layer-state cross-transport save");
        output.Position = 0; var again = DxfDocument.Load(output) ?? throw new FormatException("Consumed layer-state reload rejected");
        Check(again.Layers.StateManager.Contains("AMBIGUITY_SNAPSHOT"), "Consumed dictionary mapping lost after cross-transport save");
    }
}
