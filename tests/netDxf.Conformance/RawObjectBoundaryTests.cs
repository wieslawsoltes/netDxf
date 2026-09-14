using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawObjectBoundaryTests()
    {
        Run("objects/boundary/lifecycle", ObjectStoreLifecycle);
        Run("objects/boundary/failed-commit-repair", ObjectStoreFailedCommitRepair);
        Run("objects/boundary/enumerator-rollback", ObjectStoreEnumeratorRollback);
        Run("objects/boundary/reentrant-dispose-rollback", ObjectStoreReentrantRollback);
        Run("objects/boundary/change-budget-rollback", ObjectStoreBudgetRollback);
        Run("objects/boundary/cancellation", ObjectStoreCancellation);
        Run("objects/boundary/clone-dangling-xdata", ObjectStoreCloneDanglingXData);
        foreach (string version in new[] { "R2000", "R2018" })
        {
            string v = version;
            Run($"objects/independent/{v}", () => ObjectStoreIndependent(v));
        }
        foreach (int scenario in Enumerable.Range(0, 4))
        {
            int s = scenario;
            Run($"objects/boundary/malformed-preservation/{s}", () => ObjectStoreMalformedPreservation(s));
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"objects/boundary/opaque-preservation/{b}", () => ObjectStoreOpaquePreservation(b));
            Run($"objects/boundary/clone-xdata-reactors/{b}", () => ObjectStoreCloneXData(b));
            Run($"objects/boundary/soft-alias/{b}", () => ObjectStoreSoftAlias(b));
            Run($"objects/boundary/alias-owner-rejection/{b}", () => ObjectStoreAliasOwnerRejection(b));
            foreach (bool self in new[] { false, true })
            {
                bool s = self;
                Run($"objects/boundary/imported-alias-owner/{b}/{s}", () => ObjectStoreImportedAliasOwner(b, s));
            }
        }
        foreach (DxfTag invalid in new[] { new DxfTag(370, (short)25), new DxfTag(420, 0x123456), new DxfTag(440, 0x2000000) })
        {
            DxfTag tag = invalid;
            Run($"objects/boundary/nonstandard-xrecord/{tag.Code}", () =>
            {
                var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
                using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
                string root = tx.EnsureRootDictionary();
                Throws<ArgumentException>(() => tx.CreateXRecord(root, "INVALID", new[] { tag }));
                Check(ReferenceEquals(source.Raw, tx.Commit()), "Rejected payload changed source or staged an object.");
            });
        }
    }

    private static void ObjectStoreLifecycle()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        var store = DxfRawObjectStore.Open(source.Raw);
        using var noOp = store.BeginEdit();
        Check(ReferenceEquals(source.Raw, noOp.Commit()), "Empty commit must retain original bytes.");
        Throws<ObjectDisposedException>(() => noOp.Commit());
        Throws<ObjectDisposedException>(() => noOp.Get(store.RootDictionary!.Handle));
        noOp.Dispose(); noOp.Dispose();
        using var changed = store.BeginEdit();
        changed.CreatePlaceholder(changed.EnsureRootDictionary(), "NEW");
        changed.Commit();
        Throws<ObjectDisposedException>(() => changed.Commit());
        changed.Dispose();
        using var discarded = store.BeginEdit();
        discarded.CreatePlaceholder(discarded.EnsureRootDictionary(), "DISCARD");
        discarded.Dispose();
        Throws<ObjectDisposedException>(() => discarded.Commit());
        Check(store.RootDictionary!.Find("DISCARD") == null, "Disposal mutated original snapshot.");
    }

    private static void ObjectStoreFailedCommitRepair()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = tx.EnsureRootDictionary();
        string next = DxfRawHandleIndex.Create(source.Raw).Occurrences.Single(x => x.Role == DxfRawHandleRole.HeaderSeed).CanonicalHandle;
        string record = tx.CreateXRecord(root, "DATA", new[] { new DxfTag(330, next) });
        string valid = tx.CreatePlaceholder(root, "VALID");
        Check(record != next && valid != next, "Allocator captured a supplied unresolved reference.");
        Throws<InvalidDataException>(() => tx.Commit());
        Equal(next, (string)((DxfRawXRecord)tx.Get(record)).Data.Single().Value, "Rejected commit changed staging.");
        Check(DxfRawObjectStore.Open(source.Raw).RootDictionary!.Find("DATA") == null, "Failed commit mutated source.");
        tx.SetXRecord(record, new[] { new DxfTag(330, valid) });
        var result = DxfRawObjectStore.Open(tx.Commit());
        Equal(valid, (string)ObjectOf<DxfRawXRecord>(result, record).Data.Single().Value, "Repaired commit lost reference.");
    }

    private static void ObjectStoreEnumeratorRollback()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        var store = DxfRawObjectStore.Open(source.Raw);
        using var tx = store.BeginEdit();
        string root = tx.EnsureRootDictionary();
        IEnumerable<DxfTag> Fail()
        {
            yield return new DxfTag(330, source.Line);
            throw new IOException("Application iterator failed.");
        }
        Throws<IOException>(() => tx.CreateXRecord(root, "FAILED", Fail()));
        Check(((DxfRawDictionary)tx.Get(root)).Find("FAILED") == null, "Throwing iterator left dictionary entry.");
        string actual = tx.CreatePlaceholder(root, "AFTER");
        using var control = store.BeginEdit();
        Equal(control.CreatePlaceholder(root, "AFTER"), actual, "Failed operation leaked allocated handles.");
        Check(DxfRawObjectStore.Open(tx.Commit()).Get(actual) is DxfRawPlaceholder, "Transaction was unusable after iterator failure.");
    }

    private static void ObjectStoreReentrantRollback()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = tx.EnsureRootDictionary();
        IEnumerable<DxfTag> Reenter()
        {
            yield return new DxfTag(1, "first");
            tx.Dispose();
        }
        Throws<InvalidOperationException>(() => tx.CreateXRecord(root, "FAILED", Reenter()));
        tx.CreatePlaceholder(root, "AFTER");
        var result = DxfRawObjectStore.Open(tx.Commit());
        Check(result.RootDictionary!.Find("FAILED") == null && result.RootDictionary.Find("AFTER") != null, "Reentrant disposal closed or corrupted transaction.");
    }

    private static void ObjectStoreBudgetRollback()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        var store = DxfRawObjectStore.Open(source.Raw, new DxfRawObjectStoreOptions(maximumChanges: 1));
        using var tx = store.BeginEdit();
        string root = tx.EnsureRootDictionary();
        Throws<InvalidDataException>(() => tx.CreateDictionary(root, "OVER_BUDGET"));
        Check(((DxfRawDictionary)tx.Get(root)).Find("OVER_BUDGET") == null, "Budget failure linked partial object.");
        Check(ReferenceEquals(source.Raw, tx.Commit()), "Budget failure retained a staged allocation.");
    }

    private static void ObjectStoreCancellation()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        using var cancelled = new CancellationTokenSource();
        using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit(cancelled.Token);
        tx.CreatePlaceholder(tx.EnsureRootDictionary(), "CANCELLED");
        cancelled.Cancel();
        Throws<OperationCanceledException>(() => tx.Commit());
        tx.Dispose();
        Check(DxfRawObjectStore.Open(source.Raw).RootDictionary!.Find("CANCELLED") == null, "Cancellation changed source.");
        Throws<OperationCanceledException>(() => DxfRawObjectStore.Open(source.Raw, cancellationToken: cancelled.Token));
    }

    private static DxfRawDocument ObjectStoreReplaceRecord(DxfRawDocument raw, string handle, Func<List<DxfTag>, List<DxfTag>> edit)
    {
        var record = DxfRawHandleIndex.Create(raw).FindDefinitions(handle).Single().Record;
        return raw.WithTags(raw.Tags.Take(record.StartTagIndex).Concat(edit(record.Tags.ToList())).Concat(raw.Tags.Skip(record.EndTagIndex)));
    }

    private static void ObjectStoreOpaquePreservation(bool binary)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, binary);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), custom = setup.CreateDictionary(root, "CUSTOM");
        string opaqueHandle = setup.CreatePlaceholder(custom, "PRIVATE");
        var raw = ObjectStoreReplaceRecord(setup.Commit(), opaqueHandle, tags =>
        {
            tags[0] = new DxfTag(0, "VENDOR_PRIVATE_OBJECT");
            tags.Add(new DxfTag(100, "VendorPrivateSchema"));
            tags.Add(new DxfTag(310, new byte[] { 0, 255, 127, 1 }));
            tags.Add(new DxfTag(1, "preserve literal \\U+0041"));
            return tags;
        });
        var store = DxfRawObjectStore.Open(raw);
        Check(store.Get(opaqueHandle) is DxfRawOpaqueStoredObject, "Unknown object was silently typed.");
        var before = store.Get(opaqueHandle).Tags;
        using var tx = store.BeginEdit();
        tx.CreateVariable(root, "EDIT", "new value");
        Throws<NotSupportedException>(() => tx.CloneDictionaryTree(custom, root, "UNSAFE_CLONE"));
        Throws<NotSupportedException>(() => tx.RemoveEntry(root, "CUSTOM", true));
        var result = tx.Commit();
        using var output = new MemoryStream(); result.Save(output); output.Position = 0;
        var reloaded = DxfRawObjectStore.Open(DxfRawDocument.Load(output));
        AssertObjectTags(before, reloaded.Get(opaqueHandle).Tags, "Opaque data preservation");
        Check(reloaded.RootDictionary!.Find("UNSAFE_CLONE") == null && reloaded.RootDictionary.Find("CUSTOM") != null, "Rejected unknown-tree operation was not atomic.");
    }

    private static void ObjectStoreCloneXData(bool binary)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, binary);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), tree = setup.CreateDictionary(root, "SOURCE");
        string leaf = setup.CreatePlaceholder(tree, "LEAF");
        var raw = ObjectStoreReplaceRecord(setup.Commit(), leaf, tags =>
        {
            tags.InsertRange(2, new[] { new DxfTag(102, "{ACAD_REACTORS"), new DxfTag(330, source.Circle), new DxfTag(102, "}") });
            tags.AddRange(new[] { new DxfTag(1001, "ACAD"), new DxfTag(1005, tree), new DxfTag(1005, source.Line), new DxfTag(1000, tree) });
            return tags;
        });
        using var tx = DxfRawObjectStore.Open(raw).BeginEdit();
        string clone = tx.CloneDictionaryTree(tree, root, "COPY");
        var store = DxfRawObjectStore.Open(tx.Commit());
        string copiedLeaf = ObjectOf<DxfRawDictionary>(store, clone).Find("LEAF")!.Handle;
        var originalTags = store.Get(leaf).Tags;
        var clonedTags = store.Get(copiedLeaf).Tags;
        Check(originalTags.Any(t => t.Code == 102) && !clonedTags.Any(t => t.Code == 102), "Clone copied reactors or mutated original.");
        var pointers = clonedTags.Where(t => t.Code == 1005).Select(t => (string)t.Value).ToArray();
        Check(pointers.SequenceEqual(new[] { clone, source.Line }), "XData links did not distinguish internal and external references.");
        Equal(tree, (string)clonedTags.Single(t => t.Code == 1000).Value, "Clone changed an application string that resembles a handle.");
    }

    private static void ObjectStoreSoftAlias(bool binary)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, binary);
        using var tx = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = tx.EnsureRootDictionary(), owner = tx.CreateDictionary(root, "OWNER", hardOwner: false);
        string leaf = tx.CreatePlaceholder(owner, "LEAF");
        tx.SetDictionaryEntry(owner, "ALIAS", leaf);
        var committed = tx.Commit();
        using var output = new MemoryStream(); committed.Save(output); output.Position = 0;
        var result = DxfRawObjectStore.Open(DxfRawDocument.Load(output));
        var dictionary = ObjectOf<DxfRawDictionary>(result, owner);
        Equal(leaf, dictionary.Find("LEAF")!.Handle, "Original name lost its owned target.");
        Equal(leaf, dictionary.Find("ALIAS")!.Handle, "Same-owner soft alias lost its target.");
        Check(!dictionary.Find("ALIAS")!.IsHardOwner, "Soft alias changed its wire reference type.");
        Equal(owner, result.Get(leaf).OwnerHandle, "Alias changed the common owner.");
        using var remove = result.BeginEdit();
        Check(remove.RemoveEntry(owner, "ALIAS"), "Failed to unlink a same-owner soft alias.");
        Check(remove.Get(leaf) != null, "Unlinking an alias erased its target.");
        Check(remove.RemoveEntry(root, "OWNER", true), "Owned subtree could not be removed after alias unlink.");
        var removed = DxfRawObjectStore.Open(remove.Commit());
        Check(removed.Get(owner) == null && removed.Get(leaf) == null, "Owned subtree removal retained its objects.");
    }

    private static void ObjectStoreAliasOwnerRejection(bool binary)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, binary);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), owner = setup.CreateDictionary(root, "OWNER"), aliases = setup.CreateDictionary(root, "ALIASES", hardOwner: false);
        string foreign = setup.CreatePlaceholder(owner, "FOREIGN"), local = setup.CreatePlaceholder(aliases, "LOCAL");
        var store = DxfRawObjectStore.Open(setup.Commit());
        using var tx = store.BeginEdit();
        Throws<InvalidOperationException>(() => tx.SetDictionaryEntry(aliases, "EXTERNAL", foreign));
        Throws<InvalidOperationException>(() => tx.SetDictionaryEntry(aliases, "LOCAL", foreign));
        Throws<InvalidOperationException>(() => tx.SetDictionaryEntry(aliases, "SELF", aliases));
        Throws<InvalidOperationException>(() => tx.SetDictionaryEntry(root, "SELF", root));
        var dictionary = (DxfRawDictionary)tx.Get(aliases);
        Check(dictionary.Find("EXTERNAL") == null && dictionary.Find("SELF") == null, "Rejected alias left a staged entry.");
        Equal(local, dictionary.Find("LOCAL")!.Handle, "Rejected replacement changed the original entry.");
        Equal(owner, tx.Get(foreign).OwnerHandle, "Rejected alias changed the foreign owner.");
        Check(((DxfRawDictionary)tx.Get(root)).Find("SELF") == null, "Rejected root alias left a staged entry.");
        string after = tx.CreatePlaceholder(aliases, "AFTER");
        using var control = store.BeginEdit();
        Equal(control.CreatePlaceholder(aliases, "AFTER"), after, "Rejected alias consumed a handle.");
        var result = DxfRawObjectStore.Open(tx.Commit());
        Equal(local, ObjectOf<DxfRawDictionary>(result, aliases).Find("LOCAL")!.Handle, "Valid edit after rejection lost its sibling.");
        Check(result.Get(after) is DxfRawPlaceholder, "Transaction was unusable after rejected aliases.");
    }

    private static void ObjectStoreImportedAliasOwner(bool binary, bool self)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, binary);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), owner = setup.CreateDictionary(root, "OWNER"), aliases = setup.CreateDictionary(root, "ALIASES", hardOwner: false);
        string leaf = setup.CreatePlaceholder(owner, "LEAF");
        var raw = ObjectStoreReplaceRecord(setup.Commit(), aliases, tags =>
        {
            tags.Add(new DxfTag(3, "INVALID")); tags.Add(new DxfTag(350, self ? aliases : leaf));
            return tags;
        });
        var store = DxfRawObjectStore.Open(raw);
        using var tx = store.BeginEdit();
        tx.RenameEntry(aliases, "INVALID", "EDITED");
        Throws<InvalidDataException>(() => tx.Commit());
        Check(((DxfRawDictionary)tx.Get(aliases)).Find("EDITED") != null, "Rejected commit discarded editable staging.");
        Check(ObjectOf<DxfRawDictionary>(store, aliases).Find("INVALID") != null, "Rejected commit changed the original snapshot.");
        Check(tx.RemoveEntry(aliases, "EDITED"), "Could not repair an imported invalid alias.");
        var repaired = DxfRawObjectStore.Open(tx.Commit());
        Check(ObjectOf<DxfRawDictionary>(repaired, aliases).Entries.Count == 0, "Alias repair left an invalid dictionary link.");
        Equal(owner, repaired.Get(leaf).OwnerHandle, "Repair changed the target ownership.");
    }

    private static void ObjectStoreCloneDanglingXData()
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), tree = setup.CreateDictionary(root, "SOURCE");
        string leaf = setup.CreatePlaceholder(tree, "LEAF");
        var raw = ObjectStoreReplaceRecord(setup.Commit(), leaf, tags =>
        {
            tags.AddRange(new[] { new DxfTag(1001, "ACAD"), new DxfTag(1005, "FFFFFA") });
            return tags;
        });
        using var tx = DxfRawObjectStore.Open(raw).BeginEdit();
        tx.CloneDictionaryTree(tree, root, "COPY");
        Throws<InvalidDataException>(() => tx.Commit());
        Check(tx.RemoveEntry(root, "COPY", true), "Could not correct rejected clone.");
        var committed = DxfRawObjectStore.Open(tx.Commit());
        Check(committed.Get(leaf).Tags.Any(t => t.Code == 1005 && (string)t.Value == "FFFFFA"), "Correcting clone changed unrelated original XData.");
    }

    private static void ObjectStoreIndependent(string version)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("tests", "fixtures", $"raw-objects-{version}.dxf"));
        string expected = version == "R2000" ? "308c500a530feddca2d2f01691faac2efed871bdf4fc708e0af149d0c1702e98" : "0631df37e447399011c67a9650967995052bbe9f74a78bd5d65da7b06fa3eeee";
        Equal(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "Independent fixture hash");
        using var input = new MemoryStream(bytes);
        var raw = DxfRawDocument.Load(input);
        var store = DxfRawObjectStore.Open(raw);
        string parent = store.RootDictionary!.Find("QA_OBJECTS")!.Handle;
        var dictionary = ObjectOf<DxfRawDictionary>(store, parent);
        string record = dictionary.Find("PAYLOAD")!.Handle;
        Equal(record, dictionary.Find("PAYLOAD_ALIAS")!.Handle, "Independent dictionary alias");
        var payload = ObjectOf<DxfRawXRecord>(store, record);
        Equal("ApplicationPayloadMarker", (string)payload.Data.Single(t => t.Code == 100).Value, "Arbitrary group100 payload");
        Equal((short)77, (short)payload.Data.Single(t => t.Code == 280).Value, "Payload280 vs cloning280");
        Equal(long.MinValue, (long)payload.Data.Single(t => t.Code == 160).Value, "Payload after marker100 was discarded");
        string variable = dictionary.Find("VARIABLE")!.Handle;
        using var edit = store.BeginEdit();
        edit.SetVariable(variable, "edited independent value");
        string copy = edit.CloneDictionaryTree(parent, store.RootDictionary.Handle, "COPIED_INDEPENDENT");
        var updated = edit.Commit();
        using var output = new MemoryStream(); updated.Save(output); output.Position = 0;
        var reloaded = DxfRawObjectStore.Open(DxfRawDocument.Load(output));
        AssertObjectTags(payload.Data, ObjectOf<DxfRawXRecord>(reloaded, record).Data, "Independent XRECORD preservation");
        Equal("edited independent value", ObjectOf<DxfRawDictionaryVariable>(reloaded, variable).Value, "Independent variable edit");
        var copied = ObjectOf<DxfRawDictionary>(reloaded, copy);
        Equal(copied.Find("PAYLOAD")!.Handle, copied.Find("PAYLOAD_ALIAS")!.Handle, "Independent clone alias");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raw-object-independent-{version}.dxf"), output.ToArray());
    }

    private static void ObjectStoreMalformedPreservation(int scenario)
    {
        var source = ObjectStoreSource(DxfVersion.AutoCad2018, false);
        using var setup = DxfRawObjectStore.Open(source.Raw).BeginEdit();
        string root = setup.EnsureRootDictionary(), malformed = setup.CreatePlaceholder(root, "MALFORMED");
        var raw = ObjectStoreReplaceRecord(setup.Commit(), malformed, tags =>
        {
            if (scenario == 0)
            {
                tags[0] = new DxfTag(0, "XRECORD");
                tags.AddRange(new[] { new DxfTag(100, "AcDbXrecord"), new DxfTag(280, (short)99), new DxfTag(1, "invalid cloning flag") });
            }
            else if (scenario == 1)
            {
                tags[0] = new DxfTag(0, "DICTIONARY");
                tags.AddRange(new[] { new DxfTag(100, "AcDbDictionary"), new DxfTag(280, (short)0), new DxfTag(280, (short)1) });
            }
            else if (scenario == 2)
            {
                tags[0] = new DxfTag(0, "DICTIONARYVAR");
                tags.AddRange(new[] { new DxfTag(100, "DictionaryVariables"), new DxfTag(1, "first"), new DxfTag(1, "second") });
            }
            else
            {
                tags[0] = new DxfTag(0, "ACDBDICTIONARYWDFLT");
                tags.Add(new DxfTag(100, "AcDbDictionary"));
            }
            return tags;
        });
        var store = DxfRawObjectStore.Open(raw);
        var opaque = store.Get(malformed) as DxfRawOpaqueStoredObject;
        Check(opaque != null && opaque.Reason.Length != 0, "Malformed object was typed or silently discarded.");
        using var tx = store.BeginEdit();
        tx.CreateVariable(root, "VALID", "retained");
        var output = DxfRawObjectStore.Open(tx.Commit());
        AssertObjectTags(opaque!.Tags, output.Get(malformed).Tags, "Malformed packet changed during unrelated edit");
    }
}
