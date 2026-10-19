using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class ResourceManagerVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static ObjectId Id(ShadowSession source, string table, string name) => source.Current.Document[table].Items.Single(s => s["Name"].Text == name).Id!.Value;
    private static void Refuses(Action action) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid resource operation was accepted."); }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("resource-manager/layer metadata current rename and DXF transport", () =>
        {
            var doc = new DxfDocument(); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayers.Plan(source.Current, new(null, "Pipes", ColorIndex: 4, Description: "Process lines", Transparency: 45, FrozenInNewViewports: true, Lineweight: "W35")).Execute(editor);
            var id = Id(source, "Layers", "Pipes"); var layer = doc.Layers["Pipes"];
            Check(layer.Color.Index == 4 && !layer.Color.UseTrueColor && layer.Transparency.Value == 45 && layer.IsFrozenInNewViewports && layer.Description == "Process lines" && layer.Lineweight == Lineweight.W35, "Layer fields were not applied to native source.");
            DraftingResources.Plan(source.Current, new("layer", "current", id)).Execute(editor);
            DraftingLayers.Plan(source.Current, new(id, "Process")).Execute(editor);
            Check(doc.DrawingVariables.CLayer == "Process", "Renaming current layer lost CLayer.");
            Refuses(() => DraftingLayers.Plan(source.Current, new(id, Frozen: true)));
            Refuses(() => DraftingResources.Plan(source.Current, new("layer", "delete", id)));
            editor.Undo(); Check(doc.Layers.Contains("Pipes") && doc.DrawingVariables.CLayer == "Pipes", "Layer rename undo lost header reference."); editor.Redo();
            using var stream = new MemoryStream(); Check(doc.Save(stream), "Layer save failed."); stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
            Check(loaded.Layers["Process"].Description == layer.Description && Math.Abs(loaded.Layers["Process"].Transparency.Value - 45) <= 1 && loaded.Layers["Process"].IsFrozenInNewViewports && loaded.DrawingVariables.CLayer == "Process", "Layer metadata failed transport.");
        }));
        tests.Add(("resource-manager/delete rejects references and reserved names before mutation", () =>
        {
            var doc = new DxfDocument(); var line = new Line(Vector2.Zero, Vector2.UnitX) { Layer = new Layer("Used") }; doc.Entities.Add(line); doc.Layers.Add(new("Unused"));
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var unchanged = source.Current;
            Refuses(() => DraftingResources.Plan(source.Current, new("layer", "delete", Id(source,"Layers","Used"))));
            Refuses(() => DraftingResources.Plan(source.Current, new("layer", "delete", Id(source,"Layers","0"))));
            Refuses(() => DraftingLayers.Plan(source.Current, new(Id(source,"Layers","Used"), "unused")));
            Check(ReferenceEquals(unchanged, source.Current), "Rejected layer edit published source changes.");
            var unused = doc.Layers["Unused"]; DraftingResources.Plan(source.Current, new("layer", "delete", Id(source,"Layers","Unused"))).Execute(editor);
            Check(!doc.Layers.Contains("Unused"), "Unused layer was not removed."); editor.Undo(); Check(ReferenceEquals(doc.Layers["Unused"], unused), "Layer undo lost identity."); editor.Redo();
        }));
        tests.Add(("resource-manager/complex linetype construction reorder transport and current", () =>
        {
            var doc = new DxfDocument(); doc.ShapeStyles.Add(new ShapeStyle("Symbols", "symbols.shx")); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            var segments = ImmutableArray.Create(new LinetypeSegmentEdit("simple", 4), new LinetypeSegmentEdit("text", -2, "GAS", "Standard", .3, .5, .7, 15, "Upright"), new LinetypeSegmentEdit("shape", -1, "VALVE", "Symbols", -.5, .1, 1.2, 90, "Absolute", 12));
            DraftingResources.Plan(source.Current, new("linetype", "save", Linetype: new("Services", "Service annotation", segments))).Execute(editor);
            var id = Id(source,"Linetypes","Services"); var line = doc.Linetypes["Services"];
            Check(DraftingResources.Linetypes(source.Current).Single(l => l.Status.Name == "Services").Segments.Select(s => s.Kind).SequenceEqual(new[] { "simple", "text", "shape" }), "Manager metadata lost segment kinds.");
            Check(line.Segments.Count == 3 && line.Segments[1] is LinetypeTextSegment { Text: "GAS", RotationType: LinetypeSegmentRotationType.Upright } && line.Segments[2] is LinetypeShapeSegment { ShapeNumber: 12 }, "Complex segments lost native semantics.");
            DraftingResources.Plan(source.Current, new("linetype", "current", id)).Execute(editor);
            new DraftingPlanner().Plan(source.Current, new("LINE", [new(0,0,0),new(1,0,0)])).Execute(editor);
            Check(doc.Entities.Lines.Single().Linetype == line, "New geometry ignored current linetype.");
            Refuses(() => DraftingResources.Plan(source.Current,new("linetype","delete",id)));
            DraftingResources.Plan(source.Current, new("linetype","save",id,Linetype:new("Services2","Changed",segments.Reverse().ToImmutableArray()))).Execute(editor);
            Check(doc.DrawingVariables.CeLtype == "Services2" && line.Segments[0] is LinetypeShapeSegment, "Rename/reorder lost references.");
            editor.Undo();Check(doc.Linetypes.Contains("Services") && line.Segments[0] is LinetypeSimpleSegment,"Linetype undo lost original pattern.");editor.Redo();
            using var stream = new MemoryStream(); Check(doc.Save(stream), "Complex linetype save failed.");stream.Position=0;var loaded=DxfDocument.Load(stream)!;
            Check(loaded.Linetypes["Services2"].Segments[0] is LinetypeShapeSegment { ShapeNumber:12 } && loaded.Linetypes["Services2"].Segments[1] is LinetypeTextSegment { Text:"GAS" }, "Complex linetype failed DXF roundtrip without local SHX resolution.");
        }));
        tests.Add(("resource-manager/text font modes flags and undo preserve native references", () =>
        {
            var doc=new DxfDocument();var source=new ShadowSession(doc);var editor=new EditorSession(source);
            DraftingResources.Plan(source.Current,new("textstyle","save",TextStyle:new("Notes",FontFile:"romans.shx",BigFont:"bigfont.shx",Height:3,WidthFactor:.8,ObliqueAngle:12,Vertical:true,Backward:true,UpsideDown:true))).Execute(editor);
            var id=Id(source,"TextStyles","Notes");var style=doc.TextStyles["Notes"];
            Check(style.FontFile=="romans.shx"&&style.BigFont=="bigfont.shx"&&style.IsVertical&&style.IsBackward&&style.IsUpsideDown&&style.WidthFactor==.8,"SHX text style construction lost fields.");
            DraftingResources.Plan(source.Current,new("textstyle","current",id)).Execute(editor);
            new DraftingPlanner().Plan(source.Current,new("TEXT",[new(0,0,0)],Value:2,Text:"Test")).Execute(editor);
            Check(doc.Entities.Texts.Single().Style==style && doc.Entities.Texts.Single().Height == 3 && doc.Entities.Texts.Single().WidthFactor == .8 && doc.Entities.Texts.Single().IsBackward,"New text ignored current style geometry.");
            DraftingResources.Plan(source.Current,new("textstyle","save",id,TextStyle:new("Notes TTF",FontFile:"",BigFont:"",FontFamily:"Arial",FontStyle:"BoldItalic",Height:0,Vertical:false))).Execute(editor);
            Check(style.FontFile==""&&style.BigFont==""&&style.FontFamilyName=="Arial"&&style.FontStyle==(FontStyle.Bold|FontStyle.Italic)&&doc.DrawingVariables.TextStyle=="Notes TTF","TrueType family update failed.");
            editor.Undo();Check(style.FontFile=="romans.shx"&&style.BigFont=="bigfont.shx"&&style.FontFamilyName==""&&style.IsVertical,"Text style undo lost SHX state.");editor.Redo();
            DraftingResources.Plan(source.Current,new("textstyle","save",id,TextStyle:new("Notes TTF",FontFile:"simplex.shx",BigFont:"bigfont.shx",FontFamily:"",FontStyle:"Regular"))).Execute(editor);
            Check(style.FontFile=="simplex.shx"&&style.BigFont=="bigfont.shx"&&style.ExtendedFontData is null,"Switching family to SHX failed.");editor.Undo();editor.Redo();editor.Undo();
            using var stream=new MemoryStream();Check(doc.Save(stream),"Text style save failed.");stream.Position=0;var loaded=DxfDocument.Load(stream)!;
            Check(loaded.TextStyles["Notes TTF"].FontFamilyName=="Arial"&&loaded.TextStyles["Notes TTF"].IsBackward&&loaded.DrawingVariables.TextStyle=="Notes TTF","Text style DXF roundtrip failed.");

        }));
        tests.Add(("resource-manager/empty patterns can be created and unused resources can be removed", () =>
        {
            var doc = new DxfDocument(); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingResources.Plan(source.Current, new("linetype", "save", Linetype: new("Plain"))).Execute(editor);
            Check(doc.Linetypes["Plain"].Segments.Count == 0, "Default empty pattern was not continuous.");
            var id = Id(source, "Linetypes", "Plain"); DraftingResources.Plan(source.Current, new("linetype", "delete", id)).Execute(editor);
            Check(!doc.Linetypes.Contains("Plain"), "Unused linetype was not deleted."); editor.Undo(); editor.Redo();
            DraftingResources.Plan(source.Current, new("textstyle", "save", TextStyle: new("Unused"))).Execute(editor);
            var style = doc.TextStyles["Unused"]; DraftingResources.Plan(source.Current, new("textstyle", "delete", Id(source, "TextStyles", "Unused"))).Execute(editor);
            Check(!doc.TextStyles.Contains("Unused"), "Unused text style was not deleted."); editor.Undo(); Check(ReferenceEquals(style, doc.TextStyles["Unused"]), "Text style deletion undo lost identity.");
        }));
        tests.Add(("resource-manager/font face names match native individual flag bits", () =>
        {
            var doc = new DxfDocument(); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            foreach (var (name, expected) in new[] { ("Italic", FontStyle.Italic), ("Bold", FontStyle.Bold), ("BoldItalic", FontStyle.Italic | FontStyle.Bold) })
            {
                DraftingResources.Plan(source.Current, new("textstyle", "save", TextStyle: new(name, FontFile: "", BigFont: "", FontFamily: "Arial", FontStyle: name))).Execute(editor);
                Check(doc.TextStyles[name].FontStyle == expected, "Native font face flags are reversed.");
                Check(DraftingResources.TextStyles(source.Current).Single(s => s.Status.Name == name).FontStyle == name, "Manager font face is not canonical.");
            }
        }));
        tests.Add(("resource-manager/invalid bounds and unsupported definitions leave source untouched", () =>
        {
            var source=new ShadowSession(new DxfDocument());var before=source.Current;
            Refuses(()=>DraftingLayers.Plan(source.Current,new(null,"Invalid",Transparency:91)));
            Refuses(()=>DraftingLayers.Plan(source.Current,new(null,"Invalid",ColorIndex:256)));
            Refuses(()=>DraftingResources.Plan(source.Current,new("textstyle","save",TextStyle:new("Invalid",FontFile:"font.shx",WidthFactor:double.NaN))));
            Refuses(()=>DraftingResources.Plan(source.Current,new("textstyle","save",TextStyle:new("Invalid",FontFile:"font.ttf",BigFont:"big.shx"))));
            Refuses(()=>DraftingResources.Plan(source.Current,new("linetype","save",Linetype:new("Invalid",Segments:[new("shape",1,"Valve","missing",ShapeNumber:1)]))));
            Refuses(()=>DraftingResources.Plan(source.Current,new("linetype","save",Linetype:new("Invalid",Segments:Enumerable.Repeat(new LinetypeSegmentEdit("simple",1),257).ToImmutableArray()))));
            Refuses(()=>DraftingResources.Plan(source.Current,new("linetype","save",Id(source,"Linetypes","Continuous"),Linetype:new("Continuous"))));
            Check(ReferenceEquals(before,source.Current),"Invalid resource request mutated source.");
        }));
    }
}
