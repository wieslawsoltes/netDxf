using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Vector3[] PolyfaceGrammarPoints =
        { new(11, 12, 13), new(21, 22, 23), new(31, 32, 33), new(41, 42, 43) };

    private static void RegisterPolyfaceGrammarTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            foreach (int variant in Enumerable.Range(0, 8))
                Run($"polyface/grammar/{version}/{binary}/{variant}", () => PolyfaceGrammarRoundTrip(version, binary, variant));
            foreach (int fault in Enumerable.Range(0, 7))
                Run($"polyface/malformed/{version}/{binary}/{fault}", () => PolyfaceGrammarMalformed(version, binary, fault));
            foreach (int placement in Enumerable.Range(0, 4))
                Run($"polyface/preflight/{version}/{binary}/{placement}", () => PolyfaceGrammarPreflight(version, binary, placement));
            Run($"polyface/authoring/{version}/{binary}", () => PolyfaceGrammarAuthoring(version, binary));
            Run($"polyface/clone/{version}/{binary}", () => PolyfaceGrammarClone(version, binary));
            foreach (int context in Enumerable.Range(0, 7))
                Run($"polyface/context/{version}/{binary}/{context}", () => PolyfaceGrammarContext(version, binary, context));
        }
        foreach (int fault in Enumerable.Range(0, 7))
            Run($"polyface/constructor/{fault}", () => PolyfaceGrammarConstructor(fault));
        foreach (bool binary in new[] { false, true })
            Run($"polyface/magnitude/{binary}", () => PolyfaceGrammarMagnitude(binary));
    }

    private static short[] PolyfaceGrammarExpected(int variant) => variant switch
    {
        0 => new short[] { -1 }, 1 => new short[] { 1, -2 },
        2 => new short[] { 1, -2, 3 }, 3 or 5 => new short[] { -1, 2, -3, 4 },
        4 => new short[] { 1 }, 6 => new short[] { 1, 2, 1 },
        _ => new short[] { 1, 2, 3 }
    };

    private static DxfRawDocument PolyfaceGrammarSeed(DxfVersion version)
    {
        var doc = new DxfDocument(version); doc.Comments.Clear();
        doc.Entities.Add(new PolyfaceMesh(PolyfaceGrammarPoints, new[] { new short[] { 1, 2, 3, 4 } }));
        doc.Entities.Add(new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)));
        using var stream = new MemoryStream(); Check(doc.Save(stream), "Polyface seed save"); stream.Position = 0;
        return DxfRawDocument.Load(stream);
    }

    private static bool PolyfaceGrammarIsFace(DxfRawRecord record) => record.Name == "VERTEX" &&
        record.Tags.Any(t => t.Code == 70 && ((short)t.Value & 192) == 128);

    private static DxfRawDocument PolyfaceGrammarInput(DxfVersion version, int variant)
    {
        var raw = PolyfaceGrammarSeed(version); var entities = raw.Sections.Single(s => s.Name == "ENTITIES");
        var face = entities.Records.Single(PolyfaceGrammarIsFace);
        var tags = face.Tags.Where(t => t.Code < 71 || t.Code > 74).ToList();
        short[] slots = variant switch
        {
            0 => new short[] { -1, 0, short.MaxValue, short.MinValue },
            1 => new short[] { 1, -2, 0, 4 }, 2 => new short[] { 1, -2, 3, 0 },
            3 or 5 => new short[] { -1, 2, -3, 4 }, 4 => new short[] { 1, 0, -4, 0 },
            6 => new short[] { 1, 2, 1, 0 }, _ => new short[] { 1, 2, 3, 0 }
        };
        foreach (int slot in variant == 5 ? new[] { 3, 1, 0, 2 } : new[] { 0, 1, 2, 3 })
            if (!(variant == 4 && (slot == 1 || slot == 3)) && !(variant == 7 && slot == 3))
                tags.Add(new DxfTag((short)(71 + slot), slots[slot]));
        raw = raw.WithRecord(face, tags);
        entities = raw.Sections.Single(s => s.Name == "ENTITIES");
        var header = entities.Records.Single(r => r.Name == "POLYLINE");
        tags = header.Tags.Where(t => t.Code != 71 && t.Code != 72).ToList();
        tags.Add(new DxfTag(71, (short)-17)); tags.Add(new DxfTag(72, (short)123));
        raw = raw.WithRecord(header, tags);
        if ((variant & 1) != 0)
        {
            entities = raw.Sections.Single(s => s.Name == "ENTITIES");
            face = entities.Records.Single(PolyfaceGrammarIsFace);
            var all = raw.Tags.ToList(); int faceStart = face.StartTagIndex;
            all.RemoveRange(faceStart, face.Tags.Count);
            var firstCoordinate = entities.Records.First(r => r.Name == "VERTEX" && !PolyfaceGrammarIsFace(r));
            int coordinateStart = firstCoordinate.StartTagIndex;
            all.InsertRange(coordinateStart, face.Tags); raw = raw.WithTags(all);
        }
        return raw;
    }

    private static DxfDocument PolyfaceGrammarLoad(DxfRawDocument raw, bool binary)
    {
        using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0;
        return DxfDocument.Load(stream) ?? throw new FormatException("Polyface fixture rejected");
    }

    private static void PolyfaceGrammarRoundTrip(DxfVersion version, bool binary, int variant)
    {
        var doc = PolyfaceGrammarLoad(PolyfaceGrammarInput(version, variant), binary);
        var mesh = doc.Entities.PolyfaceMeshes.Single(); short[] expected = PolyfaceGrammarExpected(variant);
        Check(expected.SequenceEqual(mesh.Faces.Single().VertexIndexes), "Face slot order, sign or terminator changed");
        Check(PolyfaceGrammarPoints.SequenceEqual(mesh.Vertexes), "Coordinate indexing changed under unusual record order");
        var exploded = mesh.Explode().Single();
        Equal(expected.Length == 1 ? EntityType.Point : expected.Length == 2 ? EntityType.Line : EntityType.Face3D, exploded.Type, "Exploded face arity");
        if (exploded is Face3D f)
        {
            Equal(PolyfaceGrammarPoints[Math.Abs(expected[0]) - 1], f.FirstVertex, "Exploded first vertex");
            Check(f.EdgeFlags.HasFlag(Face3DEdgeFlags.First) == (expected[0] < 0), "First invisible edge changed");
            Check(f.EdgeFlags.HasFlag(Face3DEdgeFlags.Second) == (expected[1] < 0), "Second invisible edge changed");
            Check(f.EdgeFlags.HasFlag(Face3DEdgeFlags.Third) == (expected[2] < 0), "Third invisible edge changed");
        }
        Equal(new Vector3(101, 102, 103), doc.Entities.Lines.Single().StartPoint, "Following record swallowed");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Polyface output save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"polyface-grammar-{version}-{binary}-{variant}.dxf"), stream.ToArray());
        stream.Position = 0; var again = DxfDocument.Load(stream) ?? throw new FormatException("Polyface output reload");
        Check(expected.SequenceEqual(again.Entities.PolyfaceMeshes.Single().Faces.Single().VertexIndexes), "Saved face topology changed");
    }

    private static void PolyfaceGrammarMalformed(DxfVersion version, bool binary, int fault)
    {
        var raw = PolyfaceGrammarSeed(version); var entities = raw.Sections.Single(s => s.Name == "ENTITIES");
        var face = entities.Records.Single(PolyfaceGrammarIsFace); var tags = face.Tags.Where(t => t.Code < 71 || t.Code > 74).ToList();
        if (fault <= 4)
        {
            if (fault != 0) tags.Add(new DxfTag(71, fault == 1 ? (short)0 : fault == 2 ? (short)5 : fault == 3 ? (short)-5 : short.MinValue));
            tags.Add(new DxfTag(72, (short)2)); raw = raw.WithRecord(face, tags);
        }
        else if (fault == 5)
        {
            tags.Add(new DxfTag(71, (short)1)); tags.Add(new DxfTag(71, (short)1));
            raw = raw.WithRecord(face, tags);
        }
        else
        {
            var all = raw.Tags.ToList(); int start = face.StartTagIndex; all.RemoveRange(start, face.Tags.Count); raw = raw.WithTags(all);
        }
        bool rejected = false;
        try { _ = PolyfaceGrammarLoad(raw, binary); } catch (FormatException) { rejected = true; }
        Check(rejected, "Malformed public polyface accepted");
    }

    private static void PolyfaceGrammarPreflight(DxfVersion version, bool binary, int placement)
    {
        var doc = new DxfDocument(version); var mesh = new PolyfaceMesh(PolyfaceGrammarPoints, new[] { new short[] { 1, 2, 3 } });
        if (placement == 0) doc.Entities.Add(mesh);
        else if (placement == 1)
        { doc.Layouts.Add(new netDxf.Objects.Layout("FacePaper")); doc.Entities.ActiveLayout = "FacePaper"; doc.Entities.Add(mesh); doc.Entities.ActiveLayout = "Model"; }
        else
        {
            var block = new Block("FaceBlock"); block.Entities.Add(mesh);
            if (placement == 2) { var outer = new Block("FaceOuter"); outer.Entities.Add(new Insert(block)); doc.Entities.Add(new Insert(outer)); }
            else doc.Blocks.Add(block);
        }
        mesh.Faces[0].VertexIndexes[0] = placement == 0 ? (short)0 : placement == 1 ? short.MinValue : placement == 2 ? (short)5 : (short)-5;
        using var stream = new MemoryStream(); byte[] sentinel = { 71, 72, 73, 74, 0 }; stream.Write(sentinel); stream.Position = 2;
        string seed = doc.DrawingVariables.HandleSeed; int layouts = doc.Layouts.Count, apps = doc.ApplicationRegistries.Count;
        bool rejected = false; try { rejected = !doc.Save(stream, binary); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Mutated invalid face was saved"); Check(sentinel.SequenceEqual(stream.ToArray()), "Preflight changed stream bytes");
        Equal(2L, stream.Position, "Preflight advanced stream"); Equal(seed, doc.DrawingVariables.HandleSeed, "Preflight allocated handles");
        Equal(layouts, doc.Layouts.Count, "Preflight added layouts"); Equal(apps, doc.ApplicationRegistries.Count, "Preflight added applications");
    }

    private static void PolyfaceGrammarAuthoring(DxfVersion version, bool binary)
    {
        var placeholder = new PolyfaceMeshFace(); placeholder.VertexIndexes[0] = 1; placeholder.VertexIndexes[1] = -2; placeholder.VertexIndexes[2] = 3;
        var faces = new[] { new PolyfaceMeshFace(new short[] { 1, 0, short.MinValue, 5 }), new PolyfaceMeshFace(new short[] { -1, 2, 0, 5 }), placeholder };
        var mesh = new PolyfaceMesh(PolyfaceGrammarPoints, faces); var doc = new DxfDocument(version); doc.Entities.Add(mesh);
        Check(mesh.Explode().Select(e => e.Type).SequenceEqual(new[] { EntityType.Point, EntityType.Line, EntityType.Face3D }), "Authored terminators ignored by geometry");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Authored padded faces save"); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new FormatException("Authored padded faces reload");
        Check(loaded.Entities.PolyfaceMeshes.Single().Faces.Select(f => f.VertexIndexes.Length).SequenceEqual(new[] { 1, 2, 3 }), "Authored padding emitted as active indices");
    }

    private static void PolyfaceGrammarClone(DxfVersion version, bool binary)
    {
        var inherited = new PolyfaceMeshFace(new short[] { 1, -2, 3, 0 });
        var own = new PolyfaceMeshFace(new short[] { 1, 2, -3, 4 }) { Layer = new Layer("OriginalFace"), Color = AciColor.FromTrueColor(0x123456) };
        var faceClone = (PolyfaceMeshFace)inherited.Clone(); Check(faceClone.Layer == null && faceClone.Color == null, "Face clone lost null inheritance");
        var mesh = new PolyfaceMesh(PolyfaceGrammarPoints, new[] { inherited, own }) { Layer = new Layer("ParentFace"), Color = AciColor.Yellow };
        var original = new DxfDocument(version); original.Entities.Add(mesh); int sourceEvents = 0; mesh.PolyfaceMeshFaceLayerChanged += (_, _) => sourceEvents++;
        var clone = (PolyfaceMesh)mesh.Clone(); var target = new DxfDocument(version); target.Entities.Add(clone);
        Check(!ReferenceEquals(mesh.Faces[0], clone.Faces[0]) && !ReferenceEquals(mesh.Faces[0].VertexIndexes, clone.Faces[0].VertexIndexes), "Mesh clone shares face topology");
        Check(clone.Faces[0].Layer == null && clone.Faces[0].Color == null, "Mesh clone materialized inheritance");
        Check(!ReferenceEquals(own.Layer, clone.Faces[1].Layer) && !ReferenceEquals(own.Color, clone.Faces[1].Color), "Mesh clone shares explicit resources");
        clone.Faces[0].VertexIndexes[0] = -4; clone.Vertexes[0] = new Vector3(-1, -2, -3);
        clone.Faces[0].Layer = new Layer("CloneOnly"); clone.Faces[1].Layer = null; clone.Faces[1].Color = null;
        Equal(0, sourceEvents, "Clone face changes reached original event subscribers"); Check(!original.Layers.Contains("CloneOnly"), "Clone resource entered original document");
        Check(target.Layers.Contains("CloneOnly"), "Clone resource missed target document"); Equal((short)1, inherited.VertexIndexes[0], "Clone changed original face index");
        Equal(PolyfaceGrammarPoints[0], mesh.Vertexes[0], "Clone changed original coordinate"); Equal("OriginalFace", own.Layer.Name, "Clone reset original face layer");
        foreach (DxfDocument doc in new[] { original, target })
        { using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Clone graph save"); stream.Position = 0; Check(DxfDocument.Load(stream) != null, "Clone graph reload"); }
    }

    private static void PolyfaceGrammarConstructor(int fault)
    {
        bool rejected = false;
        try
        {
            if (fault == 0) _ = new PolyfaceMeshFace(Array.Empty<short>());
            else if (fault == 1) _ = new PolyfaceMeshFace(new short[5]);
            else if (fault == 2) _ = new PolyfaceMeshFace(new short[] { 0, 1 });
            else if (fault == 3) _ = new PolyfaceMesh(PolyfaceGrammarPoints, new[] { new short[] { short.MinValue } });
            else if (fault == 4) _ = new PolyfaceMesh(PolyfaceGrammarPoints, new[] { new short[] { 5 } });
            else if (fault == 5) _ = new PolyfaceMesh(PolyfaceGrammarPoints, new[] { new PolyfaceMeshFace(new short[] { -5 }) });
            else _ = new PolyfaceMesh(PolyfaceGrammarPoints, new PolyfaceMeshFace[] { null! });
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Invalid constructor topology accepted");
    }

    private static void PolyfaceGrammarContext(DxfVersion version, bool binary, int context)
    {
        var raw = PolyfaceGrammarSeed(version); var face = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(PolyfaceGrammarIsFace);
        var tags = face.Tags.ToList(); var privateTags = new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(70, (short)0), new DxfTag(71, (short)5), new DxfTag(102, "}") };
        if (context == 0) tags.InsertRange(2, privateTags);
        else if (context == 1) tags.AddRange(privateTags);
        else if (context == 2)
        { tags.Add(new DxfTag(102, "{OUTER")); tags.AddRange(privateTags); tags.Add(new DxfTag(72, (short)-5)); tags.Add(new DxfTag(102, "}")); }
        else if (context == 3)
        { tags.Add(new DxfTag(100, "VendorPrivateFace")); tags.Add(new DxfTag(70, (short)0)); tags.Add(new DxfTag(71, (short)5)); }
        else if (context == 4)
        {
            int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbFaceRecord"));
            tags.InsertRange(marker, new[] { new DxfTag(100, "VendorPrivateFace"), new DxfTag(70, (short)0), new DxfTag(71, (short)5) });
        }
        else if (context == 5)
        { tags.Add(new DxfTag(1001, "ACAD")); tags.Add(new DxfTag(1000, "ignored child XData")); tags.Add(new DxfTag(71, (short)5)); }
        else
        { tags.Add(new DxfTag(102, "{UNTERMINATED")); tags.Add(new DxfTag(71, (short)5)); }
        raw = raw.WithRecord(face, tags);
        if (context == 5 || context == 6)
        {
            if (context == 5)
            {
                using var input = new MemoryStream(); raw.Save(input, binary);
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"polyface-invalid-xdata-{version}-{binary}.dxf"), input.ToArray());
            }
            bool rejected = false; try { _ = PolyfaceGrammarLoad(raw, binary); } catch (FormatException) { rejected = true; }
            Check(rejected, "Malformed private or XData POLYFACE packet accepted"); return;
        }
        var loaded = PolyfaceGrammarLoad(raw, binary);
        Check(loaded.Entities.PolyfaceMeshes.Single().Faces.Single().VertexIndexes.SequenceEqual(new short[] { 1, 2, 3, 4 }), "Private child metadata acquired public geometry semantics");
    }

    private static void PolyfaceGrammarMagnitude(bool binary)
    {
        var vertices = new Vector3[32769]; vertices[32767] = new Vector3(91, 92, 93);
        var mesh = new PolyfaceMesh(vertices, new[] { new short[] { short.MinValue, 1, 2 } });
        Equal(vertices[32767], ((Face3D)mesh.Explode().Single()).FirstVertex, "Signed minimum index overflowed magnitude calculation");
        var clone = (PolyfaceMesh)mesh.Clone(); Check(clone.Faces[0].VertexIndexes.SequenceEqual(mesh.Faces[0].VertexIndexes), "Signed minimum clone changed");
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(mesh);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Addressable signed minimum or extra unreferenced coordinates rejected"); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new FormatException("Large coordinate sequence rejected");
        Equal(32769, loaded.Entities.PolyfaceMeshes.Single().Vertexes.Length, "Coordinate sequence truncated to signed header count");
        Equal(vertices[32767], ((Face3D)loaded.Entities.PolyfaceMeshes.Single().Explode().Single()).FirstVertex, "Signed minimum wire identity changed");
    }
}
