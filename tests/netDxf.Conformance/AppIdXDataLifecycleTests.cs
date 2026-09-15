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
        Run("appid-lifecycle/clone-names-and-cycles", AppIdNamedClone);
        Run("appid-lifecycle/retained-attributes-and-block-end", AppIdMetadataCarriers);
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
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "renamed APPID save"); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
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
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "viewport APPID save"); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
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
    private static void AppIdMetadataCarriers()
    {
        var doc = new DxfDocument(); var block = new netDxf.Blocks.Block("META"); block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "value" }); var insert = new Insert(block); doc.Entities.Add(insert);
        var registry = doc.ApplicationRegistries.Add(new ApplicationRegistry("CARRIERS")); var end = (DxfObject)typeof(netDxf.Blocks.Block).GetProperty("End", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(block)!; insert.Attributes[0].XData.Add(AppIdData(registry)); end.XData.Add(AppIdData(registry));
        var refs = doc.ApplicationRegistries.GetReferences(registry); Equal(2, refs.Sum(reference => reference.Uses), "retained carriers counted twice or omitted");
        Check(refs.Count == 2 && refs.Any(reference => ReferenceEquals(reference.Reference, insert.Attributes[0])) && refs.Any(reference => ReferenceEquals(reference.Reference, end)), "carrier reference identities changed");
        Check(!doc.ApplicationRegistries.Remove(registry), "referenced carrier APPID removed"); insert.Attributes[0].XData.Clear(); end.XData.Clear(); Check(doc.ApplicationRegistries.Remove(registry), "cleared carrier APPID retained");
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
