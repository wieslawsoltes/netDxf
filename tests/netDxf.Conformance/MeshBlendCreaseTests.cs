using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshBlendCreaseTests()
    {
        Run("mesh/blend/default", () => Check(!ProfileMesh(0).BlendCrease,"Default mesh blend flag is not false."));
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (bool enabled in new[] { false, true })
                {
                    bool e = enabled;
                    Run($"mesh/blend/api-clone-explode/{v}/{b}/{e}", () => MeshBlendApi(v,b,e));
                }
                foreach (short? value in new short?[] { null, 0, 1 })
                    foreach (bool late in new[] { false, true })
                    {
                        short? f = value; bool l = late;
                        Run($"mesh/blend/read-write/{v}/{b}/{f}/{l}", () => MeshBlendWire(v, b, f, l));
                    }
                foreach (short value in new short[] { -1, 2 })
                {
                    short f = value;
                    Run($"mesh/blend/invalid/{v}/{b}/{f}", () => MeshBlendInvalid(v, b, f));
                }
            }
    }

    private static MemoryStream MeshBlendFixture(DxfVersion version, bool binary, short? flag, bool late)
    {
        var tags = new List<DxfTag>
        {
            new(0,"SECTION"), new(2,"HEADER"), new(9,"$ACADVER"), new(1,HeaderVersion(version)), new(0,"ENDSEC"),
            new(0,"SECTION"), new(2,"ENTITIES"), new(0,"MESH"), new(5,"200"), new(100,"AcDbEntity"), new(8,"0"),
            new(100,"AcDbSubDMesh"), new(71,(short)2)
        };
        void Flag()
        {
            if (flag.HasValue) tags.Add(new DxfTag(72,flag.Value));
            if (!binary) tags.Add(new DxfTag(999,"interleaved comment"));
        }
        if (!late) Flag();
        tags.AddRange(new DxfTag[]
        {
            new(91,2), new(92,3),
            new(10,0.0),new(20,0.0),new(30,0.0),new(10,1.0),new(20,0.0),new(30,0.0),new(10,0.0),new(20,1.0),new(30,0.0),
            new(93,4),new(90,3),new(90,0),new(90,1),new(90,2),
            new(94,1),new(90,0),new(90,1),new(95,1),new(140,1.5),new(90,0)
        });
        if (late) Flag();
        tags.AddRange(new DxfTag[] { new(1001,"MESH_BLEND"),new(1000,"after mesh"),new(0,"ENDSEC"),new(0,"EOF") });
        return new MemoryStream(RawFixtureBytes(tags,binary));
    }

    private static void MeshBlendWire(DxfVersion version, bool binary, short? flag, bool late)
    {
        using var input = MeshBlendFixture(version,binary,flag,late);
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("MESH blend fixture failed to load.");
        Mesh original = document.Entities.Meshes.Single();
        Equal((byte)2,original.SubdivisionLevel,"Blend flag affected subdivision level");
        Equal(3,original.Vertexes.Count,"Blend flag affected vertices");
        Equal(1.5,original.Edges.Single().Crease,"Blend flag affected crease value");
        Equal("after mesh",(string)original.XData["MESH_BLEND"].XDataRecord.Single().Value,"Blend flag disrupted XData");
        var copy = (Mesh)original.Clone(); document.Entities.Add(copy);
        using var output = new MemoryStream(); Check(document.Save(output,!binary),"MESH blend export failed.");
        output.Position = 0; var raw = DxfRawDocument.Load(output);
        var meshes = raw.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="MESH").ToArray();
        Equal(2,meshes.Length,"Cloned MESH count");
        foreach (var mesh in meshes)
            Equal(flag ?? (short)0,(short)mesh.Tags.Single(t=>t.Code==72).Value,"MESH blend flag was reset");
        Check(input.CanRead && output.CanWrite,"MESH blend IO closed caller streams.");
        if (flag==1 && !late)
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"mesh-blend-{version}-{!binary}.dxf"),output.ToArray());
    }

    private static void MeshBlendApi(DxfVersion version, bool binary, bool enabled)
    {
        Mesh mesh = ProfileMesh(2); mesh.BlendCrease = enabled;
        var block = new Block("BlendMesh"); block.Entities.Add(mesh);
        var insert = new Insert(block);
        var copy = (Insert)insert.Clone();
        Mesh child = copy.Block.Entities.OfType<Mesh>().Single();
        Equal(enabled,child.BlendCrease,"Block clone lost blend flag");
        child.BlendCrease = !enabled;
        Equal(enabled,mesh.BlendCrease,"Clone edit changed the source blend flag");
        Mesh exploded = insert.Explode().OfType<Mesh>().Single();
        Equal(enabled,exploded.BlendCrease,"Explosion lost blend flag");
        var document = new DxfDocument(version); document.Entities.Add(insert);
        using var output = new MemoryStream(); Check(document.Save(output,binary),"Authored blend flag failed to save.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored blend flag failed to load.");
        Equal(enabled,OnlyProfileMesh(loaded).BlendCrease,"Authored blend flag changed on reload");
    }

    private static void MeshBlendInvalid(DxfVersion version, bool binary, short flag)
    {
        using var input = MeshBlendFixture(version,binary,flag,false);
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Invalid blend flag was accepted."); }
        catch (InvalidDataException exception)
        {
            Check(exception.Message.Contains("72",StringComparison.Ordinal),"Blend flag diagnostic lost the group code.");
        }
#else
        Check(DxfDocument.Load(input)==null,"Invalid blend flag was accepted.");
#endif
        Check(input.CanRead,"Invalid MESH input closed caller stream.");
    }
}
