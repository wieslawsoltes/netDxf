using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using Attribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCommonEntityDataTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (int size in new[] { -1, 0, 1, 127, 128, 129, 1025 })
            {
                int n = size;
                Run($"common-data/wire/{v}/{b}/{n}", () => CommonDataWire(v, b, n));
            }
            for (int fault = 0; fault < 15; fault++)
            {
                int f = fault;
                Run($"common-data/malformed/{v}/{b}/{f}", () => CommonDataMalformed(v, b, f));
            }
            Run($"common-data/reordered/{v}/{b}", () => CommonDataReordered(v, b));
            if (v >= DxfVersion.AutoCad2004) Run($"common-data/literal-name/{v}/{b}", () => CommonDataLiteral(v, b));
            Run($"common-data/attributes/{v}/{b}", () => CommonDataAttributes(v, b));
            Run($"common-data/producer/{v}/{b}", () => CommonDataProducer(v, b));
            Run($"common-data/attribute-subclass/{v}/{b}", () => CommonAttributeSubclass(v, b));
            Run($"common-data/payload-boundary/{v}/{b}", () => CommonDataScope(v, b));
            for (int location = 0; location < 3; location++)
            {
                int p = location;
                Run($"common-data/downgrade/{v}/{b}/{p}", () => CommonDataDowngrade(v, b, p));
            }
        }
        Run("common-data/api/isolation-geometry", CommonDataApi);
        Run("common-data/api/clone-types", CommonDataCloneTypes);
    }

    private static byte[] CommonDataBytes(int count) => Enumerable.Range(0, count).Select(i => (byte)(i * 37 + 19)).ToArray();
    private static string? CommonColor(DxfVersion v) => v < DxfVersion.AutoCad2004 ? null : "ACME$青";
    private static EntityShadowMode? CommonShadow(DxfVersion v) => v < DxfVersion.AutoCad2007 ? null : EntityShadowMode.CastAndReceive;

    private static List<DxfTag> CommonDataTags(DxfVersion v, int size)
    {
        // A native OLE binary payload follows the common proxy packet. These must
        // never be joined even though both use310. A following LINE checks framing.
        var tags = LegacyOleTags(v, 17, false);
        int at = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbOleFrame");
        var common = new List<DxfTag>();
        if (CommonColor(v) != null) common.Add(new(430, v < DxfVersion.AutoCad2007 ? @"ACME$\U+9752" : CommonColor(v)!));
        if (CommonShadow(v).HasValue) common.Add(new(284, (short)0));
        if (size >= 0)
        {
            common.Add(v < DxfVersion.AutoCad2013 ? new DxfTag(92, size) : new DxfTag(160, (long)size));
            var bytes = CommonDataBytes(size);
            for (int i = 0; i < bytes.Length; i += 128)
            {
                common.Add(new(310, bytes.Skip(i).Take(128).ToArray()));
                // Ordinary common tags may separate chunks; the count still covers only bytes.
                common.Add(new(60, (short)0));
            }
        }
        tags.InsertRange(at, common);
        return tags;
    }

    private static void AssertCommonData(EntityObject entity, DxfVersion v, int size)
    {
        Equal(CommonColor(v), entity.ColorName, "Common color name");
        Equal(CommonShadow(v), entity.ShadowMode, "Common shadow presence/value");
        if (size < 0) Check(entity.ProxyGraphics == null, "Absent proxy became present");
        else Check(entity.ProxyGraphics!.SequenceEqual(CommonDataBytes(size)), "Common proxy bytes changed");
    }

    private static List<DxfTag> CommonSubclass(DxfRawRecord record)
    {
        int at = record.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbEntity");
        return record.Tags.Skip(at + 1).TakeWhile(t => t.Code != 100 && t.Code != 0).ToList();
    }

    private static void CommonDataWire(DxfVersion v, bool binary, int size)
    {
        using var input = new MemoryStream(RawFixtureBytes(CommonDataTags(v, size), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Common packet rejected");
        var frame = doc.Entities.OleFrames.Single();
        AssertCommonData(frame, v, size);
        var clone = (OleFrame)frame.Clone();
        AssertCommonData(clone, v, size);
        doc.Entities.Add(clone);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream();
            bool transport = cycle == 0 ? binary : !binary;
            Check(doc.Save(output, transport), "Common packet save failed");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"common-data-wire-{v}-{binary}-{size}.dxf"), output.ToArray());
            output.Position = 0;
            foreach (var raw in DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Where(r => r.Name == "OLEFRAME"))
            {
                var common = CommonSubclass(raw);
                Equal(size < 0 ? 0 : 1, common.Count(t => t.Code == 92 || t.Code == 160), "Proxy count presence");
                if (size >= 0)
                {
                    var length = common.Single(t => t.Code == 92 || t.Code == 160);
                    Equal(v < DxfVersion.AutoCad2013 ? (short)92 : (short)160, length.Code, "Profile byte-count code");
                    Equal((long)size, Convert.ToInt64(length.Value), "Proxy wire length");
                    Check(common.Where(t => t.Code == 310).All(t => ((byte[])t.Value).Length <= 127), "Writer proxy chunk limit");
                    Check(common.FindIndex(t => t.Code == 92 || t.Code == 160) < common.FindIndex(t => t.Code == 310) || size == 0, "Proxy count follows bytes");
                }
                Equal(17, (int)raw.Tags.Single(t => t.Code == 90).Value, "OLE payload count was consumed");
            }
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Common packet reload failed");
            foreach (var f in doc.Entities.OleFrames)
            {
                AssertCommonData(f, v, size);
                Check(f.GetBinaryData().SequenceEqual(OlePayload(17)), "Native OLE data merged with proxy graphics");
                Equal("after legacy bytes", (string)f.XData["LEGACY_OLE"].XDataRecord.Single().Value, "Following XData");
            }
            Equal(new Vector3(10, 20, 30), doc.Entities.Lines.Single().StartPoint, "Following entity");
        }
    }

    private static void CommonDataReordered(DxfVersion v, bool binary)
    {
        var tags = CommonDataTags(v, 129);
        int index = tags.FindIndex(t => t.Code == 92 || t.Code == 160);
        var count = tags[index]; tags.RemoveAt(index);
        // A legacy92 count is also accepted in modern files, then canonicalized on save.
        if (v >= DxfVersion.AutoCad2013) count = new(92, 129);
        tags.Insert(tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbOleFrame"), count);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Reordered common packet rejected");
        AssertCommonData(doc.Entities.OleFrames.Single(), v, 129);
    }

    private static void CommonDataLiteral(DxfVersion v, bool binary)
    {
        foreach (string value in new[] { "", @"Book$青\U+0041\u+FFFF\name", "Book$\0\r\n青" })
        {
            var doc = CommonAttributeDocument(v);
            var insert = doc.Entities.Inserts.Single();
            insert.ColorName = value; insert.Attributes.Single().ColorName = value;
            doc.Blocks["COMMON_BLOCK"].AttributeDefinitions["TAG"].ColorName = value;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Literal name save");
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Literal name reload");
                insert = doc.Entities.Inserts.Single();
                Equal(value, insert.ColorName, "Entity literal color name");
                Equal(value, insert.Attributes.Single().ColorName, "Attribute literal color name");
                Equal(value, doc.Blocks["COMMON_BLOCK"].AttributeDefinitions["TAG"].ColorName, "Definition literal color name");
            }
        }
    }

    private static void CommonDataReject(List<DxfTag> tags, bool binary)
    {
        using var stream = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(stream); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("AcDbEntity", StringComparison.Ordinal), "Missing common subclass diagnostic");
            Check(stream.CanRead, "Reader closed caller stream");
            return;
        }
        throw new InvalidOperationException("Malformed common data accepted");
#else
        Check(DxfDocument.Load(stream) == null, "Malformed common data accepted");
        Check(stream.CanRead, "Reader closed caller stream");
#endif
    }

    private static void CommonDataMalformed(DxfVersion v, bool binary, int fault)
    {
        var tags = CommonDataTags(v, 129);
        int at = tags.FindIndex(t => t.Code == 92 || t.Code == 160);
        short code = tags[at].Code;
        DxfTag Length(long n) => code == 92 ? new DxfTag(code, (int)n) : new DxfTag(code, n);
        switch (fault)
        {
            case 0: tags[at] = Length(-1); break;
            case 1: tags[at] = Length(128); break;
            case 2: tags[at] = Length(130); break;
            case 3: tags[at] = Length(EntityObject.MaximumProxyGraphicsBytes + 1L); break;
            case 4: tags[at] = Length(int.MaxValue); break;
            case 5: tags.Insert(at, tags[at]); break;
            case 6: tags.RemoveAt(at); break;
            case 7: tags[at + 1] = new(310, CommonDataBytes(129)); break;
            case 8: tags.Insert(at, new(284, (short)4)); break;
            case 9: tags.InsertRange(at, new[] { new DxfTag(430, "A"), new DxfTag(430, "B") }); break;
            case 10: tags.InsertRange(at, new[] { new DxfTag(284, (short)0), new DxfTag(284, (short)1) }); break;
            case 14:
                tags.Insert(tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbOleFrame"), new(100, "AcDbEntity"));
                break;
            case 12:
                tags[tags.FindIndex(t => t.Code == 100)] = new(100, "AcDbNotEntity");
                break;
            case 13:
                if (v < DxfVersion.AutoCad2010) tags[at] = new(160, 129L);
                else tags.Insert(at + 1, new(92, 129));
                break;
            case 11:
                // Header-only malformed declaration must not reserve advertised memory.
                tags.RemoveRange(at + 1, tags.FindIndex(at + 1, t => t.Code == 100) - at - 1);
                tags[at] = Length(EntityObject.MaximumProxyGraphicsBytes);
                long start = GC.GetAllocatedBytesForCurrentThread();
                CommonDataReject(tags, binary);
                Check(GC.GetAllocatedBytesForCurrentThread() - start < 4 * 1024 * 1024, "Allocated storage proportional to missing proxy payload");
                return;
        }
        CommonDataReject(tags, binary);
    }

    private static DxfDocument CommonAttributeDocument(DxfVersion v)
    {
        var doc = new DxfDocument(v);
        var definition = new AttributeDefinition("TAG") { Value = "defined", ColorName = CommonColor(v), ShadowMode = CommonShadow(v), ProxyGraphics = CommonDataBytes(129) };
        var block = new Block("COMMON_BLOCK"); block.AttributeDefinitions.Add(definition); block.Entities.Add(new Circle(Vector3.Zero, 2));
        var insert = new Insert(block, new Vector3(10, 20, 0));
        doc.Entities.Add(insert);
        return doc;
    }

    private static void CommonDataAttributes(DxfVersion v, bool binary)
    {
        var doc = CommonAttributeDocument(v);
        var source = doc.Entities.Inserts.Single();
        var copy = (Insert)source.Clone();
        Equal(CommonColor(v), copy.Attributes.Single().ColorName, "Attribute clone name");
        Check(copy.Attributes.Single().ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Attribute clone proxy");
        Check(copy.Block.AttributeDefinitions["TAG"].ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Definition clone proxy");
        source.Attributes.Single().Value = "instance";
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Attribute common save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"common-data-attributes-{v}-{binary}.dxf"), output.ToArray());
        output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Attribute common reload");
        var att = doc.Entities.Inserts.Single().Attributes.Single();
        var def = doc.Blocks["COMMON_BLOCK"].AttributeDefinitions["TAG"];
        Equal("instance", att.Value, "Attribute value after common data"); Equal("defined", def.Value, "Definition value after common data");
        Equal(CommonColor(v), att.ColorName, "Attribute name"); Equal(CommonColor(v), def.ColorName, "Definition name");
        Equal(CommonShadow(v), att.ShadowMode, "Attribute shadow"); Equal(CommonShadow(v), def.ShadowMode, "Definition shadow");
        Check(att.ProxyGraphics!.SequenceEqual(CommonDataBytes(129)) && def.ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Attribute/definition proxy packet");
    }

    private static void CommonDataProducer(DxfVersion v, bool binary)
    {
        string year = v.ToString().Replace("AutoCad", "");
        string path = Path.Combine("tests", "fixtures", "common-entity-data", $"ezdxf-common-R{year}-{binary}.dxf");
        var doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Independent common input rejected");
        byte[] proxy = doc.Entities.Lines.Single().ProxyGraphics ?? throw new InvalidOperationException("Independent proxy absent");
        Equal(164, proxy.Length, "Independent valid polyline proxy size");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var line = doc.Entities.Lines.Single();
            Equal(CommonColor(v), line.ColorName, "Independent color name");
            Equal(v >= DxfVersion.AutoCad2007 ? EntityShadowMode.Ignore : (EntityShadowMode?)null, line.ShadowMode, "Independent shadow");
            Check(line.ProxyGraphics!.SequenceEqual(proxy), "Independent proxy changed");
            Check(doc.Entities.Inserts.Single().Attributes.Single().ProxyGraphics!.SequenceEqual(proxy), "Independent ATTRIB proxy changed");
            Check(doc.Blocks["COMMON_BLOCK"].AttributeDefinitions["TAG"].ProxyGraphics!.SequenceEqual(proxy), "Independent ATTDEF proxy changed");
            Equal(new Vector3(123, 456, 789), doc.Entities.Points.Single().Position, "Independent following entity");
            Equal("after common data", (string)line.XData["COMMON_DATA_QA"].XDataRecord[0].Value, "Independent XData");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Independent common save");
            if (cycle == 1) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"common-data-producer-{v}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Independent common reload");
        }
    }

    private static void CommonAttributeSubclass(DxfVersion v, bool binary)
    {
        string year = v.ToString().Replace("AutoCad", "");
        using var file = File.OpenRead(Path.Combine("tests", "fixtures", "common-entity-data", $"ezdxf-common-R{year}-{binary}.dxf"));
        var raw = DxfRawDocument.Load(file);
        foreach (bool duplicate in new[] { false, true })
        {
            var tags = raw.Tags.ToList();
            int start = tags.FindIndex(tag => tag.Code == 0 && (string)tag.Value == "ATTRIB");
            int common = tags.FindIndex(start, tag => tag.Code == 100);
            if (duplicate) tags.Insert(tags.FindIndex(common + 1, tag => tag.Code == 100), new(100, "AcDbEntity"));
            else tags[common] = new(100, "AcDbNotEntity");
            CommonDataReject(tags, binary);
        }
    }

    private static void CommonDataScope(DxfVersion v, bool binary)
    {
        var doc = new DxfDocument(v);
        var boundary = new Polyline2D(new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 3), new Vector2(0, 3) }, true);
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { boundary }) }, false) { ProxyGraphics = CommonDataBytes(128) };
        doc.Entities.Add(hatch);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Hatch common save");
        output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Hatch common reload");
        Check(doc.Entities.Hatches.Single().ProxyGraphics!.SequenceEqual(CommonDataBytes(128)), "Hatch proxy bytes");
        Equal(1, doc.Entities.Hatches.Single().BoundaryPaths.Count, "Hatch92 boundary count consumed by proxy reader");
    }

    private static void CommonDataDowngrade(DxfVersion v, bool binary, int location)
    {
        var doc = CommonAttributeDocument(DxfVersion.AutoCad2007);
        var insert = doc.Entities.Inserts.Single();
        var definition = doc.Blocks["COMMON_BLOCK"].AttributeDefinitions["TAG"];
        var attribute = insert.Attributes.Single();
        definition.ColorName = null; definition.ShadowMode = null; attribute.ColorName = null; attribute.ShadowMode = null;
        if (location == 0) { insert.ColorName = ""; insert.ShadowMode = EntityShadowMode.CastAndReceive; }
        if (location == 1) { definition.ColorName = ""; definition.ShadowMode = EntityShadowMode.CastAndReceive; }
        if (location == 2) { attribute.ColorName = ""; attribute.ShadowMode = EntityShadowMode.CastAndReceive; }
        doc.DrawingVariables.AcadVer = v;
        using var output = new MemoryStream(); output.Write(new byte[] { 7, 8, 9 }); long position = output.Position;
        if (v < DxfVersion.AutoCad2007)
        {
            try { doc.Save(output, binary); }
            catch (DxfVersionNotSupportedException)
            {
                Equal(position, output.Position, "Downgrade changed stream position");
                Check(output.ToArray().SequenceEqual(new byte[] { 7, 8, 9 }), "Downgrade wrote partial output"); return;
            }
            throw new InvalidOperationException("Unsupported optional metadata silently downgraded");
        }
        Check(doc.Save(output, binary), "Admitted common metadata save");
    }

    private static void CommonDataApi()
    {
        byte[] bytes = CommonDataBytes(129);
        var line = new Line(Vector3.Zero, Vector3.UnitX) { ProxyGraphics = bytes, ColorName = "", ShadowMode = EntityShadowMode.CastAndReceive };
        bytes[0] ^= 255; Check(line.ProxyGraphics![0] != bytes[0], "Proxy setter retained caller array");
        var exported = line.ProxyGraphics!; exported[0] ^= 255; Check(line.ProxyGraphics![0] != exported[0], "Proxy getter exposed storage");
        var copy = (Line)line.Clone(); var copyBytes = copy.ProxyGraphics!; copyBytes[0] = 200; copy.ProxyGraphics = copyBytes;
        Check(line.ProxyGraphics![0] != 200, "Proxy clone shares storage");
        line.TransformBy(Matrix3.Scale(2), new Vector3(3, 4, 5));
        Equal(new Vector3(5, 4, 5), line.EndPoint, "Stored proxy disabled geometry transformation");
        Check(line.ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Transform altered opaque cache");
        var polyline = new Polyline2D(new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY }) { ProxyGraphics = CommonDataBytes(129) };
        polyline.Reverse(); polyline.Reverse(); Check(polyline.ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Reverse altered opaque cache");
        try { line.ShadowMode = (EntityShadowMode)4; throw new InvalidOperationException("Invalid shadow accepted"); } catch (ArgumentOutOfRangeException) { }
        try { line.ProxyGraphics = new byte[EntityObject.MaximumProxyGraphicsBytes + 1]; throw new InvalidOperationException("Oversized proxy accepted"); } catch (ArgumentOutOfRangeException) { }
        Check(line.ProxyGraphics!.SequenceEqual(CommonDataBytes(129)), "Failed proxy setter mutated cache");
        line.ClearProxyGraphics(); Check(line.ProxyGraphics == null, "Clear kept an empty packet");
    }

    private static void CommonDataCloneTypes()
    {
        int tested = 0;
        foreach (Type type in typeof(EntityObject).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(EntityObject).IsAssignableFrom(t)))
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor == null) continue;
            var entity = (EntityObject)ctor.Invoke(null);
            entity.ColorName = ""; entity.ShadowMode = EntityShadowMode.Ignore; entity.ProxyGraphics = CommonDataBytes(3);
            var clone = (EntityObject)entity.Clone();
            Equal("", clone.ColorName, type.Name + " clone absent/empty color");
            Equal(EntityShadowMode.Ignore, clone.ShadowMode!.Value, type.Name + " clone shadow");
            Check(clone.ProxyGraphics!.SequenceEqual(CommonDataBytes(3)), type.Name + " clone proxy");
            clone.ClearProxyGraphics(); Check(entity.ProxyGraphics != null, type.Name + " clone storage isolation");
            tested++;
        }
        Check(tested >= 20, "Insufficient public entity clone coverage");
        var spline = new Spline(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }, null, 3) { ColorName = "Book", ShadowMode = EntityShadowMode.Cast, ProxyGraphics = CommonDataBytes(3) };
        foreach (EntityObject entity in new EntityObject[] { spline, new Helix(spline) })
        {
            var clone = (EntityObject)entity.Clone(); Equal("Book", clone.ColorName, "Spline/Helix clone color");
            Check(clone.ProxyGraphics!.SequenceEqual(CommonDataBytes(3)), "Spline/Helix constructor proxy");
        }
    }
}
