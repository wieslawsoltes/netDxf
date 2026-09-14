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
    private static readonly DxfVersion[] SatVersions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004, DxfVersion.AutoCad2007, DxfVersion.AutoCad2010 };
    private static readonly string[] SatKinds = { "BODY", "REGION", "3DSOLID" };

    private static void RegisterAcisSatTests()
    {
        Run("acis-sat/model-codec-transaction", AcisSatModel);
        Run("acis-sat/clone-transform-ownership", AcisSatClone);
        foreach (bool binary in new[] { false, true })
        foreach (bool existing in new[] { false, true })
        foreach (int failure in Enumerable.Range(0, 3))
        {
            bool b = binary, e = existing; int f = failure;
            Run($"acis-sat/atomic-save/{b}/{e}/{f}", () => AcisSatAtomicSave(b, e, f));
        }
        foreach (DxfVersion version in SatVersions)
        foreach (string kind in SatKinds)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; string k = kind; bool b = binary;
            Run($"acis-sat/wire/{v}/{k}/{b}", () => AcisSatRoundTrip(v, k, b));
            foreach (string sourceTransport in new[] { "ascii", "binary" })
            {
                string transport = sourceTransport;
                Run($"acis-sat/independent/{v}/{k}/{transport}/{b}", () => AcisSatIndependent(v, k, transport, b));
            }
        }
        foreach (string kind in SatKinds)
        foreach (bool binary in new[] { false, true })
        {
            string k = kind; bool b = binary;
            foreach (int failure in Enumerable.Range(0, kind == "3DSOLID" ? 20 : 16))
            {
                int f = failure;
                Run($"acis-sat/malformed/{k}/{b}/{f}", () => AcisSatMalformed(k, b, f));
            }
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            foreach (int placement in Enumerable.Range(0, 4))
            {
                int p = placement;
                Run($"acis-sat/version-preflight/{v}/{b}/{p}", () => AcisSatVersionGate(v, b, p));
            }
        }
        foreach (bool binary in new[] { false, true })
        foreach (int failure in Enumerable.Range(0, 2))
        {
            bool b = binary; int f = failure;
            Run($"acis-sat/input-bounds/{b}/{f}", () => AcisSatInputBounds(b, f));
        }
    }

    private static AcisEntity AcisSatCreate(string kind) => kind switch
    { "BODY" => new Body(), "REGION" => new Region(), _ => new Solid3D() };

    private static void AcisSatEqual(AcisEntity expected, AcisEntity actual)
    {
        Equal(expected.Type, actual.Type, "ACIS entity type");
        Equal((short)1, actual.ModelerFormatVersion, "ACIS modeler format version");
        Check(expected.EncodedSatChunks.Select(c => (c.GroupCode, c.Text)).SequenceEqual(actual.EncodedSatChunks.Select(c => (c.GroupCode, c.Text))), "Exact SAT encoded chunks changed.");
        Check(expected.SatLines.SequenceEqual(actual.SatLines), "Decoded SAT lines changed.");
        if (expected is Solid3D a && actual is Solid3D b) Equal(a.HistoryHandle, b.HistoryHandle, "History presence changed");
    }

    private static void AcisSatModel()
    {
        var entity = new Body();
        string printable = new(Enumerable.Range(32, 95).Select(c => (char)c).ToArray());
        string[] lines = { printable, "  leading and trailing  ", "", new string('0', 254) + "A" + new string('C', 260), "700 0 1 0" };
        entity.SetSatLines(lines);
        Check(lines.SequenceEqual(entity.SatLines), "SAT printable codec roundtrip.");
        Check(entity.EncodedSatChunks.Any(c => c.GroupCode == 3), "SAT long line not chunked.");
        Check(entity.EncodedSatChunks.Any(c => c.Text.EndsWith('^')), "A escape did not cross chunk boundary.");
        var snapshot = entity.EncodedSatChunks;
        var decodedSnapshot = entity.SatLines;
        Throws<ArgumentException>(() => entity.SetSatLines(new[] { "valid", "invalid\nline" }));
        Throws<ArgumentException>(() => entity.SetSatLines(new[] { "valid", "caf\u00e9" }));
        Throws<ArgumentException>(() => entity.SetSatLines(new string[] { "valid", null! }));
        Throws<ArgumentNullException>(() => entity.SetSatLines(null!));
        Throws<ArgumentNullException>(() => entity.SetEncodedSatChunks(null!));
        Throws<ArgumentException>(() => entity.SetEncodedSatChunks(new[] { new AcisSatChunk(3, "orphan") }));
        Throws<ArgumentException>(() => entity.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "valid"), null! }));
        Throws<ArgumentException>(() => entity.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "^") }));
        Throws<ArgumentException>(() => entity.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "^x") }));
        Throws<ArgumentOutOfRangeException>(() => new AcisSatChunk(2, "x"));
        Throws<ArgumentNullException>(() => new AcisSatChunk(1, null!));
        Throws<ArgumentOutOfRangeException>(() => new AcisSatChunk(1, new string('x', 256)));
        Throws<ArgumentException>(() => new AcisSatChunk(1, "\0"));
        Throws<ArgumentException>(() => new AcisSatChunk(1, "\u007f"));
        Throws<ArgumentOutOfRangeException>(() => entity.SetSatLines(new[] { new string('A', AcisEntity.MaximumSatLineCharacters) }));
        Throws<ArgumentOutOfRangeException>(() => entity.SetEncodedSatChunks(Enumerable.Repeat(new AcisSatChunk(1, ""), AcisEntity.MaximumSatChunks + 1)));
        Throws<ArgumentOutOfRangeException>(() => entity.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "x") }.Concat(Enumerable.Repeat(new AcisSatChunk(3, new string('x', 255)), AcisEntity.MaximumSatLineCharacters / 255 + 1))));
        Throws<InvalidOperationException>(() => entity.SetEncodedSatChunks(AcisFailingEnumeration()));
        Check(ReferenceEquals(snapshot, entity.EncodedSatChunks) && ReferenceEquals(decodedSnapshot, entity.SatLines), "Failed SAT setters published partial data.");
        Throws<NotSupportedException>(() => ((IList<AcisSatChunk>)snapshot)[0] = new AcisSatChunk(1, ""));
        var chunks = new List<AcisSatChunk> { new(1, "\\U+0041"), new(3, " "), new(1, "^") , new(3, " ") };
        entity.SetEncodedSatChunks(chunks); chunks.Clear();
        Equal(4, entity.EncodedSatChunks.Count, "SAT setter retained mutable collection");
        Check(lines.SequenceEqual(decodedSnapshot), "Old SAT snapshot changed.");
        entity.SetSatLines(new[] { new string('0', AcisEntity.MaximumSatLineCharacters) });
        Equal(AcisEntity.MaximumSatLineCharacters, entity.SatLines.Single().Length, "Legal SAT line boundary rejected");
        entity.SetEncodedSatChunks(Enumerable.Repeat(new AcisSatChunk(1, ""), AcisEntity.MaximumSatChunks));
        Equal(AcisEntity.MaximumSatChunks, entity.EncodedSatChunks.Count, "Legal SAT chunk-count boundary rejected");
        entity.SetSatLines(Array.Empty<string>()); Equal(0, entity.EncodedSatChunks.Count, "SAT clear");
        var solid = new Solid3D { HistoryHandle = "0" };
        Throws<NotSupportedException>(() => solid.HistoryHandle = "FF");
        Throws<NotSupportedException>(() => solid.HistoryHandle = "");
        Equal("0", solid.HistoryHandle, "Failed history setter mutation"); solid.HistoryHandle = null!;
    }

    private static XData AcisSatXData(string app, string value)
    { var data = new XData(new ApplicationRegistry(app)); data.XDataRecord.Add(new XDataRecord(XDataCode.String, value)); return data; }

    private static IEnumerable<AcisSatChunk> AcisFailingEnumeration()
    { yield return new AcisSatChunk(1, "valid"); throw new InvalidOperationException("caller enumeration failed"); }

    private static void AcisSatClone()
    {
        foreach (string kind in SatKinds)
        {
            var original = AcisSatCreate(kind); original.SetSatLines(new[] { "700 0 1 0", "opaque" });
            original.ColorName = "QA$SAT"; original.ShadowMode = EntityShadowMode.Ignore; original.ProxyGraphics = new byte[] { 0, 1, 255 };
            original.XData.Add(AcisSatXData("SAT_CLONE", "unchanged"));
            if (original is Solid3D solid) solid.HistoryHandle = "0";
            var block = new Block("ACIS_" + kind); block.Entities.Add(original);
            var doc = new DxfDocument(DxfVersion.AutoCad2010); var insert = new Insert(block); doc.Entities.Add(insert);
            var clone = (AcisEntity)original.Clone(); AcisSatEqual(original, clone);
            Check(clone.Handle == null && clone.Owner == null && clone.Reactors.Count == 0, "ACIS clone retained identity.");
            Equal(original.ColorName, clone.ColorName, "ACIS clone color name"); Equal(original.ShadowMode, clone.ShadowMode, "ACIS clone shadow");
            clone.ProxyGraphics = new byte[] { 8 }; Equal(3, original.ProxyGraphics.Length, "ACIS clone proxy alias");
            clone.SetSatLines(new[] { "changed" }); Equal("opaque", original.SatLines[1], "ACIS clone SAT alias");
            Check(!ReferenceEquals(original.XData["SAT_CLONE"], clone.XData["SAT_CLONE"]), "ACIS clone XData alias.");
            var clonedInsert = (Insert)insert.Clone(); AcisSatEqual(original, clonedInsert.Block.Entities.OfType<AcisEntity>().Single());
            AcisSatEqual(original, insert.Explode().OfType<AcisEntity>().Single());
            original.TransformBy(Matrix3.Identity, Vector3.Zero);
            EntityObject baseTyped = original;
            baseTyped.TransformBy(Matrix4.Identity);
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            foreach (double delta in new[] { double.Epsilon, 1e-20, 1e-8, double.NaN, double.PositiveInfinity })
            {
                Matrix4 invalid = Matrix4.Identity; invalid[row, column] += delta;
                if (invalid[row, column] == Matrix4.Identity[row, column]) continue; // Sub-ULP additions to a diagonal 1 are unchanged inputs.
                Throws<NotSupportedException>(() => baseTyped.TransformBy(invalid));
            }
            Throws<NotSupportedException>(() => original.TransformBy(Matrix3.Identity, new Vector3(1e-15, 0, 0)));
            Throws<NotSupportedException>(() => original.TransformBy(Matrix3.Scale(2), Vector3.Zero));
            Throws<NotSupportedException>(() => original.TransformBy(new Matrix3(1, 1e-15, 0, 0, 1, 0, 0, 0, 1), Vector3.Zero));
            Throws<NotSupportedException>(() => original.TransformBy(Matrix3.Identity, new Vector3(double.NaN, 0, 0)));
            insert.Position = Vector3.UnitX; Throws<NotSupportedException>(() => insert.Explode());
            Equal("opaque", original.SatLines[1], "Rejected transform changed SAT");
            Check(block.Entities.Remove(original), "ACIS block removal failed.");
            Check(original.Owner == null, "ACIS removal retained owner.");
        }
    }

    private static void AcisSatRoundTrip(DxfVersion version, string kind, bool binary)
    {
        foreach (bool historyPresent in new[] { false, true })
        {
            var original = AcisSatCreate(kind);
            original.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "\\U+0041  "), new AcisSatChunk(3, " "), new AcisSatChunk(1, new string('0', 254) + "^"), new AcisSatChunk(3, " "), new AcisSatChunk(1, "") });
            original.ColorName = version >= DxfVersion.AutoCad2004 ? "QA$SAT" : null;
            original.ShadowMode = version >= DxfVersion.AutoCad2007 ? EntityShadowMode.CastAndReceive : null;
            original.ProxyGraphics = new byte[] { 0, 10, 13, 255 };
            original.XData.Add(AcisSatXData("SAT_WIRE", "after chunks"));
            if (original is Solid3D solid && version >= DxfVersion.AutoCad2007 && historyPresent) solid.HistoryHandle = "0";
            var doc = new DxfDocument(version); doc.Entities.Add(original); doc.Entities.Add(new Line(new Vector3(10, 20, 30), new Vector3(40, 50, 60)));
            for (int cycle = 0; cycle < 3; cycle++)
            {
                using var output = new MemoryStream(); Check(doc.Save(output, cycle % 2 == 0 ? binary : !binary), "ACIS SAT save failed.");
                output.Position = 0; var raw = DxfRawDocument.Load(output);
                var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == kind);
                int start = record.Tags.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbModelerGeometry"));
                var tags = record.Tags.Skip(start + 1).TakeWhile(t => t.Code != 100 && t.Code < 1000).Where(t => t.Code == 1 || t.Code == 3);
                Check(tags.Select(t => (t.Code, (string)t.Value)).SequenceEqual(original.EncodedSatChunks.Select(c => (c.GroupCode, c.Text))), "Wire SAT changed encoded text/boundaries.");
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("ACIS SAT reload failed.");
                var reloaded = doc.Blocks.SelectMany(b => b.Entities).OfType<AcisEntity>().Single(); AcisSatEqual(original, reloaded);
                Equal(original.ColorName, reloaded.ColorName, "ACIS color name"); Equal(original.ShadowMode, reloaded.ShadowMode, "ACIS shadow");
                Check(original.ProxyGraphics.SequenceEqual(reloaded.ProxyGraphics), "ACIS proxy bytes changed.");
                Equal("after chunks", (string)reloaded.XData["SAT_WIRE"].XDataRecord.Single().Value, "ACIS XData");
                Equal(new Vector3(10, 20, 30), doc.Entities.Lines.Single().StartPoint, "ACIS following LINE");
                Equal(1, kind == "BODY" ? doc.Entities.Bodies.Count() : kind == "REGION" ? doc.Entities.Regions.Count() : doc.Entities.Solids3D.Count(), "ACIS typed collection");
            }
        }
    }

    private static void AcisSatIndependent(DxfVersion version, string kind, string sourceTransport, bool binary)
    {
        string name = $"independent-acis-{kind.ToLowerInvariant()}-R{version.ToString().Replace("AutoCad", "")}-{sourceTransport}";
        var doc = DxfDocument.Load(Path.Combine("tests", "fixtures", "acis-sat", name + ".dxf")) ?? throw new InvalidOperationException("Independent ACIS load failed.");
        var expected = doc.Blocks.SelectMany(b => b.Entities).OfType<AcisEntity>().Single();
        Check(expected.EncodedSatChunks.Any(c => c.GroupCode == 3), "Independent continuation missing.");
        Check(expected.EncodedSatChunks.Any(c => c.Text.Contains("\\U+0041", StringComparison.Ordinal)), "Independent literal Unicode-like escape missing.");
        Check(expected.SatLines[0].StartsWith("700 ", StringComparison.Ordinal), "SAT content version lost.");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Independent ACIS save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name + (binary ? "-roundtrip-binary.dxf" : "-roundtrip-ascii.dxf")), output.ToArray());
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Independent ACIS reload failed.");
        AcisSatEqual(expected, loaded.Blocks.SelectMany(b => b.Entities).OfType<AcisEntity>().Single());
        Equal(1, loaded.Entities.Lines.Count(), "Independent ACIS following LINE");
    }

    private static List<DxfTag> AcisSatTags(string kind, DxfVersion version = DxfVersion.AutoCad2010) => new()
    {
        new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)), new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
        new(0, "SECTION"), new(2, "ENTITIES"), new(0, kind), new(5, "210"), new(100, "AcDbEntity"), new(8, "0"),
        new(100, "AcDbModelerGeometry"), new(70, (short)1), new(1, "h o n o"), new(3, " "),
        new(0, "ENDSEC"), new(0, "EOF")
    };

    private static void AcisSatReject(List<DxfTag> tags, bool binary, bool unsupported = false)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        if (unsupported) Throws<NotSupportedException>(() => DxfDocument.Load(input));
        else Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed/unsupported ACIS input accepted.");
#endif
        Check(input.CanRead, "ACIS load closed caller stream.");
    }

    private static void AcisSatMalformed(string kind, bool binary, int failure)
    {
        var tags = AcisSatTags(kind); int start = tags.FindIndex(t => t.Code == 70); bool unsupported = false;
        switch (failure)
        {
            case 0: tags.RemoveAt(start); break;
            case 1: tags.Insert(start, new(70, (short)1)); break;
            case 2: tags[start] = new(70, (short)2); break;
            case 3: tags.RemoveRange(start + 1, 2); break;
            case 4: tags[start + 1] = new(3, "orphan"); break;
            case 5: tags[start + 1] = new(1, new string('x', 256)); break;
            case 6: tags[start + 1] = new(1, "^"); tags.RemoveAt(start + 2); break;
            case 7: tags[start + 1] = new(1, "^x"); break;
            case 8: tags.Insert(start + 1, new(310, new byte[] { 1 })); break;
            case 9: tags[start - 1] = new(100, "PrivateModeler"); break;
            case 10: tags.Insert(start + 1, new(350, "0")); break;
            case 11: tags.Insert(start + 1, new(100, "AcDb3dSolid")); break;
            case 12: tags.Insert(start + 2, new(1001, "EARLY_SAT")); break;
            case 13: tags.Insert(start + 3, new(100, "AcDb3dSolid")); tags.Insert(start + 4, new(1, "late")); break;
            case 14:
                if (kind == "3DSOLID") { tags.Insert(start + 3, new(100, "AcDb3dSolid")); tags.Insert(start + 4, new(350, "FF")); unsupported = true; }
                else tags.Insert(start + 3, new(100, "AcDb3dSolid"));
                break;
            case 15: tags[3] = new(1, "AC1027"); unsupported = true; break;
            case 16: tags.Insert(start + 3, new(100, "AcDb3dSolid")); tags.Insert(start + 4, new(100, "AcDb3dSolid")); break;
            case 17: tags.Insert(start + 3, new(100, "AcDb3dSolid")); tags.Insert(start + 4, new(350, "0")); tags.Insert(start + 5, new(350, "0")); break;
            case 18: tags.Insert(start + 3, new(100, "AcDb3dSolid")); tags.Insert(start + 4, new(70, (short)1)); break;
            case 19: tags[3] = new(1, "AC1018"); tags.Insert(start + 3, new(100, "AcDb3dSolid")); break;
        }
        AcisSatReject(tags, binary, unsupported);
    }

    private static void AcisSatVersionGate(DxfVersion version, bool binary, int placement)
    {
        foreach (string kind in SatKinds)
        {
            var entity = AcisSatCreate(kind); entity.SetSatLines(new[] { "opaque" });
            var doc = new DxfDocument(version);
            switch (placement)
            {
                case 0: doc.Entities.Add(entity); break;
                case 1: doc.Layouts.Add(new Layout("SAT_PAPER")); doc.Entities.ActiveLayout = "SAT_PAPER"; doc.Entities.Add(entity); doc.Entities.ActiveLayout = "Model"; break;
                case 2: var inner = new Block("SAT_INNER"); inner.Entities.Add(entity); var outer = new Block("SAT_OUTER"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
                default: var unused = new Block("SAT_UNUSED"); unused.Entities.Add(entity); doc.Blocks.Add(unused); break;
            }
            using var output = new MemoryStream();
            if (version >= DxfVersion.AutoCad2013)
            {
                if (placement == 0) AcisSatReject(AcisSatTags(kind, version), binary, true);
#if DEBUG
                Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
                Check(!doc.Save(output, binary), "SAB profile accepted SAT data.");
#endif
                Equal(0L, output.Length, "Unsupported SAT profile wrote partial bytes");
            }
            else
            {
                Check(doc.Save(output, binary), "SAT placement save failed."); output.Position = 0;
                var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("SAT placement load failed.");
                AcisSatEqual(entity, loaded.Blocks.SelectMany(b => b.Entities).OfType<AcisEntity>().Single());
                if (kind == "3DSOLID" && version < DxfVersion.AutoCad2007)
                {
                    ((Solid3D)entity).HistoryHandle = "0"; output.SetLength(0);
#if DEBUG
                    Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
                    Check(!doc.Save(output, binary), "Unqualified history version accepted.");
#endif
                    Equal(0L, output.Length, "History preflight wrote partial bytes"); ((Solid3D)entity).HistoryHandle = null!;
                }
                entity.SetSatLines(Array.Empty<string>()); output.SetLength(0);
#if DEBUG
                Throws<InvalidOperationException>(() => doc.Save(output, binary));
#else
                Check(!doc.Save(output, binary), "Empty SAT payload accepted.");
#endif
                Equal(0L, output.Length, "Empty SAT preflight wrote partial bytes");
            }
            Check(output.CanWrite, "SAT preflight closed caller stream.");
        }
    }

    private static void AcisSatAtomicSave(bool binary, bool existing, int failure) => WithAtomicDirectory(path =>
    {
        AtomicPrepare(path, existing);
        var doc = new DxfDocument(failure == 1 ? DxfVersion.AutoCad2018 : failure == 2 ? DxfVersion.AutoCad2004 : DxfVersion.AutoCad2010) { Name = "SAT original" };
        var solid = new Solid3D();
        if (failure != 0) solid.SetSatLines(new[] { "opaque" });
        if (failure == 2) solid.HistoryHandle = "0";
        doc.Entities.Add(solid);
        string previousFolder = doc.SupportFolders.WorkingFolder, previousSeed = doc.DrawingVariables.HandleSeed;
        string? previousHandle = solid.Handle;
        if (failure == 0) Throws<InvalidOperationException>(() => doc.SaveAtomic(path, binary));
        else Throws<NotSupportedException>(() => doc.SaveAtomic(path, binary));
        AtomicUnchanged(path, existing);
        Equal("SAT original", doc.Name, "Rejected atomic SAT changed document name");
        Equal(previousFolder, doc.SupportFolders.WorkingFolder, "Rejected atomic SAT changed working folder");
        Equal(previousSeed, doc.DrawingVariables.HandleSeed, "Rejected atomic SAT changed allocator seed");
        Equal(previousHandle, solid.Handle, "Rejected atomic SAT changed entity handle");
        Check(ReferenceEquals(solid, doc.Entities.Solids3D.Single()), "Rejected atomic SAT changed entity membership.");
    });

    private static void AcisSatInputBounds(bool binary, int failure)
    {
        var tags = AcisSatTags("BODY"); int start = tags.FindIndex(t => t.Code == 70) + 1;
        tags.RemoveRange(start, 2);
        if (failure == 0) tags.InsertRange(start, Enumerable.Repeat(new DxfTag(1, ""), AcisEntity.MaximumSatChunks + 1));
        else { tags.Insert(start++, new(1, "x")); tags.InsertRange(start, Enumerable.Repeat(new DxfTag(3, new string('x', 255)), AcisEntity.MaximumSatLineCharacters / 255 + 1)); }
        AcisSatReject(tags, binary);
    }
}
