// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Qualification
{
internal static class LayerStateIdentityCases
{
    internal sealed class Case
    {
        internal readonly string Id;
        internal readonly Action Test;
        internal Case(string id, Action test) { this.Id = id; this.Test = test; }
    }
    private static readonly DxfVersion[] SupportedVersions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
        DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message)
    { Check(EqualityComparer<T>.Default.Equals(expected, actual), message + ": " + expected + " != " + actual); }

    internal static IEnumerable<Case> All(string? directory)
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (bool populated in new[] { false, true })
            yield return new Case($"wire/{version}/{binary}/{populated}", () => LayerIdentityWire(version, binary, populated, directory));
        foreach (bool binary in new[] { false, true })
        {
            yield return new Case($"empty-wrapper/{binary}", () => LayerIdentityEmptyWrapper(binary));
            yield return new Case($"references/{binary}", () => LayerIdentityReferences(binary));
            yield return new Case($"metadata/{binary}", () => LayerIdentityMetadata(binary, directory));
            yield return new Case($"framing/{binary}", () => LayerIdentityFraming(binary));
            foreach (string kind in new[] { "pointer", "reactor", "xdata", "header", "extension", "literal" })
                yield return new Case($"remove/{binary}/{kind}", () => LayerIdentityRemoval(binary, kind));
            foreach (string fault in new[] { "missing-child", "wrong-child", "wrapper-owner", "child-owner", "state-owner", "extra-entry", "unknown-only", "duplicate-name", "duplicate-state-name", "aliased-state", "duplicate-wrapper-handle" })
                yield return new Case($"refused/{binary}/{fault}", () => LayerIdentityRefused(binary, fault));
        }
    }
    internal static void VerifyInstalled()
    {
        foreach (var item in All(null))
            try { item.Test(); }
            catch (Exception error) { throw new InvalidOperationException("Installed layer-state identity: " + item.Id, error); }
    }

    private static DxfDocument LayerIdentitySeed(DxfVersion version, bool populated)
    {
        var doc = new DxfDocument(version);
        doc.Layers.Add(new Layer("IDENTITY_LAYER") { Color = new AciColor(3) });
        doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        doc.NamedObjects.Add("UNRELATED", new DxfDictionaryVariable { Value = "unchanged" });
        if (populated)
        {
            doc.Layers.StateManager.AddNew("FIRST", "first snapshot");
            doc.Layers.StateManager.AddNew("SECOND", "second snapshot");
            var app = new ApplicationRegistry("IDENTITY_DATA");
            foreach (var state in doc.Layers.StateManager)
            {
                var data = new XData(app); data.XDataRecord.Add(new XDataRecord(XDataCode.String, state.Name)); state.XData.Add(data);
            }
        }
        return doc;
    }
    private static byte[] LayerIdentitySave(DxfDocument doc, bool binary)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Layer identity save");
        Check(stream.CanWrite, "Save closed caller stream"); return stream.ToArray();
    }
    private static DxfDocument LayerIdentityLoad(byte[] data)
    {
        var before = (byte[])data.Clone(); using var stream = new MemoryStream(data);
        var doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Layer identity load");
        Check(stream.CanRead && data.SequenceEqual(before), "Load modified source or closed stream"); return doc;
    }
    private static DxfRawDocument LayerIdentityRaw(byte[] data)
    { using var stream = new MemoryStream(data); return DxfRawDocument.Load(stream); }
    private static string LayerIdentityValue(DxfRawRecord record, short code) => (string)record.Tags.Single(t => t.Code == code).Value;
    private static (DxfRawRecord Table, DxfRawRecord Wrapper, DxfRawRecord States) LayerIdentityRecords(DxfRawDocument raw)
    {
        var table = raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, "LAYER")));
        var wrapper = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, LayerIdentityValue(table, 360))));
        var states = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, LayerIdentityValue(wrapper, 360))));
        return (table, wrapper, states);
    }
    private static string[] LayerIdentityHandles(byte[] bytes)
    {
        var records = LayerIdentityRecords(LayerIdentityRaw(bytes));
        return new[] { LayerIdentityValue(records.Table, 5), LayerIdentityValue(records.Wrapper, 5), LayerIdentityValue(records.States, 5) }
            .Concat(records.States.Tags.Where(t => t.Code == 350).Select(t => (string)t.Value)).ToArray();
    }
    private static void LayerIdentityWire(DxfVersion version, bool binary, bool populated, string? directory)
    {
        var doc = LayerIdentitySeed(version, populated); string[] stateIds = doc.Layers.StateManager.Select(s => s.Handle).ToArray();
        string prefix = $"layer-identity-{version}-{binary}-{populated}";
        byte[] source = LayerIdentitySave(doc, binary); var expected = LayerIdentityHandles(source);
        if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
        Check(expected.Skip(3).SequenceEqual(stateIds), "First save changed state identities");
        foreach (int stage in Enumerable.Range(0, 4))
        {
            byte[] bytes = LayerIdentitySave(doc, stage % 2 == 0 ? binary : !binary);
            Check(expected.SequenceEqual(LayerIdentityHandles(bytes)), "Dictionary or state identity changed across save");
            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-" + stage + ".dxf"), bytes);
            var raw = LayerIdentityRaw(bytes); var records = LayerIdentityRecords(raw);
            Equal(expected[0], LayerIdentityValue(records.Wrapper, 330), "Wrapper owner");
            Equal(expected[1], LayerIdentityValue(records.States, 330), "State dictionary owner");
            if (stage != 0) doc = LayerIdentityLoad(bytes);
            Equal(expected[1], doc.Layers.StateManager.Handle, "Registered manager identity");
            Check(doc.GetObjectByHandle(expected[2]) != null, "State dictionary not registered");
            Check(stateIds.SequenceEqual(doc.Layers.StateManager.Select(s => s.Handle)), "Loaded state identity");
            foreach (var state in doc.Layers.StateManager)
            {
                Equal(state.Name, (string)state.XData["IDENTITY_DATA"].XDataRecord.Single().Value, "State XData retention");
                Equal((short)3, state.Properties["IDENTITY_LAYER"].Color.Index, "Layer snapshot semantics");
            }
            var line = doc.Entities.Lines.Single();
            Check(line.StartPoint == new Vector3(1, 2, 3) && line.EndPoint == new Vector3(4, 5, 6), "Unrelated geometry");
            Equal("unchanged", ((DxfDictionaryVariable)doc.NamedObjects["UNRELATED"]).Value, "Unrelated object");
            Equal(0, doc.Objects.Validate().Count, "Registered graph validation");
        }
        // Mutation of the public collection must not replace either dictionary.
        doc.Layers.StateManager.AddNew("LATER"); var later = doc.Layers.StateManager["LATER"];
        byte[] added = LayerIdentitySave(doc, binary);
        Check(LayerIdentityHandles(added).Take(3).SequenceEqual(expected.Take(3)), "Adding state changed dictionaries");
        Check(doc.Layers.StateManager.Remove(later), "Removing added state failed");
        Check(LayerIdentityHandles(LayerIdentitySave(doc, binary)).SequenceEqual(expected), "Removing state changed remaining identities");
    }
    private static void LayerIdentityEmptyWrapper(bool binary)
    {
        var raw = LayerIdentityRaw(LayerIdentitySave(LayerIdentitySeed(DxfVersion.AutoCad2018, false), binary));
        var parts = LayerIdentityRecords(raw); string wrapper = LayerIdentityValue(parts.Wrapper, 5);
        raw = raw.WithRecord(parts.Wrapper, parts.Wrapper.Tags.Where(t => t.Code != 3 && t.Code != 360));
        var orphan = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, LayerIdentityValue(parts.States, 5))));
        raw = DxfRawDocument.Create(raw.Tags.Take(orphan.StartTagIndex).Concat(raw.Tags.Skip(orphan.EndTagIndex)));
        using var stream = new MemoryStream(); raw.Save(stream, binary);
        var doc = LayerIdentityLoad(stream.ToArray()); Equal(wrapper, doc.Layers.StateManager.Handle, "Empty wrapper lost source identity");
        var first = LayerIdentityHandles(LayerIdentitySave(doc, binary));
        var second = LayerIdentityHandles(LayerIdentitySave(LayerIdentityLoad(LayerIdentitySave(doc, !binary)), binary));
        Check(first.SequenceEqual(second), "Materialized empty state child is unstable");
    }
    private static void LayerIdentityReferences(bool binary)
    {
        var doc = LayerIdentitySeed(DxfVersion.AutoCad2018, true); var handles = LayerIdentityHandles(LayerIdentitySave(doc, binary));
        var record = new DxfXRecord(); foreach (string handle in handles.Skip(1)) record.Data.Add(new DxfTag(330, handle));
        doc.NamedObjects.Add("REFERENCES", record);
        var loaded = LayerIdentityLoad(LayerIdentitySave(doc, binary));
        var values = ((DxfXRecord)loaded.NamedObjects["REFERENCES"]).Data.Select(t => (string)t.Value).ToArray();
        Check(values.SequenceEqual(handles.Skip(1)) && values.All(h => loaded.GetObjectByHandle(h) != null), "Incoming source references lost their actual target");
        Check(LayerIdentityHandles(LayerIdentitySave(loaded, !binary)).SequenceEqual(handles), "Referenced identity changed on save");
    }
    private static DxfDictionary LayerIdentityExtension(DxfDocument doc, DxfObject owner)
    {
        var dictionary = new DxfDictionary(); doc.Objects.SetExtensionDictionary(owner, dictionary); return dictionary;
    }

    private static void LayerIdentityMetadata(bool binary, string? directory)
    {
        var seed = LayerIdentitySeed(DxfVersion.AutoCad2018, true);
        var ids = LayerIdentityHandles(LayerIdentitySave(seed, binary));
        var target = seed.NamedObjects["UNRELATED"];
        foreach (string id in ids.Skip(1))
        {
            var item = seed.GetObjectByHandle(id);
            item.PersistentReactors.Add(target);
            var extension = LayerIdentityExtension(seed, item);
            extension.Add("NOTE", new DxfDictionaryVariable { Value = "metadata-" + id });
            if (!item.XData.ContainsAppId("IDENTITY_DATA"))
            {
                var data = new XData(seed.ApplicationRegistries["IDENTITY_DATA"]);
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, "metadata-" + id)); item.XData.Add(data);
            }
        }
        var raw = LayerIdentityRaw(LayerIdentitySave(seed, binary));
        foreach (string id in ids.Skip(1))
        {
            var record = raw.Sections.Single(x => x.Name == "OBJECTS").Records.Single(x => x.Tags.Any(t => t.Code == 5 && Equals(t.Value, id)));
            int subclass = record.Tags.ToList().FindIndex(t => t.Code == 100);
            var tags = record.Tags.Select((t, i) => i > subclass && t.Code == 280 ? new DxfTag(280, (short)(id == ids[1] || id == ids[2] ? 0 : 3))
                : i > subclass && t.Code == 281 ? new DxfTag(281, (short)(id == ids[1] ? 4 : 5))
                : i > subclass && id == ids[1] && t.Code == 360 ? new DxfTag(350, t.Value)
                : i > subclass && id == ids[2] && t.Code == 350 ? new DxfTag(360, t.Value) : t);
            raw = raw.WithRecord(record, tags);
        }
        using var input = new MemoryStream(); raw.Save(input, binary);
        byte[] source = input.ToArray(); var doc = LayerIdentityLoad(source);
        string prefix = "layer-identity-metadata-" + binary;
        if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
        for (int stage = 0; stage < 3; stage++)
        {
            foreach (string id in ids.Skip(1))
            {
                var item = doc.GetObjectByHandle(id);
                Check(item.PersistentReactors.Any(x => x.Handle == target.Handle), "Persistent reactor lost");
                Check(item.ExtensionDictionary != null && ReferenceEquals(item.ExtensionDictionary.Owner, item), "Extension identity/owner lost");
                Equal("metadata-" + id, ((DxfDictionaryVariable)item.ExtensionDictionary!["NOTE"]).Value, "Extension payload changed");
                Check(item.XData.ContainsAppId("IDENTITY_DATA"), "Managed dictionary/state XData lost");
            }
            byte[] bytes = LayerIdentitySave(doc, stage % 2 == 0 ? binary : !binary);
            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-" + stage + ".dxf"), bytes);
            var saved = LayerIdentityRaw(bytes);
            foreach (string id in ids.Skip(1))
            {
                var before = raw.Sections.Single(x => x.Name == "OBJECTS").Records.Single(x => x.Tags.Any(t => t.Code == 5 && Equals(t.Value, id)));
                var after = saved.Sections.Single(x => x.Name == "OBJECTS").Records.Single(x => x.Tags.Any(t => t.Code == 5 && Equals(t.Value, id)));
                Check(before.Tags.Select(t => t.Code).SequenceEqual(after.Tags.Select(t => t.Code))
                    && before.Tags.Zip(after.Tags, (a, b) => Equals(a.Value, b.Value)).All(x => x), "Physical metadata/pointer/cloning packet changed: " + id);
            }
            doc = LayerIdentityLoad(bytes);
        }
    }

    private static void LayerIdentityFraming(bool binary)
    {
        foreach (string text in new[] { "bad\0value", "bad\ud800value", "bad\udfffvalue", "bad\r\nvalue" })
        for (int kind = 0; kind < 4; kind++)
        {
            var doc = LayerIdentitySeed(DxfVersion.AutoCad2018, true);
            var ids = LayerIdentityHandles(LayerIdentitySave(doc, binary));
            var state = doc.Layers.StateManager["FIRST"];
            if (kind == 0) state.Description = text;
            else
            {
                DxfObject item = kind == 1 ? state : kind == 2 ? doc.Layers.StateManager : doc.GetObjectByHandle(ids[2]);
                var data = new XData(doc.ApplicationRegistries["IDENTITY_DATA"]);
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, text)); item.XData["IDENTITY_DATA"] = data;
            }
            if (binary && text.IndexOf('\r') >= 0)
            {
                var copy = LayerIdentityLoad(LayerIdentitySave(doc, true));
                string actual = kind == 0 ? copy.Layers.StateManager["FIRST"].Description
                    : (string)copy.GetObjectByHandle(kind == 1 ? state.Handle : ids[kind == 2 ? 1 : 2]).XData["IDENTITY_DATA"].XDataRecord.Single().Value;
                Equal(text, actual, "Binary multiline text changed");
                continue;
            }
            using var stream = new MemoryStream(); stream.Write(new byte[] { 5, 1, 3 }, 0, 3);
            string seed = doc.DrawingVariables.HandleSeed;
            bool refused = false;
            try { refused = !doc.Save(stream, binary); } catch (InvalidDataException) { refused = true; }
            Check(refused && stream.CanWrite && stream.Position == 3 && stream.ToArray().SequenceEqual(new byte[] { 5, 1, 3 }), "Unsafe layer-state string changed output");
            Equal(seed, doc.DrawingVariables.HandleSeed, "Framing refusal changed handle seed");
        }
    }

    private static void LayerIdentityRemoval(bool binary, string kind)
    {
        var doc = LayerIdentitySeed(DxfVersion.AutoCad2018, true);
        var state = doc.Layers.StateManager["FIRST"]; string handle = state.Handle;
        if (kind == "pointer" || kind == "literal")
        {
            var record = new DxfXRecord(); record.Data.Add(new DxfTag((short)(kind == "pointer" ? 330 : 1), handle));
            doc.NamedObjects.Add("REFERENCE", record);
        }
        if (kind == "reactor") doc.Entities.Lines.Single().PersistentReactors.Add(state);
        if (kind == "xdata")
        {
            var data = new XData(doc.ApplicationRegistries["IDENTITY_DATA"]);
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "000" + handle.ToLowerInvariant()));
            doc.Entities.Lines.Single().XData.Add(data);
        }
        if (kind == "header") doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$STATE_REFERENCE", 330, handle));
        if (kind == "extension") LayerIdentityExtension(doc, state).Add("CHILD", new DxfDictionaryVariable { Value = "retained" });
        doc = LayerIdentityLoad(LayerIdentitySave(doc, binary)); state = doc.Layers.StateManager["FIRST"];
        if (kind == "literal")
        {
            Check(doc.Layers.StateManager.Remove(state), "Literal handle-looking text incorrectly blocked removal");
            Check(doc.GetObjectByHandle(handle) == null, "Removed state remains registered");
            return;
        }
        Check(!doc.Layers.StateManager.Remove(state), "Incoming reference or owned extension was orphaned");
        Check(ReferenceEquals(state, doc.GetObjectByHandle(handle)) && state.Owner == doc.Layers.StateManager, "Refused removal changed identity");
        Equal(0, doc.Objects.Validate().Count, "Refused removal damaged graph");
        LayerIdentitySave(doc, !binary);
    }

    private static void LayerIdentityRefused(bool binary, string fault)
    {
        var raw = LayerIdentityRaw(LayerIdentitySave(LayerIdentitySeed(DxfVersion.AutoCad2018, true), binary));
        var p = LayerIdentityRecords(raw); DxfRawRecord target = p.Wrapper;
        IEnumerable<DxfTag> tags = target.Tags;
        if (fault == "missing-child") tags = tags.Select(t => t.Code == 360 ? new DxfTag(360, "ABCDEF") : t);
        if (fault == "wrong-child") tags = tags.Select(t => t.Code == 360 ? new DxfTag(360, raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "XRECORD").Tags.First(t => t.Code == 5).Value) : t);
        if (fault == "unknown-only") tags = tags.Select(t => t.Code == 3 ? new DxfTag(3, "UNRECOGNIZED") : t);
        if (fault == "extra-entry" || fault == "duplicate-name") tags = tags.Concat(new[] { new DxfTag(3, fault == "extra-entry" ? "SIBLING" : "ACAD_LAYERSTATES"), new DxfTag(360, "ABCDEF") });
        if (fault == "wrapper-owner") tags = tags.Select(t => t.Code == 330 ? new DxfTag(330, "0") : t);
        if (fault == "child-owner") { target = p.States; tags = target.Tags.Select(t => t.Code == 330 ? new DxfTag(330, "0") : t); }
        if (fault == "state-owner")
        {
            target = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "XRECORD");
            int subclass = target.Tags.ToList().FindIndex(t => t.Code == 100);
            tags = target.Tags.Select((t, i) => t.Code == 330 && i < subclass ? new DxfTag(330, "0") : t);
        }
        if (fault == "duplicate-state-name")
        {
            target = p.States;
            string name = (string)target.Tags.First(t => t.Code == 3).Value;
            tags = target.Tags.Select(t => t.Code == 3 ? new DxfTag(3, name) : t);
        }
        if (fault == "aliased-state")
        {
            target = p.States; tags = target.Tags.Concat(new[] { new DxfTag(3, "ALIAS"),
                new DxfTag(350, target.Tags.First(t => t.Code == 350).Value) });
        }
        if (fault == "duplicate-wrapper-handle")
        {
            var list = tags.ToList(); int at = list.FindIndex(t => t.Code == 100);
            list.Insert(at, new DxfTag(5, LayerIdentityValue(p.Wrapper, 5))); tags = list;
        }
        raw = raw.WithRecord(target, tags); using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(stream) == null; }
        catch (Exception e) when (e is FormatException || e is NotSupportedException) { rejected = true; }
        Check(rejected && stream.CanRead, "Malformed layer-state ownership/projection accepted or stream closed");
    }
}
}
