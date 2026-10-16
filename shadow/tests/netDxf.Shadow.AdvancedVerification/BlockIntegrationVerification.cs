using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;

internal static class BlockIntegrationVerification
{
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.CadState(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static ObjectId Block(Studio studio, string name) => new(State(studio).GetProperty("blocks").EnumerateArray()
        .Single(b => b.GetProperty("name").GetString() == name).GetProperty("id").GetString()!);
    private static void Check(bool condition)
    { if (!condition) throw new InvalidOperationException("Block integration assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static Studio Part()
    {
        var studio = new Studio(false);
        studio.CadCommand(new(studio.CadRevision, new("CIRCLE", [new(10, 10)], Value: 5)));
        studio.CadCreateBlock(new(studio.CadRevision, "Part", new(10, 10)));
        return studio;
    }
    public static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("block-integration/published disposition wire values remain stable", () =>
        {
            Check((int)BlockSourceDisposition.Retain == 0 && (int)BlockSourceDisposition.ConvertToInsert == 1 &&
                (int)BlockSourceDisposition.Delete == 2);
            Check(BlockSourceDisposition.Convert == BlockSourceDisposition.ConvertToInsert &&
                BlockSourceDisposition.Remove == BlockSourceDisposition.Delete);
            Check(Enum.GetValues<BlockSourceDisposition>().Distinct().Count() == 3);
        }));
        tests.Add(("block-integration/definition workspace inherits and isolates perspective settings", () =>
        {
            using var studio = Part();
            studio.SetPerspectiveView(new(studio.CadRevision, "TargetPlaneHeight", 7));
            studio.CadBeginBlock(new(studio.CadRevision, Block(studio, "Part").Value));
            var branch = State(studio).GetProperty("state");
            Check(branch.GetProperty("perspectiveMode").GetString() == "TargetPlaneHeight" &&
                branch.GetProperty("perspectiveWorkPlaneZ").GetDouble() == 7);
            studio.SetPerspectiveView(new(studio.CadRevision, "Disabled", 0));
            studio.CadFinishBlock(new(studio.CadRevision, branch.GetProperty("sourceRevision").GetInt64(), false));
            var live = State(studio).GetProperty("state");
            Check(live.GetProperty("perspectiveMode").GetString() == "TargetPlaneHeight" &&
                live.GetProperty("perspectiveWorkPlaneZ").GetDouble() == 7 && !studio.CadHasBranches);
        }));
        tests.Add(("block-integration/existing block hatch and rename APIs edit only the open definition", () =>
        {
            using var studio = Part(); var before = State(studio).GetProperty("state");
            studio.CadBeginBlock(new(studio.CadRevision, Block(studio, "Part").Value));
            studio.CadSelect(new(studio.CadRevision, "all"));
            studio.CadDefineBlock(new(studio.CadRevision, new("Inner", Vec3.Zero, [])));
            studio.CadCreateHatch(new(studio.CadRevision, new(Polygons:
                [[new(10, 0), new(20, 0), new(20, 10), new(10, 10)]])));
            studio.CadRenameBlock(new(studio.CadRevision, Block(studio, "Inner"), "Nested"));
            var branch = State(studio); Check(branch.GetProperty("itemCount").GetInt32() == 2);
            studio.CadFinishBlock(new(studio.CadRevision, before.GetProperty("sourceRevision").GetInt64(), true));
            Check(State(studio).GetProperty("state").GetProperty("undoCount").GetInt32() ==
                before.GetProperty("undoCount").GetInt32() + 1);
            using var stream = new MemoryStream(studio.Save());
            var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Native reload failed.");
            Check(document.Entities.All.Count() == 1 && document.Entities.Inserts.Single().Block.Name == "Part");
            Check(document.Blocks["Part"].Entities.Count == 2 && document.Blocks["Part"].Entities.OfType<Hatch>().Count() == 1);
            Check(document.Blocks["Part"].Entities.OfType<Insert>().Single().Block == document.Blocks["Nested"] &&
                document.Blocks["Nested"].Entities.OfType<Circle>().Count() == 1);
            studio.History("undo", studio.CadRevision);
            var undone = State(studio);
            Check(undone.GetProperty("blocks").EnumerateArray().All(b => b.GetProperty("name").GetString() != "Nested"));
            Check(undone.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "Part")
                .GetProperty("entities").GetInt32() == 1);
            studio.History("redo", studio.CadRevision);
            Check(State(studio).GetProperty("itemCount").GetInt32() == 1);
        }));
        tests.Add(("block-integration/definition authoring rejects foreign layout sources", () =>
        {
            var document = new DxfDocument(); var part = new Block("Part");
            part.Entities.Add(new Circle(Vector3.Zero, 2)); document.Blocks.Add(part);
            document.Entities.Add(new Circle(new Vector3(20, 0, 0), 3));
            var source = new ShadowSession(document); var before = source.Current;
            var root = before.Document["Blocks"].Items.Single(b => b["Name"].Text == "Part").Id!.Value;
            var foreign = before.DrawingEntities().Single().Id!.Value;
            Throws<ArgumentException>(() => DraftingHatches.CreateInDefinition(before, new([foreign]), root));
            Throws<ArgumentException>(() => new DraftingPlanner().PlanBlockDefinitionInDefinition(before,
                new("Foreign", Vec3.Zero, [foreign]), root));
            Check(ReferenceEquals(before, source.Current) && part.Entities.Count == 1 && !document.Blocks.Contains("Foreign"));
        }));
        tests.Add(("block-integration/cancelled save retains the edited branch and exact live source", () =>
        {
            using var studio = Part(); var live = studio.CadStamp;
            studio.CadBeginBlock(new(studio.CadRevision, Block(studio, "Part").Value));
            studio.CadCommand(new(studio.CadRevision, new("CIRCLE", [new(20, 0)], Value: 3)));
            var revision = studio.CadRevision;
            Throws<OperationCanceledException>(() => studio.CadFinishBlock(new(revision, live.Revision, true), new(true)));
            Check(studio.CadStamp == live && studio.CadRevision == revision && studio.CadHasBranches &&
                State(studio).GetProperty("blockEditor").GetProperty("changed").GetBoolean());
        }));
    }
}
