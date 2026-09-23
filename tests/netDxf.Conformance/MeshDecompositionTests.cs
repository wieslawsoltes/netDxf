// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] DecompositionKinds = { "triangle", "quad", "concave", "ell", "collinear", "notch" };
    private static Vector2[] DecompositionRing(string kind) => kind switch
    {
        "triangle" => new Vector2[] { new(0,0),new(4,0),new(0,4) },
        "quad" => new Vector2[] { new(0,0),new(4,0),new(4,4),new(0,4) },
        "concave" => new Vector2[] { new(0,0),new(4,0),new(4,4),new(2,1),new(0,4) },
        "ell" => new Vector2[] { new(0,0),new(4,0),new(4,1),new(1,1),new(1,4),new(0,4) },
        "collinear" => new Vector2[] { new(0,0),new(2,0),new(4,0),new(4,4),new(0,4) },
        "notch" => new Vector2[] { new(0,0),new(6,0),new(6,6),new(4,6),new(4,2),new(2,2),new(2,6),new(0,6) },
        _ => throw new ArgumentException(kind)
    };
    private static Vector3 DecompositionPoint(Vector2 p, int plane, double scale) => plane switch
    {
        0 => new(p.X*scale,p.Y*scale,3*scale),
        1 => new(5*scale,p.X*scale,p.Y*scale),
        _ => new((p.X+p.Y)*scale,(p.X-p.Y)*scale,(2*p.X+3*p.Y+7)*scale)
    };
    // Reflection keeps the feature tests executable against the preceding assembly
    // so missing APIs fail rather than being represented by a temporary stub.
    private static List<Face3D> Decompose(Mesh mesh, bool control = false, int budget = 1000000)
    {
        var method = typeof(Mesh).GetMethod(control ? "ExplodeControlMesh" : "Explode", control ? new[] { typeof(int) } : Type.EmptyTypes);
        if (method == null) throw new MissingMethodException("Mesh decomposition API is absent.");
        try { return (List<Face3D>)method.Invoke(mesh, control ? new object[] { budget } : null)!; }
        catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); throw; }
    }
    private static Mesh DecompositionMesh(string kind, bool reverse, int plane, int scaleIndex)
    {
        double scale = new[] { 1.0, 1e-200, 1e200 }[scaleIndex];
        var ring = DecompositionRing(kind); var points = ring.Select(p => DecompositionPoint(p,plane,scale)).ToArray();
        var indices = Enumerable.Range(0,points.Length).ToArray(); if (reverse) Array.Reverse(indices);
        var mesh = new Mesh(points,new[] { indices }) { Layer = new Layer("DECOMPOSITION"), Color = new AciColor(5), LinetypeScale = 2.25, IsVisible = false };
        mesh.ProxyGraphics = new byte[] { 1,2,3,4 }; NormalFixtureEditAndRestore(mesh, new Vector3(2,-3,6));
        var data = new XData(new ApplicationRegistry("DECOMPOSITION")); data.XDataRecord.Add(new XDataRecord(XDataCode.String,"source payload")); mesh.XData.Add(data);
        return mesh;
    }
    private static void DecompositionModel(string kind, bool reverse, int plane, int scaleIndex, DxfVersion? version = null, bool binary = false)
    {
        var source = DecompositionMesh(kind,reverse,plane,scaleIndex); var sourceDoc = new DxfDocument(DxfVersion.AutoCad2018); sourceDoc.Entities.Add(source);
        var original = VertexAffineBits(source); var faceIdentity = source.Faces[0]; string handle = source.Handle; var owner = source.Owner;
        var triangles = Decompose(source); int n = source.Vertexes.Count;
        Equal(n-2,triangles.Count,"triangle count"); var edges = new Dictionary<(int,int),List<bool>>();
        var expectedRing = DecompositionRing(kind); double area = 0;
        foreach (Face3D triangle in triangles)
        {
            Check(triangle.Owner == null && triangle.Handle == null && triangle.ProxyGraphics == null,"detached output identity/cache");
            Equal(triangle.ThirdVertex,triangle.FourthVertex,"triangle fourth corner");
            var ids = new[] { triangle.FirstVertex,triangle.SecondVertex,triangle.ThirdVertex }.Select(p => source.Vertexes.FindIndex(v => v.X == p.X && v.Y == p.Y && v.Z == p.Z)).ToArray();
            Check(ids.All(i=>i>=0) && ids.Distinct().Count()==3,"original corner identities");
            var a=expectedRing[ids[0]]; var b=expectedRing[ids[1]];var c=expectedRing[ids[2]];
            double cross=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);Check(reverse ? cross<0 : cross>0,"triangle winding");area+=cross;
            Check((triangle.EdgeFlags & Face3DEdgeFlags.Third)!=0,"zero edge must be hidden");
            foreach(var e in new[] { (ids[0],ids[1],Face3DEdgeFlags.First),(ids[1],ids[2],Face3DEdgeFlags.Second),(ids[2],ids[0],Face3DEdgeFlags.Fourth) })
            { var key=(Math.Min(e.Item1,e.Item2),Math.Max(e.Item1,e.Item2));if(!edges.TryGetValue(key,out var values))edges[key]=values=new();values.Add((triangle.EdgeFlags&e.Item3)!=0); }
            Equal(source.Layer.Name,triangle.Layer.Name,"layer"); Equal(source.Color.Index,triangle.Color.Index,"color"); Equal(source.LinetypeScale,triangle.LinetypeScale,"linetype scale");
            Check(!triangle.IsVisible && DirectionBits(source.Normal).SequenceEqual(DirectionBits(triangle.Normal)),"appearance/auxiliary normal");
            Check(!ReferenceEquals(source.Color,triangle.Color)&&!ReferenceEquals(source.Layer,triangle.Layer)&&!ReferenceEquals(source.XData["DECOMPOSITION"],triangle.XData["DECOMPOSITION"]),"deep output isolation");
            Equal("source payload",triangle.XData["DECOMPOSITION"].XDataRecord[0].Value,"XData value");
        }
        double expectedArea=0;for(int i=0;i<n;i++){var a=expectedRing[i];var b=expectedRing[(i+1)%n];expectedArea+=a.X*b.Y-b.X*a.Y;}
        Equal(reverse ? -expectedArea : expectedArea,area,"signed area coverage");
        foreach(var edge in edges)
        { bool boundary=(edge.Key.Item1+1)%n==edge.Key.Item2||(edge.Key.Item2+1)%n==edge.Key.Item1;Equal(boundary?1:2,edge.Value.Count,"edge incidence");Check(edge.Value.All(hidden=>hidden==!boundary),"diagonal visibility"); }
        Check(original.SequenceEqual(VertexAffineBits(source))&&ReferenceEquals(faceIdentity,source.Faces[0])&&source.Handle==handle&&ReferenceEquals(owner,source.Owner),"source unchanged");
        Check(source.ProxyGraphics!.SequenceEqual(new byte[]{1,2,3,4}),"source cache retained");
        if(!version.HasValue)
        { triangles[0].Color.Index=2;triangles[0].XData["DECOMPOSITION"].XDataRecord.Clear();Equal((short)5,source.Color.Index,"source color isolation");Equal(1,source.XData["DECOMPOSITION"].XDataRecord.Count,"source XData isolation");if(triangles.Count>1){Equal((short)5,triangles[1].Color.Index,"sibling color isolation");Equal(1,triangles[1].XData["DECOMPOSITION"].XDataRecord.Count,"sibling XData isolation");}return; }
        var target = new DxfDocument(version.Value);target.Entities.Add(triangles);using var stream=new MemoryStream();Check(target.Save(stream,binary),"triangle save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"mesh-decompose-{kind}-{reverse}-{plane}-{scaleIndex}-{version}-{binary}.dxf"),stream.ToArray());
        stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new InvalidOperationException("triangle reload");Equal(n-2,loaded.Entities.Faces3D.Count(),"reloaded count");Equal(0,loaded.Objects.Validate().Count,"reloaded graph");
    }
    private static void DecompositionReject(Mesh mesh, Action action)
    {
        var points=VertexAffineBits(mesh);var proxy=mesh.ProxyGraphics;var faces=mesh.Faces.ToArray();bool rejected=false;
        try{action();}catch(ArgumentException){rejected=true;}catch(NotSupportedException){rejected=true;}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"invalid decomposition accepted");Check(points.SequenceEqual(VertexAffineBits(mesh)),"rejection mutated geometry");
        Check(faces.SequenceEqual(mesh.Faces),"rejection changed topology identities");Check(proxy==null?mesh.ProxyGraphics==null:proxy.SequenceEqual(mesh.ProxyGraphics!),"rejection changed cache");
    }
    private static void RegisterMeshDecompositionTests()
    {
        foreach(string kind in DecompositionKinds)foreach(bool reverse in new[]{false,true})foreach(int plane in new[]{0,1,2})foreach(int scale in new[]{0,1,2})
        {
            Run($"mesh-decompose/model/{kind}/{reverse}/{plane}/{scale}",()=>DecompositionModel(kind,reverse,plane,scale));
            foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
                Run($"mesh-decompose/wire/{kind}/{reverse}/{plane}/{scale}/{version}/{binary}",()=>DecompositionModel(kind,reverse,plane,scale,version,binary));
        }
        foreach(string kind in DecompositionKinds)foreach(bool reverse in new[]{false,true})
            Run($"mesh-decompose/closed/{kind}/{reverse}",()=>{var m=DecompositionMesh(kind,reverse,0,0);m.Faces[0]=m.Faces[0].Append(m.Faces[0][0]).ToArray();Equal(m.Vertexes.Count-2,Decompose(m).Count,"closed ring");});
        for(int variant=0;variant<12;variant++)
        {int v=variant;Run($"mesh-decompose/reject/{v}",()=>{var m=DecompositionMesh("quad",false,0,0);
            if(v==0)m.Faces[0]=null!;else if(v==1)m.Faces[0]=new[]{0,1};else if(v==2)m.Faces[0]=new[]{0,1,8};else if(v==3)m.Faces[0]=new[]{0,1,-1};
            else if(v==4)m.Faces[0]=new[]{0,2,1,3};else if(v==5)m.Faces[0]=new[]{0,1,1,2,3};else if(v==6)m.Faces[0]=new[]{0,1,2,1,3};
            else if(v==7)m.Vertexes[2]=new Vector3(4,4,4);else if(v==8)m.Vertexes[2]=new Vector3(double.NaN,4,3);
            else if(v==9)m.Vertexes[2]=new Vector3(double.PositiveInfinity,4,3);else if(v==10)m.Faces[0]=Enumerable.Repeat(0,1025).ToArray();
            else for(int i=0;i<4;i++)m.Vertexes[i]=new Vector3(i,0,0);
            DecompositionReject(m,()=>Decompose(m));});}
        foreach(int budget in new[]{-1,0,1})Run($"mesh-decompose/budget/{budget}",()=>{var m=DecompositionMesh("quad",false,0,0);DecompositionReject(m,()=>Decompose(m,true,budget));});
        Run("mesh-decompose/subdivision",()=>{var m=DecompositionMesh("concave",false,0,0);m.SubdivisionLevel=2;DecompositionReject(m,()=>Decompose(m));Equal(3,Decompose(m,true).Count,"explicit control cage");Equal((byte)2,m.SubdivisionLevel,"source subdivision");});
        foreach(string kind in DecompositionKinds)foreach(byte level in new byte[]{0,1,5,255})
            Run($"mesh-decompose/control/{kind}/{level}",()=>{var m=DecompositionMesh(kind,false,0,0);m.SubdivisionLevel=level;var before=VertexAffineBits(m);Equal(m.Vertexes.Count-2,Decompose(m,true,m.Vertexes.Count-2).Count,"explicit control triangle count");Check(before.SequenceEqual(VertexAffineBits(m)),"control source unchanged");Equal(level,m.SubdivisionLevel,"control subdivision retained");});
        foreach(double extent in new[]{double.Epsilon,double.MaxValue})
            Run($"mesh-decompose/extent/{BitConverter.DoubleToInt64Bits(extent):X16}",()=>{var m=new Mesh(new[]{new Vector3(-extent,-extent,0),new Vector3(extent,-extent,0),new Vector3(extent,extent,0),new Vector3(-extent,extent,0)},new[]{new[]{0,1,2,3}});var list=Decompose(m);Equal(2,list.Count,"extreme range triangulation");foreach(var f in list)foreach(var p in new[]{f.FirstVertex,f.SecondVertex,f.ThirdVertex})Check(m.Vertexes.Contains(p),"exact extreme source vertex");});
        Run("mesh-decompose/extension-dictionary",()=>{var m=DecompositionMesh("quad",false,0,0);var d=new DxfDocument();d.Entities.Add(m);var dictionary=new netDxf.Objects.DxfDictionary();d.Objects.SetExtensionDictionary(m,dictionary);DecompositionReject(m,()=>Decompose(m));Check(ReferenceEquals(dictionary,m.ExtensionDictionary),"source dictionary retained");});
        Run("mesh-decompose/nonunit-normal",()=>{var m=DecompositionMesh("quad",false,0,0);typeof(EntityObject).GetField("normal",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(m,new Vector3(0,0,2));DecompositionReject(m,()=>Decompose(m));});
        Run("mesh-decompose/empty",()=>{Equal(0,Decompose(new Mesh(Array.Empty<Vector3>(),Array.Empty<int[]>())).Count,"empty mesh");});
        Run("mesh-decompose/reactors",()=>{var m=DecompositionMesh("quad",false,0,0);m.PersistentReactors.Add(new Line());DecompositionReject(m,()=>Decompose(m));});
        Run("mesh-decompose/handle-xdata",()=>{var m=DecompositionMesh("quad",false,0,0);m.XData["DECOMPOSITION"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,"AB"));DecompositionReject(m,()=>Decompose(m));});
        Run("mesh-decompose/late-invalid-face",()=>{var m=DecompositionMesh("quad",false,0,0);m.Faces.Add(new[]{0,2,1,3});DecompositionReject(m,()=>Decompose(m));});
    }
}
