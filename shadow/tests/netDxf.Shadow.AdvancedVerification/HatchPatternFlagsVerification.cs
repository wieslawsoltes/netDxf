using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class HatchPatternFlagsVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var type in Enum.GetValues<DraftingHatchPatternType>())
            foreach (var binary in new[] { false, true })
                tests.Add(($"authoring/hatch type-double read-edit and DXF flags {type} {binary}", () => Verify(type, binary)));
    }
    private static void Verify(DraftingHatchPatternType type, bool binary)
    {
        var document = new DxfDocument();
        document.Entities.Add(new Polyline2D([new Vector2(0,0), new Vector2(20,0), new Vector2(20,20), new Vector2(0,20)], true));
        var source = new ShadowSession(document); var editor = new EditorSession(source);
        var boundary = source.Current.DrawingEntities().Single().Id!.Value;
        var request = new DraftingHatchPattern("FLAGS", Scale:2,
            Lines:[new(0,Vec3.Zero,new(0,2))], Type:type, IsDouble:true);
        var plan = DraftingHatches.Create(source.Current,new([boundary],Pattern:request)); plan.Execute(editor);
        var id = plan.Created.Single(); var captured = DraftingHatches.ReadPattern(source.Current,id);
        Require(captured.Type == type && captured.IsDouble, "Readback lost native flags.");
        DraftingHatches.ChangePattern(source.Current,[id],captured with { Scale = 3 }).Execute(editor);
        var native = document.Entities.Hatches.Single().Pattern;
        Require(native.IsDouble && native.Type.ToString() == type.ToString(), "Pattern edit changed native flags.");
        using var stream = new MemoryStream();
        editor.SynchronizeSource(() => source.MutateSource(d => Require(d.Save(stream,binary), "DXF write failed.")));
        stream.Position = 0;
        var roundtrip = DxfDocument.Load(stream)!.Entities.Hatches.Single().Pattern;
        Require(roundtrip.IsDouble && roundtrip.Type.ToString() == type.ToString() && Math.Abs(roundtrip.Scale - 3) < 1e-7,
            "DXF round trip changed flags or scale.");
        var scene = new RenderCompiler().Compile(source.Current);
        var contours = scene.Items.Single(i => i.Entity == id).Contours;
        Require(contours.Any(c => c.Points.Length > 1 && Math.Abs(c.Points[0].Y - c.Points[^1].Y) < 1e-7), "Missing base line family.");
        var vertical = contours.Any(c => c.Points.Length > 1 && Math.Abs(c.Points[0].X - c.Points[^1].X) < 1e-7);
        Require(vertical == (type == DraftingHatchPatternType.UserDefined), "Double fill applies only to user-defined patterns.");
        editor.Undo(); Require(document.Entities.Hatches.Single().Pattern.IsDouble, "Undo lost double flag.");
        editor.Redo(); Require(document.Entities.Hatches.Single().Pattern.IsDouble, "Redo lost double flag.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
