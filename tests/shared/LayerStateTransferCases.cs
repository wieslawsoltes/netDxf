// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static class LayerStateTransferCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly Action Test;
            internal Case(string id, Action test) { this.Id = id; this.Test = test; }
        }
        private static readonly DxfVersion[] Versions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
            DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
        private static readonly byte[] Original = Enumerable.Range(0, 257).Select(i => (byte)i).ToArray();
        private const string Description = "Saved Δ € 🚀 layer state";
        internal static IEnumerable<Case> All(string? artifacts)
        {
            foreach (var version in Versions) foreach (bool binary in new[] { false, true })
            {
                for (int k = 0; k < 3; k++)
                { int kind = k; yield return new Case($"legacy/blocked/{version}/{binary}/{k}", () => Blocked(version, binary, kind)); }
                foreach (bool overwrite in new[] { false, true })
                    yield return new Case($"legacy/policy/{version}/{binary}/{overwrite}", () => Policy(version, binary, overwrite));
                yield return new Case($"legacy/new/{version}/{binary}", () => NewImport(version, binary));
                yield return new Case($"wire/{version}/{binary}", () => Wire(version, binary, artifacts));
            }
            foreach (bool alpha in new[] { false, true })
                yield return new Case("legacy/export-failure/" + alpha, () => ExportFailure(alpha));
            yield return new Case("legacy/invalid-import", InvalidImport);
            foreach (bool existing in new[] { false, true }) foreach (bool manager in new[] { false, true })
            {
                yield return new Case($"atomic/success/{existing}/{manager}", () => AtomicSuccess(existing, manager));
                yield return new Case($"atomic/cancelled/{existing}/{manager}", () => AtomicCancelled(existing, manager));
                for (int f = 0; f < 7; f++)
                { int fault = f; yield return new Case($"atomic/refusal/{existing}/{manager}/{fault}", () => AtomicRefusal(existing, manager, fault)); }
            }
            for (int field = 0; field < 4; field++) foreach (string token in new[] { "bad\rvalue", "bad\nvalue", "bad\0value", "bad\ud800", "bad\udc00" })
            { int f = field; string t = token; yield return new Case($"atomic/text/{field}/{Array.IndexOf(new[] { "bad\rvalue", "bad\nvalue", "bad\0value", "bad\ud800", "bad\udc00" }, token)}", () => AtomicText(f, t)); }
            yield return new Case("atomic/path-errors", AtomicPaths);
            yield return new Case("atomic/readonly", AtomicReadOnly);
            yield return new Case("atomic/reader-visibility", AtomicVisibility);
        }
        internal static void VerifyInstalled()
        {
            foreach (var c in All(null))
                try { c.Test(); }
                catch (Exception e) { throw new InvalidOperationException("Installed LAS transfer: " + c.Id, e); }
        }
        private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
        private static void WithDirectory(Action<string> action)
        {
            string directory = Path.Combine(Path.GetTempPath(), "netdxf-las-transfer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { action(Path.Combine(directory, "Zażółć snapshot.las")); }
            finally { Directory.Delete(directory, true); }
        }
        private static DxfDocument Seed(DxfVersion version = DxfVersion.AutoCad2018)
        {
            var doc = new DxfDocument(version);
            doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
            doc.Layers.Add(new Layer("Walls") { Color = new AciColor(1), IsLocked = true, Lineweight = Lineweight.W30 });
            doc.Layers.StateManager.AddNew("Transfer", Description);
            var state = doc.Layers.StateManager["Transfer"]; state.CurrentLayer = "Walls"; state.PaperSpace = true;
            return doc;
        }
        private static byte[] Save(DxfDocument doc, bool binary)
        { using (var s = new MemoryStream()) { Check(doc.Save(s, binary) && s.CanWrite, "DXF save/stream"); return s.ToArray(); } }
        private static DxfDocument Load(byte[] bytes)
        {
            byte[] before = (byte[])bytes.Clone();
            using (var s = new MemoryStream(bytes))
            {
                var doc = DxfDocument.Load(s) ?? throw new InvalidOperationException("DXF load");
                Check(s.CanRead && bytes.SequenceEqual(before) && doc.Objects.Validate().Count == 0, "DXF input lifetime/graph");
                return doc;
            }
        }
        private static string Input(string path)
        {
            string input = path + ".incoming.las";
            var state = new LayerState("tRaNsFeR", new[] { new Layer("Walls") { Color = new AciColor(3) } })
            { CurrentLayer = "Walls", Description = "Incoming", PaperSpace = false };
            Check(state.Save(input), "LAS input save"); return input;
        }
        private static void AssertState(DxfDocument doc, LayerState state, string handle)
        {
            Check(ReferenceEquals(doc.Layers.StateManager[state.Name], state) && ReferenceEquals(state.Owner, doc.Layers.StateManager)
                && state.Handle == handle && ReferenceEquals(doc.GetObjectByHandle(handle), state), "State registration changed");
        }
        private static void Blocked(DxfVersion version, bool binary, int kind) => WithDirectory(path =>
        {
            var doc = Seed(version); var state = doc.Layers.StateManager["Transfer"]; string handle = state.Handle;
            if (kind == 0)
            { var pointer = new DxfXRecord(); pointer.Data.Add(new DxfTag(340, handle)); doc.NamedObjects.Add("TRANSFER_REFERENCE", pointer); }
            else if (kind == 1) doc.Entities.Lines.Single().PersistentReactors.Add(state);
            else doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$TRANSFER_REFERENCE", 340, handle));
            doc = Load(Save(doc, binary)); state = doc.Layers.StateManager["Transfer"];
            doc.Layers["Walls"].Color = new AciColor(7); doc.Layers["Walls"].IsLocked = false; doc.DrawingVariables.CLayer = "0";
            var color = doc.Layers["Walls"].Color; var properties = state.Properties; var walls = properties["Walls"];
            string seed = doc.DrawingVariables.HandleSeed; string input = Input(path); byte[] bytes = File.ReadAllBytes(input);
            using (var it = doc.Layers.StateManager.Items.GetEnumerator())
            {
                Check(it.MoveNext(), "Empty fixture");
                Throws<InvalidOperationException>(() => doc.Layers.StateManager.Import(input, true));
                AssertState(doc, state, handle);
                Check(doc.DrawingVariables.HandleSeed == seed && !it.MoveNext(), "Blocked import mutated handles/index");
            }
            Check(ReferenceEquals(properties, state.Properties) && ReferenceEquals(walls, state.Properties["Walls"])
                && state.Description == Description && state.PaperSpace, "Blocked import mutated snapshot");
            Check(doc.DrawingVariables.CLayer == "0" && ReferenceEquals(color, doc.Layers["Walls"].Color)
                && !doc.Layers["Walls"].IsLocked, "Blocked overwrite restored the old state into live layers");
            Check(bytes.SequenceEqual(File.ReadAllBytes(input)), "Import changed LAS input");
            Check(doc.Layers.StateManager.HasReferences(state), "Blocked import lost incoming reference");
            doc = Load(Save(doc, !binary)); AssertState(doc, doc.Layers.StateManager["Transfer"], handle);
            Check(doc.Layers["Walls"].Color.Index == 7 && !doc.Layers["Walls"].IsLocked, "Blocked import changed persisted live state");
        });
        private static void Policy(DxfVersion version, bool binary, bool overwrite) => WithDirectory(path =>
        {
            var doc = Load(Save(Seed(version), binary)); var old = doc.Layers.StateManager["Transfer"]; string handle = old.Handle;
            doc.Layers["Walls"].Color = new AciColor(7); string input = Input(path); byte[] bytes = File.ReadAllBytes(input);
            doc.Layers.StateManager.Import(input, overwrite); var state = doc.Layers.StateManager["Transfer"];
            Check(doc.Layers["Walls"].Color.Index == (overwrite ? 3 : 1) && doc.DrawingVariables.CLayer == "Walls", "Import selected wrong snapshot");
            if (overwrite) Check(!ReferenceEquals(old, state) && old.Owner == null && old.Handle == null
                && doc.GetObjectByHandle(handle) == null, "Overwrite did not retire original identity");
            else AssertState(doc, old, handle);
            Check(bytes.SequenceEqual(File.ReadAllBytes(input)), "Import changed source bytes");
            var copy = Load(Save(doc, !binary));
            Check(copy.Layers.StateManager["Transfer"].Description == (overwrite ? "Incoming" : Description), "Import persistence");
        });
        private static void NewImport(DxfVersion version, bool binary) => WithDirectory(path =>
        {
            var doc = new DxfDocument(version); doc.Layers.StateManager.Import(Input(path), false);
            Check(doc.Layers.StateManager.Count == 1 && doc.Layers["Walls"].Color.Index == 3 && doc.DrawingVariables.CLayer == "Walls", "Missing-layer import");
            Check(Load(Save(doc, binary)).Layers.StateManager["Transfer"].Description == "Incoming", "New import persistence");
        });
        private static void InvalidImport() => WithDirectory(path =>
        {
            var doc = Seed(); string handle = doc.Layers.StateManager["Transfer"].Handle;
            File.WriteAllText(path, "0\nLAYERSTATEDICTIONARY\n0\nLAYERSTATE\n1\n");
            bool refused = false; try { doc.Layers.StateManager.Import(path, true); } catch (Exception) { refused = true; }
            Check(refused && doc.Layers.StateManager["Transfer"].Handle == handle && doc.Layers["Walls"].Color.Index == 1, "Invalid input destroyed state");
        });
        private static void ExportFailure(bool alpha) => WithDirectory(path =>
        {
            var doc = Seed(); var state = doc.Layers.StateManager["Transfer"]; string handle = state.Handle;
            if (alpha) state.Properties["Walls"].Transparency = null!; else state.Properties["Walls"].Color = null!;
            Throws<IOException>(() => doc.Layers.StateManager.Export(path, "Transfer"));
            AssertState(doc, state, handle);
            using (var released = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(released.CanWrite, "Failed legacy export leaked file handle");
        });
        private static void Commit(DxfDocument doc, string path, bool manager, CancellationToken token = default(CancellationToken))
        { if (manager) doc.Layers.StateManager.ExportAtomic(path, "tRaNsFeR", token); else doc.Layers.StateManager["Transfer"].SaveAtomic(path, token); }
        private static void Prepare(string path, bool existing) { if (existing) File.WriteAllBytes(path, Original); }
        private static void Unchanged(string path, bool existing)
        {
            Check(File.Exists(path) == existing && (!existing || Original.SequenceEqual(File.ReadAllBytes(path))), "Atomic failure changed destination");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".netdxf-*.tmp").Any(), "Staging file leaked");
        }
        private static void AtomicSuccess(bool existing, bool manager) => WithDirectory(path =>
        {
            var doc = Seed(); var state = doc.Layers.StateManager["Transfer"]; string handle = state.Handle;
            string seed = doc.DrawingVariables.HandleSeed; var properties = state.Properties;
            string expected = path + ".expected"; Check(state.Save(expected), "Legacy comparison output failed");
            byte[] bytes = File.ReadAllBytes(expected); Prepare(path, existing);
            Commit(doc, path, manager);
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Atomic LAS differs from existing valid serializer");
            AssertState(doc, state, handle); Check(seed == doc.DrawingVariables.HandleSeed && ReferenceEquals(properties, state.Properties), "Export mutated drawing");
            var copy = LayerState.Load(path); Check(copy != null && copy.PaperSpace && copy.Description == Description && copy.CurrentLayer == "Walls", "Atomic LAS reload");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".netdxf-*.tmp").Any(), "Success leaked staging");
        });
        private static void AtomicCancelled(bool existing, bool manager) => WithDirectory(path =>
        {
            var doc = Seed(); Prepare(path, existing); string seed = doc.DrawingVariables.HandleSeed;
            using (var cts = new CancellationTokenSource())
            { cts.Cancel(); Throws<OperationCanceledException>(() => Commit(doc, path, manager, cts.Token)); }
            Unchanged(path, existing); Check(seed == doc.DrawingVariables.HandleSeed, "Cancellation mutated drawing");
        });
        private static void AtomicRefusal(bool existing, bool manager, int fault) => WithDirectory(path =>
        {
            var doc = Seed(); var state = doc.Layers.StateManager["Transfer"]; Prepare(path, existing);
            if (fault == 0) state.Properties["Walls"].Color = null!;
            else if (fault == 1) state.Properties["Walls"].Transparency = null!;
            else state.Description = new[] { "bad\rvalue", "bad\nvalue", "bad\0value", "bad\ud800", "bad\udc00" }[fault - 2];
            string handle = state.Handle, seed = doc.DrawingVariables.HandleSeed;
            Throws<InvalidDataException>(() => Commit(doc, path, manager)); Unchanged(path, existing);
            AssertState(doc, state, handle); Check(seed == doc.DrawingVariables.HandleSeed, "Failed export allocated handles");
        });
        private static void AtomicText(int field, string value) => WithDirectory(path =>
        {
            var state = new LayerState("Transfer");
            if (field == 0) state.Description = value;
            if (field == 1) state.CurrentLayer = value;
            if (field == 2) state.Properties.Add(value, new LayerStateProperties(value));
            if (field == 3) state.Properties.Add("Walls", new LayerStateProperties("Walls") { LinetypeName = value });
            Prepare(path, true); Throws<InvalidDataException>(() => state.SaveAtomic(path)); Unchanged(path, true);
        });
        private static void AtomicPaths() => WithDirectory(path =>
        {
            var doc = Seed(); var state = doc.Layers.StateManager["Transfer"];
            Throws<ArgumentNullException>(() => state.SaveAtomic(null!));
            Throws<ArgumentException>(() => state.SaveAtomic(""));
            Throws<IOException>(() => state.SaveAtomic(Path.GetDirectoryName(path)!));
            Throws<DirectoryNotFoundException>(() => state.SaveAtomic(Path.Combine(path, "missing.las")));
            Throws<ArgumentException>(() => doc.Layers.StateManager.ExportAtomic(path, "Absent"));
            Throws<ArgumentNullException>(() => doc.Layers.StateManager.ExportAtomic(path, null!));
            Unchanged(path, false);
        });
        private static void AtomicReadOnly() => WithDirectory(path =>
        {
            Prepare(path, true); File.SetAttributes(path, FileAttributes.ReadOnly);
            try { Throws<UnauthorizedAccessException>(() => Seed().Layers.StateManager["Transfer"].SaveAtomic(path)); }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            Unchanged(path, true);
        });
        private static void AtomicVisibility() => WithDirectory(path =>
        {
            Prepare(path, true);
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Seed().Layers.StateManager["Transfer"].SaveAtomic(path);
                using (var old = new MemoryStream())
                { reader.CopyTo(old); Check(Original.SequenceEqual(old.ToArray()), "Old reader saw replacement/prefix"); }
                Check(LayerState.Load(path) != null, "New reader did not see complete LAS");
            }
        });
        private static void Wire(DxfVersion version, bool binary, string? artifacts) => WithDirectory(path =>
        {
            var source = Seed(version); var state = source.Layers.StateManager["Transfer"]; string handle = state.Handle;
            string prefix = $"layer-state-transfer-{version}-{(binary ? "binary" : "text")}";
            byte[] before = Save(source, binary);
            source.Layers["Walls"].Color = new AciColor(7); // Export saved, not live, settings.
            source.Layers.StateManager.ExportAtomic(path, "Transfer");
            if (artifacts != null)
            {
                File.WriteAllBytes(Path.Combine(artifacts, prefix + "-source.dxf"), before);
                File.Copy(path, Path.Combine(artifacts, prefix + ".las"), true);
            }
            Check(source.Layers["Walls"].Color.Index == 7 && state.Handle == handle, "Export captured/restored live state");
            var doc = new DxfDocument(version);
            doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
            doc.Layers.StateManager.Import(path, false);
            string imported = doc.Layers.StateManager["Transfer"].Handle;
            for (int stage = 0; stage < 2; stage++)
            {
                var saved = doc.Layers.StateManager["Transfer"];
                Check(saved.PaperSpace && saved.Description == Description && saved.CurrentLayer == "Walls"
                    && saved.Properties["Walls"].Color.Index == 1 && doc.Layers["Walls"].Color.Index == 1, "LAS to DXF values");
                AssertState(doc, saved, imported);
                byte[] bytes = Save(doc, stage == 0 ? !binary : binary);
                if (artifacts != null) File.WriteAllBytes(Path.Combine(artifacts, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
                doc = Load(bytes);
            }
            Check(doc.Layers.StateManager["Transfer"].Handle == imported, "Second reload changed state identity");
        });
    }
}
