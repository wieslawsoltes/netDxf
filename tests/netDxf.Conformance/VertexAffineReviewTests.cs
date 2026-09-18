// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] VertexAffineModes = { "identity", "translate", "scale", "shear", "reflect", "rank2", "collapse", "tiny", "huge" };
    private static Matrix3 VertexAffineMatrix(string mode) => mode switch
    {
        "identity" or "translate" => Matrix3.Identity,
        "scale" => Matrix3.Scale(2, 3, 4), "shear" => new(1, 2, .5, 0, 1, .25, 0, 0, 1),
        "reflect" => Matrix3.Scale(-1, 1, 1), "rank2" => Matrix3.Scale(1, 1, 0),
        "collapse" => Matrix3.Scale(0), "tiny" => Matrix3.Scale(1e-200),
        "huge" => Matrix3.Scale(1e200), _ => throw new ArgumentException(mode)
    };
    private static Vector3 VertexAffineTranslation(string mode) => mode is "translate" or "collapse" ? new(7, -11, 13) : Vector3.Zero;
    private static EntityObject VertexAffineSubject(bool mesh, int normal)
    {
        Vector3[] points = { new(1, 2, 3), new(5, -1, 7), new(-2, 4, 9), new(8, 6, -3) };
        EntityObject entity = mesh ? new Mesh(points, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } }, new[] { new MeshEdge(0, 2, 1.5) })
            { BlendCrease = true, SubdivisionLevel = 2 } : new Face3D(points[0], points[1], points[2], points[3]) { EdgeFlags = Face3DEdgeFlags.Second | Face3DEdgeFlags.Fourth };
        entity.Normal = normal == 0 ? Vector3.UnitZ : new Vector3(2, -3, 6);
        entity.Layer = new Layer("VERTEX_AFFINE"); entity.Color = new AciColor(3); entity.IsVisible = false;
        entity.LinetypeScale = 1.75; entity.ProxyGraphics = new byte[] { 1, 3, 7, 11 };
        var data = new XData(new ApplicationRegistry("VERTEX_AFFINE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); entity.XData.Add(data);
        return entity;
    }
    private static Vector3[] VertexAffinePoints(EntityObject entity) => entity is Mesh m ? m.Vertexes.ToArray() :
        entity is Face3D f ? new[] { f.FirstVertex, f.SecondVertex, f.ThirdVertex, f.FourthVertex } : throw new ArgumentException();
    private static long[] VertexAffineBits(EntityObject entity) => VertexAffinePoints(entity).Append(entity.Normal)
        .SelectMany(v => new[] { v.X, v.Y, v.Z }).Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void VertexAffineSetPoint(EntityObject entity, int i, Vector3 v)
    {
        if (entity is Mesh m) m.Vertexes[i] = v;
        else { var f = (Face3D)entity; if (i == 0) f.FirstVertex = v; else if (i == 1) f.SecondVertex = v; else if (i == 2) f.ThirdVertex = v; else f.FourthVertex = v; }
    }
    private static void VertexAffineApply(EntityObject e, Matrix3 a, Vector3 t, bool four)
    { if (four) e.TransformBy(LineReviewMatrix4(a, t)); else e.TransformBy(a, t); }
    private static void VertexAffineModel(bool mesh, string mode, int normal, bool four, DxfVersion? version = null, bool binary = false)
    {
        var subject = VertexAffineSubject(mesh, normal); var before = (EntityObject)subject.Clone();
        var doc = new DxfDocument(version ?? DxfVersion.AutoCad2018); doc.Entities.Add(subject);
        var handle = subject.Handle; var owner = subject.Owner; var data = subject.XData["VERTEX_AFFINE"];
        var pointsIdentity = (subject as Mesh)?.Vertexes; var facesIdentity = (subject as Mesh)?.Faces;
        var edgeIdentity = (subject as Mesh)?.Edges[0];
        Matrix3 a = VertexAffineMatrix(mode); Vector3 t = VertexAffineTranslation(mode);
        VertexAffineApply(subject, a, t, four);
        var oldPoints = VertexAffinePoints(before); var points = VertexAffinePoints(subject);
        for (int i = 0; i < points.Length; i++) LineReviewNear(a * oldPoints[i] + t, points[i], "vertex image");
        Vector3 direction = a * before.Normal;
        double scale = Math.Max(Math.Abs(direction.X), Math.Max(Math.Abs(direction.Y), Math.Abs(direction.Z)));
        Vector3 expected = scale == 0 ? before.Normal : Vector3.Normalize(direction / scale);
        LineReviewNear(expected, subject.Normal, "auxiliary normal");
        if (mode == "identity") Check(before.ProxyGraphics!.SequenceEqual(subject.ProxyGraphics!), "identity proxy");
        else Check(subject.ProxyGraphics == null, "stale proxy retained");
        Equal(handle, subject.Handle, "handle"); Check(ReferenceEquals(owner, subject.Owner) && ReferenceEquals(data, subject.XData["VERTEX_AFFINE"]), "source identities");
        if (subject is Mesh sm)
        {
            Check(ReferenceEquals(pointsIdentity, sm.Vertexes) && ReferenceEquals(facesIdentity, sm.Faces) && ReferenceEquals(edgeIdentity, sm.Edges[0]), "mesh list identities");
            Check(sm.Faces[0].SequenceEqual(new[] { 0, 1, 2 }) && sm.Faces[1].SequenceEqual(new[] { 0, 2, 3 }), "face topology");
            Equal((byte)2, sm.SubdivisionLevel, "subdivision"); Check(sm.BlendCrease, "crease blending"); Equal(1.5, sm.Edges[0].Crease, "edge crease");
        }
        else Equal(((Face3D)before).EdgeFlags, ((Face3D)subject).EdgeFlags, "hidden edges");
        if (!version.HasValue) return;
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "wire save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"meshface-affine-{mesh}-{mode}-{normal}-{four}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("reload");
        EntityObject found = mesh ? loaded.Entities.Meshes.Single() : loaded.Entities.Faces3D.Single();
        var reloaded = VertexAffinePoints(found);
        for (int i = 0; i < points.Length; i++) LineReviewNear(points[i], reloaded[i], "reloaded vertex");
        Equal(0, loaded.Objects.Validate().Count, "loaded graph");
    }
    private static void VertexAffineReject(EntityObject subject, Action operation)
    {
        var bits = VertexAffineBits(subject); byte[] proxy = subject.ProxyGraphics!;
        bool rejected = false; try { operation(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "invalid transform accepted"); Check(bits.SequenceEqual(VertexAffineBits(subject)), "partial geometry mutation");
        Check(proxy.SequenceEqual(subject.ProxyGraphics!), "rejected transform changed proxy");
    }
    private static void RegisterVertexAffineReviewTests()
    {
        foreach (bool mesh in new[] { false, true })
        {
            foreach (string mode in VertexAffineModes) foreach (int n in new[] { 0, 1 }) foreach (bool four in new[] { false, true })
            {
                Run($"vertex-affine/model/{mesh}/{mode}/{n}/{four}", () => VertexAffineModel(mesh, mode, n, four));
                foreach (var v in SupportedVersions.Where(v => !mesh || v >= DxfVersion.AutoCad2010)) foreach (bool b in new[] { false, true })
                    Run($"vertex-affine/wire/{mesh}/{mode}/{n}/{four}/{v}/{b}", () => VertexAffineModel(mesh, mode, n, four, v, b));
            }
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                string label = BitConverter.DoubleToInt64Bits(invalid).ToString("X16");
                for (int k = 0; k < 16; k++)
                { int i=k; Run($"vertex-affine/reject/matrix4/{mesh}/{i}/{label}", () => { var e=VertexAffineSubject(mesh,0);var a=Matrix4.Identity;a[i/4,i%4]=invalid;VertexAffineReject(e,()=>e.TransformBy(a)); }); }
                for (int k = 0; k < 12; k++)
                { int i=k; Run($"vertex-affine/reject/source/{mesh}/{i}/{label}", () => { var e=VertexAffineSubject(mesh,0);var p=VertexAffinePoints(e)[i/3];p[i%3]=invalid;VertexAffineSetPoint(e,i/3,p);VertexAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX)); }); }
                for (int k = 0; k < 12; k++)
                { int i=k; Run($"vertex-affine/reject/matrix3/{mesh}/{i}/{label}", () => { var e=VertexAffineSubject(mesh,0);var a=Matrix3.Identity;var t=Vector3.Zero;if(i<9)a[i/3,i%3]=invalid;else t[i-9]=invalid;VertexAffineReject(e,()=>e.TransformBy(a,t)); }); }
            }
            for (int k=0;k<4;k++) {int i=k;Run($"vertex-affine/reject/projective/{mesh}/{i}",()=>{var e=VertexAffineSubject(mesh,0);var a=Matrix4.Identity;a[3,i]=2;VertexAffineReject(e,()=>e.TransformBy(a));});}
            Run($"vertex-affine/reject/late-overflow/{mesh}",()=>{var e=VertexAffineSubject(mesh,0);VertexAffineSetPoint(e,3,new(double.MaxValue,0,0));VertexAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});
            Run($"vertex-affine/reject/underflow/{mesh}",()=>{var e=VertexAffineSubject(mesh,0);VertexAffineSetPoint(e,3,new(double.Epsilon,0,0));VertexAffineReject(e,()=>e.TransformBy(Matrix3.Scale(.25),Vector3.Zero));});
            Run($"vertex-affine/boundary/cancellation/{mesh}",()=>{var e=VertexAffineSubject(mesh,0);for(int i=0;i<4;i++)VertexAffineSetPoint(e,i,new(double.MaxValue,double.MaxValue,3));e.TransformBy(new Matrix3(2,-2,1,0,0,1,0,0,1),Vector3.Zero);foreach(var p in VertexAffinePoints(e))Equal(new Vector3(3,3,3),p,"exact cancellation");});
            Run($"vertex-affine/reject/normal/{mesh}",()=>{var e=VertexAffineSubject(mesh,0);typeof(EntityObject).GetField("normal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(e,new Vector3(double.NaN,0,1));VertexAffineReject(e,()=>e.TransformBy(Matrix3.Identity,Vector3.Zero));});
        }
        Run("vertex-affine/boundary/derived-normal", () =>
        {
            var face = new VertexAffineDerivedFace { FirstVertex = Vector3.UnitX, SecondVertex = Vector3.UnitY, ThirdVertex = Vector3.UnitZ };
            face.TransformBy(Matrix3.Scale(2), Vector3.UnitX);
            Equal(new Vector3(3,0,0), face.FirstVertex, "base state publication");
        });
    }
    private sealed class VertexAffineDerivedFace : Face3D
    { public override Vector3 Normal { get => throw new InvalidOperationException("virtual read"); set => throw new InvalidOperationException("virtual write"); } }
}
