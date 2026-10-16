using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterUnresolvedDictionaryTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (bool fallback in new[] { false, true })
            Run($"dictionary/unresolved-soft/roundtrip/{version}/{binary}/{fallback}",
                () => UnresolvedDictionaryRoundTrip(version, binary, fallback));
        foreach (bool binary in new[] { false, true })
            Run($"dictionary/unresolved-soft/hard-boundaries/{binary}", () => UnresolvedDictionaryHardBoundaries(binary));
        Run("dictionary/unresolved-soft/authoring-allocation-erase", UnresolvedDictionaryAuthoring);
    }

    private static (DxfRawDocument Raw, string Dictionary, string Missing) UnresolvedDictionaryFixture(DxfVersion version, bool fallback)
    {
        var document = new DxfDocument(version);
        document.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        DxfDictionary dictionary = fallback ? new DxfDictionaryWithDefault() : new DxfDictionary();
        dictionary.IsHardOwner = false;
        var present = new DxfPlaceholder(); var missing = new DxfPlaceholder();
        dictionary.Add("PRESENT", present, false);
        dictionary.Add("MISSING", missing, false);
        dictionary.Add("MISSING_ALIAS", missing, false);
        if (dictionary is DxfDictionaryWithDefault defaults) defaults.Default = present;
        document.NamedObjects.Add("SOFT_REFERENCES", dictionary);
        using var bytes = new MemoryStream(); Check(document.Save(bytes), "Soft dictionary scaffold save.");
        bytes.Position = 0; var raw = DxfRawDocument.Load(bytes);
        raw = raw.WithoutRecord(SourceReferenceRecord(raw, missing.Handle));
        var record = SourceReferenceRecord(raw, dictionary.Handle);
        raw = raw.WithRecord(record, record.Tags.Where(tag => tag.Code != 280));
        return (raw, dictionary.Handle, missing.Handle);
    }

    private static DxfDocument LoadUnresolvedDictionary(DxfRawDocument raw, bool binary)
    {
        using var input = new MemoryStream();
        raw.WithTags(raw.Tags.Where(tag => tag.Code != 999)).Save(input, binary);
        input.Position = 0;
        return DxfDocument.Load(input) ?? throw new InvalidOperationException("Soft dictionary fixture did not load.");
    }

    private static void UnresolvedDictionaryRoundTrip(DxfVersion version, bool binary, bool fallback)
    {
        var fixture = UnresolvedDictionaryFixture(version, fallback);
        var document = LoadUnresolvedDictionary(fixture.Raw, binary);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var dictionary = (DxfDictionary)document.NamedObjects["SOFT_REFERENCES"];
            Check(!dictionary.IsHardOwner, "Absent dictionary hard-owner flag was promoted.");
            Equal(3, dictionary.Count, "Unresolved dictionary aliases were dropped.");
            Check(dictionary.Entries.Select(entry => entry.Name).SequenceEqual(new[] { "PRESENT", "MISSING", "MISSING_ALIAS" }), "Dictionary entry order changed.");
            Check(dictionary.Entries[0].IsResolved && ReferenceEquals(dictionary["PRESENT"], dictionary.Entries[0].Target), "Resolved entry access changed.");
            Equal(dictionary["PRESENT"].Handle, dictionary.Entries[0].TargetHandle, "Resolved entry handle.");
            foreach (var entry in dictionary.Entries.Skip(1))
            {
                Check(!entry.IsResolved && entry.Target == null && !entry.IsHardOwner, "Missing soft reference acquired a target or ownership.");
                Equal(fixture.Missing, entry.TargetHandle, "Missing reference handle changed.");
                Check(dictionary.Contains(entry.Name) && !dictionary.TryGetValue(entry.Name, out var target) && target == null, "Unresolved name lookup is not explicit.");
                Throws<KeyNotFoundException>(() => { _ = dictionary[entry.Name]; });
            }
            Check(!dictionary.Contains("ABSENT"), "Unknown name reported present.");
            Equal(fallback, dictionary.TryGetValue("ABSENT", out var fallbackTarget), "Dictionary fallback lookup.");
            if (fallback) Check(ReferenceEquals(dictionary["PRESENT"], fallbackTarget), "Missing name did not use configured fallback.");
            Check(document.GetObjectByHandle(fixture.Missing) == null, "A missing reference captured a generated object.");
            Equal(2, document.Objects.Validate().Count, "Strict validation lost unresolved soft diagnostics.");
            int count = document.Objects.Items.Count;
            Throws<InvalidOperationException>(() => document.Objects.Clone(dictionary, document.NamedObjects, "CLONE"));
            Equal(count, document.Objects.Items.Count, "Refused unresolved graph clone mutated registration.");
            Check(!document.NamedObjects.Contains("CLONE"), "Refused clone acquired a name.");

            using var output = new MemoryStream();
            Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Retained soft dictionary save.");
            output.Position = 0; var stored = DxfRawDocument.Load(output);
            var packet = SourceReferenceRecord(stored, fixture.Dictionary);
            Check(packet.Tags.Where(tag => tag.Code == 350).Select(tag => (string)tag.Value)
                .SequenceEqual(new[] { dictionary["PRESENT"].Handle, fixture.Missing, fixture.Missing }), "Saved soft reference handles or order changed.");
            Check(!packet.Tags.Any(tag => tag.Code == 360), "Unresolved soft reference was written as hard ownership.");
            output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Retained soft dictionary reload.");
            Equal(1, document.Entities.Lines.Count(), "Unrelated geometry changed.");
        }
    }

    private static void UnresolvedDictionaryHardBoundaries(bool binary)
    {
        foreach (string variant in new[] { "hard-entry", "hard-dictionary", "hard-default", "null-entry" })
        {
            var fixture = UnresolvedDictionaryFixture(DxfVersion.AutoCad2018, true);
            var record = SourceReferenceRecord(fixture.Raw, fixture.Dictionary);
            var tags = record.Tags.ToList();
            int missing = tags.FindIndex(tag => tag.Code == 3 && Equals(tag.Value, "MISSING")) + 1;
            if (variant == "hard-entry") tags[missing] = new DxfTag(360, fixture.Missing);
            if (variant == "hard-dictionary") tags.Insert(tags.FindIndex(tag => tag.Code == 100) + 1, new DxfTag(280, (short)1));
            if (variant == "hard-default") tags[tags.FindIndex(tag => tag.Code == 340)] = new DxfTag(340, fixture.Missing);
            if (variant == "null-entry") tags[missing] = new DxfTag(350, "0");
            var raw = fixture.Raw.WithRecord(record, tags);
            using var input = new MemoryStream(); raw.WithTags(raw.Tags.Where(tag => tag.Code != 999)).Save(input, binary); input.Position = 0;
#if DEBUG
            Throws<FormatException>(() => DxfDocument.Load(input));
#else
            Check(DxfDocument.Load(input) == null, "Unresolved hard or null dictionary reference accepted.");
#endif
        }
        var document = LoadUnresolvedDictionary(UnresolvedDictionaryFixture(DxfVersion.AutoCad2018, false).Raw, binary);
        var payload = new DxfXRecord(); document.NamedObjects.Add("HARD_POINTER", payload);
        payload.Data.Add(new DxfTag(340, "ABCDEF"));
        using var output = new MemoryStream();
        bool rejected;
        try { rejected = !document.Save(output, binary); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && output.Length == 0, "Soft dictionary save allowance bypassed hard-pointer validation.");
    }

    private static void UnresolvedDictionaryAuthoring()
    {
        var entry = DxfDictionaryEntry.CreateUnresolvedSoftReference("RETAINED", "000abc");
        Equal("ABC", entry.TargetHandle, "Unresolved entry handle normalization.");
        Check(entry.Target == null && !entry.IsResolved && !entry.IsHardOwner, "Detached unresolved entry has a target.");
        foreach (string handle in new[] { "", "0", "0000", "XYZ", "-1", "10000000000000000" })
            Throws<ArgumentException>(() => DxfDictionaryEntry.CreateUnresolvedSoftReference("BAD", handle));
        Throws<ArgumentException>(() => DxfDictionaryEntry.CreateUnresolvedSoftReference("", "AB"));
        var hard = new DxfDictionary();
        Throws<InvalidOperationException>(() => hard.AddUnresolvedSoftReference("BAD", "AB"));
        Equal(0, hard.Count, "Failed hard-owner addition mutated dictionary.");

        var document = new DxfDocument();
        var dictionary = new DxfDictionary { IsHardOwner = false };
        long seed = Convert.ToInt64(document.DrawingVariables.HandleSeed, 16);
        string missing = (seed + 1).ToString("X");
        dictionary.AddUnresolvedSoftReference("MISSING", missing);
        dictionary.Add("PRESENT", new DxfPlaceholder(), false);
        document.NamedObjects.Add("SOFT_REFERENCES", dictionary);
        Check(document.GetObjectByHandle(missing) == null, "Graph registration allocated a retained missing handle.");
        string present = dictionary["PRESENT"].Handle;
        Throws<ArgumentException>(() => dictionary.AddUnresolvedSoftReference("KNOWN", present));
        Throws<ArgumentException>(() => dictionary.AddUnresolvedSoftReference("missing", "ABC"));
        Throws<InvalidOperationException>(() => dictionary.IsHardOwner = true);
        Check(!dictionary.IsHardOwner && dictionary.Count == 2, "Failed unresolved-entry mutation changed dictionary.");
        dictionary.AddUnresolvedSoftReference("HIGH", "ABCDEF");
        var line = new Line(Vector3.Zero, Vector3.UnitY); document.Entities.Add(line);
        Check(Convert.ToInt64(line.Handle, 16) > 0xABCDEF && document.GetObjectByHandle("ABCDEF") == null, "Later entity allocation captured an unresolved reference.");
        var collision = new DxfDictionary { IsHardOwner = false };
        collision.AddUnresolvedSoftReference("KNOWN", present);
        int count = document.Objects.Items.Count;
        Throws<ArgumentException>(() => document.NamedObjects.Add("COLLISION", collision));
        Equal(count, document.Objects.Items.Count, "Refused unresolved-target adoption changed registration.");
        Check(!document.NamedObjects.Contains("COLLISION"), "Refused unresolved-target adoption acquired a name.");
        document.Objects.EraseOwnedTree((DxfDatabaseObject)dictionary["PRESENT"]);
        Check(dictionary.Count == 2 && dictionary.Contains("MISSING") && dictionary.Contains("HIGH"), "Unrelated erasure changed retained soft links.");
        document.Objects.EraseOwnedTree(dictionary);
        Check(dictionary.IsErased && !document.NamedObjects.Contains("SOFT_REFERENCES"), "Unresolved soft links blocked dictionary erasure.");
        Equal(0, document.Objects.Validate().Count, "Erased unresolved dictionary left graph errors.");
    }
}
