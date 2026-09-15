using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string corpus = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var results = new List<object>(); int failures = 0;
void Check(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
void Run(string name, Func<object?> action)
{
    try { var detail = action(); results.Add(new { name, passed = true, detail, error = (string?)null }); }
    catch (Exception error) { failures++; results.Add(new { name, passed = false, detail = (object?)null, error = error.ToString() }); }
}
Vector3[] Vertices() => new[] { Vector3.Zero, new Vector3(3,0,0), new Vector3(3,4,0), new Vector3(0,4,0) };
PolyfaceMesh Mesh(PolyfaceMeshFace? face = null) => new(Vertices(), new[] { face ?? new PolyfaceMeshFace(new short[] {1,-2,3}) });
using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpus, "manifest.json")));
foreach (var entry in manifest.RootElement.GetProperty("cases").EnumerateArray())
{
    string name = entry.GetProperty("name").GetString()!;
    Run("wire/" + name, () =>
    {
        byte[] source = File.ReadAllBytes(Path.Combine(corpus, "fixtures", name));
        Check(Convert.ToHexString(SHA256.HashData(source)).Equals(entry.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Independent source hash changed");
        bool reject = entry.GetProperty("reject").GetBoolean();
        using var input = new MemoryStream(source);
        DxfDocument? doc = null; Exception? loadError = null;
        try { doc = DxfDocument.Load(input); } catch (Exception error) { loadError = error; }
        Check(input.CanRead, "Loader closed caller stream");
        if (reject)
        {
            Check(doc == null, "Malformed active face index/duplicate/empty face was accepted");
            return new { rejection = loadError?.GetType().FullName ?? "null result" };
        }
        Check(doc != null, "Usable input rejected: " + loadError);
        var mesh = doc!.Entities.PolyfaceMeshes.Single();
        Check(mesh.Vertexes.SequenceEqual(Vertices()), "Coordinate order/value changed");
        short[] expected = entry.GetProperty("expected").EnumerateArray().Select(v => v.GetInt16()).ToArray();
        Check(mesh.Faces.Count == 1 && mesh.Faces[0].VertexIndexes.SequenceEqual(expected),
            "Face slot/first-zero/signed-index semantics changed: " + string.Join(",", mesh.Faces[0].VertexIndexes));
        var line = doc.Entities.Lines.Single();
        Check(line.StartPoint == new Vector3(71,72,73) && line.EndPoint == new Vector3(81,82,83), "Following LINE changed");
        var exploded = mesh.Explode();
        Check(exploded.Count == 1, "Face explosion changed cardinality");
        Check(expected.Length == 1 ? exploded[0] is Point : expected.Length == 2 ? exploded[0] is Line : exploded[0] is Face3D, "Face arity changed during explosion");
        bool binary = entry.GetProperty("binary").GetBoolean();
        using var encoded = new MemoryStream();
        Check(doc.Save(encoded, binary) && encoded.CanWrite, "Output failed or closed stream");
        File.WriteAllBytes(Path.Combine(output, name), encoded.ToArray());
        using var saved = new MemoryStream(encoded.ToArray());
        var reloaded = DxfDocument.Load(saved);
        Check(reloaded != null && reloaded.Entities.PolyfaceMeshes.Single().Faces.Single().VertexIndexes.SequenceEqual(expected), "Roundtrip changed face indices");
        return new { vertices = mesh.Vertexes.Length, faceIndices = expected, binary };
    });
}
Run("clone/face-null-inheritance", () =>
{
    var face = new PolyfaceMeshFace(new short[] {1,2,3}); var clone = (PolyfaceMeshFace)face.Clone();
    Check(clone.Layer == null && clone.Color == null, "Null inheritance materialized");
    Check(!ReferenceEquals(face.VertexIndexes, clone.VertexIndexes), "Face clone shares indices"); return null;
});
Run("clone/mesh-face-identity", () =>
{
    var source = Mesh(); var clone = (PolyfaceMesh)source.Clone();
    Check(!ReferenceEquals(source.Faces[0], clone.Faces[0]), "Mesh clone shares mutable face identity"); return null;
});
Run("clone/mesh-face-indices", () =>
{
    var source = Mesh(); var clone = (PolyfaceMesh)source.Clone(); clone.Faces[0].VertexIndexes[0] = 4;
    Check(source.Faces[0].VertexIndexes[0] == 1, "Clone edit changed source face index"); return null;
});
Run("clone/clone-layer-events", () =>
{
    var source = Mesh(); var clone = (PolyfaceMesh)source.Clone(); int a=0,b=0;
    source.PolyfaceMeshFaceLayerChanged += (_,_) => a++; clone.PolyfaceMeshFaceLayerChanged += (_,_) => b++;
    clone.Faces[0].Layer = new Layer("CLONE_FACE");
    Check(a==0 && b==1 && source.Faces[0].Layer==null, "Clone edit invoked source event or changed source layer"); return null;
});
Run("clone/source-layer-events", () =>
{
    var source = Mesh(); var clone = (PolyfaceMesh)source.Clone(); int a=0,b=0;
    source.PolyfaceMeshFaceLayerChanged += (_,_) => a++; clone.PolyfaceMeshFaceLayerChanged += (_,_) => b++;
    source.Faces[0].Layer = new Layer("SOURCE_FACE");
    Check(a==1 && b==0 && clone.Faces[0].Layer==null, "Source edit invoked clone event or changed clone layer"); return null;
});
Run("clone/assigned-resources", () =>
{
    var face = new PolyfaceMeshFace(new short[] {1,2,3}) { Layer = new Layer("FACE"), Color = new AciColor(2) };
    var clone = (PolyfaceMeshFace)face.Clone();
    Check(!ReferenceEquals(face.Layer,clone.Layer) && !ReferenceEquals(face.Color,clone.Color), "Face clone shares assigned resources");
    Check(clone.Layer.Name=="FACE" && clone.Color.Index==2, "Face clone changed assigned resources"); return null;
});
Run("clone/coordinates-control", () =>
{
    var source=Mesh(); var clone=(PolyfaceMesh)source.Clone(); clone.Vertexes[0]=new Vector3(100,200,300);
    Check(source.Vertexes[0]==Vector3.Zero, "Clone changed source coordinates"); return null;
});
Run("author/zero-placeholder-rejection", () =>
{
    bool rejected=false;try { _=Mesh(new PolyfaceMeshFace()); }catch(ArgumentException){rejected=true;}
    Check(rejected,"Empty default face admitted to complete mesh");return null;
});
Run("author/trailing-zero-semantics", () =>
{
    var mesh=Mesh(new PolyfaceMeshFace(new short[]{1,-2,0,short.MinValue}));
    Check(mesh.Explode().Single() is Line,"Trailing slots after zero altered authored face arity");
    var doc=new DxfDocument();doc.Entities.Add(mesh);using var stream=new MemoryStream();Check(doc.Save(stream),"Padded face save rejected");
    stream.Position=0;var copy=DxfDocument.Load(stream);
    Check(copy!=null && copy.Entities.PolyfaceMeshes.Single().Faces.Single().VertexIndexes.SequenceEqual(new short[]{1,-2}),"Padded face output semantics changed");return null;
});
foreach(short invalid in new short[]{5,short.MinValue,32767,0})
Run("author/mutable-preflight/"+invalid, () =>
{
    var mesh=Mesh();var doc=new DxfDocument();doc.Entities.Add(mesh);mesh.Faces[0].VertexIndexes[0]=invalid;
    var handleProperty=typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.Instance|BindingFlags.NonPublic)!;
    object before=handleProperty.GetValue(doc)!;using var stream=new MemoryStream();stream.WriteByte(0xA5);
    bool rejected=false;try{rejected=!doc.Save(stream);}catch{rejected=true;}
    Check(rejected,"Invalid mutable face exported");
    Check(stream.CanWrite && stream.ToArray().SequenceEqual(new byte[]{0xA5}),"Rejected export changed or closed destination stream");
    Check(Equals(before,handleProperty.GetValue(doc)),"Rejected export allocated handles");return null;
});
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new
{
    librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),
    passed=results.Count-failures,failed=failures,results
},new JsonSerializerOptions{WriteIndented=true})+"\n");
Console.WriteLine($"Independent polyface probe: {results.Count-failures} passed, {failures} failed.");
return failures==0?0:1;
