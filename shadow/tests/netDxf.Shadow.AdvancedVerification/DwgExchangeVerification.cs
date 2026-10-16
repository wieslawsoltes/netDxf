using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow.Exchange.Dwg;
using netDxf.Shadow.Studio;
using netDxf.Tables;

internal static class DwgExchangeVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var version in DrawingExchange.WriteVersions)
            tests.Add(($"exchange/DWG {version} native primitives and shared blocks round trip", () => Roundtrip(version)));
        tests.Add(("exchange/unqualified AC1014 output is refused without substituting another profile", () =>
        {
            Require(!DrawingExchange.WriteVersions.Contains("AC1014"), "Do not advertise a failed legacy qualification");
            try
            {
                new DrawingExchange().Convert(Dxf(Fixture()), DrawingEncoding.Dxf, DrawingEncoding.Dwg, "AC1014");
                throw new InvalidOperationException("Unqualified legacy export unexpectedly succeeded");
            }
            catch (ArgumentException error) { Require(error.Message.Contains("Unsupported DWG output version"), "Explicit profile admission refusal"); }
        }));
        tests.Add(("exchange/signature profile input budget and cancellation refusals", () =>
        {
            var codec = new DrawingExchange(); var bytes = Dxf(Fixture());
            Refuse(() => codec.Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dxf));
            Refuse(() => codec.Convert(bytes, DrawingEncoding.Dwg, DrawingEncoding.Dxf));
            Refuse(() => codec.Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg, "AC1021"));
            Refuse(() => codec.Convert("AC1032broken"u8.ToArray(), DrawingEncoding.Dwg, DrawingEncoding.Dxf));
            Refuse(() => new DrawingExchange(new(MaximumInputBytes:128)).Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Refuse(() => codec.Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg, cancellationToken:cancelled.Token));
            Require(DrawingExchange.IsDwg("AC1032"u8) && !DrawingExchange.IsDwg("ACX032"u8) && !DrawingExchange.IsDwg("AC"u8), "Signature detection");
        }));
        tests.Add(("exchange/output and entity budgets fail before publication", () =>
        {
            var bytes = Dxf(Fixture());
            Refuse(() => new DrawingExchange(new(MaximumOutputBytes:128)).Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg));
            Refuse(() => new DrawingExchange(new(MaximumEntities:1)).Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg));
        }));
        tests.Add(("exchange/failed encoded save leaves native drawing dirty and editable", () =>
        {
            using var studio = new Studio(false); using var hub = new StudioDocumentHub(studio);
            studio.CadCommand(new(studio.CadRevision, new("CIRCLE", [new(0,0)], Value:5)));
            var state = Json(studio.CadState()).GetProperty("state"); var context = new DefaultHttpContext();
            context.Request.Headers["X-Shadow-Document"] = state.GetProperty("documentId").GetString();
            context.Request.Headers["X-Shadow-Workspace"] = state.GetProperty("workspaceId").GetString();
            context.Request.Headers["X-Shadow-View"] = state.GetProperty("viewVersion").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var invoked = false; var sentinel = new IOException("Synthetic encoder failure");
            try { hub.Save("demo",context.Request,studio.CadRevision, _ => { invoked = true; throw sentinel; }); throw new InvalidOperationException("Encoding should fail"); }
            catch (IOException error) { Require(ReferenceEquals(error,sentinel), "Preserve original encoder exception"); }
            Require(invoked && Json(hub.Documents())[0].GetProperty("dirty").GetBoolean(), "Failed encoding must not mark saved");
            studio.History("undo",studio.CadRevision);
            Require(Json(studio.CadState()).GetProperty("itemCount").GetInt32()==0, "History remains usable after conversion failure");
        }));
    }
    private static void Roundtrip(string version)
    {
        var document = Fixture(); var input = Dxf(document); var original = input.ToArray(); var codec = new DrawingExchange();
        var encoded = codec.Convert(input,DrawingEncoding.Dxf,DrawingEncoding.Dwg,version);
        Require(input.SequenceEqual(original), "Codec mutated caller input");
        Require(Encoding.ASCII.GetString(encoded.Content.AsSpan()[..6])==version && encoded.Encoding==DrawingEncoding.Dwg, "Real DWG signature");
        Require(encoded.OutputVersion == version, "Validation transport must not replace output profile identity");
        var decoded = codec.Convert(encoded.Content.ToArray(),DrawingEncoding.Dwg,DrawingEncoding.Dxf);
        using var stream = new MemoryStream(decoded.Content.ToArray()); var loaded = DxfDocument.Load(stream)!;
        Require(loaded is not null && loaded.Entities.All.Count()==4, "Model-space root inventory");
        Require(loaded.Layouts.Any(l => l.IsPaperSpace && l.Viewport is not null), "Paper-space metadata viewport survives native transport");
        var line = loaded.Entities.Lines.Single(); Near(line.StartPoint.X,-12.5); Near(line.StartPoint.Y,17.25); Near(line.EndPoint.X,42.5);
        Require(line.Layer.Name=="GEOMETRY", "Layer assignment");
        var circle = loaded.Entities.Circles.Single(); Near(circle.Center.X,25);Near(circle.Center.Y,35);Near(circle.Radius,7.5);
        var text = loaded.Entities.Texts.Single();Require(text.Value=="DWG exchange", "Text content");Near(text.Height,3);
        var insert = loaded.Entities.Inserts.Single();Require(insert.Block.Name=="PART", "Named block reference");Near(insert.Position.X,70);Near(insert.Position.Y,20);
        Near(insert.Rotation,30);Near(insert.Scale.X,2);Require(insert.Block.Entities.Single() is Line, "Shared block definition");
        Require(encoded.NamedBlockCount==1 && decoded.NamedBlockCount==1, "Definition inventory");
        var directory = Path.Combine("artifacts","dwg-exchange"); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,version+".dwg"),encoded.Content.ToArray());
        File.WriteAllBytes(Path.Combine(directory,version+".dxf"),decoded.Content.ToArray());
        File.WriteAllText(Path.Combine(directory,version+".json"),JsonSerializer.Serialize(new { provider=DrawingExchange.Provider,
            source=Environment.GetEnvironmentVariable("GITHUB_SHA"),version,encodedBytes=encoded.Content.Length,
            entities=encoded.EntityCount,blocks=encoded.NamedBlockCount,diagnostics=encoded.Diagnostics.Concat(decoded.Diagnostics) }));
    }
    private static DxfDocument Fixture()
    {
        var document = new DxfDocument(); var layer = new Layer("GEOMETRY");
        document.Entities.Add(new Line(new Vector3(-12.5,17.25,0),new Vector3(42.5,-8,0)){Layer=layer});
        document.Entities.Add(new Circle(new Vector3(25,35,0),7.5){Layer=layer});
        document.Entities.Add(new Text("DWG exchange",new Vector3(0,60,0),3));
        var block = new Block("PART");block.Entities.Add(new Line(Vector3.Zero,new Vector3(10,0,0)));
        document.Entities.Add(new Insert(block,new Vector3(70,20,0)){Rotation=30,Scale=new Vector3(2,2,2)});
        return document;
    }
    private static byte[] Dxf(DxfDocument document)
    { using var stream = new MemoryStream(); Require(document.Save(stream), "DXF fixture write");return stream.ToArray(); }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Near(double value,double expected) => Require(Math.Abs(value-expected)<1e-7,$"Expected {expected}, got {value}");
    private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private static void Refuse(Action action)
    { try { action(); } catch(Exception error) when(error is ArgumentException or InvalidOperationException or OperationCanceledException) {return;} throw new InvalidOperationException("Expected strict exchange refusal"); }
}
