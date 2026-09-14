using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLwPolylineFidelityTests()
    {
        Run("lw-fidelity/model", LwFidelityModel);
        Run("lw-fidelity/transform", LwFidelityTransform);
        Run("lw-fidelity/smoothing-guard", LwFidelitySmoothing);
        Run("lw-fidelity/centerline-compatibility", LwFidelityCenterlineCompatibility);
        foreach (string year in new[] { "2000", "2018" })
        foreach (string mode in new[] { "absent", "zero", "positive" })
        foreach (bool binary in new[] { false, true })
        {
            string y = year, m = mode; bool b = binary;
            Run($"lw-fidelity/independent/{y}/{m}/{b}", () => LwFidelityIndependent(y, m, b));
        }
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            foreach (string mode in new[] { "absent", "zero", "positive" })
            {
                string m = mode;
                Run($"lw-fidelity/roundtrip/{v}/{b}/{m}", () => LwFidelityRoundTrip(v, b, m));
                foreach (int order in Enumerable.Range(0, 4))
                {
                    int o = order;
                    Run($"lw-fidelity/authored/{v}/{b}/{m}/{o}", () => LwFidelityAuthored(v, b, m, o));
                }
            }
            Run($"lw-fidelity/id-version/{v}/{b}", () => LwFidelityIdVersion(v, b));
            foreach (int scenario in Enumerable.Range(0, 8))
            {
                int c = scenario;
                Run($"lw-fidelity/malformed/{v}/{b}/{c}", () => LwFidelityMalformed(v, b, c));
            }
            foreach (bool closed in new[] { false, true })
            {
                bool c = closed;
                Run($"lw-fidelity/reverse-association/{v}/{b}/{c}", () => LwFidelityReverseAssociation(v, b, c));
            }
        }
    }

    private static Polyline2D LwFidelityModelPolyline(string mode, bool identifiers, bool closed = true)
    {
        var polyline = new Polyline2D(new[] {
            new Polyline2DVertex(-3, 2, 0.25) { EndWidthOverride = 1.25 },
            new Polyline2DVertex(1, -1, 0) { StartWidthOverride = 0 },
            new Polyline2DVertex(6, 4, -0.5) { StartWidthOverride = 2.5, EndWidthOverride = 3.75 },
            new Polyline2DVertex(9, -2, 0.75)
        }, closed) { Elevation = 2.5, ConstantWidth = mode == "absent" ? null : mode == "zero" ? 0 : 1.75 };
        if (identifiers)
        {
            int[] ids = { 0, -17, int.MaxValue, 42 };
            for (int i = 0; i < 4; i++) polyline.Vertexes[i].VertexIdentifier = ids[i];
        }
        return polyline;
    }

    private static void LwFidelityEqual(Polyline2D expected, Polyline2D actual)
    {
        Equal(expected.ConstantWidth, actual.ConstantWidth, "Constant-width presence/value");
        Equal(expected.Vertexes.Count, actual.Vertexes.Count, "Vertex count");
        Equal(expected.IsClosed, actual.IsClosed, "Closed state");
        Equal(expected.Elevation, actual.Elevation, "Elevation");
        for (int i = 0; i < expected.Vertexes.Count; i++)
        {
            var a = expected.Vertexes[i]; var b = actual.Vertexes[i];
            Equal(a.Position, b.Position, "Vertex position"); Equal(a.Bulge, b.Bulge, "Bulge");
            Equal(a.StartWidthOverride, b.StartWidthOverride, "Raw start-width presence/value");
            Equal(a.EndWidthOverride, b.EndWidthOverride, "Raw end-width presence/value");
            Equal(a.VertexIdentifier, b.VertexIdentifier, "Vertex identifier presence/value");
            Equal(expected.GetEffectiveStartWidth(i), actual.GetEffectiveStartWidth(i), "Effective start width");
            Equal(expected.GetEffectiveEndWidth(i), actual.GetEffectiveEndWidth(i), "Effective end width");
        }
    }

    private static void LwFidelityIndependent(string year, string mode, bool binary)
    {
        string name = $"independent-lw-fidelity-R{year}-{mode}";
        string path = Path.Combine("tests", "fixtures", "lwpolyline-fidelity", name + ".dxf");
        var expected = new Polyline2D(new[] {
            new Polyline2DVertex(0, 0, .5) { EndWidthOverride = 0 },
            new Polyline2DVertex(6, 0) { StartWidthOverride = 2 },
            new Polyline2DVertex(7, 5, -.25) { StartWidthOverride = 0, EndWidthOverride = 3 },
            new Polyline2DVertex(-2, 4)
        }, true) { ConstantWidth = mode == "absent" ? null : mode == "zero" ? 0 : 3.25, Elevation = 2.75, Thickness = .5 };
        if (year == "2018")
        {
            int[] ids = { 0, -17, int.MaxValue, 42 };
            for (int i = 0; i < 4; i++) expected.Vertexes[i].VertexIdentifier = ids[i];
        }
        var doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Independent LWP fixture failed to load.");
        var polyline = doc.Entities.Polylines2D.Single(); LwFidelityEqual(expected, polyline);
        Equal(.5, polyline.Thickness, "Independent thickness");
        Equal(1, doc.Entities.Lines.Count(), "Independent following entity");
        Check(polyline.XData.Count > 0, "Independent following XData lost.");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Independent LWP save");
        byte[] bytes = output.ToArray(); LwFidelityAssertWire(bytes, expected);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name + (binary ? "-binary.dxf" : "-ascii.dxf")), bytes);
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Independent LWP reload failed.");
        LwFidelityEqual(expected, loaded.Entities.Polylines2D.Single());
    }

    private static void LwFidelityCenterlineCompatibility()
    {
        var explicitWidth = LwFidelityModelPolyline("positive", false);
        var variableWidth = (Polyline2D)explicitWidth.Clone(); variableWidth.ConstantWidth = null;
        var explicitSegments = explicitWidth.Explode(); var variableSegments = variableWidth.Explode();
        Equal(explicitSegments.Count, variableSegments.Count, "Explicit width changed centerline explosion");
        for (int i = 0; i < explicitSegments.Count; i++)
        {
            Equal(explicitSegments[i].Type, variableSegments[i].Type, "Centerline segment type");
            if (explicitSegments[i] is Line a && variableSegments[i] is Line b)
            { Equal(a.StartPoint, b.StartPoint, "Centerline start"); Equal(a.EndPoint, b.EndPoint, "Centerline end"); }
            if (explicitSegments[i] is Arc c && variableSegments[i] is Arc d)
            { Equal(c.Center, d.Center, "Centerline arc center"); Equal(c.Radius, d.Radius, "Centerline arc radius"); Equal(c.StartAngle, d.StartAngle, "Centerline start angle"); Equal(c.EndAngle, d.EndAngle, "Centerline end angle"); }
        }
        var explicitPoints = explicitWidth.PolygonalVertexes(32);
        var variablePoints = variableWidth.PolygonalVertexes(32);
        Check(explicitPoints.SequenceEqual(variablePoints), "Explicit width changed centerline polygonal conversion.");
        var block = new Block("Wide"); block.Entities.Add(explicitWidth);
        var insert = new Insert(block) { Scale = new Vector3(2) };
        var exploded = insert.Explode().OfType<Polyline2D>().Single();
        Equal((double?)3.5, exploded.ConstantWidth, "INSERT explode did not scale constant width");
        Equal((double?)5, exploded.Vertexes[2].StartWidthOverride, "INSERT explode did not scale raw width");
        Equal((double?)1.75, explicitWidth.ConstantWidth, "INSERT explode mutated source");
    }

    private static void LwFidelityModel()
    {
        var vertex = new Polyline2DVertex(1, 2);
        Check(vertex.StartWidthOverride == null && vertex.EndWidthOverride == null && vertex.VertexIdentifier == null, "Defaults invented optional fields.");
        vertex.StartWidth = 0; vertex.EndWidth = 0; vertex.VertexIdentifier = 0;
        Equal((double?)0, vertex.StartWidthOverride, "Explicit start zero lost"); Equal((double?)0, vertex.EndWidthOverride, "Explicit end zero lost");
        var copy = (Polyline2DVertex)vertex.Clone(); Equal((int?)0, copy.VertexIdentifier, "Identifier copy");
        vertex.StartWidthOverride = null; vertex.EndWidthOverride = null;
        Equal(0.0, vertex.StartWidth, "Absent start default"); Equal(0.0, vertex.EndWidth, "Absent end default");
        Equal((double?)0, copy.StartWidthOverride, "Clone aliases presence");
        foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => vertex.StartWidth = bad);
            Throws<ArgumentOutOfRangeException>(() => vertex.EndWidthOverride = bad);
            Throws<ArgumentOutOfRangeException>(() => new Polyline2D().ConstantWidth = bad);
        }
        var polyline = LwFidelityModelPolyline("positive", true);
        for (int i = 0; i < 4; i++) { Equal(1.75, polyline.GetEffectiveStartWidth(i), "Constant precedence start"); Equal(1.75, polyline.GetEffectiveEndWidth(i), "Constant precedence end"); }
        Equal((double?)2.5, polyline.Vertexes[2].StartWidthOverride, "Constant setting destroyed raw widths");
        polyline.ConstantWidth = 0; Equal(2.5, polyline.GetEffectiveStartWidth(2), "Zero constant masked variable width");
        var clone = (Polyline2D)polyline.Clone(); clone.Vertexes[0].VertexIdentifier = int.MinValue; clone.Vertexes[2].StartWidth = 9;
        Equal((int?)0, polyline.Vertexes[0].VertexIdentifier, "Clone changed source identifier"); Equal((double?)2.5, polyline.Vertexes[2].StartWidthOverride, "Clone changed source raw width");
        polyline.ConstantWidth = 5;
        Throws<ArgumentOutOfRangeException>(() => polyline.SetConstantWidth(-1)); Equal((double?)5, polyline.ConstantWidth, "Rejected setter cleared constant width");
        polyline.SetConstantWidth(3); Check(polyline.ConstantWidth == null, "Legacy uniform setter retained overriding group43");
        Check(polyline.Vertexes.All(v => v.StartWidthOverride == 3 && v.EndWidthOverride == 3), "Legacy uniform setter changed established behavior");
    }

    private static void LwFidelityTransform()
    {
        var polyline = LwFidelityModelPolyline("positive", true); var original = (Polyline2D)polyline.Clone();
        polyline.TransformBy(Matrix3.Scale(2), Vector3.UnitX);
        Equal((double?)3.5, polyline.ConstantWidth, "Uniform scale constant width");
        Equal((double?)5, polyline.Vertexes[2].StartWidthOverride, "Uniform scale raw width");
        Check(polyline.Vertexes[0].StartWidthOverride == null, "Uniform scale invented absent width");
        Equal((double?)0, polyline.Vertexes[1].StartWidthOverride, "Uniform scale lost explicit zero");
        Equal(original.Vertexes[2].VertexIdentifier, polyline.Vertexes[2].VertexIdentifier, "Transform changed identifier");
        var before = (Polyline2D)polyline.Clone();
        Throws<NotSupportedException>(() => polyline.TransformBy(Matrix3.Scale(2, 3, 1), Vector3.Zero)); LwFidelityEqual(before, polyline);
        Throws<NotSupportedException>(() => polyline.TransformBy(Matrix3.Scale(0), Vector3.Zero)); LwFidelityEqual(before, polyline);
        Throws<NotSupportedException>(() => polyline.TransformBy(Matrix3.Scale(-1, 1, 1), Vector3.Zero)); LwFidelityEqual(before, polyline);
        Throws<NotSupportedException>(() => polyline.TransformBy(new Matrix3(1, 0, 1, 0, 1, 0, 0, 0, 1), Vector3.Zero)); LwFidelityEqual(before, polyline);
        // Uniformity is checked in the entity's plane, not against the normal-axis scale.
        polyline.TransformBy(Matrix3.Scale(3, 3, 7), Vector3.Zero); Equal((double?)10.5, polyline.ConstantWidth, "In-plane scaling");
    }

    private static void LwFidelitySmoothing()
    {
        var polyline = LwFidelityModelPolyline("positive", false); polyline.SmoothType = PolylineSmoothType.Quadratic;
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(polyline);
        using var output = new MemoryStream();
#if DEBUG
        Throws<NotSupportedException>(() => doc.Save(output));
#else
        Check(!doc.Save(output), "Smoothing discarded explicit constant width.");
#endif
        Equal(0L, output.Length, "Smoothing guard wrote partial bytes");
        var copy = (Polyline2D)polyline.Clone(); Equal(polyline.SmoothType, copy.SmoothType, "Clone lost smoothing state");
    }

    private static List<DxfTag> LwFidelityTags(DxfVersion version, string mode, bool identifiers, int order)
    {
        var polyline = LwFidelityModelPolyline(mode, identifiers);
        var tags = new List<DxfTag> {
            new(0,"SECTION"), new(2,"HEADER"), new(9,"$ACADVER"), new(1,HeaderVersion(version)), new(9,"$DWGCODEPAGE"), new(3,"ANSI_1252"),
            new(0,"ENDSEC"), new(0,"SECTION"), new(2,"ENTITIES"), new(0,"LWPOLYLINE"), new(5,"200"),
            new(100,"AcDbEntity"), new(8,"0"), new(100,"AcDbPolyline"), new(90,4), new(70,(short)1), new(38,2.5)
        };
        if (polyline.ConstantWidth.HasValue) tags.Add(new(43,polyline.ConstantWidth.Value));
        foreach (var vertex in polyline.Vertexes)
        {
            tags.Add(new(10,vertex.Position.X));
            var packet = new List<DxfTag>();
            if (vertex.StartWidthOverride.HasValue) packet.Add(new(40,vertex.StartWidthOverride.Value));
            if (vertex.EndWidthOverride.HasValue) packet.Add(new(41,vertex.EndWidthOverride.Value));
            if (vertex.VertexIdentifier.HasValue) packet.Add(new(91,vertex.VertexIdentifier.Value));
            packet.Add(new(42,vertex.Bulge));
            if ((order & 1) != 0) packet.Reverse();
            if ((order & 2) != 0) tags.AddRange(packet);
            tags.Add(new(20,vertex.Position.Y));
            if ((order & 2) == 0) tags.AddRange(packet);
        }
        tags.AddRange(new DxfTag[] { new(1001,"LW_FIDELITY"),new(1000,"after packet"),
            new(0,"LINE"),new(5,"201"),new(100,"AcDbEntity"),new(8,"0"),new(100,"AcDbLine"),
            new(10,91.0),new(20,-37.0),new(30,0.0),new(11,12.0),new(21,14.0),new(31,0.0),new(0,"ENDSEC"),new(0,"EOF") });
        return tags;
    }

    private static void LwFidelityAuthored(DxfVersion version, bool binary, string mode, int order)
    {
        bool identifiers = version >= DxfVersion.AutoCad2013;
        using var input = new MemoryStream(RawFixtureBytes(LwFidelityTags(version, mode, identifiers, order), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Authored fidelity packet failed.");
        LwFidelityEqual(LwFidelityModelPolyline(mode, identifiers), doc.Entities.Polylines2D.Single());
        Equal("after packet", doc.Entities.Polylines2D.Single().XData["LW_FIDELITY"].XDataRecord[0].Value, "Following XData");
        Equal(new Vector3(91,-37,0), doc.Entities.Lines.Single().StartPoint, "Following entity");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Authored packet save"); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored packet reload.");
        LwFidelityEqual(doc.Entities.Polylines2D.Single(), loaded.Entities.Polylines2D.Single());
    }

    private static void LwFidelityAssertWire(byte[] bytes, Polyline2D polyline)
    {
        using var stream = new MemoryStream(bytes);
        var tags = DxfRawDocument.Load(stream).Tags;
        int start = Enumerable.Range(0, tags.Count).First(i => tags[i].Code == 0 && (string)tags[i].Value == "LWPOLYLINE");
        var body = tags.Skip(start + 1).TakeWhile(t => t.Code != 0).ToList();
        Equal(polyline.ConstantWidth.HasValue ? 1 : 0, body.Count(t => t.Code == 43), "Exact group43 presence");
        if (polyline.ConstantWidth.HasValue) Equal(polyline.ConstantWidth.Value, (double)body.Single(t => t.Code == 43).Value, "Exact group43 value");
        int index = -1;
        var packets = new List<List<DxfTag>>();
        foreach (DxfTag tag in body)
        {
            if (tag.Code == 10) { index++; packets.Add(new List<DxfTag>()); }
            if (index >= 0 && (tag.Code == 40 || tag.Code == 41 || tag.Code == 91)) packets[index].Add(tag);
        }
        for (int i = 0; i < packets.Count; i++)
        {
            var p = packets[i]; var vertex = polyline.Vertexes[i];
            Equal(vertex.StartWidthOverride.HasValue ? 1 : 0, p.Count(t => t.Code == 40), "Exact group40 presence");
            Equal(vertex.EndWidthOverride.HasValue ? 1 : 0, p.Count(t => t.Code == 41), "Exact group41 presence");
            Equal(vertex.VertexIdentifier.HasValue ? 1 : 0, p.Count(t => t.Code == 91), "Exact group91 presence");
            if (vertex.VertexIdentifier.HasValue) Equal(vertex.VertexIdentifier.Value, (int)p.Single(t => t.Code == 91).Value, "Exact identifier");
        }
    }

    private static void LwFidelityRoundTrip(DxfVersion version, bool binary, string mode)
    {
        var original = LwFidelityModelPolyline(mode, version >= DxfVersion.AutoCad2013);
        var polyline = (Polyline2D)original.Clone();
        foreach (string stage in new[] { "original", "reversed", "restored" })
        {
            if (stage != "original") polyline.Reverse();
            var doc = new DxfDocument(version); doc.Entities.Add((Polyline2D)polyline.Clone());
            using var output = new MemoryStream(); Check(doc.Save(output,binary), "Fidelity save");
            byte[] bytes = output.ToArray(); LwFidelityAssertWire(bytes,polyline);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"lw-fidelity-{version}-{binary}-{mode}-{stage}.dxf"),bytes);
            output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Fidelity reload");
            LwFidelityEqual(polyline,loaded.Entities.Polylines2D.Single());
        }
        LwFidelityEqual(original,polyline);
    }

    private static void LwFidelityIdVersion(DxfVersion version, bool binary)
    {
        // A reader retains identifiers in older headers; output requires the qualified profile.
        using var input = new MemoryStream(RawFixtureBytes(LwFidelityTags(version,"absent",true,3),binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Identifier read profile");
        LwFidelityEqual(LwFidelityModelPolyline("absent",true),doc.Entities.Polylines2D.Single());
        using var output = new MemoryStream();
        if (version >= DxfVersion.AutoCad2013) Check(doc.Save(output,binary), "Qualified identifier profile rejected");
        else
        {
#if DEBUG
            Throws<NotSupportedException>(() => doc.Save(output,binary));
#else
            Check(!doc.Save(output,binary),"Unsupported identifier profile was saved");
#endif
            Equal(0L,output.Length,"Version rejection wrote bytes");
        }
    }

    private static void LwFidelityMalformed(DxfVersion version, bool binary, int scenario)
    {
        var tags = LwFidelityTags(version,"positive",true,0); int x = tags.FindIndex(t => t.Code == 10), width = tags.FindIndex(t => t.Code == 43), id = tags.FindIndex(t => t.Code == 91);
        switch (scenario)
        {
            case 0: tags.Insert(x,new(91,3)); break;
            case 1: tags.Insert(id,new(91,3)); break;
            case 2: tags.Insert(width,new(43,0.0)); break;
            case 3: tags[width] = new(43,-1.0); break;
            case 4: tags.Insert(x+1,new(40,-1.0)); break;
            case 5: tags.Insert(x+1,new(41,-1.0)); break;
            case 6: tags.Insert(tags.FindIndex(t=>t.Code==40),new(40,0.0)); break;
            case 7: tags.Insert(tags.FindIndex(t=>t.Code==41),new(41,0.0)); break;
        }
        LwPolylineReject(tags,binary);
    }

    private static void LwFidelityReverseAssociation(DxfVersion version, bool binary, bool closed)
    {
        var original = LwFidelityModelPolyline("positive",version>=DxfVersion.AutoCad2013,closed);
        var reverse = (Polyline2D)original.Clone(); reverse.Reverse(); LwPolylineAssertReverse(original,reverse);
        int count = original.Vertexes.Count;
        for (int i=0;i<count;i++)
        {
            Equal(original.Vertexes[count-1-i].VertexIdentifier,reverse.Vertexes[i].VertexIdentifier,"Identifier must follow point");
            int source = (count-2-i+count)%count;
            Equal(original.Vertexes[source].EndWidthOverride,reverse.Vertexes[i].StartWidthOverride,"Reversed optional start width");
            Equal(original.Vertexes[source].StartWidthOverride,reverse.Vertexes[i].EndWidthOverride,"Reversed optional end width");
        }
        reverse.Reverse(); LwFidelityEqual(original,reverse);
        var block = new Block("Fidelity"); block.Entities.Add(reverse); var clone = (Block)block.Clone("FidelityCopy");
        var doc = new DxfDocument(version); doc.Entities.Add(new Insert(clone));
        using var output = new MemoryStream(); Check(doc.Save(output,binary),"Block fidelity clone save");
        LwFidelityEqual(original,clone.Entities.OfType<Polyline2D>().Single());
    }
}
