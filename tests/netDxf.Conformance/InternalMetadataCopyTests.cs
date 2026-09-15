using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterInternalMetadataCopyTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"metadata/internal-copy/{v}/{b}", () => InternalMetadataCopy(v, b));
        }
        Run("metadata/public-clone-keeps-override", () =>
        {
            var app = new MetadataCallbackRegistry("PUBLIC_COPY"); int called = 0; app.Callback = () => called++;
            _ = new XData(app).Clone(); Equal(1, called, "Explicit public XData clone override dispatch");
        });
    }
    private static void InternalMetadataCopy(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var app = new MetadataCallbackRegistry("INTERNAL_COPY"); int calls = 0;
        var cycle = new XData(app); cycle.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 3, 0, 255 })); app.XData.Add(cycle);
        app.Callback = () => { calls++; throw new InvalidOperationException("A stored metadata copy invoked caller code."); };
        var root = new DxfDictionary(); var note = new DxfXRecord(); root.Add("NOTE", note); note.XData.Add(new XData(app));
        doc.NamedObjects.Add("CALLBACK_ROOT", root); Equal(0, calls, "Registration callback");
        var registered = doc.ApplicationRegistries[app.Name]; Check(!ReferenceEquals(registered, app) && registered.GetType() == typeof(ApplicationRegistry), "Registration retained overridable caller registry");
        Check(ReferenceEquals(registered.XData[app.Name].ApplicationRegistry, registered), "Stored APPID self-cycle lost");
        Check(((byte[])registered.XData[app.Name].XDataRecord[0].Value).SequenceEqual(new byte[] { 3, 0, 255 }), "Stored APPID bytes lost");
        ((byte[])registered.XData[app.Name].XDataRecord[0].Value)[0] = 9;
        Equal((byte)3, ((byte[])cycle.XDataRecord[0].Value)[0], "Registry graph binary isolation");
        var sourceApp = new MetadataCallbackRegistry("GRAPH_COPY"); doc.ApplicationRegistries.Add(sourceApp); note.XData.Add(new XData(sourceApp));
        sourceApp.Callback = () => { calls++; doc.Objects.EraseOwnedTree(root); };
        var copy = doc.Objects.Clone(root, doc.Objects.Root, "CALLBACK_COPY");
        Check(!root.IsErased && copy.Database == doc.Objects && calls == 0, "Owned graph clone invoked an APPID override");
        var update = new MetadataCallbackRegistry("LIVE_COPY"); update.Callback = () => { calls++; throw new InvalidOperationException("Live metadata callback"); }; note.XData.Add(new XData(update));
        var shared = new XData(update); var first = new Line(); var second = new Line(); first.XData.Add(shared); second.XData.Add(shared);
        doc.Entities.Add(first); doc.Entities.Add(second); Equal(0, calls, "Live/shared/entity metadata binding callback");
        var foreign = new DxfDocument(version); var foreignApp = new MetadataCallbackRegistry("FOREIGN_COPY"); foreign.ApplicationRegistries.Add(foreignApp);
        foreignApp.Callback = () => { calls++; throw new InvalidOperationException("Foreign table transfer callback"); };
        var transferred = doc.ApplicationRegistries.Add(foreignApp); Check(!ReferenceEquals(transferred, foreignApp) && foreignApp.Owner == foreign.ApplicationRegistries, "Foreign registry transfer changed source identity");
        Equal(0, calls, "Foreign registry transfer callback"); Check(doc.Objects.Validate().Count == 0, "Callback-free database validation");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Metadata callback document save");
        var loaded = DxfDocument.Load(new MemoryStream(bytes.ToArray())) ?? throw new InvalidOperationException("Metadata callback document reload failed");
        Check(loaded.Objects.Validate().Count == 0, "Metadata callback document reload");
        var loadedApp = loaded.ApplicationRegistries[app.Name]; Check(ReferenceEquals(loadedApp.XData[app.Name].ApplicationRegistry, loadedApp), "Round-trip APPID self-cycle");
        Equal((byte)9, ((byte[])loadedApp.XData[app.Name].XDataRecord[0].Value)[0], "Round-trip APPID binary metadata");
    }
    private sealed class MetadataCallbackRegistry : ApplicationRegistry
    {
        internal Action? Callback;
        internal MetadataCallbackRegistry(string name) : base(name) { }
        public override object Clone() { this.Callback?.Invoke(); return base.Clone(); }
        public override TableObject Clone(string name) { return base.Clone(name); }
    }
}
