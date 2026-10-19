using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using netDxf.Units;
using Att = netDxf.Entities.Attribute;

internal static class InsertAttributeConstructionVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("insert-attributes/" + name, run));
        foreach (bool multiline in new[] { false, true }) foreach (bool binary in new[] { false, true })
            Test("explicit preview defaults and native geometry survive history and DXF " + multiline + "/" + binary, () =>
            {
                var (doc, block, source) = Seed(multiline); var before = source.Current;
                var command = new DraftingCommand("INSERT", [new(100, 200)], Reference: Id(block), Scale: new(2, 2, 2), Rotation: 30, BasePoint: new(1, 2));
                var placement = new DraftingPlanner().Plan(before, command); var proposed = placement.Patch.Apply(before); var id = placement.Created.Single();
                proposed = InsertAttributeConstruction.Plan(proposed, id).Apply(proposed);
                Check(doc.Entities.Inserts.Count() == 1 && ReferenceEquals(before, source.Current), "Preview changed native source");
                var attributes = proposed.Read(id)["Attributes"].Items.ToArray();
                Check(attributes.Length == 3 && attributes.All(a => a["Owner"].Id == id), "Preview omitted owned attributes");
                Check(attributes[0]["TextData"]["Content"].Exists == multiline && attributes[0]["Definition"].Id == Id(block.AttributeDefinitions["TAG"]), "Preview lost text mode or definition");
                var rendered = new RenderCompiler().Compile(proposed).Items.Single(i => i.Entity == attributes[0].Id);
                Check(rendered.Text?.Content == (multiline ? "Default\\Pvalue" : "Fallback"), "Detached preview omitted the default attribute text");
                var editor = new EditorSession(source); editor.Execute("Insert", ShadowGraphPatch.Between(before, proposed));
                var inserted = doc.Entities.Inserts.Last(); var savedAttributes = inserted.Attributes.ToArray();
                var frame = inserted.GetTransformation(doc.DrawingVariables.InsUnits); var offset = inserted.Position - frame * block.Origin;
                var expected = Att.CreateTransformed(block.AttributeDefinitions["TAG"], frame, offset, doc.DrawingVariables.MirrText);
                Check(Vector3.Distance(inserted.Attributes[0].Position, expected.Position) < 1e-8 && Math.Abs(inserted.Attributes[0].Height - expected.Height) < 1e-8, "Preview adoption transformed attributes twice");
                for (int i = 0; i < 2; i++)
                {
                    editor.Undo(); Check(doc.Entities.Inserts.Count() == 1, "Insertion undo retained source objects");
                    editor.Redo(); Check(doc.Entities.Inserts.Last() == inserted && inserted.Attributes.SequenceEqual(savedAttributes), "Insertion redo lost attribute identity");
                }
                using var stream = new MemoryStream(); editor.SynchronizeSource(() => source.MutateSource(d => Check(d.Save(stream, binary), "Save failed")));
                stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
                Check(loaded.Entities.Inserts.Last().Attributes.Count == 3 && loaded.Entities.Inserts.Last().Attributes[0].HasMText == multiline, "Roundtrip changed insertion attributes");
            });
        Test("assigned values are independent and preserve embedded fallback, preset and constant defaults", () =>
        {
            var (_, block, source) = Seed(true); var before = source.Current;
            var plan = new DraftingPlanner().Plan(before, new("INSERT", [new(10, 20)], Reference: Id(block)));
            var proposed = plan.Patch.Apply(before); var id = plan.Created.Single();
            proposed = InsertAttributeConstruction.Plan(proposed, id, [new(Id(block.AttributeDefinitions["TAG"]), "Cooling\nwater"), new(Id(block.AttributeDefinitions["PRESET"]), "Explicit preset")]).Apply(proposed);
            var rows = proposed.Read(id)["Attributes"].Items.ToArray();
            Check(rows[0]["Value"].Text == "Fallback" && rows[0]["TextData"]["Content"]["Value"].Text == "Cooling\\Pwater", "Assignment lost independent fallback");
            Check(rows[1]["Value"].Text == "Explicit preset" && rows[2]["Value"].Text == "Fixed", "Preset or constant assignment differs");
            Check(block.AttributeDefinitions["TAG"].GetMText().Value == "Default\\Pvalue", "Insertion changed its definition");
        });
        Test("initialization preserves other references and insertion XData", () =>
        {
            var (doc, block, source) = Seed(false); var original = doc.Entities.Inserts.Single();original.Attributes[0].Value = "Keep";
            var empty = Insert.CreateWithAttributes(block, Array.Empty<Att>());empty.XData.Add(new XData(new netDxf.Tables.ApplicationRegistry("KEEP")));doc.Entities.Add(empty);source.Refresh();
            var plan = InsertAttributeConstruction.Plan(source.Current, Id(empty));source.Apply(plan);
            Check(original.Attributes[0].Value == "Keep" && empty.XData.ContainsAppId("KEEP"), "Initialization reset unrelated values or metadata");
        });
        Test("Studio insertion and agent preview include explicit attributes and replay portable assignments", () =>
        {
            var (doc, block, _) = Seed(false);using var seed=new MemoryStream();Check(doc.Save(seed),"Seed save failed");seed.Position=0;
            using var studio=new Studio(false);studio.Load(seed);studio.CadWorkflowStart(new(studio.CadRevision,"Attribute insertion"));
            var op=new CadInsertOperation(new("INSERT",[new(30,40)],Reference:Id(block)),[new(Id(block.AttributeDefinitions["TAG"]),"P-101")]);
            var revision=studio.CadRevision;var preview=J(studio.CadEditPreview(new(revision,"Insert with values",[new("insert",Insert:op)])));
            Check(studio.CadRevision==revision,"Preview changed the drawing");
            studio.CadEditPreviewApply(new(revision,preview.GetProperty("previewId").GetString()!));
            studio.CadInsert(new(studio.CadRevision,op with { Command=op.Command with { Points=[new(60,80)] } }));
            studio.History("undo",studio.CadRevision);studio.History("redo",studio.CadRevision);studio.CadWorkflowRoundTrip(new(studio.CadRevision));
            var replay=studio.CadWorkflowReplay(new(studio.CadRevision,studio.CadWorkflowExport()));Check(replay.Passed&&replay.Complete,J(replay).GetRawText());
            using var saved=new MemoryStream(studio.Save());var loaded=DxfDocument.Load(saved)!;
            Check(loaded.Entities.Inserts.Count()==3&&loaded.Entities.Inserts.Skip(1).All(i=>i.Attributes[0].Value=="P-101"),"Studio lost insertion values");
        });
        for(int fault=0;fault<5;fault++)
        {
            var f=fault;Test("invalid insertion assignment refuses whole plan "+f,()=>
            {
                var (_,block,source)=Seed(false);var before=source.Current;var placement=new DraftingPlanner().Plan(before,new("INSERT",[new(1,2)],Reference:Id(block)));
                var proposed=placement.Patch.Apply(before);var a=Id(block.AttributeDefinitions["TAG"]);
                ImmutableArray<InsertAttributeAssignment> values=f switch {
                    0=>[new(a,"first"),new(a,"second")],1=>[new(new("missing"),"foreign")],2=>[new(Id(block.AttributeDefinitions["CONST"]),"change")],
                    3=>[new(a,"line\nbreak")],_=>[new(a,"bad\0value")]};
                try{InsertAttributeConstruction.Plan(proposed,placement.Created.Single(),values);throw new Exception("Invalid assignment accepted");}
                catch(ArgumentException){}catch(InvalidOperationException){}
                Check(ReferenceEquals(before,source.Refresh()),"Rejected plan changed source");
            });
        }
    }
    private static (DxfDocument,Block,ShadowSession) Seed(bool multiline)
    {
        var block=new Block("ATTRIBUTE_INSERT"){Origin=new(3,4,0)};block.Record.Units=DrawingUnits.Inches;
        var tag=new AttributeDefinition("TAG"){Value="Fallback",Prompt="Equipment",Position=new(5,6,0),Height=2,Flags=AttributeFlags.Verify};
        if(multiline)tag.SetMText(new AttributeMText{Value="Default\\Pvalue",Position=new(5,6,0),Height=2});
        block.AttributeDefinitions.Add(tag);block.AttributeDefinitions.Add(new AttributeDefinition("PRESET"){Value="Preset",Flags=AttributeFlags.Predefined});
        block.AttributeDefinitions.Add(new AttributeDefinition("CONST"){Value="Fixed",Flags=AttributeFlags.Constant});
        var doc=new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018);doc.DrawingVariables.InsUnits=DrawingUnits.Millimeters;doc.Entities.Add(new Insert(block));
        return(doc,block,new(doc));
    }
    private static ObjectId Id(DxfObject value)=>new("H:"+value.Handle);
    private static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value,StudioJson.CreateOptions());
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
