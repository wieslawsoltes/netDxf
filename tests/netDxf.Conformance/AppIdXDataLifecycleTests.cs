using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Collections;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RunAppIdXDataLifecycleTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"appid-lifecycle/profile/{version}/{binary}", () => AppIdProfile(version, binary));
        foreach (bool registered in new[] { false, true }) foreach (bool replace in new[] { false, true }) foreach (bool typed in new[] { false, true })
            Run($"appid-lifecycle/indexer/{registered}/{replace}/{typed}", () => AppIdIndexer(registered, replace, typed));
        foreach (bool sameDocument in new[] { false, true }) foreach (bool registerFirst in new[] { false, true })
            Run($"appid-lifecycle/shared/{sameDocument}/{registerFirst}", () => AppIdShared(sameDocument, registerFirst));
        foreach (bool attachFirst in new[] { false, true }) foreach (bool table in new[] { false, true })
            Run($"appid-lifecycle/observer-throw/{attachFirst}/{table}", () => AppIdObserverThrow(attachFirst, table));
        foreach (bool callback in new[] { false, true }) foreach (bool table in new[] { false, true })
            Run($"appid-lifecycle/collision/{callback}/{table}", () => AppIdCollision(callback, table));
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"appid-lifecycle/layout-viewports/{version}/{binary}", () => AppIdLayoutViewports(version, binary));
        foreach (bool sameDocument in new[] { false, true })
            Run($"appid-lifecycle/merge-shared-payload/{sameDocument}", () => AppIdMergedPayload(sameDocument));
        Run("appid-lifecycle/clone-names-and-cycles", AppIdNamedClone);
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"appid-lifecycle/retained-attributes-and-block-end/{version}/{binary}", () => AppIdMetadataCarriers(version, binary));
        Run("appid-lifecycle/late-attribute-sync", AppIdAttributeSync);
        Run("appid-lifecycle/layout-viewport-replacement", AppIdViewportReplacement);
        Run("appid-lifecycle/block-end-detach-readd", AppIdBlockEndReadd);
        foreach (bool named in new[] { false, true }) foreach (bool cyclic in new[] { false, true })
            Run($"appid-lifecycle/block-clone/{named}/{cyclic}", () => AppIdBlockClone(named, cyclic));
        Run("appid-lifecycle/foreign-registration-and-removal", AppIdForeign);
        Run("appid-lifecycle/detached-replacement-and-readd", AppIdDetachedReplacement);
        Run("appid-lifecycle/clone-renamed-registries", AppIdClone);
        Run("appid-lifecycle/observer-detach-and-transfer", AppIdObserverTransfer);
    }
    private static XData AppIdData(ApplicationRegistry registry, byte value = 1)
    {
        var data = new XData(registry); data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new[] { value, (byte)2 })); return data;
    }
    private static void AppIdHas(DxfObject item, string name)
    {
        Check(item.XData.ContainsAppId(name) && item.XData.AppIds.SequenceEqual(new[] { name }), "XData keys differ from registry name");
        Equal(name, item.XData[name].ApplicationRegistry.Name, "XData registry name differs");
    }
    private static void AppIdProfile(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var line = new Line(Vector3.Zero, Vector3.UnitX); var record = new DxfXRecord();
        var layer = new Layer("QA_LAYER"); var first = new VPort("QA_VIEW"); var second = new VPort("QA_VIEW");
        DxfObject[] items = { line, record, layer, first, second };
        var callerRegistry = new ApplicationRegistry("QA_APP"); var caller = AppIdData(callerRegistry);
        foreach (DxfObject item in items) item.XData.Add(caller);
        doc.Entities.Add(line); doc.NamedObjects.Add("QA_RECORD", record); doc.Layers.Add(layer); doc.VPorts.AddRecord(first); doc.VPorts.AddRecord(second);
        var canonical = doc.ApplicationRegistries["QA_APP"];
        Check(callerRegistry.Owner == null && callerRegistry.Handle == null, "adoption changed caller registry");
        Equal(5, doc.ApplicationRegistries.GetReferences(canonical).Sum(reference => reference.Uses), "adoption reference count");
        canonical.Name = "QA_RENAMED";
        foreach (DxfObject item in items) { AppIdHas(item, "QA_RENAMED"); Check(ReferenceEquals(item.XData["QA_RENAMED"].ApplicationRegistry, canonical), "registry not canonical"); }
        callerRegistry.Name = "CALLER_ONLY";
        foreach (DxfObject item in items) AppIdHas(item, "QA_RENAMED");
        ((byte[])caller.XDataRecord[0].Value)[0] = 99;
        Check(ReferenceEquals(line.XData["QA_RENAMED"], caller), "single-container XData identity changed during canonicalization");
        Equal((byte)99, ((byte[])line.XData["QA_RENAMED"].XDataRecord[0].Value)[0], "caller-held live XData edits were lost");
        foreach (DxfObject item in items.Skip(1)) Equal((byte)1, ((byte[])item.XData["QA_RENAMED"].XDataRecord[0].Value)[0], "shared caller data aliases another container");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "renamed APPID save"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"appid-lifecycle-{version}-{binary}.dxf"), stream.ToArray()); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        DxfObject[] saved = { loaded.Entities.Lines.Single(), loaded.NamedObjects["QA_RECORD"], loaded.Layers["QA_LAYER"], loaded.VPorts.GetConfiguration("QA_VIEW")[0], loaded.VPorts.GetConfiguration("QA_VIEW")[1] };
        foreach (DxfObject item in saved) AppIdHas(item, "QA_RENAMED");
        Equal(5, loaded.ApplicationRegistries.GetReferences("QA_RENAMED").Sum(reference => reference.Uses), "loaded reference count");
        foreach (DxfObject item in saved) Check(item.XData.Remove("QA_RENAMED"), "renamed XData removal failed");
        Check(loaded.ApplicationRegistries.Remove("QA_RENAMED"), "removed XData retained registry uses");
    }
    private static void AppIdIndexer(bool registered, bool replace, bool typed)
    {
        var doc = new DxfDocument(); DxfObject item = typed ? new DxfXRecord() : new Line();
        var oldCaller = new ApplicationRegistry("APP"); if (replace) item.XData.Add(AppIdData(oldCaller));
        if (registered) { if (typed) doc.NamedObjects.Add("ITEM", item); else doc.Entities.Add((Line)item); }
        int added = 0, removed = 0; item.XDataAddAppReg += (_, _) => added++; item.XDataRemoveAppReg += (_, _) => removed++;
        var replacementRegistry = new ApplicationRegistry("APP"); var replacement = AppIdData(replacementRegistry, 7); item.XData["APP"] = replacement;
        Equal(1, added, "indexer add notification count"); Equal(replace ? 1 : 0, removed, "indexer remove notification count");
        Equal((byte)7, ((byte[])item.XData["APP"].XDataRecord[0].Value)[0], "indexer retained old records");
        if (registered)
        {
            Equal(1, doc.ApplicationRegistries.GetReferences("APP").Sum(reference => reference.Uses), "indexer registry count");
            Check(replacementRegistry.Owner == null && replacementRegistry.Handle == null, "indexer canonicalization adopted caller registry");
            replacementRegistry.Name = "CALLER"; AppIdHas(item, "APP");
            doc.ApplicationRegistries["APP"].Name = "RENAMED"; AppIdHas(item, "RENAMED");
            item.XData.Remove("RENAMED"); Equal(0, doc.ApplicationRegistries.GetReferences("RENAMED").Sum(reference => reference.Uses), "remove after indexer left uses");
        }
        else { replacementRegistry.Name = "RENAMED"; AppIdHas(item, "RENAMED"); }
        if (replace) { oldCaller.Name = "FORMER"; Check(!item.XData.ContainsAppId("FORMER"), "former registry still updates replacement"); }
    }
    private static void AppIdShared(bool sameDocument, bool registerFirst)
    {
        var source = new DxfDocument(); var target = sameDocument ? source : new DxfDocument();
        var first = new Line(); var second = new Line(); var originalRegistry = new ApplicationRegistry("SHARED"); var data = AppIdData(originalRegistry);
        first.XData.Add(data); if (registerFirst) source.Entities.Add(first); second.XData.Add(data);
        if (!registerFirst) source.Entities.Add(first); target.Entities.Add(second);
        Check(!ReferenceEquals(first.XData["SHARED"], second.XData["SHARED"]), "two containers share XData value");
        Check(originalRegistry.Owner == null && originalRegistry.Handle == null, "shared source registry was adopted");
        ((byte[])second.XData["SHARED"].XDataRecord[0].Value)[0] = 88; Equal((byte)1, ((byte[])first.XData["SHARED"].XDataRecord[0].Value)[0], "shared binary chunk aliases");
        target.ApplicationRegistries["SHARED"].Name = "TARGET"; AppIdHas(second, "TARGET"); AppIdHas(first, sameDocument ? "TARGET" : "SHARED");
        originalRegistry.Name = "CALLER"; AppIdHas(second, "TARGET"); AppIdHas(first, sameDocument ? "TARGET" : "SHARED");
    }
    private static void AppIdMergedPayload(bool sameDocument)
    {
        var source = new DxfDocument(); var target = sameDocument ? source : new DxfDocument();
        var first = new Line(); var second = new Line(); var data = AppIdData(new ApplicationRegistry("MERGE"), 17);
        first.XData.Add(data); second.XData.Add(AppIdData(new ApplicationRegistry("MERGE"), 3)); source.Entities.Add(first); target.Entities.Add(second);
        XData retained = second.XData["MERGE"]; second.XData.Add(first.XData["MERGE"]);
        Check(ReferenceEquals(retained, second.XData["MERGE"]), "merge replaced target XData value"); Equal(2, retained.XDataRecord.Count, "merge did not append records");
        ((byte[])data.XDataRecord[0].Value)[0] = 44;
        Equal((byte)17, ((byte[])retained.XDataRecord[1].Value)[0], "merge shares binary payload from another container");
        ((byte[])retained.XDataRecord[1].Value)[1] = 99; Equal((byte)2, ((byte[])data.XDataRecord[0].Value)[1], "merged payload mutation changed source");
        Equal(sameDocument ? 2 : 1, target.ApplicationRegistries.GetReferences("MERGE").Sum(reference => reference.Uses), "merged APPID membership counted twice");
    }
    private static void AppIdObserverThrow(bool attachFirst, bool table)
    {
        var doc = new DxfDocument(); var registry = new ApplicationRegistry("OLD"); var holder = new Line();
        TableObject.NameChangedEventHandler handler = (_, _) => throw new InvalidOperationException("observer failure");
        if (!attachFirst) registry.NameChanged += handler;
        if (table) doc.ApplicationRegistries.Add(registry);
        holder.XData.Add(AppIdData(registry)); if (attachFirst) registry.NameChanged += handler;
        Throws<InvalidOperationException>(() => registry.Name = "NEW"); Equal("OLD", registry.Name, "throwing observer renamed registry"); AppIdHas(holder, "OLD");
        if (table) Check(ReferenceEquals(doc.ApplicationRegistries["OLD"], registry) && !doc.ApplicationRegistries.Contains("NEW"), "throwing observer changed table index");
        registry.NameChanged -= handler; registry.Name = "NEW"; AppIdHas(holder, "NEW");
    }
    private static void AppIdCollision(bool callback, bool table)
    {
        var doc = new DxfDocument(); var registry = new ApplicationRegistry("OLD"); var holder = new Line(); holder.XData.Add(AppIdData(registry));
        if (table) doc.ApplicationRegistries.Add(registry);
        Action collide = () => { if (table) doc.ApplicationRegistries.Add(new ApplicationRegistry("NEW")); else holder.XData.Add(AppIdData(new ApplicationRegistry("NEW"))); };
        if (callback) registry.NameChanged += (_, _) => collide(); else collide();
        Throws<ArgumentException>(() => registry.Name = "NEW"); Equal("OLD", registry.Name, "collision renamed source"); Check(holder.XData.ContainsAppId("OLD"), "collision unkeyed XData");
        if (table) Check(ReferenceEquals(doc.ApplicationRegistries["OLD"], registry), "collision changed table identity");
    }
    private static void AppIdLayoutViewports(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var first = doc.Layouts.Add(new Layout("FIRST")); var second = doc.Layouts.Add(new Layout("SECOND"));
        var registry = doc.ApplicationRegistries.Add(new ApplicationRegistry("VIEWPORT_DATA"));
        first.Viewport.XData.Add(AppIdData(registry)); second.Viewport.XData.Add(AppIdData(registry));
        var refs = doc.ApplicationRegistries.GetReferences(registry);
        Equal(2, refs.Sum(reference => reference.Uses), "layout viewports missing from APPID reference count");
        Check(refs.Any(reference => ReferenceEquals(reference.Reference, first.Viewport)) && refs.Any(reference => ReferenceEquals(reference.Reference, second.Viewport)), "viewport reference identities changed");
        Check(!doc.ApplicationRegistries.Remove(registry), "referenced viewport APPID removed"); registry.Name = "RENAMED_VIEWPORT_DATA";
        AppIdHas(first.Viewport, "RENAMED_VIEWPORT_DATA"); AppIdHas(second.Viewport, "RENAMED_VIEWPORT_DATA");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "viewport APPID save"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"appid-carriers-{version}-{binary}.dxf"), stream.ToArray()); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        AppIdHas(loaded.Layouts["FIRST"].Viewport, "RENAMED_VIEWPORT_DATA"); AppIdHas(loaded.Layouts["SECOND"].Viewport, "RENAMED_VIEWPORT_DATA");
        Equal(2, loaded.ApplicationRegistries.GetReferences("RENAMED_VIEWPORT_DATA").Sum(reference => reference.Uses), "loaded viewport APPID count");
        loaded.Layouts["FIRST"].Viewport.XData.Clear(); Check(!loaded.ApplicationRegistries.Remove("RENAMED_VIEWPORT_DATA"), "second viewport use ignored");
        loaded.Layouts["SECOND"].Viewport.XData.Clear(); Check(loaded.ApplicationRegistries.Remove("RENAMED_VIEWPORT_DATA"), "cleared viewport references retained");
    }
    private static void AppIdNamedClone()
    {
        var root = new ApplicationRegistry("ROOT"); var peer = new ApplicationRegistry("PEER"); root.XData.Add(AppIdData(root)); root.XData.Add(AppIdData(peer, 3)); peer.XData.Add(AppIdData(root, 5));
        var copy = (ApplicationRegistry)root.Clone("RENAMED"); Equal("RENAMED", copy.Name, "named clone ignored requested name");
        Check(ReferenceEquals(copy.XData["RENAMED"].ApplicationRegistry, copy), "self-reference does not identify clone");
        var copiedPeer = copy.XData["PEER"].ApplicationRegistry;
        Check(!ReferenceEquals(copiedPeer, peer) && ReferenceEquals(copiedPeer.XData["RENAMED"].ApplicationRegistry, copy), "cyclic registry graph not remapped");
        ((byte[])copiedPeer.XData["RENAMED"].XDataRecord[0].Value)[0] = 99; Equal((byte)5, ((byte[])peer.XData["ROOT"].XDataRecord[0].Value)[0], "cyclic clone shares binary data");
        Equal("ROOT", root.Name, "named clone mutated source name"); Check(ReferenceEquals(root.XData["ROOT"].ApplicationRegistry, root), "source self-reference changed");
        Throws<ArgumentException>(() => root.Clone("PEER")); Equal("ROOT", root.Name, "failed conflicting clone changed source");
        var reserved = new ApplicationRegistry(ApplicationRegistry.DefaultName); var custom = (ApplicationRegistry)reserved.Clone("CUSTOM"); Equal("CUSTOM", custom.Name, "reserved registry renamed clone failed"); Check(!custom.IsReserved, "renamed clone retained reserved flag");
    }
    private static void AppIdMetadataCarriers(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var block = new netDxf.Blocks.Block("META"); block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "value" }); var insert = new Insert(block); doc.Entities.Add(insert);
        var registry = doc.ApplicationRegistries.Add(new ApplicationRegistry("CARRIERS")); var end = (DxfObject)typeof(netDxf.Blocks.Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(block)!; insert.Attributes[0].XData.Add(AppIdData(registry)); end.XData.Add(AppIdData(registry, 5)); block.XData.Add(AppIdData(registry, 3));
        var refs = doc.ApplicationRegistries.GetReferences(registry); Equal(3, refs.Sum(reference => reference.Uses), "retained carriers counted twice or omitted");
        Check(refs.Count == 3 && refs.Any(reference => ReferenceEquals(reference.Reference, block)) && refs.Any(reference => ReferenceEquals(reference.Reference, insert.Attributes[0])) && refs.Any(reference => ReferenceEquals(reference.Reference, end)), "carrier reference identities changed");
        Check(!doc.ApplicationRegistries.Remove(registry), "referenced carrier APPID removed"); registry.Name = "RENAMED_CARRIERS";
        AppIdHas(insert.Attributes[0], "RENAMED_CARRIERS"); AppIdHas(end, "RENAMED_CARRIERS"); AppIdHas(block, "RENAMED_CARRIERS");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "retained carrier save"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"appid-members-{version}-{binary}.dxf"), stream.ToArray()); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        var loadedAttribute = loaded.Entities.Inserts.Single().Attributes[0]; var loadedEnd = (DxfObject)typeof(netDxf.Blocks.Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(loaded.Blocks["META"])!;
        AppIdHas(loadedAttribute, "RENAMED_CARRIERS"); AppIdHas(loadedEnd, "RENAMED_CARRIERS"); AppIdHas(loaded.Blocks["META"], "RENAMED_CARRIERS");
        Equal((byte)1, ((byte[])loadedAttribute.XData["RENAMED_CARRIERS"].XDataRecord[0].Value)[0], "ATTRIB payload changed");
        Equal((byte)3, ((byte[])loaded.Blocks["META"].XData["RENAMED_CARRIERS"].XDataRecord[0].Value)[0], "BLOCK payload changed");
        Equal((byte)5, ((byte[])loadedEnd.XData["RENAMED_CARRIERS"].XDataRecord[0].Value)[0], "ENDBLK payload changed");
        Equal(3, loaded.ApplicationRegistries.GetReferences("RENAMED_CARRIERS").Sum(reference => reference.Uses), "loaded member APPID count");
        Check(!loaded.ApplicationRegistries.Remove("RENAMED_CARRIERS"), "loaded referenced carrier APPID removed"); loadedAttribute.XData.Clear(); Check(!loaded.ApplicationRegistries.Remove("RENAMED_CARRIERS"), "remaining block end APPID ignored"); loadedEnd.XData.Clear(); Check(!loaded.ApplicationRegistries.Remove("RENAMED_CARRIERS"), "remaining BLOCK APPID ignored"); loaded.Blocks["META"].XData.Clear(); Check(loaded.ApplicationRegistries.Remove("RENAMED_CARRIERS"), "cleared carrier APPID retained");
    }

    private static void AppIdAttributeSync()
    {
        var doc = new DxfDocument(); var block = new netDxf.Blocks.Block("SYNC"); block.AttributeDefinitions.Add(new AttributeDefinition("OLD") { Value = "old" }); var insert = new Insert(block); doc.Entities.Add(insert);
        var old = insert.Attributes[0]; old.XData.Add(AppIdData(new ApplicationRegistry("OLD_APP")));
        Check(doc.ApplicationRegistries.Contains("OLD_APP") && doc.ApplicationRegistries.GetReferences("OLD_APP").Single().Uses == 1, "late ATTRIB APPID not registered exactly once");
        block.AttributeDefinitions.Remove("OLD"); block.AttributeDefinitions.Add(new AttributeDefinition("NEW") { Value = "new" }); insert.Sync();
        Check(doc.ApplicationRegistries.Remove("OLD_APP"), "Sync removal retained old attribute reference"); old.XData.Add(AppIdData(new ApplicationRegistry("DETACHED_OLD"))); Check(!doc.ApplicationRegistries.Contains("DETACHED_OLD"), "Sync removal retained document subscription");
        var current = insert.Attributes[0]; current.XData.Add(AppIdData(new ApplicationRegistry("NEW_APP")));
        Check(doc.ApplicationRegistries.Contains("NEW_APP") && doc.ApplicationRegistries.GetReferences("NEW_APP").Single().Uses == 1, "Sync addition did not bind new attribute");
        doc.Entities.Remove(insert); Check(doc.ApplicationRegistries.Remove("NEW_APP"), "INSERT removal retained attribute reference");
        current.XData.Add(AppIdData(new ApplicationRegistry("DETACHED_CURRENT"))); Check(!doc.ApplicationRegistries.Contains("DETACHED_CURRENT"), "INSERT removal retained attribute subscription");
        doc.Entities.Add(insert); Equal(1, doc.ApplicationRegistries.GetReferences("NEW_APP").Sum(reference => reference.Uses), "INSERT re-add duplicated attribute reference");
        Equal(1, doc.ApplicationRegistries.GetReferences("DETACHED_CURRENT").Sum(reference => reference.Uses), "INSERT re-add omitted pending XData");
    }
    private static void AppIdViewportReplacement()
    {
        var doc = new DxfDocument(); var layout = doc.Layouts.Add(new Layout("REPLACE")); var old = layout.Viewport; old.XData.Add(AppIdData(new ApplicationRegistry("VIEWPORT_APP")));
        var replacement = (Viewport)old.Clone(); typeof(Layout).GetProperty(nameof(Layout.Viewport))!.SetValue(layout, replacement);
        var refs = doc.ApplicationRegistries.GetReferences("VIEWPORT_APP"); Check(refs.Count == 1 && ReferenceEquals(refs[0].Reference, replacement) && refs[0].Uses == 1, "viewport replacement reference identity/count");
        old.XData.Add(AppIdData(new ApplicationRegistry("DETACHED_VIEWPORT"))); Check(!doc.ApplicationRegistries.Contains("DETACHED_VIEWPORT"), "old viewport retained document subscription");
        replacement.XData.Add(AppIdData(new ApplicationRegistry("LIVE_VIEWPORT"))); Check(doc.ApplicationRegistries.Contains("LIVE_VIEWPORT"), "replacement viewport lacks document subscription");
        doc.Layouts.Remove(layout); Check(doc.ApplicationRegistries.Remove("LIVE_VIEWPORT"), "layout removal retained viewport reference"); replacement.XData.Add(AppIdData(new ApplicationRegistry("AFTER_LAYOUT_REMOVE"))); Check(!doc.ApplicationRegistries.Contains("AFTER_LAYOUT_REMOVE"), "layout removal retained viewport subscription");
    }
    private static void AppIdBlockEndReadd()
    {
        var doc = new DxfDocument(); var block = doc.Blocks.Add(new netDxf.Blocks.Block("END_META")); var end = (DxfObject)typeof(netDxf.Blocks.Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(block)!;
        end.XData.Add(AppIdData(new ApplicationRegistry("END_APP"))); Equal(1, doc.ApplicationRegistries.GetReferences("END_APP").Sum(reference => reference.Uses), "block end APPID count");
        Check(doc.Blocks.Remove(block), "unreferenced block removal failed"); Check(doc.ApplicationRegistries.Remove("END_APP"), "removed block end retained APPID use");
        end.XData.Add(AppIdData(new ApplicationRegistry("DETACHED_END"))); Check(!doc.ApplicationRegistries.Contains("DETACHED_END"), "removed block end retained subscription");
        doc.Blocks.Add(block); Equal(1, doc.ApplicationRegistries.GetReferences("END_APP").Sum(reference => reference.Uses), "block re-add duplicated APPID use"); Equal(1, doc.ApplicationRegistries.GetReferences("DETACHED_END").Sum(reference => reference.Uses), "block re-add omitted pending XData");
    }

    private static void AppIdBlockClone(bool named, bool cyclic)
    {
        var source = new netDxf.Blocks.Block("SOURCE");
        var endProperty = typeof(netDxf.Blocks.Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var sourceEnd = (DxfObject)endProperty.GetValue(source)!;
        var registry = new ApplicationRegistry("BLOCK_APP");
        var peer = new ApplicationRegistry("PEER_APP");
        if (cyclic)
        {
            registry.XData.Add(AppIdData(registry, 17));
            registry.XData.Add(AppIdData(peer, 19));
            peer.XData.Add(AppIdData(registry, 23));
        }
        DxfObject[] originals = { source, source.Record, sourceEnd };
        for (int index = 0; index < originals.Length; index++)
            originals[index].XData.Add(AppIdData(registry, (byte)(3 + index * 2)));

        var copy = (netDxf.Blocks.Block)(named ? source.Clone("COPY") : source.Clone());
        Equal(named ? "COPY" : "SOURCE", copy.Name, "block clone name changed");
        var copyEnd = (DxfObject)endProperty.GetValue(copy)!;
        DxfObject[] copies = { copy, copy.Record, copyEnd };
        for (int index = 0; index < copies.Length; index++)
        {
            AppIdHas(copies[index], "BLOCK_APP");
            XData original = originals[index].XData["BLOCK_APP"], cloned = copies[index].XData["BLOCK_APP"];
            Check(!ReferenceEquals(original, cloned) && !ReferenceEquals(original.ApplicationRegistry, cloned.ApplicationRegistry), "block metadata clone shares original data or registry");
            ((byte[])cloned.XDataRecord[0].Value)[1] = (byte)(10 + index);
            Equal((byte)2, ((byte[])original.XDataRecord[0].Value)[1], "clone binary mutation changed original block metadata");
            ((byte[])original.XDataRecord[0].Value)[0] = 99;
            Equal((byte)(3 + index * 2), ((byte[])cloned.XDataRecord[0].Value)[0], "original binary mutation changed cloned block metadata");
        }
        var endRegistry = copyEnd.XData["BLOCK_APP"].ApplicationRegistry;
        if (cyclic)
        {
            Check(ReferenceEquals(endRegistry.XData["BLOCK_APP"].ApplicationRegistry, endRegistry), "ENDBLK clone lost registry self-reference");
            var clonedPeer = endRegistry.XData["PEER_APP"].ApplicationRegistry;
            Check(!ReferenceEquals(clonedPeer, peer) && ReferenceEquals(clonedPeer.XData["BLOCK_APP"].ApplicationRegistry, endRegistry), "ENDBLK clone lost registry cycle");
            ((byte[])clonedPeer.XData["BLOCK_APP"].XDataRecord[0].Value)[0] = 55;
            Equal((byte)23, ((byte[])peer.XData["BLOCK_APP"].XDataRecord[0].Value)[0], "ENDBLK cyclic clone shares nested binary payload");
        }

        var destination = new DxfDocument(DxfVersion.AutoCad2018);
        destination.Blocks.Add(copy);
        var canonical = destination.ApplicationRegistries["BLOCK_APP"];
        foreach (DxfObject item in copies)
            Check(ReferenceEquals(item.XData["BLOCK_APP"].ApplicationRegistry, canonical), "cloned block metadata registry not canonical");
        canonical.Name = "CLONED_APP";
        foreach (DxfObject item in copies) AppIdHas(item, "CLONED_APP");
        foreach (DxfObject item in originals) AppIdHas(item, "BLOCK_APP");
        Equal("BLOCK_APP", registry.Name, "destination rename changed original registry");
        if (cyclic)
        {
            canonical.XData.Clear();
            destination.ApplicationRegistries["PEER_APP"].XData.Clear();
        }
        Equal(3, destination.ApplicationRegistries.GetReferences(canonical).Sum(reference => reference.Uses), "cloned BLOCK, BLOCK_RECORD, or ENDBLK metadata omitted from registry uses");
        using var stream = new MemoryStream();
        Check(destination.Save(stream, named), "cloned block metadata save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"appid-block-clone-{named}-{cyclic}.dxf"), stream.ToArray());
        stream.Position = 0;
        var loaded = DxfDocument.Load(stream)!;
        var loadedBlock = loaded.Blocks[copy.Name];
        DxfObject[] loadedItems = { loadedBlock, loadedBlock.Record, (DxfObject)endProperty.GetValue(loadedBlock)! };
        for (int index = 0; index < loadedItems.Length; index++)
        {
            Check(loadedItems[index].XData.ContainsAppId("CLONED_APP") && !loadedItems[index].XData.ContainsAppId("BLOCK_APP"), "loaded cloned metadata has missing or stale APPID");
            Check(ReferenceEquals(loadedItems[index].XData["CLONED_APP"].ApplicationRegistry, loaded.ApplicationRegistries["CLONED_APP"]), "loaded cloned metadata registry not canonical");
            Check(((byte[])loadedItems[index].XData["CLONED_APP"].XDataRecord[0].Value).SequenceEqual(new[] { (byte)(3 + index * 2), (byte)(10 + index) }), "cloned metadata payload changed during round-trip");
            Check(!loaded.ApplicationRegistries.Remove("CLONED_APP"), "referenced cloned block registry removed");
            Check(loadedItems[index].XData.Remove("CLONED_APP"), "renamed cloned metadata removal failed");
        }
        Check(loaded.ApplicationRegistries.Remove("CLONED_APP"), "cleared cloned block metadata retained registry reference");
    }

    private static void AppIdForeign()
    {
        var source = new DxfDocument(); var registry = new ApplicationRegistry("REGISTRY"); registry.XData.Add(AppIdData(new ApplicationRegistry("NESTED"), 3)); source.ApplicationRegistries.Add(registry);
        string handle = registry.Handle; var nested = registry.XData["NESTED"].ApplicationRegistry;
        var target = new DxfDocument(); var copy = target.ApplicationRegistries.Add(registry);
        Check(!ReferenceEquals(copy, registry) && ReferenceEquals(registry.Owner, source.ApplicationRegistries) && registry.Handle == handle && ReferenceEquals(source.GetObjectByHandle(handle), registry), "foreign Add moved source registry");
        ((byte[])copy.XData["NESTED"].XDataRecord[0].Value)[0] = 9; Equal((byte)3, ((byte[])registry.XData["NESTED"].XDataRecord[0].Value)[0], "registry clone shares binary metadata");
        Check(!target.ApplicationRegistries.Contains(registry) && !target.ApplicationRegistries.Remove(registry), "foreign same-name item matches collection membership");
        Check(ReferenceEquals(target.ApplicationRegistries["REGISTRY"], copy), "foreign remove deleted local registry");
        var line = new Line(); var caller = AppIdData(registry); line.XData.Add(caller); target.Entities.Add(line);
        copy.Name = "TARGET"; AppIdHas(line, "TARGET"); Equal("REGISTRY", registry.Name, "destination rename changed source registry"); Equal("NESTED", nested.Name, "destination adoption changed nested source registry");
    }
    private static void AppIdDetachedReplacement()
    {
        var holder = new Line(); var old = new ApplicationRegistry("APP"); holder.XData.Add(AppIdData(old)); var next = new ApplicationRegistry("APP"); holder.XData["APP"] = AppIdData(next);
        old.Name = "OLD_ONLY"; AppIdHas(holder, "APP"); next.Name = "RENAMED"; AppIdHas(holder, "RENAMED");
        XData removed = holder.XData["RENAMED"]; holder.XData.Remove("RENAMED"); next.Name = "DETACHED"; Check(holder.XData.Count == 0, "removed registry rekeyed collection");
        holder.XData.Add(removed); next.Name = "REATTACHED"; AppIdHas(holder, "REATTACHED");
    }
    private static void AppIdClone()
    {
        var source = new DxfDocument(); var record = new DxfXRecord(); source.NamedObjects.Add("RECORD", record); record.XData.Add(AppIdData(new ApplicationRegistry("APP")));
        source.ApplicationRegistries["APP"].Name = "SOURCE";
        var target = new DxfDocument(); var clone = target.Objects.CloneObject(record, target.NamedObjects, "CLONE");
        target.ApplicationRegistries["SOURCE"].Name = "TARGET"; AppIdHas(clone, "TARGET"); AppIdHas(record, "SOURCE");
        target.Objects.EraseOwnedTree(clone); Check(target.ApplicationRegistries.Remove("TARGET"), "erased clone retained APPID uses");
    }
    private static void AppIdObserverTransfer()
    {
        var source = new DxfDocument(); var target = new DxfDocument(); var registry = source.ApplicationRegistries.Add(new ApplicationRegistry("OLD"));
        registry.NameChanged += (_, _) => { Check(source.ApplicationRegistries.Remove(registry), "callback detach failed"); target.ApplicationRegistries.Add(registry); };
        registry.Name = "NEW"; Check(registry.Owner == target.ApplicationRegistries && ReferenceEquals(target.ApplicationRegistries["NEW"], registry) && !target.ApplicationRegistries.Contains("OLD"), "rename did not update callback-selected owner");
    }
}
