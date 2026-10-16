using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow.Exchange.Dwg;

internal static class HatchExchangeVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var version in DrawingExchange.WriteVersions)
        foreach (var style in Enum.GetValues<HatchStyle>())
        foreach (bool binary in new[] { false, true })
        tests.Add(($"exchange/hatch {version} {style} binary-source={binary} preserves flags and seeds", () =>
        {
            var original = Fixture(style); var bytes = Save(original, binary); var originalBytes = bytes.ToArray(); var originalInput = bytes;
            var codec = new DrawingExchange(); byte[] encoded = [];
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var dwg = codec.Convert(bytes, DrawingEncoding.Dxf, DrawingEncoding.Dwg, version);
                Require(dwg.OutputVersion == version, "Writer profile identity changed");
                encoded = dwg.Content.ToArray();
                bytes = codec.Convert(encoded, DrawingEncoding.Dwg, DrawingEncoding.Dxf).Content.ToArray();
                using var input = new MemoryStream(bytes);
                var loaded = DxfDocument.Load(input) ?? throw new InvalidDataException("Invalid hatch transport");
                var nested = loaded.Blocks["HATCH_PART"].Entities.OfType<Hatch>().Single();
                var model = loaded.Entities.Hatches.Single();
                Require(nested.Pattern.Style == style, "Nested hatch island style changed");
                Require(model.Pattern.Style == (HatchStyle)(((int)style + 1) % 3), "Independent model-space style changed");
                Require(nested.SeedPoints.SequenceEqual(new[] { new Vector2(-10,-20),new Vector2(3.5,7.25) }), "Seed points grew, changed or reordered");
                Require(model.SeedPoints.Count == 0, "Empty seed list gained an elevation/default point");
                Require(nested.Pattern.Type == HatchType.Custom && !nested.Pattern.IsDouble && !nested.Associative,
                    "Pattern type/double/associative flags changed");
                Near(nested.Pattern.Angle, 30); Near(nested.Pattern.Scale, 5);
                Near(nested.Elevation, 2); Near(nested.Normal.Z, 1);
                Require(nested.BoundaryPaths.Count == 2 && model.BoundaryPaths.Count == 2, "Nested analytic boundaries changed");
                var radius = nested.BoundaryPaths[0].Edges.OfType<HatchBoundaryPath.Arc>().Single().Radius;
                Near(radius,20);
            }
            Require(originalInput.SequenceEqual(originalBytes), "Exchange mutated caller input bytes");
            Require(original.Blocks["HATCH_PART"].Entities.OfType<Hatch>().Single().Pattern.Style == style, "Original source object changed");
            var directory = Path.Combine("artifacts","dwg-exchange","hatches"); Directory.CreateDirectory(directory);
            var name = $"{version}-{style}-{(binary ? "binary" : "text")}";
            File.WriteAllBytes(Path.Combine(directory,name+".dwg"),encoded);
            File.WriteAllBytes(Path.Combine(directory,name+".dxf"),bytes);
            File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(new {
                provider=DrawingExchange.Provider, source=Environment.GetEnvironmentVariable("GITHUB_SHA"),
                version, style=(int)style, binarySource=binary, repetitions=3, nestedSeedCount=2, modelSeedCount=0
            }));
        }));
        tests.Add(("exchange/hatch seed inventory budget refuses output without mutating input", () =>
        {
            var bytes=Save(Fixture(HatchStyle.Outer),false); var retained=bytes.ToArray();
            try
            {
                new DrawingExchange(new(MaximumHatchSeedPoints:1)).Convert(bytes,DrawingEncoding.Dxf,DrawingEncoding.Dwg);
                throw new Exception("Expected bounded hatch exchange refusal");
            }
            catch (DrawingExchangeException error)
            { Require(error.Message.Contains("seed-point budget"), "Expected the explicit semantic seed budget"); }
            Require(bytes.SequenceEqual(retained), "Rejected conversion mutated caller bytes");
        }));
    }

    private static DxfDocument Fixture(HatchStyle style)
    {
        var document=new DxfDocument();
        var block=new Block("HATCH_PART"); block.Entities.Add(Rings(style, true));
        document.Entities.Add(new Insert(block,new Vector3(75,25,0)));
        document.Entities.Add(Rings((HatchStyle)(((int)style+1)%3), false));
        return document;
    }
    private static Hatch Rings(HatchStyle style, bool seeds)
    {
        var pattern=new HatchPattern("SYNTHETIC") { Style=style, Type=HatchType.Custom, Angle=30, Scale=5 };
        var line=new HatchPatternLineDefinition { Angle=30, Origin=new(1,2), Delta=new(.5,2) };
        line.DashPattern.AddRange([1,-1]);pattern.LineDefinitions.Add(line);
        var hatch=new Hatch(pattern,new[] {20.0,8.0}.Select(radius=>new HatchBoundaryPath(new HatchBoundaryPath.Edge[] {
            new HatchBoundaryPath.Arc { Center=new(0,0), Radius=radius,StartAngle=0,EndAngle=360,IsCounterclockwise=true }
        })),false) { Elevation=2 };
        hatch.SeedPoints.Clear();
        if(seeds){hatch.SeedPoints.Add(new(-10,-20));hatch.SeedPoints.Add(new(3.5,7.25));}
        return hatch;
    }
    private static byte[] Save(DxfDocument document,bool binary)
    {using var stream=new MemoryStream();Require(document.Save(stream,binary),"Cannot serialize native fixture");return stream.ToArray();}
    private static void Near(double actual,double expected) => Require(Math.Abs(actual-expected)<1e-8,$"Expected {expected}, got {actual}");
    private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
}
