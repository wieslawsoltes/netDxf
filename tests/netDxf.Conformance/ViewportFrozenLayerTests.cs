// Copyright (c) netDxf contributors. Licensed under the MIT License.
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
    private static void RegisterViewportFrozenLayerTests()
    {
        foreach (string context in new[] { "detached", "block", "attached" })
        foreach (string operation in new[] { "add", "insert", "replace", "null", "foreign", "case" })
            Run($"viewport-frozen/collection/{context}/{operation}", () => VflCollection(context, operation));
        foreach (string context in new[] { "entity", "block", "paper" })
        {
            foreach (bool existing in new[] { false, true })
            foreach (bool caseChange in new[] { false, true })
                Run($"viewport-frozen/adoption/{context}/{existing}/{caseChange}",
                    () => VflAdoption(context, existing, caseChange));
            Run($"viewport-frozen/references/{context}", () => VflReferences(context));
        }
        foreach (bool block in new[] { false, true })
            Run($"viewport-frozen/foreign-adoption/{block}", () => VflForeignAdoption(block));
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"viewport-frozen/wire/{version}/{binary}", () => VflWire(version, binary));
    }

    private static void VflCollection(string context, string operation)
    {
        var viewport = new Viewport();
        var first = new Layer("FIRST"); var second = new Layer("SECOND");
        if (context == "block") new Block("DETACHED").Entities.Add(viewport);
        if (context == "attached")
        {
            var document = new DxfDocument();
            document.Entities.Add(viewport);
            first = document.Layers.Add(first); second = document.Layers.Add(second);
        }
        viewport.FrozenLayers.Add(first); viewport.FrozenLayers.Add(second);
        int added = 0, removed = 0;
        viewport.FrozenLayers.AddItem += (_, _) => added++;
        viewport.FrozenLayers.RemoveItem += (_, _) => removed++;
        switch (operation)
        {
            case "add": Throws<ArgumentException>(() => viewport.FrozenLayers.Add(first)); break;
            case "insert": Throws<ArgumentException>(() => viewport.FrozenLayers.Insert(1, first)); break;
            case "replace": viewport.FrozenLayers[1] = first; break; // IList replacement cancellation is a no-op.
            case "null": Throws<ArgumentException>(() => viewport.FrozenLayers.Add(null!)); break;
            case "foreign":
                var foreign = new DxfDocument().Layers.Add(new Layer("FOREIGN"));
                Throws<ArgumentException>(() => viewport.FrozenLayers.Add(foreign)); break;
            case "case":
                Throws<ArgumentException>(() => viewport.FrozenLayers.Add(new Layer("first"))); break;
            default: throw new InvalidOperationException("Unknown collection test.");
        }
        Equal(2, viewport.FrozenLayers.Count, "Rejected membership changed count");
        Check(ReferenceEquals(first, viewport.FrozenLayers[0]) && ReferenceEquals(second, viewport.FrozenLayers[1]),
            "Rejected membership changed identities or ordering");
        Equal(0, added, "Rejected membership sent add notification");
        Equal(0, removed, "Rejected membership sent remove notification");
        var clone = (Viewport)viewport.Clone();
        Check(clone.FrozenLayers.Count == 2 && clone.FrozenLayers.All(l => l.Owner == null), "Clone retained document resources");
        Check(!ReferenceEquals(clone.FrozenLayers[0], first), "Clone did not detach frozen layer identity");
    }

    private static void VflAdoption(string context, bool existing, bool caseChange)
    {
        var document = new DxfDocument();
        var canonical = existing ? document.Layers.Add(new Layer("FROZEN") { IsFrozenInNewViewports = true }) : null;
        var source = new Layer(caseChange ? "frozen" : "FROZEN");
        var layout = new Layout("FrozenAdoption");
        var viewport = context == "paper" ? layout.Viewport : new Viewport();
        viewport.FrozenLayers.Add(source);
        var list = viewport.FrozenLayers;
        int events = 0;
        list.BeforeAddItem += (_, e) => { events++; e.Cancel = true; };
        list.BeforeRemoveItem += (_, e) => { events++; e.Cancel = true; };
        list.AddItem += (_, _) => events++;
        list.RemoveItem += (_, _) => events++;
        if (context == "paper") document.Layouts.Add(layout);
        else if (context == "block")
        {
            var block = new Block("FROZEN_CONTAINER"); block.Entities.Add(viewport); document.Blocks.Add(block);
        }
        else document.Entities.Add(viewport);
        canonical ??= document.Layers[source.Name];
        Check(ReferenceEquals(list, viewport.FrozenLayers), "Adoption replaced observable collection");
        Check(ReferenceEquals(canonical, list.Single()), "Frozen layer did not resolve to the canonical resource");
        Check(ReferenceEquals(canonical.Owner, document.Layers) && canonical.Handle != null, "Canonical frozen layer not registered");
        Equal(0, events, "Internal identity normalization invoked cancellable user collection callbacks");
        if (existing)
        {
            Check(source.Owner == null && source.Handle == null, "Existing resource selection mutated discarded source");
            Check(canonical.IsFrozenInNewViewports, "Source defaults overwrote existing canonical layer");
        }
        var refs = document.Layers.GetReferences(canonical);
        Check(refs.Any(r => ReferenceEquals(r.Reference, viewport) && r.Uses == 1), "Adoption lost frozen reference");
        Check(!document.Layers.Remove(canonical), "Frozen-only layer was removable after adoption");
    }

    private static void VflReferences(string context)
    {
        var document = new DxfDocument(); var layout = document.Layouts.Add(new Layout("FrozenReferences"));
        var viewport = context == "paper" ? layout.Viewport : new Viewport();
        Block? block = null;
        if (context == "entity") layout.AssociatedBlock.Entities.Add(viewport);
        else if (context == "block")
        { block = new Block("REFERENCE_CONTAINER"); block.Entities.Add(viewport); document.Blocks.Add(block); }
        var first = document.Layers.Add(new Layer("FROZEN_ONLY"));
        var next = document.Layers.Add(new Layer("REPLACEMENT"));
        viewport.FrozenLayers.Add(first);
        Check(first.HasReferences() && document.Layers.HasReferences(first) && document.Layers.HasReferences("frozen_only"),
            "Frozen membership absent from a reference-query overload");
        var snapshot = first.GetReferences();
        Check(snapshot.Count == 1 && ReferenceEquals(snapshot[0].Reference, viewport) && snapshot[0].Uses == 1,
            "Frozen-only use count/identity differs");
        var unchanged = new CompatibilityState(document);
        Check(!document.Layers.Remove(first), "Removal lost a live frozen layer"); unchanged.CheckUnchanged();
        viewport.FrozenLayers[0] = next;
        Check(!first.HasReferences() && next.HasReferences(), "Indexed replacement left stale reference state");
        Check(snapshot.Count == 1 && snapshot[0].Uses == 1 && ReferenceEquals(snapshot[0].Reference, viewport),
            "Previously returned reference snapshot mutated");
        Check(document.Layers.Remove(first), "Replaced frozen layer could not be removed");
        next.Name = "RENAMED_FROZEN";
        Check(document.Layers.HasReferences("renamed_frozen"), "Layer rename lost frozen reference");
        if (context != "paper")
        {
            viewport.Layer = next;
            Equal(2, next.GetReferences().Single(r => ReferenceEquals(r.Reference, viewport)).Uses,
                "Entity appearance and frozen uses were not aggregated");
            viewport.Layer = document.Layers["0"];
        }
        viewport.FrozenLayers.Clear();
        Check(!next.HasReferences(), "Clear retained a stale frozen reference");
        viewport.FrozenLayers.Add(next);
        if (context == "entity") Check(layout.AssociatedBlock.Entities.Remove(viewport), "Viewport removal failed");
        else if (context == "block") Check(document.Blocks.Remove(block!), "Block removal failed");
        // Match the internal-setter adapter already used by AppIdXDataLifecycleTests.
        else typeof(Layout).GetProperty(nameof(Layout.Viewport))!.SetValue(layout, new Viewport());
        Check(!next.HasReferences() && document.Layers.Remove(next), "Retired viewport retained a live layer reference");
    }

    private static void VflForeignAdoption(bool inBlock)
    {
        var sourceDocument = new DxfDocument(); var target = new DxfDocument();
        var layer = sourceDocument.Layers.Add(new Layer("FOREIGN_FROZEN"));
        var viewport = new Viewport();
        Block? block = null;
        if (inBlock)
        { block = new Block("FOREIGN_CONTAINER"); block.Entities.Add(viewport); sourceDocument.Blocks.Add(block); }
        else sourceDocument.Entities.Add(viewport);
        viewport.FrozenLayers.Add(layer);
        if (inBlock) Check(sourceDocument.Blocks.Remove(block!), "Source block detach failed");
        else Check(sourceDocument.Entities.Remove(viewport), "Source viewport detach failed");
        var before = new CompatibilityState(target);
        string? handle = viewport.Handle;
        if (inBlock) Throws<InvalidOperationException>(() => target.Blocks.Add(block!));
        else Throws<InvalidOperationException>(() => target.Entities.Add(viewport));
        before.CheckUnchanged();
        Equal(handle, viewport.Handle, "Refused adoption assigned a viewport identity");
        Check(ReferenceEquals(layer.Owner, sourceDocument.Layers) && ReferenceEquals(viewport.FrozenLayers.Single(), layer),
            "Refused adoption stole a foreign resource");
        var clone = (Viewport)viewport.Clone(); target.Entities.Add(clone);
        Check(ReferenceEquals(clone.FrozenLayers.Single().Owner, target.Layers), "Detached clone could not be adopted");
    }

    private static void VflWire(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var layout = document.Layouts.Add(new Layout("FrozenWire"));
        var firstLayer = document.Layers.Add(new Layer("FROZEN_A") { IsFrozenInNewViewports = true });
        var secondLayer = document.Layers.Add(new Layer("FROZEN_B"));
        var first = new Viewport(new Vector2(10, 20), 6, 4);
        var second = new Viewport(new Vector2(40, 50), 8, 5);
        layout.AssociatedBlock.Entities.Add(first); layout.AssociatedBlock.Entities.Add(second);
        first.FrozenLayers.Add(firstLayer); second.FrozenLayers.Add(secondLayer);
        string[] stages = { "source", "output", "resave" };
        for (int stage = 0; stage < stages.Length; stage++)
        {
            bool transport = stage == 1 ? !binary : binary;
            using var output = new MemoryStream(); Check(document.Save(output, transport), "Frozen-layer wire save failed");
            byte[] bytes = output.ToArray();
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,
                $"viewport-frozen-{version}-{(binary ? "binary" : "text")}-{stages[stage]}.dxf"), bytes);
            using var rawInput = new MemoryStream(bytes); var raw = DxfRawDocument.Load(rawInput);
            var rows = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "VIEWPORT"
                && r.Tags.Any(t => t.Code == 10 && ((double)t.Value == 10 || (double)t.Value == 40))).ToArray();
            Equal(2, rows.Length, "Viewport packet inventory");
            foreach (var row in rows)
            {
                double x = (double)row.Tags.Single(t => t.Code == 10).Value;
                string expected = x == 10 && stage == 0 ? firstLayer.Handle : secondLayer.Handle;
                Equal(expected, (string)row.Tags.Single(t => t.Code == 331).Value, "Exact frozen-layer handle");
            }
            using var input = new MemoryStream(bytes);
            document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Frozen-layer wire reload failed");
            var viewports = document.Layouts["FrozenWire"].AssociatedBlock.Entities.OfType<Viewport>().ToArray();
            first = viewports.Single(v => v.Center.X == 10); second = viewports.Single(v => v.Center.X == 40);
            firstLayer = document.Layers["FROZEN_A"]; secondLayer = document.Layers["FROZEN_B"];
            Check(ReferenceEquals(first.FrozenLayers.Single(), stage == 0 ? firstLayer : secondLayer), "Typed frozen pointer differs");
            Check(ReferenceEquals(second.FrozenLayers.Single(), secondLayer), "Unedited viewport changed");
            Check(!document.Layers.Remove(secondLayer), "Reloaded frozen-only layer can be deleted");
            Equal(stage == 0, firstLayer.IsFrozenInNewViewports, "Layer A new-viewport default");
            Equal(stage != 0, secondLayer.IsFrozenInNewViewports, "Layer B new-viewport default");
            if (stage == 0)
            {
                firstLayer.IsFrozenInNewViewports = false; secondLayer.IsFrozenInNewViewports = true;
                Check(ReferenceEquals(first.FrozenLayers.Single(), firstLayer) && ReferenceEquals(second.FrozenLayers.Single(), secondLayer),
                    "Changing defaults rewrote existing viewport memberships");
                first.FrozenLayers[0] = secondLayer;
            }
        }
    }
}
