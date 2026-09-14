using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawObjectStoreTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (short cloning in Enumerable.Range(0, 6).Select(x => (short)x))
                {
                    short c = cloning;
                    Run($"objects/workflow/{v}/{b}/{c}", () => ObjectStoreWorkflow(v, b, (DxfDuplicateRecordCloning)c));
                }
                Run($"objects/extension/{v}/{b}", () => ObjectStoreExtension(v, b));
                Run($"objects/immutability/{v}/{b}", () => ObjectStoreImmutability(v, b));
                Run($"objects/deletion/{v}/{b}", () => ObjectStoreDeletion(v, b));
                Run($"objects/empty-sections/{v}/{b}", () => ObjectStoreEmptySections(v, b));
            }
        foreach (int scenario in Enumerable.Range(0, 18))
        {
            int s = scenario; Run($"objects/api/failure/{s}", () => ObjectStoreFailure(s));
        }
        foreach (string text in new[] { "Plain", "Żółć Ω", "emoji 🧪", @"literal \U+0041", "{ACAD_REACTORS}", " spaced name " })
        {
            string t = text; Run($"objects/unicode/{t}", () => ObjectStoreUnicode(t));
        }
        Run("objects/long-owned-chain", ObjectStoreLongChain);
        RegisterRawObjectBoundaryTests();
    }

    private static (DxfRawDocument Raw, string Line, string Circle, string Block) ObjectStoreSource(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version);
        var line = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        var circle = new Circle(new Vector3(7, 8, 9), 2.5);
        doc.Entities.Add(line); doc.Entities.Add(circle);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Object source save failed."); stream.Position = 0;
        var raw = DxfRawDocument.Load(stream); var index = DxfRawHandleIndex.Create(raw);
        string block = index.GetOccurrences(index.FindDefinitions(line.Handle).Single().Record)
            .Single(o => o.Role == DxfRawHandleRole.Owner).CanonicalHandle;
        return (raw, line.Handle, circle.Handle, block);
    }
    private static T ObjectOf<T>(DxfRawObjectStore store, string handle) where T : DxfRawStoredObject =>
        store.Get(handle) as T ?? throw new InvalidOperationException($"{handle} is not {typeof(T).Name}: {(store.Get(handle) as DxfRawOpaqueStoredObject)?.Reason}");

    private static List<DxfTag> ObjectData(string target) => new()
    {
        new(1, "raw application value"), new(10, 1.0000000000000002), new(20, -0.0), new(30, -13.25),
        new(70, short.MinValue), new(90, int.MaxValue), new(160, long.MinValue), new(290, true),
        new(310, Enumerable.Range(0, 127).Select(i => (byte)i).ToArray()), new(330, target), new(320, "DEAD"),
        new(100, "application subclass-like text"), new(101, "Embedded Object"), new(102, "{application payload"), new(280, (short)93)
    };
    private static void AssertObjectTags(IReadOnlyList<DxfTag> a, IReadOnlyList<DxfTag> b, string message)
    {
        Equal(a.Count, b.Count, message + " count");
        for (int i = 0; i < a.Count; ++i)
        {
            Equal(a[i].Code, b[i].Code, message + " code");
            if (a[i].Value is byte[] bytes) Check(bytes.SequenceEqual((byte[])b[i].Value), message + " bytes");
            else if (a[i].Value is double d) SameDoubleBits(d, (double)b[i].Value, message + " double");
            else Equal(a[i].Value, b[i].Value, message + " value");
        }
    }
    private static void ObjectStoreWorkflow(DxfVersion version, bool binary, DxfDuplicateRecordCloning cloning)
    {
        var source = ObjectStoreSource(version, binary); var store = DxfRawObjectStore.Open(source.Raw);
        using var tx = store.BeginEdit(); string root = tx.EnsureRootDictionary();
        string custom = tx.CreateDictionary(root, "NETDXF_OBJECT_TESTS");
        string placeholder = tx.CreatePlaceholder(custom, "Placeholder");
        var data = ObjectData(placeholder);
        string xrecord = tx.CreateXRecord(custom, "Record", data, cloning);
        string variable = tx.CreateVariable(custom, "Variable", "Żółć Ω 🧪 \\U+0041", 0);
        string buffer = tx.CreateIdBuffer(custom, "Buffer", new[] { source.Line, "0", source.Circle, source.Line });
        string defaults = tx.CreateDictionary(custom, "Fallback", withDefault: true);
        tx.SetDictionaryFlags(custom, true, cloning);
        tx.SetDictionaryEntry(custom, "Record alias", xrecord, true);
        tx.RenameEntry(custom, "variable", "VARIABLE");
        string clone = tx.CloneDictionaryTree(custom, root, "NETDXF_OBJECT_COPY");
        string? sort = null;
        if (version >= DxfVersion.AutoCad2004)
            sort = tx.SetDrawOrder(source.Block, new[] { new DxfRawSortOrderEntry(source.Line, "0"), new DxfRawSortOrderEntry(source.Circle, "FFFFFFFFFFFFFFFE") });
        var doc = tx.Commit(); Check(!ReferenceEquals(doc, source.Raw), "Changed transaction returned original.");
        Throws<ObjectDisposedException>(() => tx.CreatePlaceholder(custom, "closed"));
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            var current = DxfRawObjectStore.Open(doc); var dictionary = ObjectOf<DxfRawDictionary>(current, custom);
            Equal(6, dictionary.Entries.Count, "Dictionary alias count");
            Equal(cloning, dictionary.CloningFlag, "Cloning policy");
            Equal(xrecord, dictionary.Find("record alias")!.Handle, "Alias target");
            Check(dictionary.Find("RECORD ALIAS")!.IsHardOwner, "Per-entry hard link lost.");
            Equal("VARIABLE", dictionary.Find("variable")!.Name, "Case-only name lost");
            var record = ObjectOf<DxfRawXRecord>(current, xrecord); AssertObjectTags(data, record.Data, "XRECORD exact payload");
            Equal(cloning, record.CloningFlag, "XRECORD cloning");
            Equal("Żółć Ω 🧪 \\U+0041", ObjectOf<DxfRawDictionaryVariable>(current, variable).Value, "Portable variable text");
            Check(ObjectOf<DxfRawIdBuffer>(current, buffer).Handles.SequenceEqual(new[] { source.Line, "0", source.Circle, source.Line }), "IDBUFFER order/nulls/duplicates changed.");
            var defaultDictionary = ObjectOf<DxfRawDictionary>(current, defaults);
            Check(defaultDictionary.HasDefault && defaultDictionary.DefaultHandle == defaultDictionary.Find("Default")!.Handle, "Default pointer changed.");
            Check(current.Get(defaultDictionary.DefaultHandle) is DxfRawPlaceholder, "Default placeholder lost.");
            var copiedDictionary = ObjectOf<DxfRawDictionary>(current, clone);
            string copiedRecord = copiedDictionary.Find("Record")!.Handle, copiedPlaceholder = copiedDictionary.Find("Placeholder")!.Handle;
            Check(copiedRecord != xrecord && copiedPlaceholder != placeholder, "Subtree identities were reused.");
            Equal(copiedRecord, copiedDictionary.Find("Record alias")!.Handle, "Clone aliases no longer share a target");
            var copiedData = ObjectOf<DxfRawXRecord>(current, copiedRecord).Data;
            Equal(copiedPlaceholder, (string)copiedData.Single(t => t.Code == 330).Value, "Class-aware XRECORD reference remap");
            Equal("DEAD", (string)copiedData.Single(t => t.Code == 320).Value, "Arbitrary handle must not translate");
            if (sort != null)
            {
                var sorting = ObjectOf<DxfRawSortentsTable>(current, sort);
                Equal(source.Block, sorting.BlockRecordHandle, "Draw order block");
                Equal("0", sorting.Entries[0].SortHandle, "Null sort key is not an identity");
                Equal("FFFFFFFFFFFFFFFE", sorting.Entries[1].SortHandle, "Wide sort key");
            }
            using var output = new MemoryStream(); doc.Save(output, binary);
            if (cycle == 1 && cloning == DxfDuplicateRecordCloning.KeepExisting)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"object-store-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfRawDocument.Load(output);
        }
        Check(store.RootDictionary!.Find("NETDXF_OBJECT_TESTS") == null && source.Raw.HasOriginalBytes, "Transaction mutated original snapshot.");
        // Removing one alias cannot delete a still-referenced record; unlink only is explicit.
        using var edits = DxfRawObjectStore.Open(doc).BeginEdit();
        Throws<InvalidOperationException>(() => edits.RemoveEntry(custom, "Record alias", true));
        Check(((DxfRawDictionary)edits.Get(custom)).Find("Record alias") != null, "Failed delete did not roll back its unlink.");
        Check(edits.RemoveEntry(custom, "Record alias"), "Explicit unlink failed.");
        edits.SetXRecord(xrecord, new[] { new DxfTag(1, "edited") }, DxfDuplicateRecordCloning.UseClone);
        edits.SetVariable(variable, null, null); edits.SetIdBuffer(buffer, Array.Empty<string>());
        var edited = DxfRawObjectStore.Open(edits.Commit());
        Equal("edited", (string)ObjectOf<DxfRawXRecord>(edited, xrecord).Data.Single().Value, "Payload editing");
        Equal(15, ObjectOf<DxfRawXRecord>(edited, ObjectOf<DxfRawDictionary>(edited, clone).Find("Record")!.Handle).Data.Count, "Clone edit isolation");
        Check(ObjectOf<DxfRawDictionaryVariable>(edited, variable).Value == null && !ObjectOf<DxfRawDictionaryVariable>(edited, variable).SchemaNumber.HasValue, "Optional variable fields invented.");
    }

    private static void ObjectStoreExtension(DxfVersion version, bool binary)
    {
        var source = ObjectStoreSource(version, binary); using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string ext = tx.EnsureExtensionDictionary(source.Line); Equal(ext, tx.EnsureExtensionDictionary(source.Line), "Extension duplicated");
        string data = tx.CreateXRecord(ext, "USER_DATA", ObjectData(source.Circle));
        var doc = tx.Commit(); var index = DxfRawHandleIndex.Create(doc);
        var line = index.FindDefinitions(source.Line).Single().Record;
        Equal(ext, index.GetOccurrences(line).Single(t => t.Role == DxfRawHandleRole.ExtensionDictionary).CanonicalHandle, "Common extension pointer");
        var store = DxfRawObjectStore.Open(doc); Equal(source.Line, ObjectOf<DxfRawDictionary>(store, ext).OwnerHandle, "Extension owner");
        using var remove = store.BeginEdit(); Check(remove.RemoveExtensionDictionary(source.Line, true), "Extension removal failed.");
        var result = DxfRawObjectStore.Open(remove.Commit()); Check(result.Get(ext) == null && result.Get(data) == null, "Owned extension tree not deleted.");
        var original = source.Raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE");
        var actual = result.Document.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE");
        AssertObjectTags(original.Tags, actual.Tags, "Detach restored unchanged line fields");
    }
    private static void ObjectStoreImmutability(DxfVersion version, bool binary)
    {
        var source = ObjectStoreSource(version, binary); var store = DxfRawObjectStore.Open(source.Raw);
        using (var empty = store.BeginEdit()) Check(ReferenceEquals(source.Raw, empty.Commit()), "No-op lost original bytes.");
        using var create = store.BeginEdit(); string root = create.EnsureRootDictionary();
        string dictionary = create.CreateDictionary(root, "TEST"); string data = create.CreateXRecord(dictionary, "data", ObjectData(source.Line));
        var first = create.Commit(); var firstStore = DxfRawObjectStore.Open(first);
        using (var noOp = firstStore.BeginEdit())
        {
            var d = ObjectOf<DxfRawDictionary>(firstStore, dictionary); var x = ObjectOf<DxfRawXRecord>(firstStore, data);
            noOp.SetDictionaryFlags(dictionary, d.HardOwnerFlag, d.CloningFlag);
            noOp.SetDictionaryEntry(dictionary, "data", data); noOp.RenameEntry(dictionary, "data", "data");
            noOp.SetXRecord(data, x.Data, x.CloningFlag!.Value);
            Check(ReferenceEquals(first, noOp.Commit()), "Semantic no-op re-encoded records.");
        }
        using var later = firstStore.BeginEdit(); later.SetXRecord(data, new[] { new DxfTag(1, "later") }); var second = later.Commit();
        var beforeLine = first.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE");
        var afterLine = second.Sections.SelectMany(s => s.Records).Single(r => r.Name == "LINE");
        Check(beforeLine.Tags.Zip(afterLine.Tags).All(pair => ReferenceEquals(pair.First, pair.Second)), "Unrelated raw tag objects were replaced.");
        byte[] bytes = (byte[])ObjectOf<DxfRawXRecord>(firstStore, data).Data.Single(t => t.Code == 310).Value; bytes[0] = 201;
        Equal((byte)0, ((byte[])ObjectOf<DxfRawXRecord>(firstStore, data).Data.Single(t => t.Code == 310).Value)[0], "Payload getter aliases bytes");
        using var rollback = firstStore.BeginEdit(); rollback.SetXRecord(data, new[] { new DxfTag(1, "discard") }); rollback.Dispose();
        AssertObjectTags(ObjectData(source.Line), ObjectOf<DxfRawXRecord>(firstStore, data).Data, "Disposed edit changed source");
    }
    private static void ObjectStoreDeletion(DxfVersion version, bool binary)
    {
        var source = ObjectStoreSource(version, binary); using var create = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = create.EnsureRootDictionary(), tree = create.CreateDictionary(root, "TREE"), nested = create.CreateDictionary(tree, "NESTED", withDefault: true);
        string leaf = create.CreateXRecord(nested, "LEAF", Array.Empty<DxfTag>());
        string external = create.CreateIdBuffer(root, "EXTERNAL", new[] { leaf }); var first = create.Commit();
        using var tx = DxfRawObjectStore.Open(first).BeginEdit();
        Throws<InvalidOperationException>(() => tx.RemoveEntry(root, "TREE", true));
        Check(((DxfRawDictionary)tx.Get(root)).Find("TREE") != null, "Rejected subtree deletion modified parent.");
        tx.SetIdBuffer(external, Array.Empty<string>()); Check(tx.RemoveEntry(root, "TREE", true), "Safe subtree deletion failed.");
        var result = DxfRawObjectStore.Open(tx.Commit()); Check(result.Get(tree) == null && result.Get(nested) == null && result.Get(leaf) == null, "Subtree removal incomplete.");
        Check(result.Get(external) is DxfRawIdBuffer, "Unrelated object deleted.");
    }
    private static void ObjectStoreEmptySections(DxfVersion version, bool binary)
    {
        var source = ObjectStoreSource(version, binary);
        var remove = source.Raw.Sections.Where(s => s.Name is "OBJECTS" or "CLASSES").SelectMany(s => Enumerable.Range(s.StartTagIndex, s.EndTagIndex - s.StartTagIndex)).ToHashSet();
        var raw = source.Raw.WithTags(source.Raw.Tags.Where((t, i) => !remove.Contains(i)));
        using var tx = DxfRawObjectStore.Open(raw).BeginEdit(); string root = tx.EnsureRootDictionary(); tx.CreateDictionary(root, "Defaults", withDefault: true);
        var result = tx.Commit(); Equal(1, result.Sections.Count(s => s.Name == "OBJECTS"), "OBJECTS section insertion");
        Equal(1, result.Sections.Count(s => s.Name == "CLASSES"), "CLASSES section insertion");
        Check(result.Sections.Single(s => s.Name == "CLASSES").StartTagIndex < result.Sections.Single(s => s.Name == "TABLES").StartTagIndex, "CLASSES inserted after tables.");
        Equal(root, DxfRawObjectStore.Open(result).RootDictionary!.Handle, "New root must be first OBJECTS record");
    }

    private static void ObjectStoreFailure(int scenario)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false); using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit(); string root = tx.EnsureRootDictionary();
        string dictionary = tx.CreateDictionary(root, "CASE"); string leaf = tx.CreatePlaceholder(dictionary, "Leaf");
        Action action = scenario switch
        {
            0 => () => tx.CreatePlaceholder(dictionary, "leaf"),
            1 => () => tx.CreatePlaceholder(dictionary, ""),
            2 => () => tx.CreatePlaceholder(dictionary, "bad\nname"),
            3 => () => tx.CreateVariable(dictionary, "New", "\0"),
            4 => () => tx.CreateXRecord(dictionary, "New", new[] { new DxfTag(5, "CAFE") }),
            5 => () => tx.CreateXRecord(dictionary, "New", new[] { new DxfTag(105, "CAFE") }),
            6 => () => tx.CreateXRecord(dictionary, "New", new[] { new DxfTag(310, new byte[128]) }),
            7 => () => tx.CreateXRecord(dictionary, "New", Array.Empty<DxfTag>(), (DxfDuplicateRecordCloning)6),
            8 => () => tx.SetDefault(dictionary, leaf),
            9 => () => tx.SetDictionaryEntry(dictionary, "Missing", "FFFF"),
            10 => () => tx.SetDictionaryEntry(dictionary, "Graphic", source.Line),
            11 => () => tx.RenameEntry(dictionary, "Missing", "New"),
            12 => () => tx.CreateVariable(dictionary, "New", "\uD800"),
            13 => () => tx.CreateIdBuffer(dictionary, "New", new[] { "not a handle" }),
            14 => () => tx.SetDrawOrder(source.Line, Array.Empty<DxfRawSortOrderEntry>()),
            15 => () => tx.SetDrawOrder(source.Block, new[] { new DxfRawSortOrderEntry(source.Line, "1"), new DxfRawSortOrderEntry(source.Line, "2") }),
            16 => () => tx.DeleteOwnedTree(root),
            _ => () => tx.SetDictionaryEntry(root, "Steal", leaf, true)
        };
        bool failed = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException) { failed = true; }
        Check(failed, "Invalid operation unexpectedly accepted.");
        var current = (DxfRawDictionary)tx.Get(dictionary); Equal(1, current.Entries.Count, "Failed operation changed entry count");
        Equal(leaf, current.Find("Leaf")!.Handle, "Failed operation changed target");
        Check(tx.Get(leaf) is DxfRawPlaceholder, "Failed operation deleted leaf.");
        tx.CreateVariable(dictionary, "New", "valid"); Check(DxfRawObjectStore.Open(tx.Commit()).Get(leaf) != null, "Transaction unusable after rejected operation.");
    }
    private static void ObjectStoreUnicode(string text)
    {
        Equal(text, DxfRawObjectStore.DecodeText(DxfRawObjectStore.EncodeText(text)), "One-pass Unicode escape round trip");
        var source = ObjectStoreSource(DxfVersion.AutoCad2000, false); using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = tx.EnsureRootDictionary(); string value = tx.CreateVariable(root, text, text);
        using var output = new MemoryStream(); tx.Commit().Save(output); output.Position = 0;
        var store = DxfRawObjectStore.Open(DxfRawDocument.Load(output)); Equal(value, store.RootDictionary!.Find(text)!.Handle, "Unicode dictionary lookup");
        Equal(text, ObjectOf<DxfRawDictionaryVariable>(store, value).Value, "Unicode variable decoding");
    }
    private static void ObjectStoreLongChain()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false); using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = tx.EnsureRootDictionary(), first = tx.CreateDictionary(root, "Long"), last = first;
        for (int i = 0; i < 1200; ++i) last = tx.CreateDictionary(last, "next");
        string copy = tx.CloneDictionaryTree(first, root, "Long copy");
        var result = DxfRawObjectStore.Open(tx.Commit()); Check(result.Get(copy) is DxfRawDictionary && result.Get(last) is DxfRawDictionary, "Iterative subtree clone failed.");
        using var erase = result.BeginEdit(); Check(erase.RemoveEntry(root, "Long", true), "Iterative subtree deletion failed.");
        var final = DxfRawObjectStore.Open(erase.Commit()); Check(final.Get(last) == null && final.Get(copy) != null, "Subtree deletion affected clone.");
    }
}
