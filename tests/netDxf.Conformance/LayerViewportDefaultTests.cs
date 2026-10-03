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
    private static void RegisterLayerViewportDefaultTests()
    {
        for (int flags = 0; flags < 8; flags++)
        foreach (bool visible in new[] { false, true })
        {
            int f = flags;
            Run($"layer-viewport-default/model/{f}/{visible}", () => LvpModel(f, visible));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad12 }.Concat(SupportedVersions))
        foreach (bool binary in new[] { false, true })
            Run($"layer-viewport-default/wire/{version}/{binary}", () => LvpWire(version, binary));
        foreach (bool binary in new[] { false, true })
        {
            foreach (short flags in new short[] { -1, 8, 16, 32, 128, 256, 512, 32767 })
                Run($"layer-viewport-default/r12-refusal/{flags}/{binary}", () => LvpRefusal(flags, binary));
            Run("layer-viewport-default/r12-conflict/" + binary, () => LvpConflict(binary));
            Run("layer-viewport-default/r12-face-only/" + binary, () => LvpFaceOnly(binary));
            Run("layer-viewport-default/r12-missing-flags/" + binary, () => LvpMissingFlags(binary));
        }
        foreach (bool before in new[] { false, true })
        foreach (bool incoming in new[] { false, true })
        foreach (bool selected in new[] { false, true })
            Run($"layer-viewport-default/state/{before}/{incoming}/{selected}", () => LvpState(before, incoming, selected));
    }

    private static string LvpName(int flags, bool visible) => $"VP_{flags}_{(visible ? 1 : 0)}";
    private static Layer LvpLayer(int flags, bool visible) => new(LvpName(flags, visible))
    {
        IsFrozen = (flags & 1) != 0, IsFrozenInNewViewports = (flags & 2) != 0,
        IsLocked = (flags & 4) != 0, IsVisible = visible, Color = new AciColor(5)
    };
    private static int LvpFlags(Layer layer) => (layer.IsFrozen ? 1 : 0)
        | (layer.IsFrozenInNewViewports ? 2 : 0) | (layer.IsLocked ? 4 : 0);
    private static void LvpSame(Layer layer, int flags, bool visible)
    {
        Equal(flags, LvpFlags(layer), "Independent LAYER Boolean flags");
        Equal(visible, layer.IsVisible, "Layer on/off flag");
        Equal((short)5, layer.Color.Index, "Layer color");
    }

    private static void LvpModel(int flags, bool visible)
    {
        Check(!new Layer("Defaults").IsFrozenInNewViewports, "New viewport default must be thawed");
        var layer = LvpLayer(flags, visible);
        var viewport = new Viewport();
        viewport.FrozenLayers.Add(layer);
        var ordinaryViewport = new Viewport();
        var clone = (Layer)layer.Clone();
        var renamed = (Layer)layer.Clone("Renamed");
        LvpSame(clone, flags, visible); LvpSame(renamed, flags, visible);
        Equal("Renamed", renamed.Name, "Renamed layer clone");
        Check(!ReferenceEquals(layer.Color, clone.Color) && !ReferenceEquals(layer.Linetype, clone.Linetype),
            "Layer clone must retain independent resource values");
        var entity = new Line(Vector3.Zero, Vector3.UnitX) { Layer = layer };
        LvpSame(((Line)entity.Clone()).Layer, flags, visible);
        clone.IsFrozenInNewViewports = !clone.IsFrozenInNewViewports;
        LvpSame(clone, flags ^ 2, visible); LvpSame(layer, flags, visible);
        layer.IsFrozenInNewViewports = !layer.IsFrozenInNewViewports;
        LvpSame(layer, flags ^ 2, visible);
        Check(viewport.FrozenLayers.Count == 1 && ReferenceEquals(viewport.FrozenLayers[0], layer)
            && ordinaryViewport.FrozenLayers.Count == 0, "Default setting changed existing viewport membership");
    }

    private static EntityObject[] LvpSeeds()
    {
        var entities = new List<EntityObject>();
        for (int flags = 0; flags < 8; flags++)
        foreach (bool visible in new[] { false, true })
        {
            var layer = LvpLayer(flags, visible);
            int x = flags * 2 + (visible ? 1 : 0);
            foreach (int y in new[] { 2, 12 })
                entities.Add(new Line(new Vector3(x, y, y + 1), new Vector3(x + .5, y + 3, y + 4)) { Layer = layer });
        }
        return entities.ToArray();
    }

    private static DxfRawRecord LvpRecord(DxfRawDocument raw, string name) =>
        raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "LAYER"
            && r.Tags.Any(t => t.Code == 2 && (string)t.Value == name));
    private static DxfRawDocument LvpLoadRaw(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return DxfRawDocument.Load(stream); }
    private static DxfRawDocument LvpAddReferenced(DxfRawDocument raw)
    {
        for (int flags = 0; flags < 8; flags++)
        foreach (bool visible in new[] { false, true })
        {
            var record = LvpRecord(raw, LvpName(flags, visible));
            raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 70
                ? new DxfTag(70, (short)((short)t.Value | 64)) : t));
        }
        return raw;
    }
    private static void LvpCheckRaw(DxfRawDocument raw, bool edited, bool referenced)
    {
        for (int flags = 0; flags < 8; flags++)
        foreach (bool visible in new[] { false, true })
        {
            var record = LvpRecord(raw, LvpName(flags, visible));
            Equal((short)((edited ? flags ^ 2 : flags) | (referenced ? 64 : 0)),
                (short)record.Tags.Single(t => t.Code == 70).Value, "Exact LAYER group 70");
            Equal((short)(visible ? 5 : -5), (short)record.Tags.Single(t => t.Code == 62).Value, "Signed layer color");
        }
    }
    private static void LvpCheckEntities(IEnumerable<EntityObject> entities, bool edited)
    {
        var lines = entities.Cast<Line>().ToArray();
        Equal(32, lines.Length, "Viewport-default LINE inventory");
        for (int flags = 0; flags < 8; flags++)
        foreach (bool visible in new[] { false, true })
        {
            string name = LvpName(flags, visible); int x = flags * 2 + (visible ? 1 : 0);
            var shared = lines.Where(l => l.Layer.Name == name).ToArray();
            Equal(2, shared.Length, "Shared layer reference count");
            Check(ReferenceEquals(shared[0].Layer, shared[1].Layer), "Decoded layer resources not canonical");
            LvpSame(shared[0].Layer, edited ? flags ^ 2 : flags, visible);
            foreach (int y in new[] { 2, 12 })
            {
                var line = shared.Single(l => l.StartPoint.Y == y);
                Check(line.StartPoint == new Vector3(x, y, y + 1)
                    && line.EndPoint == new Vector3(x + .5, y + 3, y + 4), "Layer processing changed geometry");
                LvpSame(((Line)line.Clone()).Layer, edited ? flags ^ 2 : flags, visible);
            }
        }
    }

    private static void LvpWire(DxfVersion version, bool binary)
    {
        var seeds = LvpSeeds();
        DxfDocument? document = null;
        DxfRawDocument raw;
        if (version == DxfVersion.AutoCad12)
        {
            raw = DxfR12Codec.Create(seeds, binary);
            Check(seeds.All(e => e.Owner == null && e.Handle == null && e.Layer.Owner == null && e.Layer.Handle == null),
                "R12 selection changed source registration");
        }
        else
        {
            document = new DxfDocument(version);
            foreach (var seed in seeds) document.Entities.Add(seed);
            using var output = new MemoryStream();
            Check(document.Save(output, binary), "Modern layer flag save");
            raw = LvpLoadRaw(output.ToArray());
        }
        // Check actual typed writer output before deliberately adding the informational input flag.
        LvpCheckRaw(raw, false, false);
        raw = LvpAddReferenced(raw);
        for (int stage = 0; stage < 3; stage++)
        {
            bool transport = stage == 1 ? !binary : binary;
            byte[] bytes = R12Bytes(raw, transport);
            string suffix = new[] { "source", "output", "resave" }[stage];
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,
                $"layer-viewport-default-{version}-{(binary ? "binary" : "text")}-{suffix}.dxf"), bytes);
            raw = LvpLoadRaw(bytes); LvpCheckRaw(raw, stage != 0, stage == 0);
            IReadOnlyList<EntityObject> entities;
            if (version == DxfVersion.AutoCad12) entities = DxfR12Codec.ReadEntities(raw);
            else
            {
                using var input = new MemoryStream(bytes);
                document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Layer-default document did not load");
                entities = document.Entities.All.ToArray();
            }
            LvpCheckEntities(entities, stage != 0);
            if (stage == 0)
            {
                var snapshot = raw.Tags.ToArray();
                foreach (var layer in entities.Select(e => e.Layer).Distinct())
                    layer.IsFrozenInNewViewports = !layer.IsFrozenInNewViewports;
                Check(snapshot.SequenceEqual(raw.Tags), "Editing layer defaults mutated raw source");
                LvpCheckEntities(entities, true);
            }
            if (stage == 2) break;
            if (version == DxfVersion.AutoCad12) raw = DxfR12Codec.Create(entities, transport);
            else
            {
                using var output = new MemoryStream();
                Check(document!.Save(output, transport), "Edited layer-default document did not save");
                raw = LvpLoadRaw(output.ToArray());
            }
        }
    }

    private static void LvpRefusal(short flags, bool binary)
    {
        var raw = DxfR12Codec.Create(new[] { new Line(Vector3.Zero, Vector3.UnitX) { Layer = LvpLayer(2, true) } });
        var record = LvpRecord(raw, LvpName(2, true));
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 70 ? new DxfTag(70, flags) : t));
        raw = R12Reload(raw, binary);
        byte[] before = R12Bytes(raw, binary);
        Throws<NotSupportedException>(() => DxfR12Codec.ReadEntities(raw));
        Check(before.SequenceEqual(R12Bytes(raw, binary)), "Refused flags changed raw input");
    }
    private static void LvpConflict(bool binary)
    {
        var a = new Layer("SAME") { IsFrozenInNewViewports = false };
        var b = new Layer("same") { IsFrozenInNewViewports = true };
        var first = new Line(Vector3.Zero, Vector3.UnitX) { Layer = a };
        var second = new Line(Vector3.UnitY, Vector3.UnitZ) { Layer = b };
        using var stream = new MemoryStream(); stream.WriteByte(91);
        Throws<InvalidOperationException>(() => DxfR12Codec.Save(stream, new[] { first, second }, binary));
        Check(stream.Length == 1 && stream.Position == 1 && stream.ToArray()[0] == 91,
            "Conflicting layer defaults wrote output before refusal");
        Check(first.Handle == null && second.Handle == null && a.Handle == null && b.Handle == null,
            "Conflict assigned source identities");
        b.IsFrozenInNewViewports = false;
        var values = DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[] { first, second }), binary));
        Check(ReferenceEquals(values[0].Layer, values[1].Layer), "Equivalent case-insensitive defaults did not share a layer");
    }
    private static void LvpFaceOnly(bool binary)
    {
        var mesh = RmPolyface();
        var faceLayer = mesh.Faces[1].Layer;
        faceLayer.IsFrozenInNewViewports = true;
        var decoded = (PolyfaceMesh)DxfR12Codec.ReadEntities(R12Reload(DxfR12Codec.Create(new[] { mesh }), binary)).Single();
        RmSame(mesh, decoded);
        Check(decoded.Faces[1].Layer.IsFrozenInNewViewports && !decoded.Layer.IsFrozenInNewViewports,
            "Face-only layer default lost or leaked to mesh layer");
        var clone = (PolyfaceMesh)decoded.Clone();
        Check(clone.Faces[1].Layer.IsFrozenInNewViewports, "Face-only layer clone lost default");
        Check(faceLayer.Handle == null && faceLayer.Owner == null, "Face-only export adopted layer");
    }
    private static void LvpMissingFlags(bool binary)
    {
        var raw = DxfR12Codec.Create(new[] { new Line(Vector3.Zero, Vector3.UnitX) { Layer = LvpLayer(7, true) } });
        var record = LvpRecord(raw, LvpName(7, true));
        raw = raw.WithRecord(record, record.Tags.Where(t => t.Code != 70));
        var layer = DxfR12Codec.ReadEntities(R12Reload(raw, binary)).Single().Layer;
        LvpSame(layer, 0, true);
    }
    private static void LvpState(bool before, bool incoming, bool selected)
    {
        var layer = new Layer("State") { IsFrozenInNewViewports = before, IsFrozen = true, IsVisible = false, IsLocked = true };
        var viewport = new Viewport(); viewport.FrozenLayers.Add(layer);
        var state = new LayerStateProperties(layer);
        Equal(before, state.Flags.HasFlag(LayerPropertiesFlags.NewVpFrozen), "Constructor did not capture default");
        Check(state.CompareWith(layer), "Captured layer state does not match source");
        state.Flags |= LayerPropertiesFlags.VpFrozen | (LayerPropertiesFlags)0x4000;
        layer.IsFrozenInNewViewports = incoming;
        Equal(before == incoming, state.CompareWith(layer), "Comparison ignored new-viewport default");
        var options = selected ? LayerPropertiesRestoreFlags.NewVpFrozen : LayerPropertiesRestoreFlags.None;
        state.CopyFrom(layer, options);
        bool expected = selected ? incoming : before;
        Equal(expected, state.Flags.HasFlag(LayerPropertiesFlags.NewVpFrozen), "Selected capture of default");
        Check(((int)state.Flags & (32 | 0x4000)) == (32 | 0x4000), "Capture erased unsupported/unknown flags");
        var copy = (LayerStateProperties)state.Clone();
        Equal(state.Flags, copy.Flags, "Snapshot clone flags");
        layer.IsFrozenInNewViewports = !expected;
        copy.CopyTo(layer, LayerPropertiesRestoreFlags.None);
        Equal(!expected, layer.IsFrozenInNewViewports, "Unselected restore changed default");
        copy.CopyTo(layer, LayerPropertiesRestoreFlags.NewVpFrozen);
        Equal(expected, layer.IsFrozenInNewViewports, "Selected restore did not apply default");
        Check(copy.CompareWith(layer), "Restored default mismatch");
        Check(layer.IsFrozen && layer.IsLocked && !layer.IsVisible && layer.Plot,
            "Selective restore changed another layer setting");
        Check(viewport.FrozenLayers.Count == 1 && ReferenceEquals(viewport.FrozenLayers[0], layer),
            "Snapshot restore changed existing viewport membership");
        var flags = state.Flags;
        Throws<ArgumentException>(() => state.CopyFrom(new Layer("Other"), LayerPropertiesRestoreFlags.NewVpFrozen));
        Equal(flags, state.Flags, "Rejected capture changed snapshot flags");
    }
}
