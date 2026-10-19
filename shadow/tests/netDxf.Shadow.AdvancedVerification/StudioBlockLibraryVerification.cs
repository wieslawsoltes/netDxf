using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Studio;
using netDxf.Shadow.Symbols;
using netDxf.Tables;
using netDxf.Units;

internal static class StudioBlockLibraryVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (bool binary in new[] { false, true })
            tests.Add(("block-library/studio-preview-export-history/" + binary, () => Workflow(binary)));
        tests.Add(("block-library/studio-stale-proposal-and-cache-invalidation", () =>
        {
            using var studio = new Studio(false); var bytes = Seed(); var loaded = Json(studio.CadOpenBlockLibrary(studio.CadRevision, "pump.dxf", bytes));
            var operation = new CadBlockLibraryOperation("Pump", "Imported", loaded.GetProperty("libraryId").GetString());
            var proposal = Json(studio.CadEditPreview(new(studio.CadRevision, "Import pump", [new("block-library", BlockLibrary: operation)])));
            studio.CadCommand(new(studio.CadRevision, new("LINE", [new(0, 0), new(10, 0)])));
            Reject<ShadowConflictException>(() => studio.CadEditPreviewApply(new(studio.CadRevision, proposal.GetProperty("previewId").GetString()!)));
            Check(!Json(studio.CadState()).GetProperty("blocks").EnumerateArray().Any(b => b.GetProperty("name").GetString() == "Imported"), "Stale import changed source");
            using var empty = new MemoryStream(); new DxfDocument().Save(empty); empty.Position = 0; studio.Load(empty);
            Check(Json(studio.CadBlockLibraries()).GetArrayLength() == 0, "Loading another drawing retained library state");
            Reject<ShadowConflictException>(() => studio.CadEditPreview(new(studio.CadRevision, "Expired library", [new("block-library", BlockLibrary: operation)])));
        }));
        tests.Add(("block-library/studio-workflow-replays-without-upload-cache", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(studio.CadRevision, "Library import"));
            var loaded = Json(studio.CadOpenBlockLibrary(studio.CadRevision, "pump.dxf", Seed()));
            var operation = new CadBlockLibraryOperation("Pump", "Imported", loaded.GetProperty("libraryId").GetString());
            var proposal = Json(studio.CadEditPreview(new(studio.CadRevision, "Import pump", [new("block-library", BlockLibrary: operation)])));
            studio.CadEditPreviewApply(new(studio.CadRevision, proposal.GetProperty("previewId").GetString()!));
            studio.History("undo", studio.CadRevision); studio.History("redo", studio.CadRevision);
            var workflow = studio.CadWorkflowExport();
            var report = studio.CadWorkflowReplay(new(studio.CadRevision, workflow));
            Check(report.Passed, "Library workflow failed: " + Json(report));
        }));
    }

    private static byte[] Seed()
    {
        var document = new DxfDocument(); document.Layers["0"].Color = AciColor.Green;
        var block = new Block("Pump") { Origin = new(3, 4, 0), Description = "Library pump" }; block.Record.Units = DrawingUnits.Millimeters;
        block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "P-101" });
        var circle = new Circle(Vector3.Zero, 5); block.Entities.Add(circle);
        block.Entities.Add(new Hatch(HatchPattern.Solid, [new HatchBoundaryPath(new[] { circle })], true));
        block.EndRecord.XData.Add(new XData(new ApplicationRegistry("LIBRARY_META")));
        block.EndRecord.XData["LIBRARY_META"].XDataRecord.Add(new XDataRecord(XDataCode.String, "source"));
        document.Entities.Add(new Insert(block)); using var bytes = new MemoryStream(); document.Save(bytes); return bytes.ToArray();
    }
    private static void Workflow(bool binary)
    {
        using var studio = new Studio(false); var before = Json(studio.CadState()).GetProperty("state");
        var loaded = Json(studio.CadOpenBlockLibrary(studio.CadRevision, "pump.dxf", Seed()));
        Check(loaded.GetProperty("entries").GetArrayLength() == 2 && studio.CadRevision == before.GetProperty("revision").GetInt64(), "Library browse changed drawing or omitted model space");
        var operation = new CadBlockLibraryOperation("Pump", "Imported", loaded.GetProperty("libraryId").GetString());
        var proposal = Json(studio.CadEditPreview(new(studio.CadRevision, "Import pump", [new("block-library", BlockLibrary: operation)])));
        var id = proposal.GetProperty("previewId").GetString()!;
        var blockId = proposal.GetProperty("blockLibraries")[0].GetProperty("block").GetProperty("value").GetString()!;
        var image = studio.CadEditPreviewRender(id, true, backend: "svg", block: blockId);
        Check(image.Bytes.Length > 200 && proposal.GetProperty("blockLibraries")[0].GetProperty("resources").GetArrayLength() > 0, "Missing definition preview/resource report");
        Check(studio.CadRevision == before.GetProperty("revision").GetInt64(), "Preview mutated source");
        studio.CadEditPreviewApply(new(studio.CadRevision, id)); var imported = Json(studio.CadState());
        Check(imported.GetProperty("state").GetProperty("undoCount").GetInt32() == before.GetProperty("undoCount").GetInt32() + 1, "Import was not one undo entry");
        var state = Json(studio.CadState()).GetRawText(); var exported = studio.CadExportBlockLibrary(new(studio.CadRevision, new(blockId), binary));
        Check(Json(studio.CadState()).GetRawText() == state, "Export modified drawing/save state");
        using var bytes = new MemoryStream(exported); var document = DxfDocument.Load(bytes)!; var block = document.Blocks["Imported"];
        Check(block.Description == "Library pump" && block.Origin == new Vector3(3, 4, 0) && block.Record.Units == DrawingUnits.Millimeters, "Export changed insertion metadata");
        Check(block.Entities.OfType<Hatch>().Single().Associative && block.EndRecord.XData.ContainsAppId("LIBRARY_META"), "Export lost source links or ENDBLK metadata");
        studio.History("undo", studio.CadRevision); studio.History("redo", studio.CadRevision);
        Check(Json(studio.CadState()).GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "Imported").GetProperty("id").GetString() == blockId, "History replaced imported block identity");
        var library = DrawingBlockLibrary.Read(Seed()); var root = library.Entries.Single(e => e.Name == "Pump").Block;
        using var direct = new MemoryStream(DrawingBlockLibrary.Export(library.Snapshot, root, binary));
        Check(DxfDocument.Load(direct)!.Layers["0"].Color.Index == AciColor.Green.Index, "Standalone export lost the source layer-zero appearance");
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
