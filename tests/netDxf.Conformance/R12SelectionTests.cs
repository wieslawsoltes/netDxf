// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Threading;
using System.Threading.Tasks;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12SelectionTests()
    {
        RegisterR12SelectionIntegrityTests();
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad12 }.Concat(SupportedVersions))
        foreach (bool binary in new[] { false, true })
        {
            Run($"r12-selection/wire/{version}/{binary}", () => RsWire(version, binary));
            Run($"r12-selection/budget/{version}/{binary}", () => RsBudget(version, binary));
        }
        foreach (DxfVersion version in SupportedVersions)
        {
            Run("r12-selection/default-resources/" + version, () => RsDefaults(version));
            Run("r12-selection/independent-documents/" + version, () => RsIndependent(version));
        }
        Run("r12-selection/invalid-targets", RsInvalidTargets);
        Run("r12-selection/cancellation", RsCancellation);
        Run("r12-selection/one-pass-snapshot", RsSnapshot);
        Run("r12-selection/raw-input-immutable", RsRaw);
        Run("r12-selection/concurrent-readers", RsConcurrent);
        Run("r12-selection/deep-adoption", RsDeep);
        Run("r12-selection/output-stream-contract", RsStream);
        Run("r12-selection/unsupported-source-atomic", RsUnsupported);
    }

    private static EntityObject[] RsSeeds() => RbSeeds().Cast<EntityObject>()
        .Concat(new EntityObject[] { RaSeed(TextAlignment.MiddleCenter, 5) }).ToArray();

    private static Insert RsAttributeRoot(IEnumerable<EntityObject> roots) =>
        roots.OfType<Insert>().Single(i => i.Block.Name == "ATTR_BLOCK");

    private static void RsGraph(IEnumerable<EntityObject> roots)
    {
        var values = roots.Cast<Insert>().ToArray(); Equal(3, values.Length, "Converted root inventory");
        var outer = values.Where(i => i.Block.Name == "OUTER").ToArray();
        Check(ReferenceEquals(outer[0].Block, outer[1].Block), "Conversion duplicated a shared root block");
        var nested = outer[0].Block.Entities.Cast<Insert>().ToArray();
        Check(ReferenceEquals(nested[0].Block, nested[1].Block), "Conversion duplicated a shared nested block");
        RbSameInsert(RbSeeds()[0], outer[0]);
        var actual = RsAttributeRoot(values); var expected = RaSeed(TextAlignment.MiddleCenter, 5);
        RaSame(expected.Attributes.Single(), actual.Attributes.Single());
        RaSame(expected.Block.AttributeDefinitions.Values.Single(), actual.Block.AttributeDefinitions.Values.Single());
        Check(ReferenceEquals(actual.Attributes.Single().Definition, actual.Block.AttributeDefinitions.Values.Single()),
            "Conversion lost canonical attribute definition");
    }

    private static void RsWire(DxfVersion version, bool binary)
    {
        var source = RsSeeds(); var plan = DxfR12SelectionPlan.Prepare(source);
        Equal(3, plan.RootEntityCount, "Plan root inventory"); Equal(3, plan.BlockCount, "Plan block inventory");
        Equal(1, plan.AttributeDefinitionCount, "Plan ATTDEF inventory"); Equal(1, plan.AttributeCount, "Plan ATTRIB inventory");
        var tags = plan.NormalizedSelection.Tags.ToArray();
        using var output = new MemoryStream(); plan.Save(output, version, binary);
        byte[] bytes = output.ToArray();
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-selection-{version}-{(binary ? "binary" : "text")}.dxf"), bytes);
        using var input = new MemoryStream(bytes);
        if (version == DxfVersion.AutoCad12)
        {
            var raw = DxfRawDocument.Load(input); Equal(DxfVersion.AutoCad12, raw.Version, "R12 selection version");
            RsGraph(DxfR12Codec.ReadEntities(raw));
        }
        else
        {
            var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Converted document did not load");
            Equal(version, document.DrawingVariables.AcadVer, "Modern selection target version");
            Equal(DrawingUnits.Unitless, document.DrawingVariables.InsUnits, "Converted drawing units");
            RsGraph(document.Entities.All);
            var root = RsAttributeRoot(document.Entities.All);
            Check(ReferenceEquals(root.Block.Record.Owner, document.Blocks), "Converted block is not registered");
            Check(ReferenceEquals(root.Attributes.Single().Layer.Owner, document.Layers), "Converted attribute layer is not registered");
            Check(ReferenceEquals(root.Attributes.Single().Style.Owner, document.TextStyles), "Converted attribute style is not registered");
        }
        Check(tags.SequenceEqual(plan.NormalizedSelection.Tags), "Saving a plan changed its immutable snapshot");
        Check(source.All(e => e.Handle == null && e.Owner == null), "Selection conversion adopted caller entities");
    }

    private static void RsDefaults(DxfVersion version)
    {
        var layer = new Layer("0") { Color = new AciColor(2), IsFrozen = true, IsFrozenInNewViewports = true, IsLocked = true, IsVisible = false };
        var style = new TextStyle("Standard", "romans.shx") { Height = 2, WidthFactor = .7, ObliqueAngle = 11, IsVertical = true };
        var text = new Text("Default resource preservation", Vector3.Zero, 2, style) { Layer = layer };
        var plan = DxfR12SelectionPlan.Prepare(new[] { text }); var document = plan.CreateDocument(version);
        var actual = document.Entities.All.OfType<Text>().Single();
        Check(ReferenceEquals(actual.Layer, document.Layers["0"]) && ReferenceEquals(actual.Style, document.TextStyles["Standard"]),
            "Default resources were not canonical");
        Equal((short)2, actual.Layer.Color.Index, "Destination Layer 0 overwrote selected color");
        Check(actual.Layer.IsFrozen && actual.Layer.IsFrozenInNewViewports && actual.Layer.IsLocked && !actual.Layer.IsVisible,
            "Destination Layer 0 overwrote selected flags");
        Equal("romans.shx", actual.Style.FontFile, "Destination Standard overwrote selected font");
        Near(2, actual.Style.Height, "Selected Standard fixed height"); Near(.7, actual.Style.WidthFactor, "Selected Standard width");
        Near(11, actual.Style.ObliqueAngle, "Selected Standard oblique"); Check(actual.Style.IsVertical, "Selected Standard vertical flag");
        using var bytes = new MemoryStream(); plan.Save(bytes, version);
        bytes.Position = 0; var loaded = DxfDocument.Load(bytes)!;
        Equal((short)2, loaded.Layers["0"].Color.Index, "Resaved Layer 0 color");
        Equal("romans.shx", loaded.TextStyles["Standard"].FontFile, "Resaved Standard font");
    }

    private static void RsIndependent(DxfVersion version)
    {
        var plan = DxfR12SelectionPlan.Prepare(RsSeeds());
        var first = plan.CreateDocument(version); var second = plan.CreateDocument(version);
        var a = RsAttributeRoot(first.Entities.All); var b = RsAttributeRoot(second.Entities.All);
        Check(!ReferenceEquals(a.Block, b.Block) && !ReferenceEquals(a.Attributes.Single().Layer, b.Attributes.Single().Layer),
            "Plan outputs share mutable graph objects");
        a.Attributes.Single().Value = "first only"; a.Block.Origin = new Vector3(99, 98, 97);
        b.Attributes.Single().Layer.Color = new AciColor(1);
        var third = plan.CreateDocument(version); var c = RsAttributeRoot(third.Entities.All);
        Equal("Actual café\t^\nvalue", c.Attributes.Single().Value, "A previous output mutated the plan");
        R12Vector(new Vector3(3, 4, 5), c.Block.Origin);
        Equal((short)4, c.Attributes.Single().Layer.Color.Index, "A previous output mutated plan resources");
    }

    private static void RsBudget(DxfVersion version, bool binary)
    {
        var plan = DxfR12SelectionPlan.Prepare(RsSeeds());
        foreach (var limits in new[] { new DxfRawOptions(128), new DxfRawOptions(1000000, 20), new DxfRawOptions(1000000, 100000, 3) })
        {
            using var destination = new MemoryStream(); destination.Write(new byte[] { 11, 22, 33 }); destination.Position = 1;
            R12Refuses(() => plan.Save(destination, version, binary, limits));
            Check(destination.Position == 1 && destination.ToArray().SequenceEqual(new byte[] { 11, 22, 33 }), "Budget failure changed destination");
            Check(destination.CanWrite, "Budget failure closed caller stream");
        }
    }

    private static void RsInvalidTargets()
    {
        var plan = DxfR12SelectionPlan.Prepare(RsSeeds());
        foreach (DxfVersion target in Enum.GetValues<DxfVersion>().Where(v => !SupportedVersions.Contains(v) && v != DxfVersion.AutoCad12).Append((DxfVersion)123456))
        {
            using var output = new MemoryStream(); output.WriteByte(77);
            Throws<NotSupportedException>(() => plan.CreateDocument(target));
            Throws<NotSupportedException>(() => plan.Save(output, target));
            Check(output.Position == 1 && output.Length == 1, "Unsupported target touched destination");
        }
        Throws<NotSupportedException>(() => plan.CreateDocument(DxfVersion.AutoCad12));
    }

    private static void RsCancellation()
    {
        var plan = DxfR12SelectionPlan.Prepare(RsSeeds()); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad12 }.Concat(SupportedVersions))
        {
            using var output = new MemoryStream(); output.WriteByte(19);
            Throws<OperationCanceledException>(() => plan.Save(output, version, cancellationToken: cancelled.Token));
            Check(output.Length == 1 && output.Position == 1 && output.CanWrite, "Cancelled save changed caller stream");
            if (version != DxfVersion.AutoCad12)
                Throws<OperationCanceledException>(() => plan.CreateDocument(version, cancelled.Token));
        }
    }

    private static void RsSnapshot()
    {
        int enumerations = 0; var source = RsSeeds();
        IEnumerable<EntityObject> Once()
        {
            if (++enumerations != 1) throw new InvalidOperationException("Source was re-enumerated");
            foreach (var entity in source) yield return entity;
        }
        var plan = DxfR12SelectionPlan.Prepare(Once());
        RsAttributeRoot(source).Attributes.Single().Value = "changed after preparation";
        var output = plan.CreateDocument(DxfVersion.AutoCad2018);
        Equal(1, enumerations, "Snapshot enumerated source more than once");
        Equal("Actual café\t^\nvalue", RsAttributeRoot(output.Entities.All).Attributes.Single().Value, "Plan retained mutable source values");
    }

    private static void RsRaw()
    {
        var raw = DxfR12Codec.Create(RsSeeds()); var before = raw.Tags.ToArray();
        var plan = DxfR12SelectionPlan.Prepare(raw);
        Check(before.SequenceEqual(raw.Tags), "Preparing raw selection changed original packet");
        Equal(3, plan.RootEntityCount, "Raw selection root count");
        RsGraph(plan.CreateDocument(DxfVersion.AutoCad2000).Entities.All);
        Throws<ArgumentNullException>(() => DxfR12SelectionPlan.Prepare((DxfRawDocument)null!));
        Throws<ArgumentNullException>(() => DxfR12SelectionPlan.Prepare((IEnumerable<EntityObject>)null!));
    }

    private static void RsConcurrent()
    {
        var plan = DxfR12SelectionPlan.Prepare(RsSeeds());
        var values = new string[8];
        Parallel.For(0, values.Length, i =>
        {
            var document = plan.CreateDocument(SupportedVersions[i % SupportedVersions.Length]);
            values[i] = RsAttributeRoot(document.Entities.All).Attributes.Single().Value;
        });
        Check(values.All(v => v == "Actual café\t^\nvalue"), "Concurrent plan reader changed snapshot state");
    }

    private static void RsDeep()
    {
        Block current = new Block("PLAN_0"); current.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        for (int i = 1; i < 512; i++)
        { var parent = new Block("PLAN_" + i); parent.Entities.Add(new Insert(current)); current = parent; }
        var plan = DxfR12SelectionPlan.Prepare(new[] { new Insert(current) });
        var document = plan.CreateDocument(DxfVersion.AutoCad2018); var node = document.Entities.All.OfType<Insert>().Single().Block;
        for (int i = 511; i > 0; i--) { Equal("PLAN_" + i, node.Name, "Deep plan graph order"); node = node.Entities.OfType<Insert>().Single().Block; }
        Equal("PLAN_0", node.Name, "Deep plan leaf");
    }

    private static void RsStream()
    {
        var plan = DxfR12SelectionPlan.Prepare(new EntityObject[] { new Point(Vector3.Zero) });
        using var output = new MemoryStream(); output.Write(new byte[] { 1, 2, 3 });
        plan.Save(output, DxfVersion.AutoCad12); Check(output.CanWrite && output.ToArray().Take(3).SequenceEqual(new byte[] { 1, 2, 3 }), "Save overwrote prefix or closed stream");
        Throws<ArgumentNullException>(() => plan.Save(null!, DxfVersion.AutoCad12));
        using var readOnly = new MemoryStream(new byte[10], false);
        Throws<ArgumentException>(() => plan.Save(readOnly, DxfVersion.AutoCad12));
    }

    private static void RsUnsupported()
    {
        var source = RaSeed(); source.Block.Description = "Not silently discarded";
        R12Refuses(() => DxfR12SelectionPlan.Prepare(new[] { source }));
        Check(source.Handle == null && source.Owner == null && source.Block.Handle == null, "Failed selection plan adopted source");
    }
}
