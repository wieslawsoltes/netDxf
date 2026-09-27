// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateLifecycleTests()
    {
        foreach (bool stored in new[] { false, true })
        foreach (bool early in new[] { false, true })
        {
            Run($"layer-state-lifecycle/rename/{stored}/{early}", () => LslRename(stored, early));
            Run($"layer-state-lifecycle/veto/{stored}/{early}", () => LslVeto(stored, early));
        }
        foreach (bool stored in new[] { false, true })
        foreach (bool paper in new[] { false, true })
        foreach (bool named in new[] { false, true })
            Run($"layer-state-lifecycle/clone/{stored}/{paper}/{named}", () => LslClone(stored, paper, named));
        for (int mode = 0; mode < 9; mode++)
        {
            int m = mode; Run("layer-state-lifecycle/callback/" + m, () => LslCallback(m));
        }
        Run("layer-state-lifecycle/invalid-noop", LslNoop);
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (bool clone in new[] { false, true })
            Run($"layer-state-lifecycle/wire/{version}/{binary}/{clone}", () => LslWire(version, binary, clone));
    }

    private static DxfDocument LslSeed(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var doc = LsiSeed(version);
        doc.Layers.Add(new Layer("Walls") { Color = new AciColor(3), IsLocked = true, Lineweight = Lineweight.W30 });
        doc.Layers.StateManager.AddNew("Before", "Paper state");
        doc.Layers.StateManager.AddNew("Untouched", "Model state");
        doc.Layers.StateManager["Before"].PaperSpace = true;
        doc.Layers.StateManager["Before"].CurrentLayer = "Walls";
        return doc;
    }

    private static void LslIndex(DxfDocument doc, LayerState item, string name)
    {
        Check(item.Name == name && ReferenceEquals(item.Owner, doc.Layers.StateManager)
            && ReferenceEquals(doc.Layers.StateManager[name], item)
            && ReferenceEquals(doc.GetObjectByHandle(item.Handle), item), "State name/owner/index/handle disagree");
        Check(doc.Layers.StateManager.Count == doc.Layers.StateManager.Names.Count, "State index cardinality");
        Check(doc.Objects.Validate().Count == 0, "State graph invalid");
    }

    private static void LslRename(bool stored, bool early)
    {
        var doc = LslSeed(); if (stored) doc = LsiLoad(LsiSave(doc, false));
        var item = doc.Layers.StateManager["Before"]; string handle = item.Handle;
        var dictionary = item.Properties; var properties = dictionary.Values.ToArray();
        var other = doc.Layers.StateManager["Untouched"];
        int calls = 0;
        TableObject.NameChangedEventHandler observer = (sender, args) =>
        {
            calls++;
            LslIndex(doc, item, "Before");
            Check(doc.Layers.StateManager["After"] == null && args.OldValue == "Before" && args.NewValue == "After",
                "Rename became visible before observer acceptance");
        };
        if (early)
        {
            Check(doc.Layers.StateManager.Remove(item), "Prepare early observer");
            item.NameChanged += observer; doc.Layers.StateManager.Add(item); handle = item.Handle;
        }
        else item.NameChanged += observer;
        var reference = new DxfXRecord(); reference.Data.Add(new DxfTag(340, item.Handle));
        doc.NamedObjects.Add("STATE_LINK", reference); doc.Entities.Lines.Single().PersistentReactors.Add(item);
        string seed = doc.DrawingVariables.HandleSeed;
        using var enumerator = dictionary.GetEnumerator(); Check(enumerator.MoveNext(), "Empty fixture");
        item.Name = "After";
        Equal(1, calls, "Observer count"); LslIndex(doc, item, "After");
        Check(doc.Layers.StateManager["Before"] == null && ReferenceEquals(doc.Layers.StateManager["Untouched"], other),
            "Wrong state index altered");
        Check(handle == item.Handle && seed == doc.DrawingVariables.HandleSeed && ReferenceEquals(dictionary, item.Properties)
            && properties.SequenceEqual(item.Properties.Values) && enumerator.MoveNext(), "Rename changed identity/settings/enumeration");
        Check((string)reference.Data[0].Value == handle && ReferenceEquals(doc.Entities.Lines.Single().PersistentReactors.Single(), item),
            "Incoming reference changed");
        Check(item.PaperSpace && item.CurrentLayer == "Walls" && item.Description == "Paper state", "Rename changed saved header");
        Check(!doc.Layers.StateManager.Remove(item), "Rename lost removal protection");
        item.NameChanged -= observer;
        doc = LsiLoad(LsiSave(doc, true)); LslIndex(doc, doc.Layers.StateManager["After"], "After");
    }

    private static void LslVeto(bool stored, bool early)
    {
        var doc = LslSeed(); if (stored) doc = LsiLoad(LsiSave(doc, false));
        var item = doc.Layers.StateManager["Before"];
        TableObject.NameChangedEventHandler veto = (s, e) => throw new ApplicationException("Observer veto");
        if (early)
        {
            Check(doc.Layers.StateManager.Remove(item), "Prepare early veto");
            item.NameChanged += veto; doc.Layers.StateManager.Add(item);
        }
        else item.NameChanged += veto;
        LslIndex(doc, item, "Before"); // Initialize lazy graph services before the mutation snapshot.
        string seed = doc.DrawingVariables.HandleSeed, handle = item.Handle;
        Throws<ApplicationException>(() => item.Name = "After");
        LslIndex(doc, item, "Before");
        Check(doc.Layers.StateManager["After"] == null && item.Handle == handle && seed == doc.DrawingVariables.HandleSeed,
            "Rejected rename changed collection or identity");
        item.NameChanged -= veto;
        item.Name = "After"; LslIndex(doc, item, "After"); // guard must reset after failure
    }

    private static void LslClone(bool stored, bool paper, bool named)
    {
        var doc = LslSeed(); var source = doc.Layers.StateManager["Before"]; source.PaperSpace = paper;
        if (stored) { doc = LsiLoad(LsiSave(doc, false)); source = doc.Layers.StateManager["Before"]; }
        var copy = named ? (LayerState)source.Clone("Copy") : (LayerState)source.Clone();
        Check(copy.Name == (named ? "Copy" : "Before") && copy.PaperSpace == paper
            && copy.CurrentLayer == source.CurrentLayer && copy.Description == source.Description
            && copy.Handle == null && copy.Owner == null, "Clone lost header or copied database identity");
        Check(!ReferenceEquals(source.Properties, copy.Properties) && source.Properties.Count == copy.Properties.Count,
            "Clone aliases property storage");
        foreach (var pair in source.Properties)
        {
            var p = copy.Properties[pair.Key];
            Check(!ReferenceEquals(p, pair.Value) && !ReferenceEquals(p.Color, pair.Value.Color)
                && !ReferenceEquals(p.Transparency, pair.Value.Transparency) && p.Flags == pair.Value.Flags
                && p.LinetypeName == pair.Value.LinetypeName && p.Lineweight == pair.Value.Lineweight, "Clone values/ownership");
        }
        copy.PaperSpace = !paper; copy.Properties["Walls"].Flags = LayerPropertiesFlags.None;
        Check(source.PaperSpace == paper && source.Properties["Walls"].Flags.HasFlag(LayerPropertiesFlags.Locked),
            "Clone mutation reached source");
        copy.PaperSpace = paper;
        string path = Path.Combine(ArtifactDirectory, $"lifecycle-clone-{stored}-{paper}-{named}.las");
        Check(copy.Save(path), "LAS clone save"); var reloaded = LayerState.Load(path);
        Check(reloaded != null && reloaded.PaperSpace == paper && reloaded.Name == copy.Name, "LAS clone lost header");
    }

    private static void LslCallback(int mode)
    {
        var doc = LslSeed(); var foreign = LslSeed(); var item = doc.Layers.StateManager["Before"];
        if (mode == 0) { Throws<ArgumentException>(() => item.Name = "untouched"); LslIndex(doc, item, "Before"); return; }
        int calls = 0;
        TableObject.NameChangedEventHandler handler = (s, e) =>
        {
            calls++; Check(calls == 1, "Recursive notification escaped guard");
            switch (mode)
            {
                case 1: doc.Layers.StateManager.AddNew("After"); break;
                case 2: Check(doc.Layers.StateManager.Remove(item), "Detach in observer"); break;
                case 3:
                    Check(doc.Layers.StateManager.Remove(item), "Remove before adoption");
                    Check(foreign.Layers.StateManager.Remove("Before"), "Remove foreign occupant");
                    foreign.Layers.StateManager.Add(item); break;
                case 4: doc.Layers.StateManager["Untouched"].Name = "Other"; break;
                case 5: item.Name = "Nested"; break;
                case 6: Throws<InvalidOperationException>(() => item.Name = "Nested"); break;
                case 7: item.PaperSpace = false; item.Description = "Callback edit"; break;
                case 8: item.Description = "Callback edit"; throw new ApplicationException("Late veto");
            }
        };
        item.NameChanged += handler;
        if (mode == 1) Throws<ArgumentException>(() => item.Name = "After");
        else if (mode == 5) Throws<InvalidOperationException>(() => item.Name = "After");
        else if (mode == 8) Throws<ApplicationException>(() => item.Name = "After");
        else item.Name = "After";
        item.NameChanged -= handler;
        if (mode == 1 || mode == 5 || mode == 8)
        {
            LslIndex(doc, item, "Before"); Check(doc.Layers.StateManager["Nested"] == null, "Nested rename published");
            if (mode == 1) Check(!ReferenceEquals(doc.Layers.StateManager["After"], item), "Collision lost callback object");
            if (mode == 8) Equal("Callback edit", item.Description, "Caller side effect was rolled back");
        }
        else if (mode == 2)
            Check(item.Owner == null && item.Name == "After" && doc.Layers.StateManager["Before"] == null
                && doc.Layers.StateManager["After"] == null, "Detach used stale owner");
        else if (mode == 3)
        {
            LslIndex(foreign, item, "After");
            Check(doc.Layers.StateManager["Before"] == null && doc.Layers.StateManager["After"] == null, "Adoption changed former owner");
        }
        else
        {
            LslIndex(doc, item, "After");
            if (mode == 4) Check(doc.Layers.StateManager["Other"] != null, "Different-object callback rename lost");
            if (mode == 7) Check(!item.PaperSpace && item.Description == "Callback edit", "Callback settings overwritten");
        }
        Check(doc.Objects.Validate().Count == 0 && foreign.Objects.Validate().Count == 0, "Callback graph corruption");
    }

    private static void LslNoop()
    {
        var doc = LslSeed(); var item = doc.Layers.StateManager["Before"]; int count = 0;
        item.NameChanged += (s, e) => count++;
        using var states = doc.Layers.StateManager.GetEnumerator(); Check(states.MoveNext(), "No-op enumerator");
        item.Name = "before";
        Check(count == 0 && item.Name == "Before" && states.MoveNext(), "Case-only no-op changed collection");
        foreach (string invalid in new[] { "", "A/B", "*Invalid" })
        { Throws<ArgumentException>(() => item.Name = invalid); LslIndex(doc, item, "Before"); }
        Equal(0, count, "Invalid names invoked observers");
    }

    private static void LslWire(DxfVersion version, bool binary, bool clone)
    {
        var doc = LslSeed(version);
        byte[] source = LsiSave(doc, binary); doc = LsiLoad(source);
        var before = doc.Layers.StateManager["Before"]; string handle = before.Handle;
        string prefix = $"layer-state-lifecycle-{version}-{(binary ? "binary" : "text")}-{(clone ? "clone" : "rename")}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        if (clone) doc.Layers.StateManager.Add((LayerState)before.Clone("Copy"));
        else before.Name = "After";
        string copyHandle = clone ? doc.Layers.StateManager["Copy"].Handle : handle;
        for (int stage = 0; stage < 2; stage++)
        {
            byte[] output = LsiSave(doc, stage == 0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), output);
            doc = LsiLoad(output);
            var changed = doc.Layers.StateManager[clone ? "Copy" : "After"];
            Check(changed.Handle == copyHandle && changed.PaperSpace && changed.CurrentLayer == "Walls"
                && changed.Description == "Paper state", "Persisted renamed/cloned header or identity lost");
            LslIndex(doc, changed, changed.Name);
            if (clone) Check(copyHandle != handle && doc.Layers.StateManager["Before"].Handle == handle, "Clone reused source identity");
            else Check(doc.Layers.StateManager["Before"] == null && changed.Handle == handle, "Rename allocated identity");
            var untouched = doc.Layers.StateManager["Untouched"];
            Check(!untouched.PaperSpace && untouched.CurrentLayer == "0" && untouched.Description == "Model state", "Other state changed");
        }
    }
}
