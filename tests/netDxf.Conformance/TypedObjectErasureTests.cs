using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private static void RunTypedObjectErasureTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"typed-erasure/lifecycle/{version}/{binary}", () => ErasureLifecycle(version, binary));
            Run($"typed-erasure/independent/{version}/{binary}", () => ErasureIndependent(version, binary));
        }
        foreach (bool flag in new[] { false, true }) foreach (bool hard in new[] { false, true })
            Run($"typed-erasure/ownership/{flag}/{hard}", () => ErasureOwnership(flag, hard));
        foreach (string kind in new[] { "idbuffer", "dictionary-default", "dictionary-entry", "reactor-document", "reactor-object", "reactor-line", "reactor-layer", "reactor-attribute", "reactor-layout-viewport", "xdata-document", "xdata-object", "xdata-line", "xdata-layer", "xdata-attribute", "xdata-attdef", "xdata-layout-viewport", "layout-shade", "standalone-shade", "header", "header-identity", "xrecord330", "xrecord339", "xrecord340", "xrecord349", "xrecord350", "xrecord359", "xrecord360", "xrecord369", "xrecord390", "xrecord399", "xrecord480", "xrecord481", "lowercase-padded" })
            Run("typed-erasure/incoming/" + kind, () => ErasureIncoming(kind));
        Run("typed-erasure/terminal-adoption", ErasureTerminal);
        Run("typed-erasure/appid-bookkeeping-and-handlers", ErasureAppIds);
        Run("typed-erasure/renamed-shared-appid-unregistration", ErasureRenamedAppId);
        Run("typed-erasure/unlinked-orphan-and-unrelated-invalid", ErasureOrphan);
        Run("typed-erasure/xrecord-owned-descendants", ErasureXRecordOwner);
        foreach (string host in new[] { "line", "block", "record", "dictionary" }) Run("typed-erasure/extension-host/" + host, () => ErasureHost(host));
        Run("typed-erasure/extension-plus-owning-aliases", ErasureExtensionAliases);
        Run("typed-erasure/nested-tombstone-rejection", ErasureNestedTombstone);
        Run("typed-erasure/mleader-and-unused-block", ErasureMLeader);
        Run("typed-erasure/arbitrary-handles-and-sort-keys", ErasureArbitrary);
        Run("typed-erasure/same-name-table-object-identity", ErasureSameNameIdentity);
        Run("typed-erasure/opaque-owned-rejection", () => ErasureOpaque(true, 330));
        foreach (short code in new short[] { 5, 320, 330, 340, 360, 390, 480, 1005 }) Run("typed-erasure/opaque-exposed/" + code, () => ErasureOpaque(false, code));
        Run("typed-erasure/opaque-unrelated-preservation", ErasureOpaqueUnrelated);
        Run("typed-erasure/callback-erases-clone-source", ErasureCloneCallback);
        Run("typed-erasure/deep-ownership-without-recursion", ErasureDeep);
    }
    private static DxfDictionary ErasureGraph(DxfDocument doc)
    {
        var root = new DxfDictionary { IsHardOwner = false }; var record = new DxfXRecord();
        record.Data.Add(new DxfTag(1, @"retained \U+0041")); record.Data.Add(new DxfTag(310, new byte[] { 0, 0xFE, 0xFF }));
        root.Add("primary", record, false); root.Add("alias", record, true); doc.NamedObjects.Add("ERASE", root); doc.NamedObjects.Add("ERASE_ALIAS", root, false);
        var extension = new DxfDictionary(); var leaf = new DxfDictionaryVariable { Value = "leaf" }; extension.Add("child", leaf); doc.Objects.SetExtensionDictionary(record, extension);
        record.Data.Add(new DxfTag(330, root.Handle)); record.Data.Add(new DxfTag(340, leaf.Handle));
        foreach (DxfDatabaseObject item in new DxfDatabaseObject[] { root, record, extension, leaf })
        {
            item.PersistentReactors.Add(item.Owner); var data = new XData(new ApplicationRegistry("ERASURE_APP"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, root.Handle)); data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 1, 3, 5 })); item.XData.Add(data);
        }
        return root;
    }
    private static string ErasureState(DxfDocument doc)
    {
        static int Id(object? item) => item == null ? 0 : RuntimeHelpers.GetHashCode(item);
        static string Value(object? value) => value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        _ = doc.Objects;
        var registered = (IEnumerable<KeyValuePair<string, DxfObject>>)typeof(DxfDocument).GetField("AddedObjects", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(doc)!;
        IEnumerable<DxfObject> carriers = registered.Select(p => p.Value).Concat(doc.Objects.Items).Concat(doc.Entities.Inserts.SelectMany(i => i.Attributes)).Concat(doc.Layouts.Where(l => l.Viewport != null).Select(l => l.Viewport));
        var rows = new List<string> { doc.DrawingVariables.HandleSeed, doc.Objects.Items.Count.ToString(CultureInfo.InvariantCulture) };
        foreach (DxfObject item in carriers.Distinct<DxfObject>(ReferenceEqualityComparer.Instance))
        {
            rows.Add($"{Id(item)}|{item.Handle}|{Id(item.Owner)}|{Id(item.ExtensionDictionary)}|{Id(doc.GetObjectByHandle(item.Handle))}");
            if (item is DxfDatabaseObject db) rows.Add($"database:{Id(db.Database)}:{db.IsErased}");
            foreach (DxfObject reactor in item.PersistentReactors) rows.Add("reactor:" + Id(reactor));
            foreach (XData data in item.XData.Values)
            {
                rows.Add($"app:{Id(data.ApplicationRegistry)}:{data.ApplicationRegistry.Name}:{data.ApplicationRegistry.Handle}:{Id(data.ApplicationRegistry.Owner)}");
                foreach (XDataRecord tag in data.XDataRecord) rows.Add($"xdata:{tag.Code}:{Value(tag.Value)}");
            }
            if (item is DxfDictionary dictionary) foreach (DxfDictionaryEntry entry in dictionary.Entries) rows.Add($"entry:{entry.Name}:{Id(entry.Target)}:{entry.IsHardOwner}");
            if (item is DxfDictionaryWithDefault fallback) rows.Add("default:" + Id(fallback.Default));
            if (item is DxfXRecord record) foreach (DxfTag tag in record.Data) rows.Add($"tag:{tag.Code}:{Value(tag.Value)}");
            if (item is DxfIdBuffer buffer) foreach (DxfObject target in buffer.References) rows.Add("buffer:" + Id(target));
            if (item is Layout layout) rows.Add("shade:" + Id(layout.PlotSettings.ShadePlotObject));
        }
        foreach (ApplicationRegistry app in doc.ApplicationRegistries) rows.Add($"registry:{app.Name}:{app.Handle}:{doc.ApplicationRegistries.GetReferences(app).Sum(r => r.Uses)}");
        foreach (HeaderVariable variable in doc.DrawingVariables.CustomValues()) rows.Add($"header:{variable.Name}:{variable.GroupCode}:{Value(variable.Value)}");
        return string.Join("\n", rows);
    }
    private static void ErasureReject(DxfDocument doc, Action operation)
    {
        string before = ErasureState(doc); bool rejected = false;
        try { operation(); } catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is NotSupportedException) { rejected = true; }
        Check(rejected, "Unsafe erasure or resurrection was accepted."); Equal(before, ErasureState(doc), "Rejected operation changed exact graph snapshot");
    }
    private static void ErasureLifecycle(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.Entities.Add(new Line(Vector3.Zero, new Vector3(3, 4, 5))); DxfDictionary root = ErasureGraph(doc);
        var record = (DxfXRecord)root["primary"]; var extension = record.ExtensionDictionary; var leaf = (DxfDatabaseObject)extension["child"];
        DxfDatabaseObject[] removed = { root, record, extension, leaf }; string[] handles = removed.Select(o => o.Handle).ToArray();
        string seed = doc.DrawingVariables.HandleSeed; int count = doc.Objects.Items.Count; doc.Objects.EraseOwnedTree(root);
        Equal(seed, doc.DrawingVariables.HandleSeed, "Erasure consumed or lowered seed"); Equal(count - 4, doc.Objects.Items.Count, "Alias caused duplicate deletion");
        Check(!doc.NamedObjects.Contains("ERASE") && !doc.NamedObjects.Contains("ERASE_ALIAS"), "Owning root aliases survived.");
        for (int i = 0; i < removed.Length; ++i) { Check(removed[i].IsErased && removed[i].Database == null, "Incomplete tombstone."); Equal(handles[i], removed[i].Handle, "Erased handle changed"); Check(doc.GetObjectByHandle(handles[i]) == null, "Erased object remains registered."); }
        Check(root.Owner == null && ReferenceEquals(record.Owner, root) && ReferenceEquals(extension.Owner, record) && ReferenceEquals(leaf.Owner, extension), "Terminal ownership links changed.");
        Equal(root.Handle, (string)record.Data[2].Value, "Internal packet handle changed"); Equal(root.Handle, (string)leaf.XData["ERASURE_APP"].XDataRecord[0].Value, "Internal XData handle changed"); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(root));
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var fresh = new DxfXRecord(); doc.NamedObjects.Add("FRESH" + cycle, fresh); Check(Convert.ToUInt64(fresh.Handle, 16) >= Convert.ToUInt64(seed, 16) && !handles.Contains(fresh.Handle), "Erased handle was reused.");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Erased graph save failed."); output.Position = 0; doc = DxfDocument.Load(output) ?? throw new Exception("Erased graph reload failed.");
            foreach (string handle in handles) Check(doc.GetObjectByHandle(handle) == null, "Erased identity reappeared after reload."); Equal(0, doc.Objects.Validate().Count, "Erased loaded graph invalid");
        }
    }
    private static void ErasureOwnership(bool flag, bool hard)
    { var doc = new DxfDocument(); var root = new DxfDictionary { IsHardOwner = flag }; var child = new DxfXRecord(); root.Add("x", child, hard); root.Add("alias", child, !hard); doc.NamedObjects.Add("P", root); doc.Objects.EraseOwnedTree(root); Check(root.IsErased && child.IsErased, "Ownership closure depends on pointer strength."); }
    private static (DxfDocument Doc, DxfXRecord Target) ErasureLeaf()
    { var doc = new DxfDocument(); var target = new DxfXRecord(); doc.NamedObjects.Add("TARGET", target); return (doc, target); }
    private static void ErasureIncoming(string kind)
    {
        var (doc, target) = ErasureLeaf(); Action clear;
        if (kind.StartsWith("xrecord", StringComparison.Ordinal) || kind == "lowercase-padded")
        {
            short code = kind == "lowercase-padded" ? (short)340 : short.Parse(kind.Substring(7), CultureInfo.InvariantCulture); var source = new DxfXRecord(); doc.NamedObjects.Add("SOURCE", source);
            var tag = new DxfTag(code, "000" + target.Handle.ToLowerInvariant()); if (code <= 369) source.Data.Add(tag); else typeof(DxfXRecord).GetMethod("AddLoadedData", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(source, new object[] { tag }); clear = () => source.Data.Clear();
        }
        else if (kind == "idbuffer") { var source = new DxfIdBuffer(); source.References.Add(target); source.References.Add(target); doc.NamedObjects.Add("SOURCE", source); clear = () => source.References.Clear(); }
        else if (kind == "dictionary-default") { var source = new DxfDictionaryWithDefault(); doc.NamedObjects.Add("SOURCE", source); source.Default = target; clear = () => source.Default = null; }
        else if (kind == "dictionary-entry") { var source = new DxfDictionary(); doc.NamedObjects.Add("SOURCE", source); typeof(DxfDictionary).GetMethod("AddLoaded", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(source, new object[] { "foreign alias", target, false }); clear = () => source.Remove("foreign alias"); }
        else if (kind is "header" or "header-identity") { doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$ERASURE_REF", kind == "header" ? (short)347 : (short)5, "000" + target.Handle.ToLowerInvariant())); clear = () => doc.DrawingVariables.RemoveCustomVariable("$ERASURE_REF"); }
        else if (kind == "layout-shade") { var settings = doc.Layouts.First().PlotSettings; settings.ShadePlotObject = target; clear = () => settings.ShadePlotObject = null; }
        else if (kind == "standalone-shade") { var source = doc.Objects.AddPlotSettings("PAGE", new PlotSettings { ShadePlotObject = target }); clear = () => source.Settings.ShadePlotObject = null; }
        else
        {
            DxfObject source;
            if (kind.EndsWith("layout-viewport", StringComparison.Ordinal)) { source = doc.Layouts.Add(new Layout("CARRIER")).Viewport; Check(doc.GetObjectByHandle(source.Handle) == null, "Layout viewport test is not exercising a nonregistry carrier."); }
            else if (kind.EndsWith("attribute", StringComparison.Ordinal) || kind.EndsWith("attdef", StringComparison.Ordinal))
            {
                var block = new Block("ATTR_BLOCK"); var definition = new AttributeDefinition("TAG") { Value = "sentinel" }; block.AttributeDefinitions.Add(definition); var insert = new Insert(block); doc.Entities.Add(insert);
                source = kind.EndsWith("attdef", StringComparison.Ordinal) ? definition : insert.Attributes.Single(); if (source is netDxf.Entities.Attribute) Check(doc.GetObjectByHandle(source.Handle) == null, "ATTRIB test is not exercising a nonregistry carrier.");
            }
            else if (kind.EndsWith("line", StringComparison.Ordinal)) { var line = new Line(); doc.Entities.Add(line); source = line; }
            else if (kind.EndsWith("layer", StringComparison.Ordinal)) source = doc.Layers["0"];
            else if (kind.EndsWith("document", StringComparison.Ordinal)) source = doc;
            else { var record = new DxfXRecord(); doc.NamedObjects.Add("SOURCE", record); source = record; }
            if (kind.StartsWith("reactor", StringComparison.Ordinal)) { source.PersistentReactors.Add(target); clear = () => source.PersistentReactors.Clear(); }
            else { var data = new XData(new ApplicationRegistry("INCOMING")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "00" + target.Handle.ToLowerInvariant())); source.XData.Add(data); clear = () => source.XData.Remove("INCOMING"); }
        }
        ErasureReject(doc, () => doc.Objects.EraseOwnedTree(target)); clear(); doc.Objects.EraseOwnedTree(target); Check(target.IsErased, "Corrected dependency did not permit retry.");
    }
    private static void ErasureTerminal()
    {
        var doc = new DxfDocument(); var root = ErasureGraph(doc); var child = (DxfDatabaseObject)root["primary"]; doc.Objects.EraseOwnedTree(root); var line = new Line(); doc.Entities.Add(line); var other = new DxfDocument(); _ = other.Objects;
        foreach (DxfDatabaseObject item in new[] { root, child }) { ErasureReject(doc, () => doc.NamedObjects.Add("RESURRECT", item)); ErasureReject(other, () => other.NamedObjects.Add("RESURRECT", item)); ErasureReject(doc, () => new DxfDictionary().Add("RESURRECT", item)); ErasureReject(doc, () => doc.Objects.CloneObject(item, doc.NamedObjects, "RESURRECT")); }
        ErasureReject(doc, () => doc.Objects.SetExtensionDictionary(line, root)); ErasureReject(doc, () => doc.Objects.Clone(root, doc.NamedObjects, "RESURRECT"));
        var fresh = new DxfXRecord(); ErasureReject(doc, () => root.Add("RESURRECT", fresh)); Check(fresh.Owner == null && fresh.Database == null && fresh.Handle == null, "Dead dictionary adopted a fresh object.");
        var buffer = new DxfIdBuffer(); doc.NamedObjects.Add("BUFFER", buffer); ErasureReject(doc, () => buffer.References.Add(child));
        ErasureReject(doc, () => doc.Objects.EraseOwnedTree(doc.NamedObjects)); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(new DxfXRecord())); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(null!));
        var foreign = new DxfXRecord(); other.NamedObjects.Add("FOREIGN", foreign); string state = ErasureState(other); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(foreign)); Equal(state, ErasureState(other), "Foreign source changed");
    }
    private static void ErasureAppIds()
    {
        var doc = new DxfDocument(); var root = ErasureGraph(doc); var registry = doc.ApplicationRegistries["ERASURE_APP"]; Equal(4, doc.ApplicationRegistries.GetReferences(registry).Sum(r => r.Uses), "APPID initial uses");
        int userEvents = 0; root.XDataAddAppReg += (_, _) => userEvents++; doc.Objects.EraseOwnedTree(root); Equal(0, doc.ApplicationRegistries.GetReferences(registry).Sum(r => r.Uses), "APPID erase uses"); Check(doc.ApplicationRegistries.Remove(registry), "Erased APPID use retained.");
        root.XData.Remove("ERASURE_APP"); root.XData.Add(new XData(new ApplicationRegistry("AFTER_ERASE"))); Equal(1, userEvents, "External object event subscription changed"); Check(!doc.ApplicationRegistries.Contains("AFTER_ERASE"), "Erased XData still registers application IDs.");
        var retained = new DxfXRecord(); doc.NamedObjects.Add("KEEP", retained); retained.XData.Add(new XData(new ApplicationRegistry("SHARED"))); var dead = new DxfXRecord(); doc.NamedObjects.Add("DEAD", dead); dead.XData.Add(new XData(new ApplicationRegistry("SHARED"))); doc.Objects.EraseOwnedTree(dead);
        Equal(1, doc.ApplicationRegistries.GetReferences("SHARED").Sum(r => r.Uses), "Shared APPID count"); Check(!doc.ApplicationRegistries.Remove("SHARED"), "Live APPID use removed.");
    }
    private static void ErasureOrphan()
    { var (doc, target) = ErasureLeaf(); doc.NamedObjects.Remove("TARGET"); var unrelated = new DxfXRecord(); doc.NamedObjects.Add("INVALID", unrelated); unrelated.Data.Add(new DxfTag(340, "FFFF")); Check(doc.Objects.Validate().Count > 0, "Orphan test must start invalid."); doc.Objects.EraseOwnedTree(target); Check(target.IsErased, "Registered orphan not erased."); Equal("FFFF", (string)unrelated.Data[0].Value, "Unrelated invalid payload changed"); }
    private static void ErasureRenamedAppId()
    {
        var (doc, first) = ErasureLeaf(); var second = new DxfXRecord(); doc.NamedObjects.Add("SECOND", second);
        first.XData.Add(new XData(new ApplicationRegistry("SHARED"))); second.XData.Add(new XData(new ApplicationRegistry("SHARED")));
        var registry = doc.ApplicationRegistries["SHARED"]; registry.Name = "RENAMED";
        Equal(2, doc.ApplicationRegistries.GetReferences(registry).Sum(r => r.Uses), "Renamed shared initial uses");
        doc.Objects.EraseOwnedTree(first); Equal(1, doc.ApplicationRegistries.GetReferences(registry).Sum(r => r.Uses), "Renamed APPID first erase count");
        Check(!doc.ApplicationRegistries.Remove(registry), "Live renamed APPID use was lost."); doc.Objects.EraseOwnedTree(second);
        Equal(0, doc.ApplicationRegistries.GetReferences(registry).Sum(r => r.Uses), "Renamed APPID final erase count"); Check(doc.ApplicationRegistries.Remove(registry), "Renamed APPID could not be removed.");
        foreach (DxfXRecord dead in new[] { first, second }) { dead.XData.Clear(); dead.XData.Add(new XData(new ApplicationRegistry("AFTER_RENAME_ERASE"))); }
        Check(!doc.ApplicationRegistries.Contains("AFTER_RENAME_ERASE"), "Renamed tombstone retained registration handlers.");
    }
    private static void ErasureXRecordOwner()
    {
        var (doc, root) = ErasureLeaf(); var child = new DxfXRecord(); doc.NamedObjects.Add("TEMP", child); doc.NamedObjects.Remove("TEMP"); typeof(DxfObject).GetProperty(nameof(DxfObject.Owner))!.SetValue(child, root); root.Data.Add(new DxfTag(360, child.Handle));
        var grandchild = new DxfDictionary(); doc.Objects.SetExtensionDictionary(child, grandchild); grandchild.Add("VAR", new DxfDictionaryVariable()); doc.Objects.EraseOwnedTree(root);
        Check(root.IsErased && child.IsErased && grandchild.IsErased && ((DxfDatabaseObject)grandchild["VAR"]).IsErased, "Non-dictionary owner descendants survived."); Check(ReferenceEquals(child.Owner, root), "Internal XRECORD ownership changed.");
    }
    private static void ErasureHost(string kind)
    {
        var doc = new DxfDocument(); DxfObject host;
        if (kind == "block") host = doc.Blocks.Add(new Block("HOST")).Record;
        else if (kind == "line") { var line = new Line(); doc.Entities.Add(line); host = line; }
        else { var item = kind == "record" ? (DxfDatabaseObject)new DxfXRecord() : new DxfDictionary(); doc.NamedObjects.Add("HOST", item); host = item; }
        var extension = new DxfDictionary(); extension.Add("CHILD", new DxfXRecord()); doc.Objects.SetExtensionDictionary(host, extension); var incoming = new DxfXRecord(); doc.NamedObjects.Add("INCOMING", incoming); incoming.Data.Add(new DxfTag(340, extension["CHILD"].Handle));
        ErasureReject(doc, () => doc.Objects.EraseOwnedTree(extension)); incoming.Data.Clear(); doc.Objects.EraseOwnedTree(extension); Check(host.ExtensionDictionary == null && ReferenceEquals(doc.GetObjectByHandle(host.Handle), host), "Live host was erased."); doc.Objects.SetExtensionDictionary(host, new DxfDictionary());
    }
    private static void ErasureExtensionAliases()
    {
        var doc = new DxfDocument(); var parent = new DxfDictionaryWithDefault(); doc.NamedObjects.Add("PARENT", parent); var extension = new DxfDictionary(); extension.Add("CHILD", new DxfXRecord()); parent.Add("PRIMARY", extension, false); parent.Add("ALIAS", extension, true); doc.Objects.SetExtensionDictionary(parent, extension); parent.Default = extension;
        ErasureReject(doc, () => doc.Objects.EraseOwnedTree(extension)); parent.Default = null; doc.Objects.EraseOwnedTree(extension); Check(!parent.IsErased && parent.Count == 0 && parent.ExtensionDictionary == null, "Combined aliases and extension not removed.");
    }
    private static void ErasureNestedTombstone()
    {
        var (doc, dead) = ErasureLeaf(); doc.Objects.EraseOwnedTree(dead); var host = new Line(); doc.Entities.Add(host);
        foreach (bool attach in new[] { false, true }) foreach (bool fallback in new[] { false, true })
        {
            var graph = new DxfDictionary(); if (fallback) { var nested = new DxfDictionaryWithDefault { Default = dead }; graph.Add("NESTED", nested); } else { var nested = new DxfIdBuffer(); nested.References.Add(dead); graph.Add("NESTED", nested); }
            ErasureReject(doc, () => { if (attach) doc.Objects.SetExtensionDictionary(host, graph); else doc.NamedObjects.Add("NESTED", graph); }); Check(graph.Database == null && graph.Handle == null && graph.Owner == null && host.ExtensionDictionary == null, "Nested rejection adopted graph."); Check(((DxfDatabaseObject)graph["NESTED"]).Handle == null, "Nested rejection allocated child.");
        }
    }
    private static void ErasureMLeader()
    { var (doc, leader) = NewMLeader(DxfVersion.AutoCad2018); var block = doc.Blocks.Add(new Block("UNUSED")); block.Entities.Add(leader); var style = leader.Properties.Style; ErasureReject(doc, () => doc.Objects.EraseOwnedTree(style)); block.Entities.Remove(leader); doc.Objects.EraseOwnedTree(style); Check(style.IsErased, "MULTILEADER style cleanup failed."); }
    private static void ErasureArbitrary()
    {
        var (doc, target) = ErasureLeaf(); var source = new DxfXRecord(); source.Data.Add(new DxfTag(320, target.Handle)); source.Data.Add(new DxfTag(1, target.Handle)); doc.NamedObjects.Add("ARBITRARY", source); var line = new Line(); doc.Entities.Add(line);
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2018;
        var sort = doc.Objects.CreateSortentsTable(line.Owner.Record, new[] { new DxfSortOrderEntry(line, target.Handle) }); doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$ARBITRARY", 320, target.Handle)); doc.Objects.EraseOwnedTree(target);
        Equal(target.Handle, sort.Entries[0].SortHandle, "Sort key changed"); Equal(target.Handle, (string)source.Data[0].Value, "Arbitrary handle changed"); using var output = new MemoryStream(); Check(doc.Save(output, true), "Preserved arbitrary handles failed export.");
    }
    private static void ErasureSameNameIdentity()
    {
        var (doc, target) = ErasureLeaf(); var real = doc.TextStyles["Standard"]; var foreign = new DxfDocument(); var decoy = foreign.TextStyles["Standard"]; Check(real.Equals(decoy) && !ReferenceEquals(real, decoy), "Same-name identity precondition."); real.PersistentReactors.Add(target);
        ErasureReject(doc, () => doc.Objects.EraseOwnedTree(target)); real.PersistentReactors.Clear(); decoy.PersistentReactors.Add(target); doc.Objects.EraseOwnedTree(target); Check(target.IsErased && ReferenceEquals(decoy.PersistentReactors[0], target), "Foreign same-name carrier affected local erasure.");
    }
    private static DxfOpaqueObject ErasureNewOpaque(short code, string handle)
    { return (DxfOpaqueObject)Activator.CreateInstance(typeof(DxfOpaqueObject), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "QA_PRIVATE", new List<DxfTag> { new DxfTag(100, "AcDbQaPrivate"), new DxfTag(code, handle) } }, CultureInfo.InvariantCulture)!; }
    private static void ErasureOpaque(bool owned, short code)
    { var (doc, target) = ErasureLeaf(); var opaque = ErasureNewOpaque(code, target.Handle); doc.NamedObjects.Add("OPAQUE", opaque); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(owned ? opaque : target)); if (owned) { doc.NamedObjects.Remove("OPAQUE"); typeof(DxfObject).GetProperty(nameof(DxfObject.Owner))!.SetValue(opaque, target); ErasureReject(doc, () => doc.Objects.EraseOwnedTree(target)); } }
    private static void ErasureOpaqueUnrelated()
    { var (doc, target) = ErasureLeaf(); var opaque = ErasureNewOpaque(340, doc.Layers["0"].Handle); doc.NamedObjects.Add("OPAQUE", opaque); string stored = (string)opaque.Tags[1].Value; doc.Objects.EraseOwnedTree(target); Check(!opaque.IsErased && ReferenceEquals(opaque.Database, doc.Objects), "Unrelated opaque object erased."); Equal(stored, (string)opaque.Tags[1].Value, "Opaque payload changed"); }
    private static void ErasureCloneCallback()
    { var source = new DxfDocument(); var root = ErasureGraph(source); var target = new DxfDocument(); _ = target.Objects; var callback = new ContainerCallbackMappings(() => source.Objects.EraseOwnedTree(root)); ErasureReject(target, () => target.Objects.Clone(root, target.NamedObjects, "COPY", callback)); Check(root.IsErased, "Mapping callback did not execute."); }
    private static void ErasureDeep()
    { var doc = new DxfDocument(); var root = new DxfDictionary(); doc.NamedObjects.Add("DEEP", root); DxfDictionary current = root; for (int i = 0; i < 2048; i++) { var child = new DxfDictionary(); current.Add("CHILD", child); current = child; } doc.Objects.EraseOwnedTree(root); Check(root.IsErased && current.IsErased, "Deep ownership closure incomplete."); Equal(1, doc.Objects.Items.Count, "Deep registration residue"); }
    private static void ErasureIndependent(DxfVersion version, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", ""); string directory = Path.Combine("tests", "fixtures", "typed-erasure"); using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        JsonElement fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32().ToString(CultureInfo.InvariantCulture) == year); string path = Path.Combine(directory, fixture.GetProperty("file").GetString()!);
        Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), "Independent erasure fixture provenance");
        var doc = DxfDocument.Load(path) ?? throw new Exception("Independent erasure input failed."); var root = (DxfDictionary)doc.NamedObjects["QA_ERASE"]; string[] removed = fixture.GetProperty("erased_handles").EnumerateObject().Select(p => p.Value.GetString()!).ToArray();
        string seed = doc.DrawingVariables.HandleSeed; doc.Objects.EraseOwnedTree(root); Equal(seed, doc.DrawingVariables.HandleSeed, "Independent erase seed"); foreach (string handle in removed) Check(doc.GetObjectByHandle(handle) == null, "Independent erased descendant retained.");
        var fresh = new DxfXRecord(); fresh.Data.Add(new DxfTag(1, "fresh after erase")); doc.NamedObjects.Add("QA_FRESH", fresh); Check(Convert.ToUInt64(fresh.Handle, 16) >= Convert.ToUInt64(seed, 16), "Independent fresh identity reused a deleted handle.");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Independent erased output failed."); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"typed-erasure-R{year}-{(binary ? "binary" : "ascii")}.dxf"), output.ToArray());
    }
}
