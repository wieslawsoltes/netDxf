// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterLayerStateResourceTests()
    {
        foreach (bool stored in new[] { false, true })
        foreach (bool mixed in new[] { false, true })
        foreach (bool header in new[] { false, true })
            Run($"layer-state-resources/rename/{stored}/{mixed}/{header}", () => LsrRename(stored, mixed, header));
        foreach (bool layer in new[] { false, true })
        {
            Run("layer-state-resources/references/" + layer, () => LsrReferences(layer));
            Run("layer-state-resources/foreign-removal/" + layer, () => LsrForeignRemoval(layer));
        }
        for (int mode = 0; mode < 7; mode++)
        {
            int m = mode;
            Run("layer-state-resources/observer/" + mode, () => LsrObserver(m));
        }
        Run("layer-state-resources/shared-snapshot", LsrShared);
        Run("layer-state-resources/document-isolation", LsrDocuments);
        Run("layer-state-resources/callback-adoption", LsrAdoption);
        Run("layer-state-resources/invalid-and-noop", LsrInvalid);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"layer-state-resources/wire/{version}/{binary}", () => LsrWire(version, binary));
    }

    private static DxfDocument LsrSeed(DxfVersion version = DxfVersion.AutoCad2018)
    {
        var doc = LsiSeed(version);
        var old = doc.Linetypes.Add(new Linetype("OLD_DASH"));
        doc.Linetypes.Add(new Linetype("OTHER_DASH"));
        doc.Layers.Add(new Layer("Walls") { Linetype = old, Color = new AciColor(1), IsLocked = true });
        doc.Layers.Add(new Layer("Doors") { Linetype = old, Color = new AciColor(3), Transparency = new Transparency(75) });
        doc.Layers.StateManager.AddNew("FIRST", "Saved linetype references");
        doc.Layers.StateManager.AddNew("SECOND", "Independent settings");
        doc.Layers.StateManager["FIRST"].CurrentLayer = "Walls";
        doc.Layers.StateManager["SECOND"].Properties["Doors"].LinetypeName = "OTHER_DASH";
        doc.Layers["Walls"].Linetype = Linetype.Continuous;
        doc.Layers["Doors"].Linetype = Linetype.Continuous;
        doc.DrawingVariables.CeLtype = "OLD_DASH";
        return doc;
    }

    private static string LsrValues(LayerStateProperties p) => $"{p.Name}|{(int)p.Flags}|{p.Color.Index}|{(int)p.Lineweight}|{p.Transparency.Value}";
    private static LayerState[] LsrStates(DxfDocument doc) => doc.Layers.StateManager.ToArray();
    private static int LsrUses(IEnumerable<DxfObjectReference> values, LayerState state) =>
        values.Where(r => ReferenceEquals(r.Reference, state)).Select(r => r.Uses).SingleOrDefault();

    private static void LsrRename(bool stored, bool mixed, bool header)
    {
        var doc = LsrSeed();
        if (stored) doc = LsiLoad(LsiSave(doc, false));
        var states = LsrStates(doc); var old = doc.Linetypes["OLD_DASH"];
        if (mixed) foreach (var state in states) foreach (var p in state.Properties.Values)
            if (p.LinetypeName == "OLD_DASH") p.LinetypeName = "old_Dash";
        doc.DrawingVariables.CeLtype = header ? "old_Dash" : "ByLayer";
        var properties = states.SelectMany(s => s.Properties.Values).ToArray();
        var untouched = properties.Select(LsrValues).ToArray();
        var colors = properties.Select(p => p.Color).ToArray();
        var alpha = properties.Select(p => p.Transparency).ToArray();
        using var enumerator = states[0].Properties.GetEnumerator(); Check(enumerator.MoveNext(), "Missing snapshot");
        int observed = 0;
        old.NameChanged += (s, e) =>
        {
            observed++;
            Check(doc.Linetypes["OLD_DASH"] == old && doc.Linetypes["NEW_DASH"] == null, "Rename published before observers");
            Check(states[0].Properties["Walls"].LinetypeName.Equals("OLD_DASH", StringComparison.OrdinalIgnoreCase), "Snapshots published before observers");
        };
        string handle = old.Handle; string seed = doc.DrawingVariables.HandleSeed;
        old.Name = "NEW_DASH";
        Equal(1, observed, "Unexpected observer count");
        Check(ReferenceEquals(doc.Linetypes["NEW_DASH"], old) && doc.Linetypes["OLD_DASH"] == null
            && ReferenceEquals(doc.GetObjectByHandle(handle), old), "Linetype identity/index changed");
        Equal(seed, doc.DrawingVariables.HandleSeed, "Rename allocated handles");
        Equal(header ? "NEW_DASH" : "ByLayer", doc.DrawingVariables.CeLtype, "Current linetype not followed selectively");
        Check(properties.SequenceEqual(states.SelectMany(s => s.Properties.Values)) && enumerator.MoveNext(), "Snapshots or enumeration replaced");
        Check(untouched.SequenceEqual(properties.Select(LsrValues)) && colors.SequenceEqual(properties.Select(p => p.Color))
            && alpha.SequenceEqual(properties.Select(p => p.Transparency)), "Unselected snapshot values changed");
        foreach (var state in states)
        {
            Equal("NEW_DASH", state.Properties["Walls"].LinetypeName, "Saved wall linetype not renamed");
            Equal(state.Name == "FIRST" ? "NEW_DASH" : "OTHER_DASH", state.Properties["Doors"].LinetypeName, "Wrong saved door linetype changed");
        }
        Equal(2, LsrUses(old.GetReferences(), states[0]), "FIRST use count");
        Equal(1, LsrUses(old.GetReferences(), states[1]), "SECOND use count");
        doc.Layers.StateManager.Restore("FIRST");
        Check(ReferenceEquals(old, doc.Layers["Walls"].Linetype) && ReferenceEquals(old, doc.Layers["Doors"].Linetype), "Restore lost renamed resource");
    }

    private static void LsrReferences(bool layer)
    {
        var doc = LsrSeed(); var states = LsrStates(doc);
        if (layer)
        {
            var resource = doc.Layers["Walls"];
            Check(resource.HasReferences() && doc.Layers.HasReferences(resource) && doc.Layers.HasReferences("walls"), "Saved layer use not found");
            Equal(2, LsrUses(resource.GetReferences(), states[0]), "Snapshot/current-layer multiplicity");
            Equal(1, LsrUses(doc.Layers.GetReferences("Walls"), states[1]), "Named layer query differs");
            Check(!doc.Layers.Remove(resource), "Referenced layer removed");
            states[0].Properties.Remove("Walls"); states[1].Properties.Remove("Walls");
            Check(!doc.Layers.Remove(resource), "Saved current-layer reference ignored");
            states[0].CurrentLayer = "0";
            Check(!resource.HasReferences() && doc.Layers.Remove(resource), "Cleared saved layer uses were cached");
        }
        else
        {
            var resource = doc.Linetypes["OLD_DASH"];
            Check(resource.HasReferences() && doc.Linetypes.HasReferences("old_dash"), "Saved linetype use not found");
            Equal(2, LsrUses(resource.GetReferences(), states[0]), "Linetype count FIRST");
            Equal(1, LsrUses(doc.Linetypes.GetReferences("OLD_DASH"), states[1]), "Linetype count SECOND");
            Check(!doc.Linetypes.Remove(resource), "Saved-only linetype removed");
            foreach (var state in states) foreach (var p in state.Properties.Values)
                if (p.LinetypeName == "OLD_DASH") p.LinetypeName = "OTHER_DASH";
            doc.DrawingVariables.CeLtype = "ByLayer";
            Check(!resource.HasReferences() && doc.Linetypes.Remove(resource), "Live snapshot changes were not reflected");
        }
        Check(doc.Objects.Validate().Count == 0, "Resource removal damaged graph");
    }

    private static void LsrForeignRemoval(bool layer)
    {
        var doc = LsiSeed(DxfVersion.AutoCad2018); var foreign = LsiSeed(DxfVersion.AutoCad2018);
        if (layer)
        {
            var a = doc.Layers.Add(new Layer("Same")); var b = foreign.Layers.Add(new Layer("Same"));
            string ah = a.Handle, bh = b.Handle;
            Check(!doc.Layers.Remove(b) && !doc.Layers.Remove((Layer)a.Clone()), "Foreign/detached layer removed local object");
            Check(ReferenceEquals(doc.GetObjectByHandle(ah), a) && ReferenceEquals(foreign.GetObjectByHandle(bh), b)
                && ReferenceEquals(a.Owner, doc.Layers) && ReferenceEquals(b.Owner, foreign.Layers), "Foreign removal changed either document");
            Check(doc.Layers.Remove(a) && foreign.Layers.Remove(b), "Legitimate layer removal failed");
        }
        else
        {
            var a = doc.Linetypes.Add(new Linetype("Same")); var b = foreign.Linetypes.Add(new Linetype("Same"));
            Check(!doc.Linetypes.Remove(b) && !doc.Linetypes.Remove((Linetype)a.Clone()), "Foreign/detached linetype removed");
            Check(doc.Linetypes.Remove(a) && foreign.Linetypes.Remove(b), "Legitimate linetype removal failed");
        }
    }

    private static void LsrObserver(int mode)
    {
        var doc = LsrSeed(); var type = doc.Linetypes["OLD_DASH"];
        var first = doc.Layers.StateManager["FIRST"];
        type.NameChanged += (s, e) =>
        {
            switch (mode)
            {
                case 0: throw new InvalidOperationException("veto");
                case 1: first.Properties["Walls"].LinetypeName = "OTHER_DASH"; break;
                case 2: doc.Layers.StateManager.Remove("FIRST"); break;
                case 3:
                    doc.Layers.StateManager.AddNew("LATE");
                    doc.Layers.StateManager["LATE"].Properties["Walls"].LinetypeName = "OLD_DASH"; break;
                case 4: doc.Linetypes.Add(new Linetype("NEW_DASH")); break;
                case 5: doc.DrawingVariables.CeLtype = "OTHER_DASH"; break;
                case 6:
                    doc.Layers.StateManager.RemoveAll();
                    Check(doc.Linetypes.Remove(type), "Callback removal failed"); break;
            }
        };
        if (mode is 0 or 4)
        {
            bool refused = false;
            try { type.Name = "NEW_DASH"; } catch (Exception e) when (e is InvalidOperationException || e is ArgumentException) { refused = true; }
            Check(refused && type.Name == "OLD_DASH" && ReferenceEquals(doc.Linetypes["OLD_DASH"], type), "Refused rename published indexes");
            Equal("OLD_DASH", first.Properties["Walls"].LinetypeName, "Refused rename changed saved reference");
            Equal("OLD_DASH", doc.DrawingVariables.CeLtype, "Refused rename changed header");
            if (mode == 4) Check(doc.Linetypes.Contains("NEW_DASH"), "Caller side effect was incorrectly rolled back");
            return;
        }
        type.Name = "NEW_DASH";
        Equal("NEW_DASH", type.Name, "Observer rename failed");
        if (mode == 1) Equal("OTHER_DASH", first.Properties["Walls"].LinetypeName, "Post-observer value overwritten");
        if (mode == 2) Equal("OLD_DASH", first.Properties["Walls"].LinetypeName, "Detached state was modified");
        if (mode == 3) Equal("NEW_DASH", doc.Layers.StateManager["LATE"].Properties["Walls"].LinetypeName, "Late state omitted");
        if (mode == 5) Equal("OTHER_DASH", doc.DrawingVariables.CeLtype, "Post-observer header overwritten");
        if (mode == 6)
        {
            Check(type.Owner == null && !doc.Linetypes.Contains("NEW_DASH"), "Detached target reinserted");
            Equal("OLD_DASH", first.Properties["Walls"].LinetypeName, "Removed snapshots changed");
        }
    }

    private static void LsrShared()
    {
        var doc = LsrSeed(); var first = doc.Layers.StateManager["FIRST"]; var second = doc.Layers.StateManager["SECOND"];
        second.Properties["Walls"] = first.Properties["Walls"];
        var shared = first.Properties["Walls"]; var color = shared.Color;
        doc.Linetypes["OLD_DASH"].Name = "NEW_DASH";
        Check(ReferenceEquals(shared, second.Properties["Walls"]) && ReferenceEquals(shared.Color, color), "Shared snapshot replaced");
        Equal("NEW_DASH", shared.LinetypeName, "Shared linetype reference stale");
    }

    private static void LsrDocuments()
    {
        var a = LsrSeed(); var b = LsrSeed();
        a.Linetypes["OLD_DASH"].Name = "NEW_DASH";
        Check(b.Linetypes["OLD_DASH"] != null && b.Linetypes["NEW_DASH"] == null, "Rename escaped owning document");
        Equal("OLD_DASH", b.Layers.StateManager["FIRST"].Properties["Walls"].LinetypeName, "Foreign snapshot changed");
        Equal("OLD_DASH", b.DrawingVariables.CeLtype, "Foreign header changed");
    }

    private static void LsrAdoption()
    {
        var source = LsrSeed(); var other = LsiSeed(DxfVersion.AutoCad2018);
        var old = source.Linetypes["OLD_DASH"]; var detached = source.Layers.StateManager["FIRST"];
        source.Layers.StateManager.RemoveAll();
        old.NameChanged += (s, e) =>
        {
            Check(source.Linetypes.Remove(old), "Callback could not detach unreferenced resource");
            other.Linetypes.Add(old);
            other.Layers.Add(new Layer("Late") { Linetype = old });
            other.Layers.StateManager.AddNew("LATE");
            other.DrawingVariables.CeLtype = "OLD_DASH";
        };
        old.Name = "NEW_DASH";
        Check(ReferenceEquals(other.Linetypes["NEW_DASH"], old) && source.Linetypes["NEW_DASH"] == null,
            "Rename used the owner from before callbacks");
        Equal("NEW_DASH", other.Layers.StateManager["LATE"].Properties["Late"].LinetypeName, "Adopted-owner snapshot missed");
        Equal("NEW_DASH", other.DrawingVariables.CeLtype, "Adopted-owner header missed");
        Equal("OLD_DASH", detached.Properties["Walls"].LinetypeName, "Detached snapshot changed after adoption");
    }

    private static void LsrInvalid()
    {
        var doc = LsrSeed(); var type = doc.Linetypes["OLD_DASH"]; int events = 0;
        type.NameChanged += (_, _) => events++;
        foreach (string name in new[] { "", "bad/name", "OTHER_DASH", " " })
        {
            bool rejected = false;
            try { type.Name = name; } catch (ArgumentException) { rejected = true; }
            Check(rejected && type.Name == "OLD_DASH", "Invalid name changed resource");
            Equal("OLD_DASH", doc.Layers.StateManager["FIRST"].Properties["Walls"].LinetypeName, "Invalid rename changed snapshot");
        }
        Equal(0, events, "Invalid name reached observers");
        type.Name = "old_dash";
        Equal(0, events, "Case-only no-op raised event");
        Equal("OLD_DASH", type.Name, "Case-only no-op changed spelling");
    }

    private static void LsrWire(DxfVersion version, bool binary)
    {
        var doc = LsrSeed(version); string prefix = $"layer-state-resources-{version}-{(binary ? "binary" : "text")}";
        byte[] source = LsiSave(doc, binary); var ids = LsiIds(source); var states = LspIds(doc);
        string typeId = doc.Linetypes["OLD_DASH"].Handle;
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        doc = LsiLoad(source); doc.Linetypes["OLD_DASH"].Name = "NEW_DASH";
        for (int stage = 0; stage < 2; stage++)
        {
            byte[] output = LsiSave(doc, stage == 0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), output);
            Equal(ids, LsiIds(output), "Rename/save lost dictionary identity");
            doc = LsiLoad(output); LspRegistered(doc, states);
            var resource = doc.Linetypes["NEW_DASH"];
            Check(resource != null && resource.Handle == typeId && !doc.Linetypes.Contains("OLD_DASH"), "Saved linetype reverted/recreated");
            Equal("NEW_DASH", doc.DrawingVariables.CeLtype, "Saved current linetype changed");
            Equal(2, LsrUses(resource!.GetReferences(), doc.Layers.StateManager["FIRST"]), "Saved reference graph lost");
            Equal("NEW_DASH", doc.Layers.StateManager["SECOND"].Properties["Walls"].LinetypeName, "Saved snapshot linetype lost");
            Check(doc.Objects.Validate().Count == 0, "Saved resource graph invalid");
        }
        doc.Layers.StateManager.Restore("FIRST");
        Check(ReferenceEquals(doc.Layers["Walls"].Linetype, doc.Linetypes["NEW_DASH"]), "Loaded restore used stale name");
    }
}
