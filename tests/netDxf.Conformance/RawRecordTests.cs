using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawRecordTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"raw-record/index-partition/{v}/{b}", () => RawRecordPartition(v, b));
                Run($"raw-record/replace-unknown/{v}/{b}", () => RawRecordReplace(v, b));
                Run($"raw-record/remove/{v}/{b}", () => RawRecordRemove(v, b));
                Run($"raw-record/duplicate-names/{v}/{b}", () => RawRecordDuplicates(v, b));
                Run($"raw-record/header-values/{v}/{b}", () => RawRecordHeader(v, b));
                Run($"raw-record/known-geometry/{v}/{b}", () => RawRecordKnown(v, b));
                Run($"raw-record/foreign-and-stale/{v}/{b}", () => RawRecordForeign(v, b));
                Run($"raw-record/invalid-replacements/{v}/{b}", () => RawRecordInvalid(v, b));
                Run($"raw-record/budget-and-disposal/{v}/{b}", () => RawRecordBudget(v, b));
            }
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            Run($"raw-record/comment-partition/{v}", () => RawRecordComments(v));
        }
        Run("raw-record/empty-records-and-preamble", RawRecordEmpty);
        Run("raw-record/immutable-views", RawRecordImmutable);
        Run("raw-record/concurrent-index-publication", RawRecordConcurrent);
    }

    private static DxfRawRecord FindRawRecord(DxfRawDocument raw, string section, string name)
        => raw.Sections.Single(s => s.Name == section).Records.Single(r => r.Name == name);

    private static DxfRawDocument RawRecordSource(DxfVersion version, bool binary)
        => LoadRaw(RawFixtureBytes(RawFixtureTags(version), binary));

    private static void CheckRawRecordPartition(DxfRawDocument raw)
    {
        foreach (DxfRawSection section in raw.Sections)
        {
            var partition = section.Preamble.Concat(section.Records.SelectMany(r => r.Tags)).ToArray();
            SameRawTags(section.Content, partition);
            int offset = section.ContentStartTagIndex;
            foreach (DxfTag tag in section.Preamble)
                Check(ReferenceEquals(tag, raw.Tags[offset++]), "Preamble copied or changed a tag.");
            foreach (DxfRawRecord record in section.Records)
            {
                Equal(section.Name, record.SectionName, "Record section name");
                Equal(offset, record.StartTagIndex, "Record start");
                Equal(record.EndTagIndex - record.StartTagIndex, record.Tags.Count, "Record tag count");
                Equal(record.Tags.Count - 1, record.Content.Count, "Record content count");
                Equal(record.Name, (string)record.Tags[0].Value, "Record name");
                Equal(record.MarkerCode, record.Tags[0].Code, "Record boundary code");
                Equal((short)(section.Name.Equals("HEADER", StringComparison.OrdinalIgnoreCase) ? 9 : 0), record.MarkerCode, "Section boundary rule");
                SameRawTags(record.Tags.Skip(1).ToArray(), record.Content);
                foreach (DxfTag tag in record.Tags)
                    Check(ReferenceEquals(tag, raw.Tags[offset++]), "Record changed original tag identity.");
                Equal(offset, record.EndTagIndex, "Record end");
                Throws<ArgumentOutOfRangeException>(() => _ = record.Tags[-1]);
                Throws<ArgumentOutOfRangeException>(() => _ = record.Content[record.Content.Count]);
            }
            Equal(section.EndTagIndex - 1, offset, "Section range was not completely partitioned");
        }
    }

    private static void RawRecordPartition(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary); CheckRawRecordPartition(raw);
        int[] expected = { 4, 1, 3, 3, 3, 1, 1, 1 };
        Check(expected.SequenceEqual(raw.Sections.Select(s => s.Records.Count)), "Record counts by section");
        Equal("SECTION", FindRawRecord(raw, "ENTITIES", "SECTION").Name, "SECTION entity was treated as file structure");
        Check(raw.Sections.Single(s => s.Name == "TABLES").Records.Select(r => r.Name).SequenceEqual(new[] { "TABLE", "FUTURE_ENTRY", "ENDTAB" }), "TABLE grouping became a destructive aggregate.");
        Check(raw.HasOriginalBytes, "Indexing lost byte identity.");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, !binary)).Tags);
    }

    private static void AssertOutsideRecordUnchanged(DxfRawDocument original, DxfRawRecord record, DxfRawDocument changed, int replacementLength)
    {
        Equal(original.Tags.Count - record.Tags.Count + replacementLength, changed.Tags.Count, "Replacement length");
        for (int i = 0; i < record.StartTagIndex; i++)
            Check(ReferenceEquals(original.Tags[i], changed.Tags[i]), "A prefix tag outside the record was replaced.");
        int delta = replacementLength - record.Tags.Count;
        for (int i = record.EndTagIndex; i < original.Tags.Count; i++)
            Check(ReferenceEquals(original.Tags[i], changed.Tags[i + delta]), "A suffix tag outside the record was replaced.");
    }

    private static void RawRecordReplace(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary);
        var record = FindRawRecord(raw, "ENTITIES", "FUTURE_ENTITY");
        byte[] before = SaveRaw(raw);
        var replacement = record.Tags.Select(t => t.Code == 160 ? new DxfTag(160, long.MaxValue) : t).ToArray();
        var edited = raw.WithRecord(record, replacement);
        Check(!edited.HasOriginalBytes, "Record edit incorrectly retains original-byte output.");
        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Length);
        SameRawTags(replacement, FindRawRecord(edited, "ENTITIES", "FUTURE_ENTITY").Tags);
        Equal(long.MinValue, (long)record.Content.Single(t => t.Code == 160).Value, "Edit mutated source field");
        Check(before.SequenceEqual(SaveRaw(raw)), "Edit modified original bytes.");
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !binary)).Tags);
        CheckRawRecordPartition(edited);
    }

    private static void RawRecordRemove(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary);
        var record = FindRawRecord(raw, "VENDOR_SECTION", "VENDOR_RECORD");
        var edited = raw.WithoutRecord(record);
        AssertOutsideRecordUnchanged(raw, record, edited, 0);
        var empty = edited.Sections.Single(s => s.Name == "VENDOR_SECTION");
        Equal(0, empty.Records.Count, "Removed record remains"); Equal(0, empty.Content.Count, "Removing the sole record left payload");
        Equal(1, raw.Sections.Single(s => s.Name == "VENDOR_SECTION").Records.Count, "Removal mutated source");
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !binary)).Tags);
        // Removal is lexical: it must not silently remap other records' handles or references.
        Check(raw.Tags.Where(t => t.HandleKind != DxfHandleKind.None).Select(t => t.Value)
            .SequenceEqual(edited.Tags.Where(t => t.HandleKind != DxfHandleKind.None).Select(t => t.Value)), "Removal rewrote unrelated references.");
    }

    private static void RawRecordDuplicates(DxfVersion version, bool binary)
    {
        var tags = RawFixtureTags(version);
        int sectionEnd = tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "VENDOR_SECTION"));
        tags.InsertRange(sectionEnd + 1, new[] { new DxfTag(0, "VENDOR_RECORD"), new DxfTag(1, "first") });
        var raw = LoadRaw(RawFixtureBytes(tags, binary));
        var records = raw.Sections.Single(s => s.Name == "VENDOR_SECTION").Records;
        Equal(2, records.Count, "Duplicate record names were collapsed");
        var edit = records[1].Tags.Select(t => t.Code == 1 ? new DxfTag(1, "second changed") : t);
        var edited = raw.WithRecord(records[1], edit);
        SameRawTags(records[0].Tags, edited.Sections.Single(s => s.Name == "VENDOR_SECTION").Records[0].Tags);
        Equal("second changed", (string)edited.Sections.Single(s => s.Name == "VENDOR_SECTION").Records[1].Content.Single(t => t.Code == 1).Value, "Wrong duplicate was edited");
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !binary)).Tags);
    }

    private static void RawRecordHeader(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary);
        var name = FindRawRecord(raw, "HEADER", "$PROJECTNAME");
        var replacement = new[] { new DxfTag(9, "$PROJECTNAME"), new DxfTag(1, "EOF") };
        var edited = raw.WithRecord(name, replacement);
        SameRawTags(replacement, FindRawRecord(edited, "HEADER", "$PROJECTNAME").Tags);
        AssertOutsideRecordUnchanged(raw, name, edited, replacement.Length);
        var removed = raw.WithoutRecord(name);
        Check(!removed.Sections.Single(s => s.Name == "HEADER").Records.Any(r => r.Name == "$PROJECTNAME"), "Custom header removal failed.");
        var versionRecord = FindRawRecord(raw, "HEADER", "$ACADVER");
        Throws<NotSupportedException>(() => raw.WithRecord(versionRecord,
            new[] { new DxfTag(9, "$ACADVER"), new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1032" : "AC1015") }));
        Throws<FormatException>(() => raw.WithoutRecord(versionRecord));
        Throws<NotSupportedException>(() => raw.WithoutRecord(FindRawRecord(raw, "HEADER", "$DWGCODEPAGE")));
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !binary)).Tags);
    }

    private static void RawRecordKnown(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version); document.Comments.Clear();
        document.Entities.Add(new Line(new Vector3(1e-20, 2, 3), new Vector3(4, 5, 6)));
        document.Entities.Add(new Circle(new Vector3(7, 8, 9), 1.25));
        using var source = new MemoryStream(); Check(document.Save(source, binary), "Record fixture save failed.");
        source.Position = 0; var raw = DxfRawDocument.Load(source);
        var line = FindRawRecord(raw, "ENTITIES", "LINE");
        var edited = raw.WithRecord(line, line.Tags.Select(t => t.Code == 11 ? new DxfTag(11, 7.123456789012345) : t));
        byte[] bytes = SaveRaw(edited, !binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raw-record-edit-{version}-{!binary}.dxf"), bytes);
        using var input = new MemoryStream(bytes);
        var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Record-scoped edit failed semantic loading.");
        SameDoubleBits(1e-20, loaded.Entities.Lines.Single().StartPoint.X, "Record edit changed untouched numeric bits");
        SameDoubleBits(7.123456789012345, loaded.Entities.Lines.Single().EndPoint.X, "Record edit lost new numeric bits");
        Equal(new Vector3(7, 8, 9), loaded.Entities.Circles.Single().Center, "Record edit changed adjacent geometry");
        SameDoubleBits(1.25, loaded.Entities.Circles.Single().Radius, "Record edit changed adjacent radius");
        AssertOutsideRecordUnchanged(raw, line, edited, line.Tags.Count);
    }

    private static void RawRecordForeign(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary);
        var record = FindRawRecord(raw, "ENTITIES", "LINE");
        var other = raw.WithTags(raw.Tags);
        bool enumerated = false;
        IEnumerable<DxfTag> Replacement() { enumerated = true; yield return new DxfTag(0, "LINE"); }
        Throws<ArgumentException>(() => other.WithRecord(record, Replacement()));
        Check(!enumerated, "Foreign record consumed a replacement before validation.");
        Throws<ArgumentException>(() => other.WithoutRecord(record));
        var edited = raw.WithRecord(record, record.Tags);
        Throws<ArgumentException>(() => edited.WithRecord(record, Replacement()));
        Check(!enumerated, "Stale record consumed a replacement before validation.");
        Throws<ArgumentNullException>(() => raw.WithRecord(null!, Replacement()));
        Throws<ArgumentNullException>(() => raw.WithoutRecord(null!));
        Throws<ArgumentNullException>(() => raw.WithRecord(record, null!));
    }

    private static void RawRecordInvalid(DxfVersion version, bool binary)
    {
        var raw = RawRecordSource(version, binary); var record = FindRawRecord(raw, "ENTITIES", "LINE");
        DxfTag[][] invalid =
        {
            Array.Empty<DxfTag>(), new[] { new DxfTag(0, "") }, new[] { new DxfTag(1, "LINE") },
            new[] { new DxfTag(9, "$PROJECTNAME"), new DxfTag(1, "LINE") },
            new[] { new DxfTag(0, "eof") }, new[] { new DxfTag(0, "endsec") },
            new[] { new DxfTag(0, "LINE"), new DxfTag(0, "CIRCLE") },
            new[] { new DxfTag(0, "LINE"), new DxfTag(0, "EOF") },
            new DxfTag[] { null! }, new[] { new DxfTag(0, "LINE"), null! }
        };
        byte[] before = SaveRaw(raw);
        foreach (var replacement in invalid) Throws<ArgumentException>(() => raw.WithRecord(record, replacement));
        var header = FindRawRecord(raw, "HEADER", "$PROJECTNAME");
        Throws<ArgumentException>(() => raw.WithRecord(header, new[] { new DxfTag(9, "$PROJECTNAME"), new DxfTag(0, "LINE") }));
        Throws<ArgumentException>(() => raw.WithRecord(header, new[] { new DxfTag(9, "$A"), new DxfTag(1, "a"), new DxfTag(9, "$B") }));
        Check(before.SequenceEqual(SaveRaw(raw)), "Invalid edits changed original bytes.");
    }

    private static void RawRecordBudget(DxfVersion version, bool binary)
    {
        var tags = RawFixtureTags(version);
        var raw = DxfRawDocument.Create(tags, binary, new DxfRawOptions(maximumTags: tags.Count));
        var record = FindRawRecord(raw, "ENTITIES", "LINE");
        Throws<InvalidDataException>(() => raw.WithRecord(record, record.Tags.Concat(new[] { new DxfTag(1, "over budget") })));
        bool disposed = false;
        IEnumerable<DxfTag> Infinite()
        {
            try { yield return new DxfTag(0, "LINE"); while (true) yield return new DxfTag(1, "bounded"); }
            finally { disposed = true; }
        }
        Throws<InvalidDataException>(() => raw.WithRecord(record, Infinite()));
        Check(disposed, "Budget rejection did not dispose the replacement iterator.");
        IEnumerable<DxfTag> Failing()
        {
            try { yield return new DxfTag(0, "LINE"); throw new IOException("Injected replacement failure."); }
            finally { disposed = true; }
        }
        disposed = false;
        Throws<IOException>(() => raw.WithRecord(record, Failing()));
        Check(disposed, "Replacement failure did not dispose its iterator.");
        SameRawTags(tags, raw.Tags);
        // An equal-size replacement is permitted at the inclusive tag budget.
        SameRawTags(tags, raw.WithRecord(record, record.Tags).Tags);
    }

    private static void RawRecordComments(DxfVersion version)
    {
        var tags = RawFixtureTags(version, true);
        int index = tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "ENTITIES"));
        tags.Insert(index + 1, new DxfTag(999, "preamble"));
        index = tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "HEADER"));
        tags.Insert(index + 1, new DxfTag(999, "header preamble"));
        var raw = LoadRaw(RawFixtureBytes(tags, false)); CheckRawRecordPartition(raw);
        Equal(1, raw.Sections.Single(s => s.Name == "ENTITIES").Preamble.Count, "Entity preamble count");
        Equal(1, raw.Sections.Single(s => s.Name == "HEADER").Preamble.Count, "Header preamble count");
        var record = FindRawRecord(raw, "ENTITIES", "FUTURE_ENTITY");
        Equal(2, record.Tags.Count(t => t.Code == 999), "Interior comments left their record range");
        var edited = raw.WithRecord(record, record.Tags.Where(t => t.Code != 999));
        AssertOutsideRecordUnchanged(raw, record, edited, record.Tags.Count - 2);
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited)).Tags);
    }

    private static void RawRecordEmpty()
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018);
        tags.InsertRange(tags.Count - 1, new[] { new DxfTag(0, "SECTION"), new DxfTag(2, "THUMBNAILIMAGE"),
            new DxfTag(90, 2), new DxfTag(310, new byte[] { 1, 2 }), new DxfTag(0, "ENDSEC"),
            new DxfTag(0, "SECTION"), new DxfTag(2, "EMPTY"), new DxfTag(0, "ENDSEC") });
        var raw = DxfRawDocument.Create(tags); CheckRawRecordPartition(raw);
        var thumbnail = raw.Sections.Single(s => s.Name == "THUMBNAILIMAGE");
        Equal(0, thumbnail.Records.Count, "Invented records in marker-free thumbnail");
        SameRawTags(thumbnail.Content, thumbnail.Preamble);
        Equal(0, raw.Sections.Single(s => s.Name == "EMPTY").Preamble.Count, "Empty section preamble");
        var empty = FindRawRecord(raw, "TABLES", "ENDTAB");
        Equal(1, empty.Tags.Count, "Empty record marker"); Equal(0, empty.Content.Count, "Empty record content");
        var lower = raw.WithTags(raw.Tags.Select(t => t.Code == 2 && Equals(t.Value, "HEADER") ? new DxfTag(2, "header") : t));
        Equal(4, lower.Sections.First().Records.Count, "Case spelling changed HEADER indexing");
        Equal((short)9, lower.Sections.First().Records[0].MarkerCode, "Lowercase HEADER boundary rule");
    }

    private static void RawRecordConcurrent()
    {
        var raw = RawRecordSource(DxfVersion.AutoCad2018, false);
        var section = raw.Sections.Single(s => s.Name == "ENTITIES");
        var records = new IReadOnlyList<DxfRawRecord>[128];
        Parallel.For(0, records.Length, i => records[i] = section.Records);
        foreach (var result in records)
        {
            Check(ReferenceEquals(records[0], result), "Concurrent access published different record collections.");
            Equal(3, result.Count, "Concurrent record count");
            Check(ReferenceEquals(records[0][0], result[0]), "Concurrent access published different record indexes.");
        }
    }

    private static void RawRecordImmutable()
    {
        var raw = RawRecordSource(DxfVersion.AutoCad2018, false);
        var section = raw.Sections.Single(s => s.Name == "ENTITIES");
        Throws<NotSupportedException>(() => ((IList<DxfRawRecord>)section.Records).Clear());
        var record = section.Records[0];
        Check(record.Tags is not IList<DxfTag> && record.Content is not IList<DxfTag>, "Raw slices expose mutable list interfaces.");
        Check(ReferenceEquals(record, section.Records[0]), "Record identity is unstable within a snapshot.");
        byte[] bytes = (byte[])FindRawRecord(raw, "ENTITIES", "FUTURE_ENTITY").Tags.First(t => t.Code == 310).Value;
        bytes[0] = 42;
        Equal((byte)0, ((byte[])FindRawRecord(raw, "ENTITIES", "FUTURE_ENTITY").Tags.First(t => t.Code == 310).Value)[0], "Record exposed binary storage.");
    }
}
