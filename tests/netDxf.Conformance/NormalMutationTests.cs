// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using DxfAttribute = netDxf.Entities.Attribute;
using DxfPoint = netDxf.Entities.Point;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] NormalMutationProxy = { 1, 7, 19, 33, 255 };
    private static readonly string[] NormalMutationKinds = DirectionSlotKinds
        .Where(kind => kind.EndsWith("-normal", StringComparison.Ordinal)).ToArray();
    private static byte[]? NormalProxy(object host) => host switch
    {
        EntityObject e => e.ProxyGraphics, DxfAttribute a => a.ProxyGraphics,
        AttributeDefinition d => d.ProxyGraphics, _ => throw new ArgumentException(nameof(host))
    };
    private static void SetNormalProxy(object host, byte[]? value)
    {
        if (host is EntityObject e) e.ProxyGraphics = value;
        else if (host is DxfAttribute a) a.ProxyGraphics = value;
        else if (host is AttributeDefinition d) d.ProxyGraphics = value;
        else throw new ArgumentException(nameof(host));
    }
    private static long[] NormalBits(Vector3 value) => new[] { value.X, value.Y, value.Z }
        .Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static Vector3 NormalMutationInput(int mode) => mode switch
    {
        0 => Vector3.UnitZ, 1 => Vector3.UnitX,
        2 => new Vector3(0, 0, 8), 3 => new Vector3(0, 0, double.Epsilon),
        4 => new Vector3(0, 0, double.MaxValue), 5 => new Vector3(2, -3, 6),
        6 => new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 0, 1),
        7 => new Vector3(1e-13, 0, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    // Only fixture setup calls this helper. Reattach synthetic bytes after proving
    // that the setup edit invalidates its old cache; retain the original operation assertions.
    private static void NormalFixtureEditAndRestore(EntityObject source, Vector3 value)
    {
        byte[] proxy = source.ProxyGraphics ?? throw new InvalidOperationException("Fixture proxy required");
        long[] before = NormalBits(source.Normal); source.Normal = value;
        Check(!before.SequenceEqual(NormalBits(source.Normal)), "Fixture must make a real normal edit");
        Check(source.ProxyGraphics == null, "Fixture normal edit did not invalidate its stale proxy");
        source.ProxyGraphics = proxy;
        Check(source.ProxyGraphics!.SequenceEqual(proxy), "Fixture cache reattachment changed bytes");
    }
    private static void RegisterNormalMutationTests()
    {
        foreach (string kind in NormalMutationKinds)
        for (int mode = 0; mode < 8; mode++)
        foreach (bool emptyProxy in new[] { false, true })
        {
            int m = mode;
            Run($"normal-mutation/api/{kind}/{m}/{emptyProxy}", () =>
            {
                var slot = MakeDirectionSlot(kind); slot.Set(Vector3.UnitZ);
                byte[] proxy = emptyProxy ? Array.Empty<byte>() : NormalMutationProxy;
                SetNormalProxy(slot.Owner, proxy);
                long[] before = NormalBits(slot.Get()); Vector3 input = NormalMutationInput(m);
                slot.Set(input); Vector3 actual = slot.Get(); CheckUnitDirection(input, actual);
                bool changed = !before.SequenceEqual(NormalBits(actual)); byte[]? after = NormalProxy(slot.Owner);
                Check(changed ? after == null : after != null && after.SequenceEqual(proxy),
                    "Normal assignment must invalidate exactly when normalized stored bits change");
                Check(actual.IsNormalized, "Normal cache state was not transferred");
                SetNormalProxy(slot.Owner, proxy); slot.Set(actual);
                Check(NormalBits(actual).SequenceEqual(NormalBits(slot.Get())), "Repeated normal drift");
                Check(NormalProxy(slot.Owner)!.SequenceEqual(proxy), "Identical normal discarded proxy");
                object clone = ((ICloneable)slot.Owner).Clone();
                var cloneNormal = (Vector3)clone.GetType().GetProperty("Normal")!.GetValue(clone)!;
                Check(NormalBits(actual).SequenceEqual(NormalBits(cloneNormal)), "Clone normal");
                Check(NormalProxy(clone)!.SequenceEqual(proxy), "Clone did not preserve retained proxy");
                clone.GetType().GetProperty("Normal")!.SetValue(clone, -actual);
                Check(NormalProxy(clone) == null, "Clone retained stale proxy after normal edit");
                Check(NormalProxy(slot.Owner)!.SequenceEqual(proxy), "Clone edit damaged source proxy");
            });
        }
        foreach (string kind in NormalMutationKinds)
        foreach (Vector3 invalid in new[] { Vector3.Zero, new Vector3(double.NaN, 0, 1),
            new Vector3(0, double.NaN, 1), new Vector3(0, 1, double.NaN),
            new Vector3(double.PositiveInfinity, 0, 1), new Vector3(0, double.NegativeInfinity, 1),
            new Vector3(1, 0, double.PositiveInfinity) })
            Run($"normal-mutation/reject/{kind}/{ParameterBits(invalid.X)}/{ParameterBits(invalid.Y)}/{ParameterBits(invalid.Z)}", () =>
            {
                var slot = MakeDirectionSlot(kind); slot.Set(new Vector3(2, -3, 6));
                var before = NormalBits(slot.Get()); SetNormalProxy(slot.Owner, NormalMutationProxy);
                ArgumentException? caught = null;
                try { slot.Set(invalid); } catch (ArgumentException e) { caught = e; }
                Check(caught != null && caught.ParamName == "value", "Original direction admission/parameter changed");
                Check(before.SequenceEqual(NormalBits(slot.Get())), "Rejected normal changed stored direction");
                Check(NormalProxy(slot.Owner)!.SequenceEqual(NormalMutationProxy), "Rejected normal changed proxy");
            });
        foreach (string kind in NormalMutationKinds)
            Run($"normal-mutation/absent/{kind}", () =>
            {
                var slot = MakeDirectionSlot(kind); SetNormalProxy(slot.Owner, null);
                slot.Set(Vector3.UnitX); Check(NormalProxy(slot.Owner) == null, "Absent proxy was manufactured");
            });
        foreach (bool mesh in new[] { false, true })
        for (int mode = 0; mode < 8; mode++)
        {
            int m = mode;
            Run($"normal-mutation/wcs-clone/{mesh}/{m}", () =>
            {
                Vector3[] points = { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(2, 3, 1) };
                EntityObject host = mesh ? new Mesh(points, new[] { new[] { 0, 1, 2 } }) : new Spline(points, null, (short)2);
                host.Normal = Vector3.UnitZ; host.ProxyGraphics = NormalMutationProxy;
                long[] before = NormalBits(host.Normal); host.Normal = NormalMutationInput(m); Vector3 actual = host.Normal;
                bool changed = !before.SequenceEqual(NormalBits(actual));
                Check(changed ? host.ProxyGraphics == null : host.ProxyGraphics!.SequenceEqual(NormalMutationProxy), "WCS family normal cache policy");
                host.ProxyGraphics = NormalMutationProxy; var clone = (EntityObject)host.Clone();
                Check(NormalBits(actual).SequenceEqual(NormalBits(clone.Normal)), "WCS clone normal");
                Check(clone.ProxyGraphics!.SequenceEqual(NormalMutationProxy), "WCS clone retained cache");
                clone.Normal = -actual; Check(clone.ProxyGraphics == null, "WCS clone stale cache");
                Check(host.ProxyGraphics!.SequenceEqual(NormalMutationProxy), "WCS clone changed source cache");
                Throws<ArgumentException>(() => host.Normal = Vector3.Zero);
                Check(NormalBits(actual).SequenceEqual(NormalBits(host.Normal)) && host.ProxyGraphics!.SequenceEqual(NormalMutationProxy),
                    "WCS rejected assignment changed state");
            });
        }
        for (int kind = 0; kind < 8; kind++)
        {
            int k = kind;
            Run($"normal-mutation/dimension/{k}", () =>
            {
                var dimension = TextBlockDimension(k); dimension.UserText = "FIXED";
                dimension.Normal = Vector3.UnitZ; dimension.ProxyGraphics = NormalMutationProxy;
                dimension.Normal = Vector3.UnitX;
                Check(dimension.ProxyGraphics == null, "Dimension inherited normal retained stale common graphics");
                dimension.ProxyGraphics = NormalMutationProxy; dimension.Normal = dimension.Normal;
                Check(dimension.ProxyGraphics!.SequenceEqual(NormalMutationProxy), "Dimension normal no-op");
                Equal("FIXED", dimension.UserText, "Normal edit changed dimension label metadata");
            });
        }
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"normal-mutation/wire/{version}/{binary}/{p}", () =>
            {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                var primitives = Enumerable.Range(0, 16).Select(NormalWireEntity).ToArray();
                var inserts = new List<Insert>();
                for (int mode = 0; mode < 4; mode++)
                {
                    var definition = new AttributeDefinition("NM" + mode)
                    { Value = "definition", Position = new Vector3(1, 2, 3), Normal = Vector3.UnitZ };
                    var block = new Block("NORMAL_ATTRIBUTE_" + mode); block.AttributeDefinitions.Add(definition);
                    var insert = new Insert(block) { Layer = new Layer("NORMAL_INSERT_" + mode) };
                    insert.Attributes.Single().Value = "attribute"; inserts.Add(insert);
                }
                var hosts = primitives.Concat<EntityObject>(inserts).ToArray();
                if (p == 0) foreach (var e in hosts) doc.Entities.Add(e);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("NORMAL_PAPER"));
                    foreach (var e in hosts) doc.Layouts["NORMAL_PAPER"].AssociatedBlock.Entities.Add(e);
                }
                else
                {
                    var holder = new Block("NORMAL_HOLDER", hosts);
                    if (p == 2) doc.Entities.Add(new Insert(holder)); else doc.Blocks.Add(holder);
                }
                for (int mode = 0; mode < 4; mode++)
                {
                    var definition = doc.Blocks["NORMAL_ATTRIBUTE_" + mode].AttributeDefinitions.Values.Single();
                    var attribute = inserts[mode].Attributes.Single();
                    definition.ProxyGraphics = NormalMutationProxy; attribute.ProxyGraphics = NormalMutationProxy;
                    ApplyNormalWire(v => definition.Normal = v, mode); ApplyNormalWire(v => attribute.Normal = v, mode);
                }
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)) { Layer = new Layer("NORMAL_FOLLOWING") });
                string[] handles = primitives.Select(e => e.Handle).ToArray(); CheckNormalWireDocument(doc, handles);
                string stem = $"normal-mutation-{version}-{binary}-{p}";
                using var source = new MemoryStream(); Check(doc.Save(source, binary), "Normal source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray());
                source.Position = 0;
                var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Normal load failed");
                CheckNormalWireDocument(loaded, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Normal resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray()); stream.Position = 0;
                    var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Normal reload failed");
                    CheckNormalWireDocument(second, handles);
                }
                Check(source.CanRead, "Normal load closed caller stream");
            });
        }
    }
    private static EntityObject NormalWireEntity(int row)
    {
        int kind = row / 4, mode = row % 4;
        EntityObject entity = kind switch
        {
            0 => new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)) { Thickness = 2 },
            1 => new DxfPoint(new Vector3(1, 2, 3)) { Thickness = 2, Rotation = 30 },
            2 => new Circle(new Vector3(1, 2, 3), 2) { Thickness = 2 },
            _ => new Arc(new Vector3(1, 2, 3), 2, 30, 120) { Thickness = 2 }
        };
        entity.Layer = new Layer("NORMAL_HOST_" + row.ToString("D2")); entity.Color = new AciColor(3);
        var xdata = new XData(new ApplicationRegistry("NORMAL_KEEP"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.String, "unmodified"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 17, 33, 201 }));
        entity.XData.Add(xdata); entity.ProxyGraphics = NormalMutationProxy;
        ApplyNormalWire(v => entity.Normal = v, mode); return entity;
    }
    private static void ApplyNormalWire(Action<Vector3> assign, int mode)
    {
        if (mode == 3) { Throws<ArgumentException>(() => assign(Vector3.Zero)); return; }
        assign(mode == 0 ? new Vector3(0, 0, 8) : mode == 1 ? Vector3.UnitX : NormalMutationInput(6));
    }
    private static void CheckNormalWireValue(object host, Vector3 normal, int mode)
    {
        var expected = mode == 1 ? Vector3.UnitX : mode == 2 ? NormalMutationInput(6) : Vector3.UnitZ;
        RawLinePointBits(expected, normal); byte[]? proxy = NormalProxy(host);
        Check(mode is 1 or 2 ? proxy == null : proxy != null && proxy.SequenceEqual(NormalMutationProxy), "Normal wire common graphics presence/content");
    }
    private static void CheckNormalWireDocument(DxfDocument doc, string[] handles)
    {
        var entities = doc.Blocks.SelectMany(b => b.Entities).Where(e => e.Layer.Name.StartsWith("NORMAL_HOST_", StringComparison.Ordinal))
            .OrderBy(e => e.Layer.Name, StringComparer.Ordinal).ToArray(); Equal(16, entities.Length, "Normal host inventory");
        for (int row = 0; row < entities.Length; row++)
        {
            var e = entities[row]; Equal(handles[row], e.Handle, "Normal host identity"); CheckNormalWireValue(e, e.Normal, row % 4);
            Equal((short)3, e.Color.Index, "Normal edit changed appearance");
            Equal("unmodified", (string)e.XData["NORMAL_KEEP"].XDataRecord[0].Value, "Normal edit changed text XData");
            Check(((byte[])e.XData["NORMAL_KEEP"].XDataRecord[1].Value).SequenceEqual(new byte[] { 17, 33, 201 }), "Normal edit changed binary XData");
            switch (e)
            {
                case Line l: RawLinePointBits(new Vector3(1, 2, 3), l.StartPoint); RawLinePointBits(new Vector3(4, 5, 6), l.EndPoint); SameDoubleBits(2, l.Thickness, "LINE thickness"); break;
                case DxfPoint pt: RawLinePointBits(new Vector3(1, 2, 3), pt.Position); SameDoubleBits(2, pt.Thickness, "POINT thickness"); SameDoubleBits(30, pt.Rotation, "POINT rotation"); break;
                case Circle c: RawLinePointBits(new Vector3(1, 2, 3), c.Center); SameDoubleBits(2, c.Radius, "CIRCLE radius"); SameDoubleBits(2, c.Thickness, "CIRCLE thickness"); break;
                case Arc a: RawLinePointBits(new Vector3(1, 2, 3), a.Center); SameDoubleBits(2, a.Radius, "ARC radius"); SameDoubleBits(30, a.StartAngle, "ARC start"); SameDoubleBits(120, a.EndAngle, "ARC end"); break;
            }
            var clone = (EntityObject)e.Clone(); CheckNormalWireValue(clone, clone.Normal, row % 4);
        }
        for (int mode = 0; mode < 4; mode++)
        {
            var def = doc.Blocks["NORMAL_ATTRIBUTE_" + mode].AttributeDefinitions.Values.Single();
            var insert = doc.Blocks.SelectMany(b => b.Entities).OfType<Insert>().Single(i => i.Layer.Name == "NORMAL_INSERT_" + mode);
            var attribute = insert.Attributes.Single(); CheckNormalWireValue(def, def.Normal, mode); CheckNormalWireValue(attribute, attribute.Normal, mode);
            Equal("definition", def.Value, "Definition value"); Equal("attribute", attribute.Value, "Attribute value");
        }
        var line = doc.Entities.Lines.Single(e => e.Layer.Name == "NORMAL_FOLLOWING");
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
        Equal(0, doc.Objects.Validate().Count, "Normal object graph");
    }
}
