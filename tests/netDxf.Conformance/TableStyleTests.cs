using System.IO.Compression;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterTableStyleTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
                Run($"table-style/native/{file}/{binary}", () => TableStyleNative(file, binary));
            foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
                Run($"table-style/full-native/{file}/{binary}", () => TableStyleNative(file, binary, true));
            foreach (string boundary in new[] { "older-profile", "private-class", "private-class-empty", "typed-class-conflict" })
                Run($"table-style/boundary/{boundary}/{binary}", () => TableStyleBoundary(boundary, binary));
            foreach (string decoy in new[] { "absent", "unknown-entity", "discarded-underlay", "dictionary-entity", "ignored-section", "private-identity" })
                Run($"table-style/source-identity/{decoy}/{binary}", () => TableStyleSourceIdentity(decoy, binary));
            foreach (bool normalized in new[] { false, true })
                Run($"table-style/opaque-source-identity/{normalized}/{binary}", () => TableStyleOpaqueSourceIdentity(normalized, binary));
            foreach (DxfVersion version in new[] { DxfVersion.AutoCad2004, DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
                Run($"table-style/lifecycle/{version}/{binary}", () => TableStyleLifecycle(version, binary));
            foreach (string variant in new[] { "leading280", "duplicate-margin", "missing-height", "duplicate-height", "private-application", "private-subclass", "unknown-first", "four-rows", "case-spelling" })
                Run($"table-style/projection/{variant}/{binary}", () => TableStyleProjection(variant, binary));
            foreach (string kind in new[] { "appid", "entity", "block-member", "attribute", "endblock" })
                Run($"table-style/removal/{kind}/{binary}", () => TableStyleRemoval(kind, binary));
            foreach (string variant in new[] { "unclosed-control", "unmatched-control", "bare-xdata" })
                Run($"table-style/malformed/{variant}/{binary}", () => TableStyleMalformed(variant, binary));
        }
        foreach (short code in new short[] { 320, 329, 330, 340, 350, 360, 369, 390, 399, 480, 481 })
            Run($"table-style/semantic-handle/{code}", () => TableStyleHandle(code));
    }
    private static List<DxfTag> TableStylePacket()
    {
        var tags = new List<DxfTag> { new(100, "AcDbTableStyle"), new(3, "Independent description"), new(70, (short)0), new(71, (short)0), new(40, 0.06), new(41, 0.06), new(280, (short)0), new(281, (short)0) };
        for (int i = 0; i < 3; i++) tags.AddRange(new DxfTag[] { new(7, "STYLE_\\U+0052EF"), new(140, 1.0 + i), new(170, (short)5), new(62, (short)0), new(63, (short)257), new(283, (short)0), new(90, 512), new(91, 0), new(1, "private raw format"), new(284, (short)1) });
        return tags;
    }
    private static DxfDocument TableStyleLoad(List<DxfTag> payload, bool binary, DxfVersion version = DxfVersion.AutoCad2018, string kind = "xrecord", Func<DxfRawDocument, DxfRawDocument>? mutate = null)
    {
        var raw = TableStyleInput(payload, binary, version, kind);
        if (mutate != null) raw = mutate(raw);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        return DxfDocument.Load(input) ?? throw new FormatException("TABLESTYLE input was rejected.");
    }
    private static DxfRawDocument TableStyleInput(List<DxfTag> payload, bool binary, DxfVersion version = DxfVersion.AutoCad2018, string kind = "xrecord")
    {
        var doc = new DxfDocument(version); doc.TextStyles.Add(new TextStyle("STYLE_REF", "txt.shx"));
        doc.TextStyles.Add(new TextStyle("PRIVATE_STYLE", "txt.shx"));
        var owner = new DxfDictionary(); var carrier = new DxfXRecord(); owner.Add("STYLE", carrier); doc.Objects.Root.Add("STYLE_CONTAINER", owner);
        DxfObject target;
        if (kind == "appid") target = doc.ApplicationRegistries.Add(new ApplicationRegistry("STYLE_APP"));
        else if (kind == "entity") { var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line); target = line; }
        else if (kind == "block-member") { var block = new Block("STYLE_TARGET"); var line = new Line(Vector3.Zero, Vector3.UnitX); block.Entities.Add(line); doc.Blocks.Add(block); target = line; }
        else if (kind == "attribute")
        {
            var block = new Block("ATTR_SOURCE"); block.AttributeDefinitions.Add(new AttributeDefinition("KEY")); var insert = new Insert(block); doc.Entities.Add(insert); target = insert.Attributes.Single();
        }
        else if (kind == "endblock")
        {
            var block = new Block("STYLE_TARGET"); doc.Blocks.Add(block);
            target = (DxfObject)typeof(Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(block)!;
        }
        else { var record = new DxfXRecord(); doc.Objects.Root.Add("STYLE_TARGET", record); target = record; }
        using var setup = new MemoryStream(); Check(doc.Save(setup, binary), "TABLESTYLE carrier setup"); setup.Position = 0;
        var raw = DxfRawDocument.Load(setup); var old = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == carrier.Handle));
        var prefix = old.Tags.TakeWhile(t => t.Code != 100).Select(t => t.Code == 0 ? new DxfTag(0, "TABLESTYLE") : t);
        raw = raw.WithRecord(old, prefix.Concat(payload.Select(t => t.ValueType == DxfTagValueType.Handle && (string)t.Value == "C0FFEE" ? new DxfTag(t.Code, target.Handle) : t)));
        return raw;
    }
    private static DxfTableStyle TableStyleObject(DxfDocument doc) => doc.Objects.Items.OfType<DxfTableStyle>().Single();
    private static DxfRawDocument TableStyleSave(DxfDocument doc, bool binary, string name)
    {
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "TABLESTYLE output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), output.ToArray()); output.Position = 0;
        return DxfRawDocument.Load(output);
    }
    private static DxfRawRecord TableStyleRaw(DxfRawDocument doc, string name = "TABLESTYLE") => doc.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == name);
    // Keep native object and formatting resource records intact. The source STYLE/LTYPE
    // common owners are mapped to their carrier tables. External entity reactors use
    // explicit placeholders; complete drawings are qualified separately.
    private static DxfRawDocument TableStyleNativeCarrier(DxfRawDocument source)
    {
        var native = TableStyleRaw(source); var map = TableStyleRaw(source, "CELLSTYLEMAP");
        var extension = SourceReferenceRecord(source, (string)native.Tags.Single(t => t.Code == 360).Value);
        string ownerHandle = (string)native.Tags.TakeWhile(t => t.Code != 100).Last(t => t.Code == 330).Value;
        var owner = SourceReferenceRecord(source, ownerHandle);
        var nativeRecords = new[] { native, extension, map, owner }.Concat(extension.Tags.Where(t => t.Code is 350 or 360)
            .Select(t => SourceReferenceRecord(source, (string)t.Value)).Where(r => !ReferenceEquals(r, map))).Distinct().ToArray();
        var identities = nativeRecords.Select(r => (string)r.Tags.Single(t => t.Code == 5).Value).Append("C").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var external = nativeRecords.SelectMany(r => r.Tags.TakeWhile(t => t.Code != 100)).Where(t => t.Code == 330)
            .Select(t => (string)t.Value).Where(h => h != "0" && !identities.Contains(h)).Distinct().ToArray();
        var seed = new DxfDocument(source.Version); seed.Comments.Clear(); string root = seed.Objects.Root.Handle, standard = seed.TextStyles["Standard"].Handle;
        var sourceLinetypes = map.Tags.Where(t => t.Code == 340 && Convert.ToUInt64((string)t.Value, 16) != 0)
            .Select(t => SourceReferenceRecord(source, (string)t.Value)).Where(r => r.Name == "LTYPE").Distinct().ToArray();
        var linetypeHandles = sourceLinetypes.ToDictionary(r => seed.Linetypes[(string)r.Tags.Single(t => t.Code == 2).Value].Handle,
            r => (string)r.Tags.Single(t => t.Code == 5).Value, StringComparer.OrdinalIgnoreCase);
        using var bytes = new MemoryStream(); Check(seed.Save(bytes), "native TABLESTYLE extraction seed"); bytes.Position = 0;
        var raw = DxfRawDocument.Load(bytes);
        var handles = raw.Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records).SelectMany(r => r.Tags.TakeWhile(t => t.Code != 100))
            .Where(t => t.Code is 5 or 105).Select(t => (string)t.Value).Distinct().ToDictionary(h => h,
                h => h == root ? "C" : h == standard ? "11" : linetypeHandles.TryGetValue(h, out string? nativeHandle) ? nativeHandle : (Convert.ToInt64(h, 16) + 0x10000).ToString("X"), StringComparer.OrdinalIgnoreCase);
        raw = raw.WithTags(raw.Tags.Select(t => t.ValueType == DxfTagValueType.Handle && handles.TryGetValue((string)t.Value, out string? h) ? new DxfTag(t.Code, h) : t));
        var resource = SourceReferenceRecord(raw, "11"); string resourceOwner = (string)resource.Tags.First(t => t.Code == 330).Value;
        var nativeResource = SourceReferenceRecord(source, "11");
        raw = raw.WithRecord(resource, nativeResource.Tags.Select(t => t.Code == 330 ? new DxfTag(330, resourceOwner) : t));
        foreach (var sourceLinetype in sourceLinetypes)
        {
            var carrierLinetype = SourceReferenceRecord(raw, (string)sourceLinetype.Tags.Single(t => t.Code == 5).Value);
            string tableOwner = (string)carrierLinetype.Tags.First(t => t.Code == 330).Value;
            raw = raw.WithRecord(carrierLinetype, sourceLinetype.Tags.Select(t => t.Code == 330 ? new DxfTag(330, tableOwner) : t));
        }
        var rootRecord = SourceReferenceRecord(raw, "C");
        var entries = new List<DxfTag> { new(3, "ACAD_TABLESTYLE"), new(360, ownerHandle) };
        foreach (string handle in external) entries.AddRange(new DxfTag[] { new(3, "NATIVE_EXTERNAL_" + handle), new(360, handle) });
        raw = raw.WithRecord(rootRecord, rootRecord.Tags.Concat(entries));
        var additions = nativeRecords.SelectMany(r => r.Tags).ToList();
        foreach (string handle in external) additions.AddRange(new DxfTag[] { new(0, "ACDBPLACEHOLDER"), new(5, handle), new(330, "C"), new(100, "AcDbPlaceHolder") });
        int end = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1;
        return raw.WithTags(raw.Tags.Take(end).Concat(additions).Concat(raw.Tags.Skip(end)));
    }
    private static void TableStyleNative(string file, bool binary, bool full = false)
    {
        var raw = StoredTableSource(file); var carrier = full ? raw : TableStyleNativeCarrier(raw);
        using var input = new MemoryStream(); carrier.Save(input, binary); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Native TABLESTYLE drawing failed to load"); var style = TableStyleObject(doc);
        if (full) Check(doc.Entities.StoredTables.Any(t => t.References.Contains(style)), "native TABLE binds exact accepted TABLESTYLE source token");
        Equal(raw.Version, style.SourceVersion, "native TABLESTYLE profile"); Equal(3, style.Rows.Count, "native ordered row packets");
        Check(style.Header != null, "recognized native header is projected");
        Equal(file.Contains("AC1024") ? (short?)0 : null, style.Header!.StoredVersion, "leading format version is distinct from title suppression");
        if (style.Header != null) Equal(file.StartsWith("acad_table_") ? 1.5 : 0.06, style.Header.HorizontalCellMargin, "native header margin");
        Check(style.Rows.All(r => r.Values != null && ReferenceEquals(r.TextStyle, doc.GetObjectByHandle("11"))), "native exact STYLE identity and row scalars");
        Equal(file.StartsWith("acad_table_") ? 6.0 : 0.25, style.Rows[1].Values!.TextHeight, "native second ordered row height");
        Check(style.CellStyleMap != null && ReferenceEquals(style.CellStyleMap.Owner, style.ExtensionDictionary), "native stored map ownership");
        Check(ReferenceEquals(style.ExtensionDictionary!["ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP"], style.CellStyleMap), "native map dictionary slot");
        var original = TableStyleRaw(raw); int first = original.Tags.ToList().FindIndex(t => t.Code == 100);
        Check(OwnershipTagValues(original.Tags.Skip(first)).SequenceEqual(OwnershipTagValues(style.Tags)), "complete native style packet loaded");
        var saved = TableStyleSave(doc, binary, $"table-style-{(full ? "full-native" : "native")}-{file}-{binary}.dxf");
        var after = TableStyleRaw(saved); int afterFirst = after.Tags.ToList().FindIndex(t => t.Code == 100);
        Check(OwnershipTagValues(original.Tags.Skip(first)).SequenceEqual(OwnershipTagValues(after.Tags.Skip(afterFirst))), "native style packet unchanged");
        var beforeMap = TableStyleRaw(raw, "CELLSTYLEMAP"); var afterMap = TableStyleRaw(saved, "CELLSTYLEMAP");
        Check(OwnershipTagValues(beforeMap.Tags.SkipWhile(t => t.Code != 100)).SequenceEqual(OwnershipTagValues(afterMap.Tags.SkipWhile(t => t.Code != 100))), "native owned map packet unchanged");
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(style));
        Equal(seed, OwnershipSeed(doc), "opaque-map erasure rejection retained seed"); Equal(count, doc.Objects.Items.Count, "opaque-map erasure rejection retained graph");
    }
    private static void TableStyleLifecycle(DxfVersion version, bool binary)
    {
        var doc = TableStyleLoad(TableStylePacket(), binary, version); var style = TableStyleObject(doc); var resource = doc.TextStyles["STYLE_REF"];
        Check(style.References.Count(r => ReferenceEquals(r, resource)) == 3, "three exact STYLE dependencies");
        Check(doc.TextStyles.GetReferences(resource).Any(r => ReferenceEquals(r.Reference, style)), "STYLE dependency query");
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        Check(!doc.TextStyles.Remove(resource), "STYLE removal refused"); Equal(seed, OwnershipSeed(doc), "failed STYLE removal retained seed");
        var owner = (DxfDictionary)style.Owner; var foreign = new DxfDocument(version); _ = foreign.Objects.Root; long foreignSeed = OwnershipSeed(foreign);
        Throws<NotSupportedException>(() => doc.Objects.Clone(owner, doc.Objects.Root, "COPY"));
        Throws<NotSupportedException>(() => foreign.Objects.Clone(owner, foreign.Objects.Root, "COPY"));
        Equal(seed, OwnershipSeed(doc), "clone refusal retained source allocation"); Equal(count, doc.Objects.Items.Count, "clone refusal retained source membership");
        Equal(foreignSeed, OwnershipSeed(foreign), "clone refusal retained destination allocation"); Check(!foreign.Objects.Root.Contains("COPY"), "clone refusal retained destination names");
        Throws<ArgumentException>(() => foreign.Objects.Root.Add("FOREIGN_STYLE", style));
        resource.Name = "RENAMED_Ω_STYLE";
        var saved = TableStyleSave(doc, binary, $"table-style-lifecycle-{version}-{binary}.dxf");
        Check(TableStyleRaw(saved).Tags.Where(t => t.Code == 7).All(t => (string)t.Value == "RENAMED_Ω_STYLE" || (string)t.Value == "RENAMED_\\U+03A9_STYLE"), "bound STYLE renamed in payload");
        seed = OwnershipSeed(doc);
        doc.DrawingVariables.AcadVer = version == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018;
        using var output = new MemoryStream(); bool rejected; try { rejected = !doc.Save(output, binary); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && output.Length == 0, "cross-profile TABLESTYLE rejected before output"); Equal(seed, OwnershipSeed(doc), "profile refusal retained allocation");
        doc.DrawingVariables.AcadVer = version;
        string handle = style.Handle; doc.Objects.EraseOwnedTree(style); Check(style.IsErased && doc.GetObjectByHandle(handle) == null, "standalone style erase is terminal");
        Check(doc.TextStyles.Remove(resource), "erased STYLE dependency released");
        Throws<InvalidOperationException>(() => owner.Add("RESURRECT", style)); Check(!owner.Contains("RESURRECT"), "erased style cannot reattach");
    }
    private static void TableStyleProjection(string variant, bool binary)
    {
        var payload = TableStylePacket(); int first = payload.FindIndex(t => t.Code == 7);
        if (variant == "leading280") payload.Insert(1, new DxfTag(280, (short)0));
        if (variant == "duplicate-margin") payload.Insert(first, new DxfTag(40, 3.0));
        if (variant == "missing-height") payload.RemoveAt(first + 1);
        if (variant == "duplicate-height") payload.Insert(first + 1, new DxfTag(140, 3.0));
        if (variant == "private-application") payload.InsertRange(first + 1, new DxfTag[] { new(102, "{PRIVATE"), new(7, "PRIVATE_STYLE"), new(140, 99.0), new(102, "}") });
        if (variant == "private-subclass") payload.AddRange(new DxfTag[] { new(100, "PrivateStyle"), new(7, "PRIVATE_STYLE"), new(310, new byte[] { 0, 255 }) });
        if (variant == "unknown-first") payload[0] = new DxfTag(100, "PrivateStyle");
        if (variant == "four-rows") payload.AddRange(payload.Skip(first).Take(10).ToArray());
        if (variant == "case-spelling") payload = payload.Select(t => t.Code == 7 ? new DxfTag(7, "style_\\U+0072ef") : t).ToList();
        var doc = TableStyleLoad(payload, binary);
        if (variant == "unknown-first")
        {
            var opaque = doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o => o.CodeName == "TABLESTYLE");
            Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(opaque.Tags)), "unknown subclass remains wholly opaque");
            TableStyleSave(doc, binary, $"table-style-projection-{variant}-{binary}.dxf"); return;
        }
        var style = TableStyleObject(doc);
        if (variant is "duplicate-margin" or "unknown-first") Check(style.Header == null, "ambiguous header declined");
        if (variant == "leading280") Equal((short?)0, style.Header!.StoredVersion, "recognized version-zero prefix");
        if (variant is "unknown-first" or "four-rows") Equal(0, style.Rows.Count, "unknown row schema declined");
        else
        {
            Equal(3, style.Rows.Count, "known ordered row packets remain available");
            Equal(variant is "missing-height" or "duplicate-height", style.Rows[0].Values == null, "incomplete row values declined");
        }
        Check(!style.References.Contains(doc.TextStyles["PRIVATE_STYLE"]), "private group7 never binds STYLE");
        Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(style.Tags)), "complete source packet retained");
        var saved = TableStyleSave(doc, binary, $"table-style-projection-{variant}-{binary}.dxf");
        Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(TableStyleRaw(saved).Tags.SkipWhile(t => t.Code != 100))), "unchanged style projection preserved complete wire spelling");
    }
    private static void TableStyleRemoval(string kind, bool binary)
    {
        var payload = TableStylePacket(); payload.Add(new DxfTag(340, "C0FFEE"));
        var doc = TableStyleLoad(payload, binary, kind: kind); var style = TableStyleObject(doc); long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        if (kind == "appid") Check(!doc.ApplicationRegistries.Remove(doc.ApplicationRegistries["STYLE_APP"]), "referenced APPID removal rejected");
        else if (kind == "entity") Check(!doc.Entities.Remove(doc.Entities.Lines.Single()), "referenced entity removal rejected");
        else if (kind == "block-member") { var block = doc.Blocks["STYLE_TARGET"]; Check(!block.Entities.Remove(block.Entities.Single()), "referenced block member retained"); Check(!doc.Blocks.Remove(block), "referenced member's block retained"); }
        else if (kind == "endblock") Check(!doc.Blocks.Remove(doc.Blocks["STYLE_TARGET"]), "referenced ENDBLK retained with owner");
        else
        {
            var insert = doc.Entities.Inserts.Single(); Check(style.References.Contains(insert.Attributes.Single()), "retained ATTRIB identity bound");
            Check(!doc.Entities.Remove(insert), "referenced attribute's INSERT retained");
            insert.Block.AttributeDefinitions.Clear(); int callbacks = 0; insert.AttributeRemoved += (_, _) => callbacks++;
            Throws<InvalidOperationException>(() => insert.Sync()); Equal(0, callbacks, "rejected Sync did not raise removal callback"); Equal(1, insert.Attributes.Count, "rejected Sync retained ATTRIB");
        }
        Equal(seed, OwnershipSeed(doc), "removal rejection retained allocation"); Equal(count, doc.Objects.Items.Count, "removal rejection retained object graph");
        TableStyleSave(doc, binary, $"table-style-removal-{kind}-{binary}.dxf");
    }
    private static void TableStyleHandle(short code)
    {
        var packet = TableStylePacket(); packet.Add(new DxfTag(code, "C0FFEE")); var doc = TableStyleLoad(packet, false); var style = TableStyleObject(doc); var target = (DxfXRecord)doc.Objects.Root["STYLE_TARGET"];
        Equal(code >= 330, style.References.Contains(target), "semantic pointer classification");
        if (code >= 330) Throws<InvalidOperationException>(() => doc.Objects.EraseOwnedTree(target));
        else { doc.Objects.EraseOwnedTree(target); Check(target.IsErased, "arbitrary handle does not protect erasure"); }
    }
    private static void TableStyleBoundary(string variant, bool binary)
    {
        if (variant == "typed-class-conflict")
        {
            var typed = TableStyleLoad(TableStylePacket(), binary); typed.Classes.Add(new DxfClass("TABLESTYLE", "PrivateCpp", "PrivateApp"));
            long seed = OwnershipSeed(typed); int count = typed.Objects.Items.Count;
            using var output = new MemoryStream(); bool rejected; try { rejected = !typed.Save(output, binary); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && output.Length == 0, "typed CLASS conflict rejected before bytes");
            Equal(seed, OwnershipSeed(typed), "CLASS conflict retained allocation"); Equal(count, typed.Objects.Items.Count, "CLASS conflict retained membership"); return;
        }
        DxfDocument doc;
        if (variant == "older-profile")
        {
            doc = TableStyleLoad(TableStylePacket(), binary, DxfVersion.AutoCad2000);
            Check(doc.Objects.Items.Single(o => o.CodeName == "TABLESTYLE") is DxfOpaqueObject, "unqualified R2000 remains opaque");
        }
        else
        {
            doc = variant == "private-class" ? TableStyleLoad(new List<DxfTag> { new(100, "PrivateTableStyle"), new(1, "private payload") }, binary) : new DxfDocument(DxfVersion.AutoCad2018);
            doc.Classes.Add(new DxfClass("TABLESTYLE", "PrivateCpp", "PrivateApp") { InstanceCount = 73, ProxyFlags = 17, WasProxy = true });
        }
        var saved = TableStyleSave(doc, binary, $"table-style-boundary-{variant}-{binary}.dxf");
        if (variant == "older-profile") Check(OwnershipTagValues(TableStylePacket()).SequenceEqual(OwnershipTagValues(TableStyleRaw(saved).Tags.SkipWhile(t => t.Code != 100))), "R2000 opaque payload retained");
        else
        {
            var definition = saved.Sections.Single(s => s.Name == "CLASSES").Records.Single(r => r.Tags.Any(t => t.Code == 1 && Equals(t.Value, "TABLESTYLE")));
            Check(definition.Tags.Any(t => t.Code == 2 && Equals(t.Value, "PrivateCpp")) && definition.Tags.Any(t => t.Code == 91 && Equals(t.Value, 73)), "untyped private CLASS retained");
        }
    }
    private static void TableStyleSourceIdentity(string decoy, bool binary)
    {
        const string missing = "C0FFEE01";
        var payload = TableStylePacket(); payload.Add(new DxfTag(340, missing));
        if (decoy is "unknown-entity" or "dictionary-entity")
        {
            SourceReferenceRejectMalformedEntity(SourceReferenceDecoy(TableStyleInput(payload, binary), missing, decoy), binary);
            return;
        }
        var doc = TableStyleLoad(payload, binary, mutate: raw =>
        {
            if (decoy != "private-identity") return SourceReferenceDecoy(raw, missing, decoy);
            var line = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "STYLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "STYLE_REF")));
            return raw.WithRecord(line, line.Tags.Concat(new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(5, missing), new DxfTag(102, "}") }));
        });
        var style = TableStyleObject(doc);
        Check(!style.References.Any(r => string.Equals(r.Handle, missing, StringComparison.OrdinalIgnoreCase)), "discarded or private identity did not bind semantic reference");
        Check(style.Rows.All(r => ReferenceEquals(r.TextStyle, doc.TextStyles["STYLE_REF"])), "real physical STYLE token still binds by name");
        Check(doc.TextStyles["STYLE_REF"].Handle != missing, "private group5 cannot replace actual STYLE identity");
        TableStyleSave(doc, !binary, $"table-style-identity-{decoy}-{binary}.dxf");
    }
    private static void TableStyleOpaqueSourceIdentity(bool normalized, bool binary)
    {
        const string handle = "C0FFEE01";
        string spelling = normalized ? "000" + handle.ToLowerInvariant() : handle;
        var payload = TableStylePacket(); payload.Add(new DxfTag(340, spelling));
        var doc = TableStyleLoad(payload, binary, mutate: raw =>
        {
            raw = SourceReferenceOpaqueTarget(raw, handle, normalized);
            using var input = new MemoryStream(); raw.Save(input, binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"qualified-style-source-{normalized}-{binary}.dxf"), input.ToArray());
            Check(System.Text.Encoding.ASCII.GetString(input.ToArray()).Contains(spelling, StringComparison.Ordinal), "physical input must retain the requested source handle spelling");
            return raw;
        });
        var target = doc.GetObjectByHandle(handle) as DxfOpaqueEntity;
        Check(target != null && ReferenceEquals(doc.Entities.All.Single(), target), "opaque physical source must bind its actual registered identity");
        var style = TableStyleObject(doc);
        Check(style.References.Contains(target!), "TABLESTYLE lost its qualified opaque semantic reference");
        Check(style.Rows.All(r => ReferenceEquals(r.TextStyle, doc.TextStyles["STYLE_REF"])), "independent STYLE name reference changed");
        Check(!doc.Entities.Remove(target!), "TABLESTYLE must protect the actual opaque target from removal");
        var saved = TableStyleSave(doc, !binary, $"table-style-opaque-identity-{normalized}-{binary}.dxf");
        using var input = new MemoryStream(); saved.Save(input, !binary); input.Position = 0;
        var again = DxfDocument.Load(input) ?? throw new Exception("opaque source reference reload");
        var reloaded = again.GetObjectByHandle(handle) as DxfOpaqueEntity;
        Check(reloaded != null && TableStyleObject(again).References.Contains(reloaded), "opaque actual identity lost on cross-transport reload");
        Check(!again.Entities.Remove(reloaded!), "reloaded TABLESTYLE reference failed to protect opaque target");
    }
    private static void TableStyleMalformed(string variant, bool binary)
    {
        var packet = TableStylePacket();
        if (variant == "unclosed-control") packet.Add(new DxfTag(102, "{PRIVATE"));
        if (variant == "unmatched-control") packet.Add(new DxfTag(102, "}"));
        if (variant == "bare-xdata") packet.Add(new DxfTag(1000, "missing registry"));
        bool rejected = false;
        try { TableStyleLoad(packet, binary); }
        catch (Exception error) when (error is FormatException || error is NotSupportedException || error is InvalidDataException) { rejected = true; }
        Check(rejected, "invalid TABLESTYLE carrier rejected explicitly");
    }
}
