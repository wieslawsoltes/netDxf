using System.Text.Json;
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
    private static void RunTypedContainerTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            Run($"typed-containers/author/{version}/{binary}", () => TypedContainerAuthor(version, binary));
            Run($"typed-containers/independent/{version}/{binary}", () => TypedContainerIndependent(version, binary));
        }
        Run("typed-containers/idbuffer-clone", TypedContainerBufferClone);
        Run("typed-containers/extension-clone", TypedContainerExtensionClone);
        Run("typed-containers/defensive-boundary", TypedContainerDefensiveBoundary);
        Run("typed-containers/invalid-references", TypedContainerInvalidReferences);
        Run("typed-containers/sorting-atomicity", TypedContainerSortingAtomicity);
        Run("typed-containers/version-gate", TypedContainerVersionGate);
        Run("typed-containers/class-preflight", TypedContainerClassPreflight);
        Run("typed-containers/mapping-callback-preconditions", TypedContainerMappingCallbacks);
        Run("typed-containers/spatial-attachment-retry", TypedContainerSpatialAttachmentRetry);
        for (int scenario = 0; scenario < 8; scenario++)
        {
            int captured = scenario;
            Run($"typed-containers/malformed/{scenario}", () => TypedContainerMalformed(captured));
        }
    }
    private static DxfDocument BuildTypedContainers(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        var line = new Line(new Vector3(1.25, -2.5, 3.75), new Vector3(8.5, 9.25, -4.125));
        var second = new Line(new Vector3(-10, -20, -30), new Vector3(-40, -50, -60));
        var circle = new Circle(new Vector3(4.5, -5.25, 6.125), 2.75);
        doc.Entities.Add(line); doc.Entities.Add(second); doc.Entities.Add(circle);
        var graph = new DxfDictionary(); var variable = new DxfDictionaryVariable { Value = "typed mode" };
        graph.Add("MODE", variable); doc.NamedObjects.Add("TYPED_CONTAINERS", graph);
        var block = new Block("TYPED_CONTENT", new EntityObject[] { new Line(new Vector3(-5, -4, 0), new Vector3(8, 7, 0)), new Circle(new Vector3(1, 2, 0), 3) });
        for (int index = 0; index < 2; index++)
        {
            var insert = new Insert(block, new Vector3(10 + index, 20, 3)); doc.Entities.Add(insert);
            var spatial = new DxfSpatialFilter
            {
                Normal = new Vector3(0, 0.6, 0.8), Origin = new Vector3(1, -2, 3),
                IsClippingEnabled = index != 0, FrontClippingDistance = index == 0 ? 2.5 : null,
                BackClippingDistance = index == 1 ? -7.25 : null,
                InverseInsertTransform = new Matrix4(1, 0, 0, -10 - index, 0, 1, 0, -20, 0, 0, 1, -3, 0, 0, 0, 1),
                ClipBoundaryTransform = new Matrix4(1.25, 0.375, -0.25, 7.5 + index, 0.125, 1.5, 0.625, -11.25, 0.25, -0.125, 2.25, 3.125, 0, 0, 0, 1)
            };
            spatial.SetBoundary(index == 0 ? new[] { new Vector2(-2, -1), new Vector2(7, 5) } : new[] { new Vector2(-3, -1), new Vector2(5, -2), new Vector2(8, 3), new Vector2(3, 7), new Vector2(-4, 4) });
            var data = new XData(new ApplicationRegistry("TYPED_CONTAINERS"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.String, index == 0 ? "rectangle" : "polygon"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, insert.Handle)); spatial.XData.Add(data);
            doc.Objects.SetSpatialFilter(insert, spatial);
        }
        var buffer = new DxfIdBuffer();
        foreach (DxfObject? item in new DxfObject?[] { circle, null, line, circle, variable, doc.Entities.Inserts.First(), null }) buffer.References.Add(item!);
        buffer.PersistentReactors.Add(graph); graph.Add("BUFFER", buffer); graph.Add("EMPTY", new DxfIdBuffer());
        var meta = new XData(new ApplicationRegistry("TYPED_CONTAINERS")); meta.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, line.Handle)); buffer.XData.Add(meta);
        if (version >= DxfVersion.AutoCad2004)
        {
            doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$SORTENTS", 280, (short)1));
            doc.Objects.CreateSortentsTable(line.Owner.Record, new[] {
                new DxfSortOrderEntry(circle, "FFFFFFFFFFFFFFFE"), new DxfSortOrderEntry(second, "0"),
                new DxfSortOrderEntry(doc.Entities.Inserts.First(), second.Handle), new DxfSortOrderEntry(line, "FFFFFFFFFFFFFFFE") });
        }
        return doc;
    }
    private static DxfSpatialFilter Spatial(Insert insert) => (DxfSpatialFilter)((DxfDictionary)insert.ExtensionDictionary["ACAD_FILTER"])["SPATIAL"];
    private static void TypedContainerAuthor(DxfVersion version, bool binary)
    {
        var doc = BuildTypedContainers(version); Equal(0, doc.Objects.Validate().Count, "authored graph");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "typed save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"typed-containers-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed container load failed.");
        Equal(0, loaded.Objects.Validate().Count, "loaded graph");
        var graph = (DxfDictionary)loaded.NamedObjects["TYPED_CONTAINERS"]; var buffer = (DxfIdBuffer)graph["BUFFER"];
        Equal(7, buffer.References.Count, "ordered buffer length"); Check(buffer.References[1] == null && buffer.References[6] == null, "Null references lost.");
        Check(ReferenceEquals(buffer.References[0], buffer.References[3]), "Duplicate buffer references lost identity.");
        Check(ReferenceEquals(buffer.References[4], graph["MODE"]), "Forward nongraphical reference lost.");
        Equal(0, ((DxfIdBuffer)graph["EMPTY"]).References.Count, "empty buffer");
        Check(ReferenceEquals(graph, buffer.PersistentReactors.Single()), "Buffer reactor lost.");
        var inserts = loaded.Entities.Inserts.ToArray();
        for (int i = 0; i < 2; i++)
        {
            var original = Spatial(doc.Entities.Inserts.ElementAt(i)); var actual = Spatial(inserts[i]);
            Equal(original.FrontClippingDistance, actual.FrontClippingDistance, "front plane versus matrix40");
            Equal(original.BackClippingDistance, actual.BackClippingDistance, "optional back plane");
            Check(original.Boundary.SequenceEqual(actual.Boundary), "Spatial boundary differs.");
            Equal(original.Normal, actual.Normal, "OCS normal"); Equal(original.Origin, actual.Origin, "OCS origin");
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
            {
                Near(original.InverseInsertTransform[row, col], actual.InverseInsertTransform[row, col], "inverse matrix");
                Near(original.ClipBoundaryTransform[row, col], actual.ClipBoundaryTransform[row, col], "boundary matrix");
            }
            Equal(inserts[i].Handle, (string)actual.XData["TYPED_CONTAINERS"].XDataRecord[1].Value, "filter XData");
            Check(actual.PersistentReactors.Contains(actual.Owner), "Spatial owner reactor missing.");
        }
        if (version >= DxfVersion.AutoCad2004)
        {
            var table = loaded.Objects.Items.OfType<DxfSortentsTable>().Single();
            Check(table.Entries.Select(e => e.SortHandle).SequenceEqual(new[] { "FFFFFFFFFFFFFFFE", "0", loaded.Entities.Lines.Last().Handle, "FFFFFFFFFFFFFFFE" }), "Opaque sort keys changed.");
            Check(ReferenceEquals(table.BlockRecord, loaded.Entities.Lines.First().Owner.Record), "Sort block link changed.");
            Check(loaded.DrawingVariables.TryGetCustomVariable("$SORTENTS", out var sorting), "SORTENTS header missing."); Equal((short)17, (short)sorting.Value, "preserved and enabled sorting flags");
            var first = table.Entries[0]; table.Entries.RemoveAt(0); table.Entries.Add(first);
        }
        // Verify editing loaded objects reaches a second independent serialization.
        buffer.References.RemoveAt(1); buffer.References.Insert(0, graph["MODE"]); Spatial(inserts[0]).FrontClippingDistance = null;
        Spatial(inserts[1]).SetBoundary(new[] { new Vector2(-9, -8), new Vector2(9, 8) });
        using var changed = new MemoryStream(); Check(loaded.Save(changed, !binary), "edited container save"); changed.Position = 0;
        var edited = DxfDocument.Load(changed) ?? throw new InvalidOperationException("Edited container load failed.");
        Equal(null, Spatial(edited.Entities.Inserts.First()).FrontClippingDistance, "plane removal");
        Equal(2, Spatial(edited.Entities.Inserts.Last()).Boundary.Count, "boundary replacement");
        var editedGraph = (DxfDictionary)edited.NamedObjects["TYPED_CONTAINERS"];
        Check(ReferenceEquals(editedGraph["MODE"], ((DxfIdBuffer)editedGraph["BUFFER"]).References[0]), "Edited buffer order lost.");
    }
    private static string ContainerFixtureDirectory => Path.Combine("tests", "fixtures", "typed-containers");
    private static void TypedContainerIndependent(DxfVersion version, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", ""); string file = $"independent-typed-containers-R{year}.dxf";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContainerFixtureDirectory, "manifest.json")));
        var expected = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(e => e.GetProperty("file").GetString() == file);
        byte[] bytes = File.ReadAllBytes(Path.Combine(ContainerFixtureDirectory, file));
        Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "independent producer hash");
        using var input = new MemoryStream(bytes); var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Independent container load failed.");
        Equal(version, doc.DrawingVariables.AcadVer, "fixture version"); Equal(0, doc.Objects.Validate().Count, "independent graph");
        var buffer = (DxfIdBuffer)doc.GetObjectByHandle(expected.GetProperty("buffer").GetString());
        Check(buffer.References.Select(r => r?.Handle ?? "0").SequenceEqual(expected.GetProperty("buffer_handles").EnumerateArray().Select(v => v.GetString())), "Independent buffer sequence changed.");
        foreach (var clip in expected.GetProperty("clips").EnumerateArray())
        {
            var filter = (DxfSpatialFilter)doc.GetObjectByHandle(clip.GetProperty("spatial").GetString());
            var front = clip.GetProperty("front_distance"); var back = clip.GetProperty("back_distance");
            Equal(front.ValueKind == JsonValueKind.Null ? null : (double?)front.GetDouble(), filter.FrontClippingDistance, "independent front distance");
            Equal(back.ValueKind == JsonValueKind.Null ? null : (double?)back.GetDouble(), filter.BackClippingDistance, "independent back distance");
            foreach (string field in new[] { "inverse_matrix", "transform_matrix" })
            {
                double[] matrix = clip.GetProperty(field).EnumerateArray().Select(v => v.GetDouble()).ToArray();
                Matrix4 actual = field == "inverse_matrix" ? filter.InverseInsertTransform : filter.ClipBoundaryTransform;
                // ezdxf uses row vectors; the public netDxf matrix uses column vectors.
                for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) Near(matrix[col * 4 + row], actual[row, col], "independent transposed matrix");
            }
        }
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "independent re-export");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"independent-typed-containers-R{year}-{(binary ? "binary" : "ascii")}.dxf"), output.ToArray());
    }
    private static void TypedContainerBufferClone()
    {
        var source = BuildTypedContainers(DxfVersion.AutoCad2018); var graph = (DxfDictionary)source.NamedObjects["TYPED_CONTAINERS"];
        var same = source.Objects.Clone(graph, source.NamedObjects, "SAME"); var sameBuffer = (DxfIdBuffer)same["BUFFER"];
        Check(ReferenceEquals(sameBuffer.References[4], same["MODE"]), "Internal IDBUFFER pointer not remapped.");
        Check(ReferenceEquals(sameBuffer.References[0], source.Entities.Circles.Single()), "Same-document external pointer changed.");
        var target = new DxfDocument(); var replacementLine = new Line(Vector3.Zero, Vector3.UnitY); var replacementCircle = new Circle(Vector3.Zero, 5);
        var replacementInsert = new Insert((Block)source.Entities.Inserts.First().Block.Clone());
        target.Entities.Add(replacementLine); target.Entities.Add(replacementCircle); target.Entities.Add(replacementInsert);
        int count = target.Objects.Items.Count;
        Throws<InvalidOperationException>(() => target.Objects.Clone(graph, target.NamedObjects, "FAILED")); Equal(count, target.Objects.Items.Count, "failed clone atomicity");
        var copied = target.Objects.Clone(graph, target.NamedObjects, "COPY", new Dictionary<DxfObject, DxfObject> { [source.Entities.Lines.First()] = replacementLine, [source.Entities.Circles.Single()] = replacementCircle, [source.Entities.Inserts.First()] = replacementInsert });
        var buffer = (DxfIdBuffer)copied["BUFFER"];
        Check(ReferenceEquals(buffer.References[0], replacementCircle) && ReferenceEquals(buffer.References[3], replacementCircle), "External duplicate mapping lost.");
        Check(buffer.References[1] == null && buffer.References[6] == null, "Cloned null references changed.");
        Check(ReferenceEquals(buffer.PersistentReactors.Single(), copied), "Cloned buffer reactor lost.");
        Equal(replacementLine.Handle, (string)buffer.XData["TYPED_CONTAINERS"].XDataRecord[0].Value, "Cloned XData handle");
        Equal(0, target.Objects.Validate().Count, "cloned buffer graph");
    }
    private static void TypedContainerExtensionClone()
    {
        var source = BuildTypedContainers(DxfVersion.AutoCad2018); var target = new DxfDocument(DxfVersion.AutoCad2018);
        var originalInsert = source.Entities.Inserts.First(); var insert = new Insert((Block)originalInsert.Block.Clone()); target.Entities.Add(insert);
        target.Objects.CloneExtensionDictionary(originalInsert, insert);
        var filter = Spatial(insert); Check(!ReferenceEquals(filter, Spatial(originalInsert)), "Filter clone shared identity.");
        Equal(insert.Handle, (string)filter.XData["TYPED_CONTAINERS"].XDataRecord[1].Value, "Automatic extension owner mapping");
        Check(ReferenceEquals(filter.Owner, filter.PersistentReactors.Single()), "Spatial owner reactor mapping");
        var mappedLine = new Line(Vector3.Zero, Vector3.UnitX); var mappedSecond = new Line(Vector3.UnitY, Vector3.UnitZ); var mappedCircle = new Circle(Vector3.Zero, 3);
        target.Entities.Add(mappedLine); target.Entities.Add(mappedSecond); target.Entities.Add(mappedCircle);
        var originalTable = source.Objects.Items.OfType<DxfSortentsTable>().Single(); int count = target.Objects.Items.Count;
        Throws<InvalidOperationException>(() => target.Objects.CloneExtensionDictionary(originalTable.BlockRecord, mappedLine.Owner.Record));
        Equal(count, target.Objects.Items.Count, "failed extension clone registration"); Check(mappedLine.Owner.Record.ExtensionDictionary == null, "failed extension clone published");
        var map = new Dictionary<DxfObject, DxfObject> { [source.Entities.Circles.Single()] = mappedCircle, [source.Entities.Lines.First()] = mappedLine, [source.Entities.Lines.Last()] = mappedSecond, [originalInsert] = insert };
        target.Objects.CloneExtensionDictionary(originalTable.BlockRecord, mappedLine.Owner.Record, map);
        var table = target.Objects.Items.OfType<DxfSortentsTable>().Single();
        Check(table.Entries.Select(e => e.SortHandle).SequenceEqual(originalTable.Entries.Select(e => e.SortHandle)), "Clone remapped opaque keys.");
        Check(ReferenceEquals(table.BlockRecord, mappedLine.Owner.Record), "Cloned block pointer not remapped.");
        Equal(0, target.Objects.Validate().Count, "extension clone graph");
        using var output = new MemoryStream(); Check(target.Save(output, true), "extension clone save"); output.Position = 0;
        Check(DxfDocument.Load(output)?.Objects.Items.OfType<DxfSortentsTable>().Count() == 1, "extension clone reload");
        var invalid = new DxfDocument(); var owner = new Line(Vector3.Zero, Vector3.UnitX); invalid.Entities.Add(owner); count = invalid.Objects.Items.Count;
        Throws<InvalidOperationException>(() => invalid.Objects.CloneExtensionDictionary(originalInsert, owner)); Equal(count, invalid.Objects.Items.Count, "invalid clone owner atomicity");
    }
    private static void TypedContainerDefensiveBoundary()
    {
        var filter = new DxfSpatialFilter(); var boundary = new[] { new Vector2(-1, -2), new Vector2(3, 4) }; filter.SetBoundary(boundary);
        boundary[0] = new Vector2(99, 99); Equal(new Vector2(-1, -2), filter.Boundary[0], "input boundary isolation");
        var snapshot = filter.Boundary; filter.SetBoundary(new[] { new Vector2(-3, -4), new Vector2(5, 6) }); Equal(new Vector2(-1, -2), snapshot[0], "snapshot isolation");
        Throws<ArgumentException>(() => filter.SetBoundary(new[] { Vector2.Zero })); Throws<ArgumentOutOfRangeException>(() => filter.SetBoundary(new[] { Vector2.Zero, new Vector2(double.NaN, 2) }));
        Throws<IOException>(() => filter.SetBoundary(FailingVertices())); Equal(new Vector2(-3, -4), filter.Boundary[0], "failed enumeration changed boundary");
        Throws<ArgumentException>(() => filter.Normal = Vector3.Zero); Throws<ArgumentOutOfRangeException>(() => filter.FrontClippingDistance = double.PositiveInfinity);
        var projective = Matrix4.Identity; projective.M41 = 1; Throws<ArgumentException>(() => filter.ClipBoundaryTransform = projective);
        Equal(Matrix4.Identity, filter.ClipBoundaryTransform, "failed matrix mutation");
    }
    private static IEnumerable<Vector2> FailingVertices() { yield return Vector2.Zero; throw new IOException("fixture enumeration"); }
    private static void TypedContainerInvalidReferences()
    {
        var doc = new DxfDocument(); var buffer = new DxfIdBuffer(); buffer.References.Add(new Line()); int count = doc.Objects.Items.Count;
        Throws<ArgumentException>(() => doc.NamedObjects.Add("INVALID", buffer)); Equal(count, doc.Objects.Items.Count, "invalid adoption registration");
        buffer.References.Clear(); doc.NamedObjects.Add("BUFFER", buffer); var foreign = new DxfDocument(); var line = new Line(); foreign.Entities.Add(line);
        Throws<ArgumentException>(() => buffer.References.Add(line)); Throws<ArgumentException>(() => buffer.References.Add(doc));
        var own = new Line(); doc.Entities.Add(own); buffer.References.Add(own); doc.Entities.Remove(own);
        Check(doc.Objects.Validate().Count > 0, "Deleted buffer target accepted.");
        using var output = new MemoryStream(); CheckSaveRejected(doc, output); Equal(0L, output.Length, "invalid graph output");
        var invalid = new DxfDocument(); invalid.NamedObjects.Add("MISPLACED", new DxfSpatialFilter());
        Check(invalid.Objects.Validate().Count > 0, "Misplaced spatial filter accepted.");
        using var other = new MemoryStream(); CheckSaveRejected(invalid, other); Equal(0L, other.Length, "misplaced filter output");
    }
    private static void CheckSaveRejected(DxfDocument doc, Stream output)
    { bool rejected; try { rejected = !doc.Save(output); } catch (InvalidOperationException) { rejected = true; } catch (InvalidDataException) { rejected = true; } Check(rejected, "Invalid document saved."); }
    private static void TypedContainerSortingAtomicity()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var first = new Line(); var second = new Line(); doc.Entities.Add(first); doc.Entities.Add(second);
        var otherBlock = doc.Blocks.Add(new Block("OTHER", new[] { new Line() })); var other = otherBlock.Entities.First();
        int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
        Throws<ArgumentException>(() => doc.Objects.CreateSortentsTable(first.Owner.Record, new[] { new DxfSortOrderEntry(first, "10"), new DxfSortOrderEntry(other, "11") }));
        Equal(count, doc.Objects.Items.Count, "invalid sort registration"); Equal(seed, doc.DrawingVariables.HandleSeed, "invalid sort handle allocation"); Check(first.Owner.Record.ExtensionDictionary == null, "invalid sort published");
        Throws<IOException>(() => doc.Objects.CreateSortentsTable(first.Owner.Record, FailingSortEntries(first))); Equal(count, doc.Objects.Items.Count, "failing iterator registration");
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$SORTENTS", 280, (short)2));
        var table = doc.Objects.CreateSortentsTable(first.Owner.Record, new[] { new DxfSortOrderEntry(first, "FFFFFFFFFFFFFFFF"), new DxfSortOrderEntry(second, "FFFFFFFFFFFFFFFF") });
        var allocated = new Line(); doc.Entities.Add(allocated); Check(Convert.ToInt64(allocated.Handle, 16) < 0xABCD, "Opaque sort key inflated allocation.");
        table.Entries[0] = new DxfSortOrderEntry(first, "7FFFFFFFFFFFFFFF");
        table.Entries[1] = new DxfSortOrderEntry(second, "abcdef");
        Equal("abcdef", table.Entries[1].SortHandle, "literal sort key casing");
        table.Entries.Add(new DxfSortOrderEntry(allocated, "FFFFFFFFFFFFFFFF"));
        Throws<ArgumentException>(() => new DxfSortOrderEntry(first, "0x12"));
        foreach (bool binary in new[] { false, true })
        {
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "maximum virtual sort key save");
            Check(System.Text.Encoding.ASCII.GetString(output.ToArray()).Contains("abcdef", StringComparison.Ordinal), "Authored lexical key spelling changed on output.");
            output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Maximum sort key load failed.");
            Check(loaded.Objects.Items.OfType<DxfSortentsTable>().Single().Entries.Select(e => e.SortHandle).SequenceEqual(new[] { "7FFFFFFFFFFFFFFF", "ABCDEF", "FFFFFFFFFFFFFFFF" }), "Virtual sort key magnitude or normalized spelling changed.");
        }
        Throws<ArgumentException>(() => table.Entries.Add(new DxfSortOrderEntry(first, "0")));
        seed = doc.DrawingVariables.HandleSeed; Throws<ArgumentOutOfRangeException>(() => table.Entries[99] = new DxfSortOrderEntry(first, "ABCDEF")); Equal(seed, doc.DrawingVariables.HandleSeed, "bad index changed allocator");
        table.Entries.RemoveAt(0); table.Entries.Add(new DxfSortOrderEntry(first, "0")); table.Entries.Clear(); table.Entries.Add(new DxfSortOrderEntry(first, "0"));
        Check(doc.DrawingVariables.TryGetCustomVariable("$SORTENTS", out var sorting), "sorting flags"); Equal((short)18, (short)sorting.Value, "sorting flag preservation");
    }
    private static IEnumerable<DxfSortOrderEntry> FailingSortEntries(EntityObject entity) { yield return new DxfSortOrderEntry(entity, "1"); throw new IOException("fixture enumeration"); }
    private static void TypedContainerVersionGate()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2000); var line = new Line(); doc.Entities.Add(line);
        Throws<NotSupportedException>(() => doc.Objects.CreateSortentsTable(line.Owner.Record, Array.Empty<DxfSortOrderEntry>()));
        using var input = File.OpenRead(Path.Combine(ContainerFixtureDirectory, "compatibility-probe", "sortentstable-R2000.dxf"));
        var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("R2000 compatibility probe load failed.");
        Equal(1, loaded.Objects.Items.OfType<DxfSortentsTable>().Count(), "R2000 inspectable table"); Check(loaded.Objects.Validate().Count > 0, "R2000 export gate absent");
        using var output = new MemoryStream(); CheckSaveRejected(loaded, output); Equal(0L, output.Length, "version gate wrote output");
        loaded.DrawingVariables.AcadVer = DxfVersion.AutoCad2004; Equal(0, loaded.Objects.Validate().Count, "promoted table"); Check(loaded.Save(output), "promoted sort save");
    }
    private static void TypedContainerClassPreflight()
    {
        var doc = BuildTypedContainers(DxfVersion.AutoCad2018);
        doc.Classes.Add(new DxfClass("IDBUFFER", "WrongCppClass", "ObjectDBX Classes"));
        using var output = new MemoryStream(); CheckSaveRejected(doc, output); Equal(0L, output.Length, "class mismatch wrote output");
    }
    private static void TypedContainerSpatialAttachmentRetry()
    {
        for (int existing = 0; existing < 3; existing++)
        for (int failure = 0; failure < 2; failure++)
        {
            var doc = new DxfDocument(); var insert = new Insert(new Block("CLIPPED")); doc.Entities.Add(insert);
            if (existing != 0)
            {
                var extension = new DxfDictionary();
                if (existing == 2) extension.Add("ACAD_FILTER", new DxfDictionary());
                else extension.Add("APP", new DxfDictionaryVariable());
                doc.Objects.SetExtensionDictionary(insert, extension);
            }
            var previousExtension = insert.ExtensionDictionary; var filter = new DxfSpatialFilter();
            var badReactor = new DxfXRecord();
            if (failure == 0) filter.PersistentReactors.Add(badReactor);
            else
            {
                var data = new XData(new ApplicationRegistry("BAD_RESERVATION")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "7FFFFFFFFFFFFFFF")); filter.XData.Add(data);
            }
            int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
            Throws<ArgumentException>(() => doc.Objects.SetSpatialFilter(insert, filter));
            Check(filter.Owner == null && filter.Database == null, "Failed attachment stranded the supplied filter.");
            Check(ReferenceEquals(previousExtension, insert.ExtensionDictionary), "Failed attachment changed extension identity.");
            Equal(failure == 0 ? 1 : 0, filter.PersistentReactors.Count, "Failed attachment retained automatic reactor");
            Equal(count, doc.Objects.Items.Count, "Failed attachment registered objects"); Equal(seed, doc.DrawingVariables.HandleSeed, "Failed attachment allocated handles");
            Equal(0, doc.Objects.Validate().Count, "Failed attachment changed source graph");
            if (failure == 0) filter.PersistentReactors.Remove(badReactor); else filter.XData.Remove("BAD_RESERVATION");
            doc.Objects.SetSpatialFilter(insert, filter); Check(ReferenceEquals(filter, Spatial(insert)), "Corrected attachment retry failed.");
            Equal(0, doc.Objects.Validate().Count, "Retried attachment graph");
        }
    }
    private static void TypedContainerMappingCallbacks()
    {
        var source = new DxfDocument(); var graph = new DxfDictionary(); graph.Add("VALUE", new DxfDictionaryVariable()); source.NamedObjects.Add("SOURCE", graph);
        var target = new DxfDocument(); int afterCallback = 0; string callbackSeed = "";
        var installed = new DxfDictionary(); installed.Add("CALLBACK", new DxfDictionaryVariable());
        var mappings = new ContainerCallbackMappings(() => { target.NamedObjects.Add("COPY", installed); afterCallback = target.Objects.Items.Count; callbackSeed = target.DrawingVariables.HandleSeed; });
        Throws<ArgumentException>(() => target.Objects.Clone(graph, target.NamedObjects, "COPY", mappings));
        Equal(afterCallback, target.Objects.Items.Count, "callback name collision leaked clone objects"); Equal(callbackSeed, target.DrawingVariables.HandleSeed, "callback name collision allocated clone handles");
        Check(ReferenceEquals(installed, target.NamedObjects["COPY"]), "Callback dictionary replaced.");
        var sourceLine = new Line(); source.Entities.Add(sourceLine); var sourceExtension = new DxfDictionary(); sourceExtension.Add("DATA", new DxfXRecord()); source.Objects.SetExtensionDictionary(sourceLine, sourceExtension);
        var targetLine = new Line(); target.Entities.Add(targetLine); var callbackExtension = new DxfDictionary(); callbackExtension.Add("CALLBACK", new DxfXRecord());
        mappings = new ContainerCallbackMappings(() => { target.Objects.SetExtensionDictionary(targetLine, callbackExtension); afterCallback = target.Objects.Items.Count; callbackSeed = target.DrawingVariables.HandleSeed; });
        Throws<InvalidOperationException>(() => target.Objects.CloneExtensionDictionary(sourceLine, targetLine, mappings));
        Equal(afterCallback, target.Objects.Items.Count, "callback extension collision leaked objects"); Equal(callbackSeed, target.DrawingVariables.HandleSeed, "callback extension collision allocated handles");
        Check(ReferenceEquals(callbackExtension, targetLine.ExtensionDictionary), "Callback extension replaced."); Equal(0, target.Objects.Validate().Count, "callback destination graph");
    }
    private sealed class ContainerCallbackMappings : IReadOnlyDictionary<DxfObject, DxfObject>
    {
        private readonly Action callback;
        internal ContainerCallbackMappings(Action callback) { this.callback = callback; }
        public int Count => 0;
        public IEnumerable<DxfObject> Keys => Array.Empty<DxfObject>();
        public IEnumerable<DxfObject> Values => Array.Empty<DxfObject>();
        public DxfObject this[DxfObject key] => throw new KeyNotFoundException();
        public bool ContainsKey(DxfObject key) => false;
        public bool TryGetValue(DxfObject key, out DxfObject value) { value = null!; return false; }
        public IEnumerator<KeyValuePair<DxfObject, DxfObject>> GetEnumerator() { this.callback(); return Enumerable.Empty<KeyValuePair<DxfObject, DxfObject>>().GetEnumerator(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
    private static void TypedContainerMalformed(int scenario)
    {
        using var input = File.OpenRead(Path.Combine(ContainerFixtureDirectory, "independent-typed-containers-R2018.dxf"));
        var raw = DxfRawDocument.Load(input); var store = DxfRawObjectStore.Open(raw);
        string handle = store.Objects.First(o => o.TypeName == (scenario == 0 ? "IDBUFFER" : scenario == 1 || scenario == 2 ? "SORTENTSTABLE" : "SPATIAL_FILTER")).Handle;
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int body = tags.FindLastIndex(t => t.Code == 100) + 1;
            if (scenario == 0) tags[body] = new DxfTag(330, "FFFFFFF");
            else if (scenario == 1) tags.RemoveAt(tags.FindIndex(body, t => t.Code == 5));
            else if (scenario == 2) tags[body] = new DxfTag(330, "0");
            else if (scenario == 3) tags[tags.FindIndex(body, t => t.Code == 70)] = new DxfTag(70, (short)123);
            else if (scenario == 4) tags[tags.FindIndex(body, t => t.Code == 72)] = new DxfTag(72, (short)2);
            else if (scenario == 5) tags.RemoveAt(tags.FindLastIndex(t => t.Code == 40));
            else if (scenario == 6) tags.RemoveAt(tags.FindIndex(body, t => t.Code == 220));
            else tags.Insert(body, new DxfTag(41, 3.5));
            return tags;
        });
        using var corrupted = new MemoryStream(); raw.Save(corrupted); corrupted.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(corrupted) == null; } catch (FormatException) { rejected = true; } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Malformed typed container accepted.");
    }
}
