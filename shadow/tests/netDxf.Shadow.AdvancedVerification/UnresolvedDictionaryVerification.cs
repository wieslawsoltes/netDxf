using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class UnresolvedDictionaryVerification
{
    private const string DictionaryName = "DeferredReferences";
    private const string EntryName = "Deferred";
    private const string TargetHandle = "FA0000";

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("dictionary/unresolved soft links survive property history, rendering and save", RetainedSoftLink));
        tests.Add(("dictionary/source factory preserves unresolved soft links and rejects hard ownership", SourceFactory));
    }

    private static DxfDocument Create()
    {
        var document = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2013);
        document.Entities.Add(new Line(new Vector3(1, 2, 0), new Vector3(8, 5, 0)));
        var dictionary = new DxfDictionary { IsHardOwner = false };
        document.Objects.Root.Add(DictionaryName, dictionary);
        dictionary.AddUnresolvedSoftReference(EntryName, TargetHandle);
        return document;
    }

    private static void RetainedSoftLink()
    {
        var document = Create();
        var line = document.Entities.Lines.Single();
        var lineId = new ObjectId("H:" + line.Handle.ToUpperInvariant());
        var source = new ShadowSession(document); var editor = new EditorSession(source);
        var retained = source.Current;
        var entryId = UnresolvedEntry(retained).Id;
        Check(document, retained, entryId);
        editor.SetProperty(lineId, "EndPoint", ShadowValue.From(new Vec3(20, 9, 0)));
        Require(line.EndPoint == new Vector3(20, 9, 0), "The ordinary line edit was not applied.");
        Check(document, source.Current, entryId);
        for (var i = 0; i < 3; i++)
        {
            editor.Undo();
            Require(line.EndPoint == new Vector3(8, 5, 0), "Undo did not restore the line endpoint.");
            Check(document, source.Current, entryId);
            editor.Redo();
            Require(line.EndPoint == new Vector3(20, 9, 0), "Redo did not restore the edited line endpoint.");
            Check(document, source.Current, entryId);
        }
        Require(retained.Read(lineId)["EndPoint"].Vector() == new Vec3(8, 5, 0), "History mutated the retained snapshot.");
        foreach (var binary in new[] { false, true })
        {
            using var stream = new MemoryStream();
            editor.SynchronizeSource(() => source.MutateSource(value =>
                Require(value.Save(stream, binary), "Typed save failed.")));
            Check(document, source.Current, entryId);
            stream.Position = 0;
            var reloaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed reload failed.");
            var replay = new ShadowSession(reloaded).Current;
            Check(reloaded, replay, UnresolvedEntry(replay).Id);
            Require(reloaded.Entities.Lines.Single().EndPoint == new Vector3(20, 9, 0), "Save changed the edited geometry.");
        }
        editor.Undo();
        Require(line.EndPoint == new Vector3(8, 5, 0), "Save discarded the property undo history.");
        Check(document, source.Current, entryId);
        editor.Redo();
        Require(line.EndPoint == new Vector3(20, 9, 0), "Save discarded the property redo history.");
        Check(document, source.Current, entryId);
    }

    private static void Check(DxfDocument document, ShadowSnapshot snapshot, ObjectId entryId)
    {
        var dictionary = (DxfDictionary)document.Objects.Root[DictionaryName];
        var entry = dictionary.Entries.Single();
        Require(!dictionary.IsHardOwner && entry.Name == EntryName && !entry.IsHardOwner && !entry.IsResolved
            && entry.Target is null && entry.TargetHandle == TargetHandle, "The source soft link changed.");
        Require(document.GetObjectByHandle(TargetHandle) is null, "A target object was fabricated for the soft link.");
        var captured = snapshot.Read(entryId);
        Require(captured["Name"].Text == EntryName && captured["Target"].Value is ShadowValue.NullValue
            && captured["TargetHandle"].Text == TargetHandle && !captured["IsHardOwner"].Boolean()
            && !captured["IsResolved"].Boolean(), "Capture changed the unresolved soft link.");
        var rendering = new RenderCompiler().Compile(snapshot);
        Require(rendering.Items.Length == 1 && !rendering.Bounds.IsEmpty,
            "Unresolved metadata prevented line rendering.");
        Require(!rendering.Diagnostics.Any(d => d.Code is "geometry.invalid" or "render.geometry"),
            "Unresolved metadata introduced a geometry failure.");
    }

    private static ShadowObject UnresolvedEntry(ShadowSnapshot snapshot) => snapshot.Objects.Values.Single(node =>
        node.TypeName == typeof(DxfDictionaryEntry).FullName && node["Target"] is ShadowValue.NullValue);

    private static void SourceFactory()
    {
        var node = UnresolvedEntry(new ShadowSession(Create()).Current);
        // Exercise construction without enabling structural editing of the read-only
        // dictionary collection or exposing adapter internals as public API.
        var create = typeof(ShadowObjectFactories).GetMethod("Create", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var factories = new ShadowObjectFactories(); var resolver = new ScalarResolver();
        var copy = (DxfDictionaryEntry)create.Invoke(factories, [node, resolver, false])!;
        Require(copy.Name == EntryName && copy.TargetHandle == TargetHandle && copy.Target is null
            && !copy.IsHardOwner && !copy.IsResolved, "Source construction changed the soft reference.");
        foreach (var flag in new[] { ShadowValue.From(true), ShadowValue.Null })
        {
            var invalid = node with { Properties = node.Properties.SetItem("IsHardOwner", flag) };
            try { create.Invoke(factories, [invalid, resolver, false]); }
            catch (TargetInvocationException error) when (error.InnerException is NotSupportedException) { continue; }
            throw new InvalidOperationException("Source construction accepted an unresolved link without explicit soft ownership.");
        }
    }

    private sealed class ScalarResolver : IShadowObjectResolver
    {
        public object Resolve(ObjectId id) => throw new InvalidOperationException("An unresolved entry must not resolve an object.");
        public object? Convert(ShadowValue value, Type targetType) => value is ShadowValue.Atom atom && targetType == typeof(string)
            ? atom.Text : throw new InvalidOperationException("Unexpected source-factory conversion.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
