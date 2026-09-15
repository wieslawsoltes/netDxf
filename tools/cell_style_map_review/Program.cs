using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Header;
using netDxf.Entities;
using netDxf.Blocks;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static string Repo = "", Out = "";
    private static int Failed;
    private static void Main(string[] args)
    {
        Repo = Path.GetFullPath(args[0]); Out = Path.GetFullPath(args[1]); Directory.CreateDirectory(Out);
        string mode = args.Length > 2 ? args[2] : "SUNSTUDY";
        CellStyles();
        File.WriteAllText(Path.Combine(Out, "results.json"), JsonSerializer.Serialize(new { failed = Failed, cases = Results.Count, librarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(), results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count} cases; {Failed} failed."); Environment.ExitCode = Failed == 0 ? 0 : 1;
    }
    private static void Run(string name, Action action)
    {
        try { action(); Results.Add(new { name, passed = true, error = "" }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Failed++; Results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    private static DxfRawDocument Raw(string file) { using var input = File.OpenRead(file); return DxfRawDocument.Load(input); }
    private static byte[] Bytes(DxfRawDocument raw, bool binary) { using var output = new MemoryStream(); if (binary && raw.Tags.Any(t => t.Code == 999)) raw = DxfRawDocument.Create(raw.Tags.Where(t => t.Code != 999)); raw.Save(output, binary); return output.ToArray(); }
    private static DxfDocument Load(DxfRawDocument raw, bool binary)
    { using var stream = new MemoryStream(Bytes(raw, binary)); return DxfDocument.Load(stream) ?? throw new FormatException("Load returned null."); }
    private static DxfRawRecord Record(DxfRawDocument raw, string type) => raw.Sections.SelectMany(s => s.Records).First(r => r.Name == type);
    private static DxfRawDocument Mutate(DxfRawDocument raw, string type, Action<List<DxfTag>> edit)
    { var record = Record(raw, type); var tags = record.Tags.ToList(); edit(tags); return raw.WithRecord(record, tags); }
    private static int Start(List<DxfTag> tags) => tags.FindIndex(t => t.Code == 100);
    private static void Replace(List<DxfTag> tags, short code, object value)
    { int i = tags.FindIndex(Start(tags) + 1, t => t.Code == code); Check(i >= 0, "Mutation field absent."); tags[i] = new DxfTag(code, value); }
    private static string Handle(DxfRawRecord record) => (string)record.Tags.First(t => t.Code == 5).Value;
    private static string Pair(DxfTag tag) => tag.Code + ":" + (tag.ValueType == DxfTagValueType.Handle ? Convert.ToUInt64((string)tag.Value, 16).ToString("X", CultureInfo.InvariantCulture) : tag.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
    private static string[] Body(DxfRawRecord record) => record.Tags.SkipWhile(t => t.Code != 100).Select(Pair).ToArray();
    private static DxfDatabaseObject Target(DxfDocument doc, string type) => doc.Objects.Items.First(x => x.CodeName == type);
    private static void Positive(string name, DxfRawDocument raw, string type, bool binary, bool typed = true, string? handle = null)
    {
        var source = handle == null ? Record(raw, type) : raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == type && Handle(r) == handle);
        var doc = Load(raw, binary); var item = (DxfDatabaseObject)doc.GetObjectByHandle(Handle(source));
        Check(item.GetType().Name == (typed ? "DxfStoredCellStyleMap" : "DxfOpaqueObject"), "Unexpected representation: " + item.GetType().Name);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Save returned false."); output.Position = 0;
        var saved = DxfRawDocument.Load(output); var record = saved.Sections.SelectMany(s => s.Records).Single(r => r.Name == type && Handle(r) == item.Handle);
        Check(Body(source).SequenceEqual(Body(record)), "Stored body changed after typed load/save.");
        File.WriteAllBytes(Path.Combine(Out, name + ".dxf"), output.ToArray());
        if (typed)
        {
            var prop = item.GetType().GetProperty("Payload") ?? throw new Exception("Missing Payload.");
            var payload = prop.GetValue(item) as IReadOnlyList<DxfTag> ?? throw new Exception("Unexpected Payload type.");
            Check(payload.Select(Pair).SequenceEqual(source.Tags.SkipWhile(t => t.Code != 100).TakeWhile(t => t.Code != 1001).Select(Pair)), "Public Payload changed original body.");
            if (payload is IList list) { Check(list.IsReadOnly, "Payload is mutable."); }
        }
    }
    private static void Reject(DxfRawDocument raw, bool binary)
    { bool rejected = false; try { _ = Load(raw, binary); } catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException or InvalidOperationException) { rejected = true; } Check(rejected, "Malformed source was accepted."); }
    private static string State(DxfDocument doc) =>
        typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc) + "/" +
        string.Join("|", doc.Objects.Items.OrderBy(x => x.Handle).Select(x => x.Handle + ":" + x.Owner?.Handle + ":" + (x is DxfDictionary d ? string.Join(",", d.Entries.Select(e => e.Name + "=" + e.Target.Handle)) : "")));
    private static void RefuseUnchanged(DxfDocument doc, Action action)
    { string before = State(doc); bool refused = false; try { action(); } catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException) { refused = true; } Check(refused, "Unsupported lifecycle operation succeeded."); Check(before == State(doc), "Refused operation changed registration, ownership or handle seed."); }
    private static void Lifecycle(DxfRawDocument raw, string type, bool binary, string operation)
    {
        var doc = Load(raw, binary); var item = Target(doc, type);
        if (operation == "erase") RefuseUnchanged(doc, () => doc.Objects.EraseOwnedTree(item));
        else if (operation == "ancestor-erase") RefuseUnchanged(doc, () => doc.Objects.EraseOwnedTree((DxfDatabaseObject)item.Owner));
        else if (operation == "ancestor-clone")
        {
            DxfObject ancestor = item.Owner; while (ancestor is not DxfDictionary && ancestor.Owner != null) ancestor = ancestor.Owner;
            RefuseUnchanged(doc, () => doc.Objects.Clone((DxfDictionary)ancestor, doc.Objects.Root, "INDEPENDENT_COPY"));
        }
        else if (operation == "foreign")
        {
            var foreign = new DxfDocument(doc.DrawingVariables.AcadVer); _ = foreign.Objects.Root; string other = State(foreign);
            RefuseUnchanged(doc, () => foreign.Objects.Root.Add("FOREIGN", item)); Check(other == State(foreign), "Foreign failure changed destination.");
        }
        else if (operation == "profile")
        {
            doc.DrawingVariables.AcadVer = doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018;
            string before = State(doc); using var stream = new MemoryStream(); bool rejected = false;
            try { rejected = !doc.Save(stream, binary); } catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException) { rejected = true; }
            Check(rejected && stream.Length == 0, "Profile conversion wrote or accepted output."); Check(before == State(doc), "Profile preflight changed source.");
        }
    }
    private static DxfRawDocument AddStyle(DxfRawDocument raw)
    {
        var source = Record(raw, "STYLE"); var style = source.Tags.Select(t => t.Code == 5 ? new DxfTag(5, "FEA123") : t.Code == 2 ? new DxfTag(2, "INDEPENDENT_STYLE") : t).ToList();
        var tags = raw.Tags.ToList(); tags.InsertRange(source.EndTagIndex, style); return raw.WithTags(tags);
    }
    private static DxfRawDocument AddIntermediateSunOwner(DxfRawDocument raw)
    {
        var sun = Record(raw, "SUNSTUDY"); string handle = Handle(sun); string ownerHandle = (string)sun.Tags.First(t => t.Code == 330).Value;
        var owner = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "DICTIONARY" && Handle(r) == ownerHandle);
        var ownerTags = owner.Tags.ToList();
        for (int i = ownerTags.Count - 1; i > 0; i--)
            if (ownerTags[i].Code is 350 or 360 && Equals(ownerTags[i].Value, handle) && ownerTags[i - 1].Code == 3)
            { ownerTags.RemoveAt(i); ownerTags.RemoveAt(i - 1); i--; }
        ownerTags.Add(new DxfTag(3, "INDEPENDENT_OWNER")); ownerTags.Add(new DxfTag(360, "EFA123")); raw = raw.WithRecord(owner, ownerTags);
        raw = Mutate(raw, "SUNSTUDY", tags => { int i = tags.FindIndex(t => t.Code == 330); tags[i] = new DxfTag(330, "EFA123"); });
        var all = raw.Tags.ToList(); int position = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Last().EndTagIndex;
        all.InsertRange(position, new[] { new DxfTag(0, "DICTIONARY"), new DxfTag(5, "EFA123"), new DxfTag(330, ownerHandle), new DxfTag(100, "AcDbDictionary"), new DxfTag(280, (short)1), new DxfTag(281, (short)1), new DxfTag(3, "INDEPENDENT_STUDY"), new DxfTag(360, handle) });
        return raw.WithTags(all);
    }
    private static void Removal(DxfRawDocument raw, string type, short code, bool binary, string variant)
    {
        raw = AddStyle(raw);
        raw = Mutate(raw, type, tags =>
        {
            Replace(tags, code, variant == "arbitrary" ? "0" : "FEA123");
            if (variant != "typed") tags.Add(new DxfTag(100, "IndependentUnknownSubclass"));
            if (variant == "arbitrary") tags.Add(new DxfTag(320, "FEA123"));
        });
        var doc = Load(raw, binary); var style = doc.TextStyles["INDEPENDENT_STYLE"]; Check(style != null, "Synthetic exact STYLE identity missing.");
        bool removed = doc.TextStyles.Remove(style); Check(removed == (variant == "arbitrary"), "Unexpected STYLE removal: " + removed);
        if (removed) { using var output = new MemoryStream(); Check(doc.Save(output, binary), "Arbitrary stored number prevented save."); }
    }
    private static void Sun()
    {
        var dir = Path.Combine(Repo, "tests/fixtures/sunstudy-producer/typed-carriers");
        foreach (var file in Directory.GetFiles(dir, "*.dxf")) foreach (bool binary in new[] { false, true })
        { string name = "sun-native-" + Path.GetFileNameWithoutExtension(file) + "-" + binary; Run(name, () => Positive(name, Raw(file), "SUNSTUDY", binary)); }
        var raw = Raw(Path.Combine(dir, "ixmilia-sunstudy-R2018-ascii-no-dates-hours.dxf"));
        foreach (bool binary in new[] { false, true })
        {
            foreach (short code in new short[] { 340, 341, 342, 343 })
            {
                Run($"sun-missing-{code}-{binary}", () => Reject(Mutate(raw, "SUNSTUDY", t => Replace(t, code, "FFFABCDE")), binary));
                string name = $"sun-null-{code}-{binary}"; Run(name, () => Positive(name, Mutate(raw, "SUNSTUDY", t => Replace(t, code, "0000")), "SUNSTUDY", binary));
            }
            foreach (string operation in new[] { "erase", "ancestor-erase", "ancestor-clone", "foreign", "profile" }) Run($"sun-{operation}-{binary}", () => Lifecycle(raw, "SUNSTUDY", binary, operation));
            foreach (string operation in new[] { "ancestor-erase", "ancestor-clone", "foreign" }) Run($"sun-intermediate-{operation}-{binary}", () => Lifecycle(AddIntermediateSunOwner(raw), "SUNSTUDY", binary, operation));
            foreach (string variant in new[] { "typed", "opaque", "arbitrary" }) Run($"sun-style-{variant}-{binary}", () => Removal(raw, "SUNSTUDY", 343, binary, variant));
            Run($"sun-owner-self-{binary}", () => Reject(Mutate(raw, "SUNSTUDY", t => { int i = t.FindIndex(t => t.Code == 330); t[i] = new DxfTag(330, (string)t.First(t => t.Code == 5).Value); }), binary));
            Run($"sun-owner-missing-{binary}", () => Reject(Mutate(raw, "SUNSTUDY", t => { int i = t.FindIndex(t => t.Code == 330); t[i] = new DxfTag(330, "FFFABCD"); }), binary));
            string unknown = $"sun-unknown-{binary}"; Run(unknown, () => Positive(unknown, Mutate(raw, "SUNSTUDY", t => t.Add(new DxfTag(300, "Opaque future packet"))), "SUNSTUDY", binary, false));
            string version = $"sun-version-{binary}"; Run(version, () => Positive(version, Mutate(raw, "SUNSTUDY", t => Replace(t, 90, 99)), "SUNSTUDY", binary, false));
            string dates = $"sun-dates-{binary}"; Run(dates, () => Positive(dates, Mutate(raw, "SUNSTUDY", t => { Replace(t, 91, 1); int i = t.FindIndex(t => t.Code == 91) + 1; t.InsertRange(i, new[] { new DxfTag(90, 2461299), new DxfTag(90, 28800) }); }), "SUNSTUDY", binary, false));
            foreach (short count in new short[] { -1, 0, 3, 5, 32767 }) Run($"sun-hour-count-{count}-{binary}", () => Reject(Mutate(raw, "SUNSTUDY", t => Replace(t, 73, count)), binary));
            Run($"sun-alias-removal-{binary}", () =>
            {
                var doc = Load(raw, binary); var study = Target(doc, "SUNSTUDY"); var owner = (DxfDictionary)study.Owner;
                var names = owner.Entries.Where(e => ReferenceEquals(e.Target, study)).Select(e => e.Name).ToArray(); Check(names.Length > 0, "No source alias.");
                owner.Add("INDEPENDENT_ALIAS", study); foreach (string name in names) owner.Remove(name);
                using var allowed = new MemoryStream(); Check(doc.Save(allowed, binary), "Removing original alias while another remains rejected.");
                Check(ReferenceEquals(doc.GetObjectByHandle(study.Handle), study) && ReferenceEquals(study.Owner, owner), "Alias edit changed identity.");
                owner.Remove("INDEPENDENT_ALIAS"); string before = State(doc); using var refused = new MemoryStream(); bool rejected = false;
                try { rejected = !doc.Save(refused, binary); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && refused.Length == 0 && before == State(doc), "Removing all owning aliases was accepted or preflight mutated.");
            });
            foreach (short code in new short[] { 340, 341, 342, 343 }) Run($"sun-generic-identity-{code}-{binary}", () =>
            {
                var variant = Mutate(AddStyle(raw), "SUNSTUDY", t => Replace(t, code, "FEA123")); var doc = Load(variant, binary); var study = Target(doc, "SUNSTUDY");
                string property = code == 340 ? "PageSetup" : code == 341 ? "View" : code == 342 ? "VisualStyle" : "TextStyle";
                object? reference = study.GetType().GetProperty(property)!.GetValue(study);
                Check(ReferenceEquals(reference, doc.GetObjectByHandle("FEA123")), "Role projection did not retain exact generic source identity.");
                Check(!doc.TextStyles.Remove(doc.TextStyles["INDEPENDENT_STYLE"]), "Generic role identity was removable.");
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Generic source binding could not save.");
            });
        }
    }
    private static void Geometry()
    {
        var dir = Path.Combine(Repo, "tests/fixtures/table-content/extracted");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repo, "tests/fixtures/table-content/manifest.json")));
        foreach (var fixture in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            string file = Path.GetFullPath(Path.Combine(Repo, "tests/fixtures/table-content", fixture.GetProperty("fixture").GetString()!));
            byte[] bytes = File.ReadAllBytes(file);
            if (file.EndsWith(".gz", StringComparison.Ordinal))
            { using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var output = new MemoryStream(); gzip.CopyTo(output); bytes = output.ToArray(); }
            using var input = new MemoryStream(bytes); var native = DxfRawDocument.Load(input);
            foreach (var record in native.Sections.SelectMany(s => s.Records).Where(r => r.Name == "TABLEGEOMETRY")) foreach (bool binary in new[] { false, true })
            { string handle = Handle(record); string name = "geometry-native-" + Path.GetFileNameWithoutExtension(fixture.GetProperty("file").GetString()!) + "-" + handle + "-" + binary; Run(name, () => Positive(name, native, "TABLEGEOMETRY", binary, true, handle)); }
        }
        var raw = Raw(Path.Combine(dir, "sample_AC1024_ascii.dxf"));
        foreach (bool binary in new[] { false, true })
        {
            foreach (string operation in new[] { "erase", "ancestor-erase", "ancestor-clone", "foreign", "profile" }) Run($"geometry-{operation}-{binary}", () => Lifecycle(raw, "TABLEGEOMETRY", binary, operation));
            foreach (string variant in new[] { "typed", "opaque", "arbitrary" }) Run($"geometry-style-{variant}-{binary}", () => Removal(raw, "TABLEGEOMETRY", 330, binary, variant));
            Run($"geometry-missing-ref-{binary}", () => Reject(Mutate(raw, "TABLEGEOMETRY", t => Replace(t, 330, "FFFABCDE")), binary));
            foreach (short code in new short[] { 92, 94 }) foreach (int count in new[] { -1, 999999 }) Run($"geometry-count-{code}-{count}-{binary}", () => Reject(Mutate(raw, "TABLEGEOMETRY", t => Replace(t, code, count)), binary));
            string unknown = $"geometry-unknown-{binary}"; Run(unknown, () => Positive(unknown, Mutate(raw, "TABLEGEOMETRY", t => t.Add(new DxfTag(300, "Opaque future packet"))), "TABLEGEOMETRY", binary, false));
            string metadata = $"geometry-endblk-{binary}"; Run(metadata, () =>
            {
                string handle = Handle(Record(raw, "ENDBLK")); var variant = Mutate(raw, "TABLEGEOMETRY", t => Replace(t, 330, handle));
                Positive(metadata, variant, "TABLEGEOMETRY", binary);
                var doc = Load(variant, binary); var item = Target(doc, "TABLEGEOMETRY"); var references = (IEnumerable<DxfObject>)item.GetType().GetProperty("References")!.GetValue(item)!;
                Check(references.Any(r => r.Handle == handle && r.CodeName == "ENDBLK"), "Metadata ENDBLK reference not retained.");
            });
            string attrib = $"geometry-attrib-{binary}"; Run(attrib, () =>
            {
                var authored = Load(raw, binary); var block = new Block("INDEPENDENT_ATTRIBUTES"); block.AttributeDefinitions.Add(new AttributeDefinition("ID") { Value = "Retained value" });
                var insert = new Insert(block); authored.Entities.Add(insert);
                using var output = new MemoryStream(); Check(authored.Save(output, binary), "Authored ATTRIB carrier save failed."); output.Position = 0; var carrier = DxfRawDocument.Load(output);
                string handle = Handle(Record(carrier, "ATTRIB")); carrier = Mutate(carrier, "TABLEGEOMETRY", t => Replace(t, 330, handle));
                Positive(attrib, carrier, "TABLEGEOMETRY", binary); var loaded = Load(carrier, binary);
                var owner = loaded.Entities.Inserts.Single(i => i.Block.Name == "INDEPENDENT_ATTRIBUTES");
                Check(!loaded.Entities.Remove(owner), "An INSERT owning a referenced ATTRIB was removable.");
            });
            string privateBody = $"geometry-private-context-{binary}"; Run(privateBody, () => Positive(privateBody, Mutate(raw, "TABLEGEOMETRY", t =>
            {
                t.InsertRange(Start(t) + 1, new[] { new DxfTag(102, "{INDEPENDENT_PRIVATE"), new DxfTag(5, "ABCDC"), new DxfTag(330, "ABCDD"), new DxfTag(1001, "NOT_AN_APPID"), new DxfTag(1000, "Private data"), new DxfTag(102, "}") });
            }), "TABLEGEOMETRY", binary, false));
            string xdata = $"geometry-xdata-{binary}"; Run(xdata, () => Positive(xdata, Mutate(raw, "TABLEGEOMETRY", t => t.AddRange(new[] { new DxfTag(1001, "ACAD"), new DxfTag(1000, "Independent trailing XData") })), "TABLEGEOMETRY", binary));
            Run($"geometry-owner-self-{binary}", () => Reject(Mutate(raw, "TABLEGEOMETRY", t => { int i = t.FindIndex(t => t.Code == 330); t[i] = new DxfTag(330, (string)t.First(t => t.Code == 5).Value); }), binary));
            Run($"geometry-owner-missing-{binary}", () => Reject(Mutate(raw, "TABLEGEOMETRY", t => { int i = t.FindIndex(t => t.Code == 330); t[i] = new DxfTag(330, "FFFABCD"); }), binary));
            Run($"geometry-dropped-symbol-identity-{binary}", () =>
            {
                var variant = AddStyle(raw); var style = variant.Sections.SelectMany(s => s.Records).Single(r => r.Name == "STYLE" && Handle(r) == "FEA123");
                var tags = style.Tags.Select(t => t.Code == 2 ? new DxfTag(2, "STANDARD") : t).ToList(); variant = variant.WithRecord(style, tags);
                variant = Mutate(variant, "TABLEGEOMETRY", t => Replace(t, 330, "FEA123")); Reject(variant, binary);
            });
            foreach ((short code, object value) in new (short, object)[] { (93, int.MinValue), (95, int.MaxValue), (40, -2.0e300), (43, -123.456), (10, -2.0e300), (90, 777), (91, 888) })
            {
                string name = $"geometry-raw-field-{code}-{binary}"; Run(name, () => Positive(name, Mutate(raw, "TABLEGEOMETRY", t => Replace(t, code, value)), "TABLEGEOMETRY", binary));
            }
        }
    }
    private static DxfRawDocument MapNative(string file)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Repo, "tests/fixtures/table-content/manifest.json")));
        var fixture = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("file").GetString() == file);
        string path = Path.GetFullPath(Path.Combine(Repo, "tests/fixtures/table-content", fixture.GetProperty("fixture").GetString()!));
        byte[] bytes = File.ReadAllBytes(path);
        if (path.EndsWith(".gz", StringComparison.Ordinal))
        { using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress); using var output = new MemoryStream(); gzip.CopyTo(output); bytes = output.ToArray(); }
        string expected = fixture.TryGetProperty("sha256", out var hash) ? hash.GetString()! : fixture.GetProperty("source_sha256").GetString()!;
        Check(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() == expected, "Native fixture hash changed.");
        using var input = new MemoryStream(bytes); return DxfRawDocument.Load(input);
    }
    private static void CellStyles()
    {
        foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
        foreach (bool binary in new[] { false, true })
        {
            string name = "map-native-" + file[..^4] + "-" + binary;
            Run(name, () =>
            {
                var raw = MapNative(file); Positive(name, raw, "CELLSTYLEMAP", binary);
                var doc = Load(raw, binary); var map = Target(doc, "CELLSTYLEMAP"); var style = doc.Objects.Items.OfType<DxfTableStyle>().Single();
                Check(ReferenceEquals(style.CellStyleMap, map) && ReferenceEquals(map.Owner, style.ExtensionDictionary), "TABLESTYLE map projection lost the exact extension child.");
                Check(ReferenceEquals(style.GetType().GetProperty("StoredCellStyleMap")!.GetValue(style), map), "Typed TABLESTYLE map convenience projection is missing.");
                var typed = (DxfStoredCellStyleMap)map; var rawTags = Record(raw, "CELLSTYLEMAP").Tags.ToList(); int index = 0;
                foreach (var entry in typed.Entries)
                {
                    int formatStart = rawTags.FindIndex(index, t => t.Code == 1 && Equals(t.Value, "TABLEFORMAT_BEGIN"));
                    int formatEnd = rawTags.FindIndex(formatStart, t => t.Code == 309 && Equals(t.Value, "TABLEFORMAT_END"));
                    Check(entry.FormatPayload.Select(Pair).SequenceEqual(rawTags.Skip(formatStart).Take(formatEnd - formatStart + 1).Select(Pair)), "Entry format projection changed.");
                    int entryStart = formatEnd + 1; Check(entry.Id == (int)rawTags[entryStart + 1].Value && entry.StoredType == (int)rawTags[entryStart + 2].Value && entry.Name == (string)rawTags[entryStart + 3].Value, "Entry scalar projection changed.");
                    index = entryStart + 5;
                    Check(((IList)entry.FormatPayload).IsReadOnly, "Entry format is mutable.");
                }
                Check(typed.References.Count > 0 && typed.References.All(target => ReferenceEquals(doc.GetObjectByHandle(target.Handle), target)), "Native reference projection lost source identity.");
            });
        }
        var source = MapNative("sample_AC1024_ascii.dxf");
        foreach (bool binary in new[] { false, true })
        {
            foreach (string operation in new[] { "erase", "ancestor-erase", "ancestor-clone", "foreign", "profile" }) Run($"map-{operation}-{binary}", () => Lifecycle(source, "CELLSTYLEMAP", binary, operation));
            foreach (string variant in new[] { "typed", "opaque", "arbitrary" }) Run($"map-style-{variant}-{binary}", () => Removal(source, "CELLSTYLEMAP", 340, binary, variant));
            Run($"map-missing-reference-{binary}", () => Reject(Mutate(source, "CELLSTYLEMAP", t => Replace(t, 340, "FFFABCDE")), binary));
            foreach (int count in new[] { -1, 2, 4, int.MaxValue }) Run($"map-count-{count}-{binary}", () => Reject(Mutate(source, "CELLSTYLEMAP", t => Replace(t, 90, count)), binary));
            foreach ((short code, object value) in new (short, object)[] { (90, int.MinValue), (91, int.MaxValue), (300, "TABLEFORMAT_BEGIN") })
            {
                string name = $"map-raw-entry-{code}-{binary}";
                Run(name, () => Positive(name, Mutate(source, "CELLSTYLEMAP", t =>
                {
                    int begin = t.FindIndex(x => x.Code == 1 && Equals(x.Value, "CELLSTYLE_BEGIN"));
                    int at = t.FindIndex(begin + 1, x => x.Code == code); Check(at >= 0, "Native entry field missing."); t[at] = new DxfTag(code, value);
                }), "CELLSTYLEMAP", binary));
            }
            string unknown = $"map-unknown-{binary}"; Run(unknown, () => Positive(unknown, Mutate(source, "CELLSTYLEMAP", t => t.Add(new DxfTag(100, "IndependentUnknownSubclass"))), "CELLSTYLEMAP", binary, false));
            string privateBody = $"map-private-context-{binary}"; Run(privateBody, () => Positive(privateBody, Mutate(source, "CELLSTYLEMAP", t =>
            {
                t.InsertRange(Start(t) + 1, new[] { new DxfTag(102, "{INDEPENDENT_PRIVATE"), new DxfTag(5, "ABCDC"), new DxfTag(330, "ABCDD"), new DxfTag(1001, "NOT_AN_APPID"), new DxfTag(1000, "Private data"), new DxfTag(102, "}") });
            }), "CELLSTYLEMAP", binary, false));
            string xdata = $"map-xdata-{binary}"; Run(xdata, () => Positive(xdata, Mutate(source, "CELLSTYLEMAP", t => t.AddRange(new[] { new DxfTag(1001, "ACAD"), new DxfTag(1000, "Independent trailing XData") })), "CELLSTYLEMAP", binary));
            Run($"map-surplus-outer-tag-{binary}", () => Reject(Mutate(source, "CELLSTYLEMAP", t => t.Add(new DxfTag(300, "Surplus known outer group"))), binary));
            string empty = $"map-empty-{binary}"; Run(empty, () => Positive(empty, Mutate(source, "CELLSTYLEMAP", t =>
            { int start = Start(t); t.RemoveRange(start + 2, t.Count - start - 2); t[start + 1] = new DxfTag(90, 0); }), "CELLSTYLEMAP", binary));
            Run($"map-duplicate-entry-fields-{binary}", () =>
            {
                var mutated = Mutate(source, "CELLSTYLEMAP", t =>
                {
                    for (int i = 0; i < t.Count; i++) if (t[i].Code == 1 && Equals(t[i].Value, "CELLSTYLE_BEGIN"))
                    { t[i + 1] = new DxfTag(90, -123); t[i + 2] = new DxfTag(91, int.MinValue); t[i + 3] = new DxfTag(300, "Repeated name"); }
                });
                string name = $"map-duplicates-{binary}"; Positive(name, mutated, "CELLSTYLEMAP", binary);
                var map = (DxfStoredCellStyleMap)Target(Load(mutated, binary), "CELLSTYLEMAP");
                Check(map.Entries.Count == 3 && map.Entries.All(e => e.Id == -123 && e.StoredType == int.MinValue && e.Name == "Repeated name"), "Raw duplicate entry projections changed.");
            });
            Run($"map-missing-known-frame-{binary}", () => Reject(Mutate(source, "CELLSTYLEMAP", t =>
            { int at = t.FindIndex(x => x.Code == 309 && Equals(x.Value, "TABLEFORMAT_END")); t.RemoveAt(at); }), binary));
            Run($"map-owner-self-{binary}", () => Reject(Mutate(source, "CELLSTYLEMAP", t => { int i = t.Take(Start(t)).Select((tag, index) => (tag, index)).Where(p => p.tag.Code == 330).Last().index; t[i] = new DxfTag(330, (string)t.First(t => t.Code == 5).Value); }), binary));
        }
    }

}
