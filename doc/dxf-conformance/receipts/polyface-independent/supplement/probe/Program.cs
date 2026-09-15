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
Run("magnitude/min-short-existing-coordinate", () =>
{
    var coordinates=Enumerable.Range(0,32768).Select(i=>new Vector3(i,1,2)).ToArray();
    var mesh=new PolyfaceMesh(coordinates,new[]{new short[]{short.MinValue,2,3}});
    var face=(Face3D)mesh.Explode().Single();
    Check(face.FirstVertex==coordinates[32767],"Signed minimum was not resolved with int magnitude");
    Check((face.EdgeFlags&Face3DEdgeFlags.First)!=0,"Signed minimum lost hidden edge");return null;
});
Run("magnitude/unreferenced-extra-coordinates", () =>
{
    var coordinates=Enumerable.Range(0,32769).Select(i=>new Vector3(i,1,2)).ToArray();
    var mesh=new PolyfaceMesh(coordinates,new[]{new short[]{1,2,3}});
    Check(mesh.Vertexes.Length==32769 && mesh.Explode().Count==1,"Unreferenced coordinates introduced an undocumented total-count rejection");return null;
});
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new
{
    librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),
    passed=results.Count-failures,failed=failures,results
},new JsonSerializerOptions{WriteIndented=true})+"\n");
Console.WriteLine($"Independent polyface probe: {results.Count-failures} passed, {failures} failed.");
return failures==0?0:1;
