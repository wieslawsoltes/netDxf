// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly int[] LsmWireMasks = { 0, 1, 2, 4, 8, 15, 64, 128, 256, 512, 2047, 16 | 32 | 1024 };

    private static void RegisterLayerStateMutationTests()
    {
        for (int mask = 0; mask < 16; mask++)
        {
            int selected = mask;
            Run("layer-state-mutation/flag-mask/" + mask, () => LsmFlagMask(selected));
            Run("layer-state-mutation/scalar-mask/" + mask, () => LsmScalarMask(selected));
        }
        foreach (bool loaded in new[] { false, true })
        foreach (int foreign in new[] { 0, 1, 2, 3 })
        foreach (bool referenced in new[] { false, true })
            Run($"layer-state-mutation/foreign-removal/{loaded}/{foreign}/{referenced}", () => LsmForeignRemoval(loaded, foreign, referenced));
        Run("layer-state-mutation/invalid-source", LsmInvalidSource);
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"layer-state-mutation/wire/{version}/{binary}", () => LsmWire(version, binary));
    }

    private static Layer LsmLayer(int flags) => new Layer("Walls")
    {
        IsVisible = (flags & 1) == 0, IsFrozen = (flags & 2) != 0,
        IsLocked = (flags & 4) != 0, Plot = (flags & 8) != 0,
        Color = new AciColor(3), Linetype = new Linetype("NEW_DASH"),
        Lineweight = Lineweight.W50, Transparency = new Transparency(25)
    };

    private static LayerStateProperties LsmProperties(int flags) => new LayerStateProperties("Walls")
    {
        Flags = (LayerPropertiesFlags)flags, Color = new AciColor(1),
        LinetypeName = "OLD_DASH", Lineweight = Lineweight.W20, Transparency = new Transparency(75)
    };

    private static void LsmFlagMask(int mask)
    {
        // Cover every stored defined flag combination, every incoming supported
        // flag combination, and a private bit that must never be manufactured/lost.
        foreach (int extra in new[] { 0, 0x4000 })
        for (int before = 0; before < 64; before++)
        for (int incoming = 0; incoming < 16; incoming++)
        {
            var layer = LsmLayer(incoming); var properties = LsmProperties(before | extra);
            var color = properties.Color; var alpha = properties.Transparency;
            properties.CopyFrom(layer, (LayerPropertiesRestoreFlags)(mask | 16 | 32 | 1024 | 0x8000));
            int expected = before | extra;
            for (int bit = 1; bit <= 8; bit <<= 1)
                if ((mask & bit) != 0) expected = (expected & ~bit) | (incoming & bit);
            Equal(expected, (int)properties.Flags, "Unselected stored flag was lost or selected flag not copied");
            Check(ReferenceEquals(color, properties.Color) && ReferenceEquals(alpha, properties.Transparency)
                && properties.LinetypeName == "OLD_DASH" && properties.Lineweight == Lineweight.W20,
                "Flag-only capture changed nonflag values");
            properties.CopyFrom(layer, (LayerPropertiesRestoreFlags)mask);
            Equal(expected, (int)properties.Flags, "Repeated selective capture changed flags");
            var target = LsmLayer(15 ^ incoming);
            properties.CopyTo(target, LayerPropertiesRestoreFlags.Hidden | LayerPropertiesRestoreFlags.Frozen
                | LayerPropertiesRestoreFlags.Locked | LayerPropertiesRestoreFlags.Plot);
            int restored = (!target.IsVisible ? 1 : 0) | (target.IsFrozen ? 2 : 0)
                | (target.IsLocked ? 4 : 0) | (target.Plot ? 8 : 0);
            Equal(expected & 15, restored, "Restore no longer reflects stored flags");
        }
    }

    private static void LsmScalarMask(int fields)
    {
        int mask = ((fields & 1) != 0 ? 64 : 0) | ((fields & 2) != 0 ? 128 : 0)
            | ((fields & 4) != 0 ? 256 : 0) | ((fields & 8) != 0 ? 512 : 0);
        var layer = LsmLayer(0); var properties = LsmProperties(63 | 0x4000);
        var color = properties.Color; var alpha = properties.Transparency;
        properties.CopyFrom(layer, (LayerPropertiesRestoreFlags)mask);
        Equal(63 | 0x4000, (int)properties.Flags, "Nonflag selection erased flags");
        Equal((short)((fields & 1) == 0 ? 1 : 3), properties.Color.Index, "Color selection");
        Equal((fields & 2) == 0 ? "OLD_DASH" : "NEW_DASH", properties.LinetypeName, "Linetype selection");
        Equal((fields & 4) == 0 ? Lineweight.W20 : Lineweight.W50, properties.Lineweight, "Lineweight selection");
        Equal((short)((fields & 8) == 0 ? 75 : 25), (short)properties.Transparency.Value, "Transparency selection");
        Check((fields & 1) != 0 ? !ReferenceEquals(layer.Color, properties.Color) : ReferenceEquals(color, properties.Color), "Color copy/retention identity");
        Check((fields & 8) != 0 ? !ReferenceEquals(layer.Transparency, properties.Transparency) : ReferenceEquals(alpha, properties.Transparency), "Transparency copy/retention identity");
        Equal(0, (!layer.IsVisible ? 1 : 0) | (layer.IsFrozen ? 2 : 0) | (layer.IsLocked ? 4 : 0) | (layer.Plot ? 8 : 0), "Capture mutated source layer");
    }

    private static void LsmForeignRemoval(bool loaded, int foreignKind, bool referenced)
    {
        var doc = LspSeed(DxfVersion.AutoCad2018, 1);
        if (loaded) doc = LsiLoad(LsiSave(doc, false));
        var manager = doc.Layers.StateManager; var actual = manager["State_0"];
        string handle = actual.Handle; var properties = actual.Properties; string state = LspValues(actual);
        var other = LspSeed(DxfVersion.AutoCad2018, 1);
        LayerState wrong = foreignKind == 0 ? (LayerState)actual.Clone()
            : foreignKind == 1 ? other.Layers.StateManager["State_0"]
            : foreignKind == 2 ? new LayerState("state_0") : new LayerState("Absent");
        var wrongOwner = wrong.Owner; string? wrongHandle = wrong.Handle;
        if (referenced)
        {
            var pointer = new DxfXRecord(); pointer.Data.Add(new netDxf.IO.DxfTag(340, handle));
            doc.NamedObjects.Add("REF", pointer);
        }
        using var iterator = manager.Items.GetEnumerator(); Check(iterator.MoveNext(), "Fixture is empty");
        Check(!manager.Remove(wrong), "Foreign/detached state removal accepted");
        Check(ReferenceEquals(actual, manager["State_0"]) && ReferenceEquals(manager, actual.Owner)
            && actual.Handle == handle && ReferenceEquals(doc.GetObjectByHandle(handle), actual)
            && ReferenceEquals(properties, actual.Properties) && LspValues(actual) == state,
            "Foreign removal changed actual registered state");
        Check(ReferenceEquals(wrong.Owner, wrongOwner) && wrong.Handle == wrongHandle,
            "Foreign removal altered the supplied object");
        Check(!iterator.MoveNext(), "Rejected foreign removal invalidated the collection iterator");
        Check(ReferenceEquals(other.Layers.StateManager["State_0"], other.GetObjectByHandle(other.Layers.StateManager["State_0"].Handle)), "Foreign document registration changed");
        Check(doc.Objects.Validate().Count == 0 && other.Objects.Validate().Count == 0, "Foreign removal damaged a graph");
        Check(!manager.Remove((LayerState)null!), "Null removal accepted");
        if (referenced) Check(!manager.Remove(actual), "Actual referenced state was removed");
        else
        {
            Check(manager.Remove(actual), "Actual unreferenced state was not removed");
            Check(manager.Count == 0 && doc.GetObjectByHandle(handle) == null && actual.Owner == null && actual.Handle == null,
                "Actual removal did not retire the identity");
        }
    }

    private static void LsmInvalidSource()
    {
        var properties = LsmProperties(63);
        var oldColor = properties.Color; var oldAlpha = properties.Transparency;
        Throws<ArgumentException>(() => properties.CopyFrom(new Layer("Other"), LayerPropertiesRestoreFlags.All));
        Check((int)properties.Flags == 63 && ReferenceEquals(properties.Color, oldColor)
            && ReferenceEquals(properties.Transparency, oldAlpha), "Name refusal mutated the snapshot");
        properties.CopyFrom(LsmLayer(0), LayerPropertiesRestoreFlags.None);
        Equal(63, (int)properties.Flags, "None is not a no-op");
        properties.CopyFrom(new Layer("walls"), LayerPropertiesRestoreFlags.Plot);
        Equal(63, (int)properties.Flags, "Case-insensitive copy admission changed");
    }

    private static void LsmVerifyState(LayerState state, int mask, bool changed)
    {
        var p = state.Properties["Walls"];
        int expected = 63;
        if (changed)
            for (int bit = 1; bit <= 8; bit <<= 1)
                if ((mask & bit) != 0) expected = (expected & ~bit) | (2 & bit);
        Equal(expected, (int)p.Flags, "Stored selective update flags");
        Equal((short)(changed && (mask & 64) != 0 ? 3 : 1), p.Color.Index, "Stored selective color");
        Equal(changed && (mask & 128) != 0 ? "NEW_DASH" : "OLD_DASH", p.LinetypeName, "Stored selective linetype");
        Equal(changed && (mask & 256) != 0 ? Lineweight.W50 : Lineweight.W20, p.Lineweight, "Stored selective lineweight");
        Equal((short)(changed && (mask & 512) != 0 ? 25 : 75), (short)p.Transparency.Value, "Stored selective transparency");
    }

    private static void LsmWire(DxfVersion version, bool binary)
    {
        var doc = LsiSeed(version);
        doc.Linetypes.Add(new Linetype("OLD_DASH"));
        doc.Layers.Add(LsmLayer(2));
        foreach (int mask in LsmWireMasks)
        {
            doc.Layers.StateManager.AddNew("MASK_" + mask, "Selective capture");
            var p = doc.Layers.StateManager["MASK_" + mask].Properties["Walls"];
            var seed = LsmProperties(63);
            p.Flags = seed.Flags; p.Color = seed.Color; p.LinetypeName = seed.LinetypeName;
            p.Lineweight = seed.Lineweight; p.Transparency = seed.Transparency;
        }
        var ids = LspIds(doc); string lineId = doc.Entities.Lines.Single().Handle;
        string prefix = $"layer-state-mutation-{version}-{(binary ? "binary" : "text")}";
        byte[] source = LsiSave(doc, binary); var dictionaries = LsiIds(source);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        doc = LsiLoad(source); LspRegistered(doc, ids);
        foreach (int mask in LsmWireMasks)
        {
            var state = doc.Layers.StateManager["MASK_" + mask]; LsmVerifyState(state, mask, false);
            var properties = state.Properties; var p = properties["Walls"];
            var zero = properties["0"]; int zeroFlags = (int)zero.Flags;
            doc.Layers.StateManager.Options = (LayerPropertiesRestoreFlags)mask;
            doc.Layers.StateManager.Update(state.Name);
            Check(ReferenceEquals(p, state.Properties["Walls"]) && ReferenceEquals(properties, state.Properties), "Update replaced snapshot identities");
            Equal(zeroFlags, (int)zero.Flags, "Unchanged layer flags changed"); LsmVerifyState(state, mask, true);
        }
        for (int stage = 0; stage < 2; stage++)
        {
            byte[] bytes = LsiSave(doc, stage == 0 ? !binary : binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
            Equal(dictionaries, LsiIds(bytes), "Selective update changed dictionary identity");
            doc = LsiLoad(bytes); LspRegistered(doc, ids);
            foreach (int mask in LsmWireMasks) LsmVerifyState(doc.Layers.StateManager["MASK_" + mask], mask, true);
            var line = (Line)doc.GetObjectByHandle(lineId);
            Check(line.StartPoint == new Vector3(1,2,3) && line.EndPoint == new Vector3(4,5,6), "Following LINE changed");
        }
    }
}
