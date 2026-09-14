using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string ThirdFontName = "MIXED_FONT";
    private const string ThirdBlockName = "MIXED_BLOCK";
    private const string ThirdFamily = "Mixed Font 青";
    private const int ThirdFontBits = unchecked((int)0xF3123456);

    private static void RegisterThirdMixedModuleTests()
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"third-mixed/source/{v}/{b}", () => ThirdMixedSource(v,b));
        }
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2010, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            Run($"third-mixed/copy/{v}/{b}", () => ThirdMixedCopy(v,b));
        }
    }

    private static Body ThirdSatBody(DxfVersion version = DxfVersion.AutoCad2010)
    {
        var doc = DxfDocument.Load(Path.Combine("tests","fixtures","acis-sat",$"independent-acis-body-R{version.ToString().Replace("AutoCad","")}-ascii.dxf"))
            ?? throw new InvalidOperationException("Independent SAT fixture did not load");
        return (Body)doc.Entities.All.OfType<Body>().Single().Clone();
    }

    private static DxfDictionary ThirdDictionary(DxfDocument doc, string name)
    {
        if (doc.NamedObjects.Contains(name)) return (DxfDictionary)doc.NamedObjects[name];
        var result = new DxfDictionary(); doc.NamedObjects.Add(name,result); return result;
    }

    private static DxfDocument ThirdScaffold(DxfVersion version, int handleOffset = 0)
    {
        var doc = new DxfDocument(version);
        for (int i = 0; i < handleOffset; i++) doc.Layers.Add(new Layer("HANDLE_OFFSET_" + i));
        if (version >= DxfVersion.AutoCad2007)
        {
            string year = version.ToString().Replace("AutoCad", "");
            string fixture = Path.Combine("tests", "fixtures", "output-settings", $"independent-output-settings-R{year}.dxf");
            var independent = DxfDocument.Load(fixture)!;
            var visualClass = (DxfClass)independent.Classes["VISUALSTYLE"].Clone();
            visualClass.InstanceCount = 1;
            doc.Classes.Add(visualClass);
            using var fixtureStream = File.OpenRead(fixture);
            var visual = DxfRawDocument.Load(fixtureStream).Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "VISUALSTYLE");
            var payload = visual.Tags.SkipWhile(t => t.Code != 100).ToArray();
            Check(payload.Length != 0 && payload.All(t => t.ValueType != DxfTagValueType.Handle), "Independent visual payload must remain reference-free");
            // Prepare a schema-specific fixture with a fresh identity. Generic opaque cloning remains unsupported.
            using var initial = new MemoryStream(); Check(doc.Save(initial), "Scaffold save"); initial.Position = 0;
            var raw = DxfRawDocument.Load(initial);
            using var transaction = DxfRawObjectStore.Open(raw).BeginEdit();
            string root = transaction.EnsureRootDictionary();
            string handle = transaction.CreatePlaceholder(root, "MIXED_RENDER");
            raw = transaction.Commit();
            var placeholder = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "ACDBPLACEHOLDER" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == handle));
            var tags = new List<DxfTag> { new(0, "VISUALSTYLE"), new(5, handle), new(102, "{ACAD_REACTORS"), new(330, root), new(102, "}"), new(330, root) };
            tags.AddRange(payload);
            using var prepared = new MemoryStream(); raw.WithRecord(placeholder, tags).Save(prepared); prepared.Position = 0;
            doc = DxfDocument.Load(prepared) ?? throw new InvalidOperationException("Prepared visual fixture did not load");
            Equal(1, doc.Objects.Items.Count(o => o.CodeName == "VISUALSTYLE"), "Scaffold visual inventory");
            Check(!doc.NamedObjects.Contains("ACAD_PLOTSETTINGS"), "Scaffold imported unrelated named pages");
        }
        doc.BuildDimensionBlocks = true;
        return doc;
    }

    private static void ThirdLinks(DxfObject item, string role, params DxfObject[] references)
    {
        var data = new XData(new ApplicationRegistry("THIRD_MIXED"));
        data.XDataRecord.Add(new(XDataCode.String,role));
        foreach (var target in references) data.XDataRecord.Add(new(XDataCode.DatabaseHandle,target.Handle));
        item.XData.Add(data);
    }

    private static void ThirdHeader(DxfDocument doc)
    {
        doc.DrawingVariables.DimStyle = "MIXED_DIM";
        foreach (string name in new[] { "$DIMTSZ", "$DIMTVP", "$DIMUPT" }) doc.DrawingVariables.RemoveCustomVariable(name);
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$DIMTSZ",40,0.0));
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$DIMTVP",40,-2.0));
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$DIMUPT",70,(short)0));
    }

    private static MultiLeader ThirdLeader(DxfDocument doc, bool blockContent, DxfMLeaderStyle leaderStyle)
    {
        var font = doc.TextStyles[ThirdFontName]; var block = doc.Blocks[ThirdBlockName];
        var result = new MultiLeader { ColorName = "MIXED$青" };
        result.Properties.Style = leaderStyle;
        result.Properties.TextStyle = font;
        result.Properties.LeaderLinetype = doc.Linetypes["MIXED_LINE"];
        result.Properties.Block = block.Record;
        result.Properties.ContentType = blockContent ? (short)1 : (short)2;
        result.Context.BasePoint = new Vector3(3,4,0);
        result.Context.TextHeight = 2.25;
        if (doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2010)
        {
            result.Properties.TextAttachmentDirection = 0;
            result.Context.TopAttachment = 9; result.Context.BottomAttachment = 9;
        }
        if (doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2013) result.Properties.LeaderExtendToText = true;
        if (blockContent)
        {
            result.Context.Block = new MLeaderBlockContent { Block = block.Record, Position = new Vector3(3,4,0) };
            result.Properties.BlockAttributes.Add(new MLeaderBlockAttribute { Definition = block.AttributeDefinitions["LABEL"], Text = "Leader label", Index = 0, Width = 1.25 });
        }
        else result.Context.MText = new MLeaderMTextContent { Style = font, Text = "Mixed leader 青", Position = new Vector3(3,4,0), Width = 8, DefinedHeight = 3 };
        var branch = new MLeaderNode(); var line = new MLeaderLine();
        line.Vertices.Add(new Vector3(-2,-3,0)); line.Vertices.Add(new Vector3(3,4,0));
        branch.Lines.Add(line); result.Context.Leaders.Add(branch);
        ThirdLinks(result,blockContent ? "block leader" : "text leader");
        return result;
    }

    private static DxfDocument ThirdBuild(DxfVersion version)
    {
        var doc = ThirdScaffold(version);
        var font = new TextStyle(ThirdFontName,"Arial.ttf")
        {
            Flags = (TextStyleFlags)0x4074, TextGenerationFlags = 0x4006, LastHeight = 9.75,
            ExtendedFontData = new TextStyleFontData(ThirdFamily,ThirdFontBits)
        };
        var fontData = font.XData["ACAD"].XDataRecord;
        fontData.Add(XDataRecord.OpenControlString); fontData.Add(new(XDataCode.String,"font payload retained"));
        fontData.Add(new(XDataCode.Int32,73)); fontData.Add(XDataRecord.CloseControlString);
        doc.TextStyles.Add(font); doc.Linetypes.Add(new Linetype("MIXED_LINE"));
        var block = new Block(ThirdBlockName);
        block.Entities.Add(new Line(Vector3.Zero,new Vector3(5,0,0)));
        block.AttributeDefinitions.Add(new AttributeDefinition("LABEL") { Position = new Vector3(1,2,0), Height = 2, Value = "Definition", Style = font });
        if (version <= DxfVersion.AutoCad2010) block.Entities.Add(ThirdSatBody(version));
        doc.Blocks.Add(block);
        var dimStyle = new DimensionStyle("MIXED_DIM") { TextStyle = font, TickSize = 1.5, TextVerticalPosition = -0.625, UserPositionedText = true };
        doc.DimensionStyles.Add(dimStyle); ThirdHeader(doc);
        var dimension = new AlignedDimension(Vector2.Zero,new Vector2(8,0),2,dimStyle);
        dimension.StyleOverrides.Add(new(DimensionStyleOverrideType.TickSize,2.75));
        dimension.StyleOverrides.Add(new(DimensionStyleOverrideType.TextVerticalPosition,-0.875));
        dimension.StyleOverrides.Add(new(DimensionStyleOverrideType.UserPositionedText,false));
        dimension.StyleOverrides.Add(new(DimensionStyleOverrideType.DimRoundoff,0.125));
        dimension.StyleOverrides.Add(new(DimensionStyleOverrideType.AltUnitsEnabled,true));
        doc.Entities.Add(dimension);
        doc.Entities.Add(new Text("Shared font",new Vector3(-4,3,0),2,font));
        var insert = new Insert(block,new Vector3(10,20,0)); insert.Attributes.Single().Value = "Insert label";
        doc.Entities.Add(insert);
        doc.Entities.Add(new Wipeout(0,0,4,3)); doc.Objects.SetWipeoutVariables(true);
        doc.Entities.Add(new Point(new Vector3(123,456,789)));
        var plot = new PlotSettings { StandardScaleType = 25, StandardScaleFactor = 0.02, PlotType = PlotType.Window,
            WindowBottomLeft = new Vector2(-10,-20), WindowUpRight = new Vector2(200,300), PlotterName = @"Printer\U+0041 青" };
        if (version >= DxfVersion.AutoCad2007)
            plot.ShadePlotObject = doc.NamedObjects["MIXED_RENDER"];
        var page = doc.Objects.AddPlotSettings("MIXED_PAGE",plot);
        ThirdLinks(page,"page resources",font,block.Record);
        doc.Layouts.Add(new Layout("MIXED_SHEET")).PlotSettings = (PlotSettings)page.Settings.Clone();
        if (version >= DxfVersion.AutoCad2007)
        {
            var leaderStyle = new DxfMLeaderStyle(); leaderStyle.Properties.TextStyle = font;
            leaderStyle.Properties.LeaderLinetype = doc.Linetypes["MIXED_LINE"];
            leaderStyle.Properties.Block = block.Record; leaderStyle.Properties.TextHeight = 2.25;
            doc.Objects.AddMLeaderStyle("MIXED_LEADER",leaderStyle);
            ThirdLinks(leaderStyle,"leader resources",font,block.Record);
            doc.Entities.Add(ThirdLeader(doc,false,leaderStyle)); doc.Entities.Add(ThirdLeader(doc,true,leaderStyle));
        }
        return doc;
    }

    private static Dictionary<string,string> ThirdHandles(DxfDocument doc)
    {
        var values = new Dictionary<string,string> {
            ["font"] = doc.TextStyles[ThirdFontName].Handle, ["dimstyle"] = doc.DimensionStyles["MIXED_DIM"].Handle,
            ["linetype"] = doc.Linetypes["MIXED_LINE"].Handle, ["block"] = doc.Blocks[ThirdBlockName].Record.Handle,
            ["definition"] = doc.Blocks[ThirdBlockName].AttributeDefinitions["LABEL"].Handle,
            ["page"] = ((DxfDictionary)doc.NamedObjects["ACAD_PLOTSETTINGS"])["MIXED_PAGE"].Handle,
            ["wipeout_variables"] = doc.Objects.GetWipeoutVariables().Handle
        };
        if (doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
        {
            values["leader_style"] = ((DxfDictionary)doc.NamedObjects["ACAD_MLEADERSTYLE"])["MIXED_LEADER"].Handle;
            values["render"] = doc.NamedObjects["MIXED_RENDER"].Handle;
        }
        return values;
    }

    private static void ThirdAssert(DxfDocument doc, DxfVersion version)
    {
        Equal(version,doc.DrawingVariables.AcadVer,"Mixed declared profile");
        var font = doc.TextStyles[ThirdFontName]; var block = doc.Blocks[ThirdBlockName];
        Equal("Arial.ttf",font.FontFile,"Mixed primary font file"); Equal(ThirdFamily,font.FontFamilyName,"Mixed font family");
        Equal(ThirdFontBits,font.ExtendedFontData!.Flags,"Mixed full font bits"); Equal((double?)9.75,font.LastHeight,"Mixed optional last height");
        Equal((TextStyleFlags)0x4074,font.Flags,"Mixed flags"); Equal((short)0x4006,font.TextGenerationFlags,"Mixed generation flags");
        Equal(6,font.XData["ACAD"].XDataRecord.Count,"Mixed unrelated font XData");
        Equal("font payload retained",(string)font.XData["ACAD"].XDataRecord[3].Value,"Mixed font suffix");
        var dimStyle = doc.DimensionStyles["MIXED_DIM"];
        Check(ReferenceEquals(font,dimStyle.TextStyle),"DIMSTYLE does not share canonical font");
        Equal(1.5,dimStyle.TickSize,"Table DIMTSZ"); Equal(-0.625,dimStyle.TextVerticalPosition,"Table DIMTVP"); Check(dimStyle.UserPositionedText,"Table DIMUPT");
        var dimension = doc.Entities.Dimensions.Single(); Check(ReferenceEquals(dimStyle,dimension.Style),"Dimension style identity");
        Check(dimension.Block != null && ReferenceEquals(doc.Blocks[dimension.Block.Name],dimension.Block),"Dimension geometry block is not registered");
        Equal(2.75,(double)dimension.StyleOverrides[DimensionStyleOverrideType.TickSize].Value,"Override DIMTSZ");
        Equal(-0.875,(double)dimension.StyleOverrides[DimensionStyleOverrideType.TextVerticalPosition].Value,"Override DIMTVP");
        Equal(false,(bool)dimension.StyleOverrides[DimensionStyleOverrideType.UserPositionedText].Value,"Override DIMUPT");
        Equal(0.125,(double)dimension.StyleOverrides[DimensionStyleOverrideType.DimRoundoff].Value,"Override DIMRND");
        Equal(true,(bool)dimension.StyleOverrides[DimensionStyleOverrideType.AltUnitsEnabled].Value,"Override DIMALT");
        foreach (var entry in new[] { ("$DIMTSZ",(object)0.0), ("$DIMTVP",(object)(-2.0)), ("$DIMUPT",(object)(short)0) })
        { Check(doc.DrawingVariables.TryGetCustomVariable(entry.Item1,out var variable),"Explicit dimension header missing"); Equal(entry.Item2,variable.Value,"Explicit header was derived from table style"); }
        Check(ReferenceEquals(doc.Entities.Texts.Single().Style,font),"TEXT font identity");
        Check(ReferenceEquals(block.AttributeDefinitions["LABEL"].Style,font),"ATTDEF font identity");
        var insert = doc.Entities.Inserts.Single(); Check(ReferenceEquals(insert.Block,block),"INSERT block identity");
        Check(ReferenceEquals(insert.Attributes.Single().Style,font),"ATTRIB font identity"); Equal("Insert label",(string)insert.Attributes.Single().Value,"Insert attribute value");
        var bodies = block.Entities.OfType<Body>().ToList(); Equal(version <= DxfVersion.AutoCad2010 ? 1 : 0,bodies.Count,"SAT capability profile");
        if (bodies.Count != 0) Check(bodies[0].SatLines.SequenceEqual(ThirdSatBody(version).SatLines),"Shared block SAT payload changed");
        Equal(1,doc.Entities.Wipeouts.Count(),"Wipeout entity missing");
        Check(doc.Objects.GetWipeoutVariables().DisplayFrame,"Wipeout global variables changed");
        Equal(new Vector3(123,456,789),doc.Entities.Points.Single().Position,"Following entity framing");
        var dictionary = (DxfDictionary)doc.NamedObjects["ACAD_PLOTSETTINGS"];
        Equal(1,dictionary.Entries.Count,"Mixed named page inventory");
        var page = (DxfPlotSettingsObject)dictionary["MIXED_PAGE"];
        Check(ReferenceEquals(page.Owner,dictionary) && page.PersistentReactors.Contains(dictionary),"Page ownership/reactor identity");
        Equal((short)25,page.Settings.StandardScaleType,"Page scale code"); Equal((double?)0.02,page.Settings.StandardScaleFactor,"Page stored scale");
        Equal(new Vector2(-10,-20),page.Settings.WindowBottomLeft,"Page lower-left"); Equal(new Vector2(200,300),page.Settings.WindowUpRight,"Page upper-right");
        Equal(@"Printer\U+0041 青",page.Settings.PlotterName,"Page literal string");
        var layout = doc.Layouts["MIXED_SHEET"].PlotSettings;
        Equal(page.Settings.StandardScaleFactor,layout.StandardScaleFactor,"Embedded/named settings agreement");
        ThirdAssertLinks(page,font,block.Record);
        var leaders = doc.Entities.All.OfType<MultiLeader>().ToList(); Equal(version >= DxfVersion.AutoCad2007 ? 2 : 0,leaders.Count,"MLEADER capability profile");
        if (leaders.Count != 0)
        {
            var leaderStyle = (DxfMLeaderStyle)((DxfDictionary)doc.NamedObjects["ACAD_MLEADERSTYLE"])["MIXED_LEADER"];
            Check(ReferenceEquals(leaderStyle.Properties.TextStyle,font) && ReferenceEquals(leaderStyle.Properties.Block,block.Record),"MLEADERSTYLE shared references");
            ThirdAssertLinks(leaderStyle,font,block.Record);
            foreach (var leader in leaders)
            {
                leader.Validate(); Check(ReferenceEquals(leader.Properties.Style,leaderStyle),"MLEADERSTYLE identity");
                Check(ReferenceEquals(leader.Properties.TextStyle,font) && ReferenceEquals(leader.Properties.Block,block.Record),"MLEADER shared entity references");
                Check(ReferenceEquals(leader.Properties.LeaderLinetype,doc.Linetypes["MIXED_LINE"]),"MLEADER shared linetype");
                Equal("MIXED$青",leader.ColorName,"MLEADER common packet identity");
                if (leader.Context.MText != null) Check(ReferenceEquals(leader.Context.MText.Style,font),"Embedded MTEXT font identity");
                else
                {
                    Check(ReferenceEquals(leader.Context.Block.Block,block.Record),"Embedded block identity");
                    Check(ReferenceEquals(leader.Properties.BlockAttributes.Single().Definition,block.AttributeDefinitions["LABEL"]),"MLEADER ATTDEF identity");
                    Equal("Leader label",leader.Properties.BlockAttributes.Single().Text,"MLEADER attribute text");
                }
            }
            var render = doc.NamedObjects["MIXED_RENDER"];
            Equal("VISUALSTYLE",render.CodeName,"Independent shade object type");
            Check(ReferenceEquals(render,page.Settings.ShadePlotObject) && ReferenceEquals(render,layout.ShadePlotObject),"Named/embedded shade reference fixup");
        }
        else Check(page.Settings.ShadePlotObject == null && layout.ShadePlotObject == null,"Old profile acquired shade handle");
        Check(!doc.TextStyles.Remove(font),"Shared font removed despite live references");
        Check(!doc.Blocks.Remove(block),"Shared block removed despite live references");
        Equal(0,doc.Objects.Validate().Count,"Mixed database graph validation");
    }

    private static void ThirdAssertLinks(DxfObject owner, params DxfObject[] expected)
    {
        var data = owner.XData["THIRD_MIXED"];
        var actual = data.XDataRecord.Where(r => r.Code == XDataCode.DatabaseHandle).Select(r => (string)r.Value).ToArray();
        Check(actual.SequenceEqual(expected.Select(o => o.Handle)),"Mixed XData reference mapping");
        Check(data.ApplicationRegistry.Owner != null,"Mixed XData APPID unregistered");
    }

    private static DxfDocument ThirdRoundTrips(DxfDocument doc, DxfVersion version, bool binary, string kind)
    {
        for (int cycle = 0; cycle < 2; cycle++)
        {
            ThirdAssert(doc,version); var handles = ThirdHandles(doc);
            var fontData = doc.TextStyles[ThirdFontName].XData["ACAD"].XDataRecord.ToArray();
            using var output = new MemoryStream(); Check(doc.Save(output,cycle == 0 ? !binary : binary),"Mixed save");
            Check(fontData.Zip(doc.TextStyles[ThirdFontName].XData["ACAD"].XDataRecord).All(p => ReferenceEquals(p.First,p.Second)),"Combined save mutated STYLE font records");
            if (cycle == 1) File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"third-mixed-{kind}-{version}-{binary}.dxf"),output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Mixed reload");
            ThirdAssert(doc,version);
            foreach (var pair in handles) Equal(pair.Value,ThirdHandles(doc)[pair.Key],"Mixed stable handle: " + pair.Key);
        }
        return doc;
    }

    private static void ThirdMixedSource(DxfVersion version, bool binary)
    {
        var doc = ThirdBuild(version);
        if (version < DxfVersion.AutoCad2007)
        {
            int count = doc.Entities.All.Count(); string seed = doc.DrawingVariables.HandleSeed;
            Throws<NotSupportedException>(() => doc.Entities.Add(new MultiLeader()));
            Equal(count,doc.Entities.All.Count(),"Unsupported MLEADER changed source"); Equal(seed,doc.DrawingVariables.HandleSeed,"Unsupported MLEADER allocated handles");
        }
        if (version >= DxfVersion.AutoCad2013)
        {
            var unsupported = ThirdSatBody(); doc.Entities.Add(unsupported);
            using var stream = new MemoryStream(); stream.Write(new byte[] {1,2,3,4}); stream.Position = 2;
            bool rejected; try { rejected = !doc.Save(stream,binary); } catch (NotSupportedException) { rejected = true; }
            Check(rejected,"Unsupported newer SAT profile accepted"); Check(stream.ToArray().SequenceEqual(new byte[] {1,2,3,4}) && stream.Position == 2,"Rejected SAT save wrote output");
            doc.Entities.Remove(unsupported);
        }
        ThirdRoundTrips(doc,version,binary,"source");
    }

    private static void ThirdMixedCopy(DxfVersion version, bool binary)
    {
        var source = DxfDocument.Load(Path.Combine(ArtifactDirectory,$"third-mixed-source-{version}-{binary}.dxf"))!;
        int handleOffset = checked((int)ThirdHandles(source).Values.Select(h => Convert.ToInt64(h,16)).Max() + 16);
        var target = ThirdScaffold(version,handleOffset);
        var font = (TextStyle)source.TextStyles[ThirdFontName].Clone(); target.TextStyles.Add(font);
        var line = (Linetype)source.Linetypes["MIXED_LINE"].Clone(); target.Linetypes.Add(line);
        var block = (Block)source.Blocks[ThirdBlockName].Clone(); block.AttributeDefinitions["LABEL"].Style = font; target.Blocks.Add(block);
        var dimStyle = (DimensionStyle)source.DimensionStyles["MIXED_DIM"].Clone(); dimStyle.TextStyle = font; target.DimensionStyles.Add(dimStyle);
        // Headers are document state, so copying them is explicit rather than an object-graph side effect.
        ThirdHeader(target);
        var map = new Dictionary<DxfObject,DxfObject> {
            [source.TextStyles[ThirdFontName]] = font, [source.Linetypes["MIXED_LINE"]] = line,
            [source.Blocks[ThirdBlockName].Record] = block.Record,
            [source.Blocks[ThirdBlockName].AttributeDefinitions["LABEL"]] = block.AttributeDefinitions["LABEL"],
            [source.NamedObjects] = target.NamedObjects
        };
        if (version >= DxfVersion.AutoCad2007)
        {
            int opaqueCount = target.Objects.Items.Count; string opaqueSeed = target.DrawingVariables.HandleSeed;
            Throws<NotSupportedException>(() => target.Objects.CloneObject((DxfDatabaseObject)source.NamedObjects["MIXED_RENDER"],target.NamedObjects,"UNSUPPORTED_OPAQUE"));
            Equal(opaqueCount,target.Objects.Items.Count,"Rejected opaque clone changed inventory");
            Equal(opaqueSeed,target.DrawingVariables.HandleSeed,"Rejected opaque clone allocated handles");
            map[source.NamedObjects["MIXED_RENDER"]] = target.NamedObjects["MIXED_RENDER"];
        }
        var sourcePage = (DxfPlotSettingsObject)((DxfDictionary)source.NamedObjects["ACAD_PLOTSETTINGS"])["MIXED_PAGE"];
        var pageDictionary = ThirdDictionary(target,"ACAD_PLOTSETTINGS");
        int objects = target.Objects.Items.Count; string beforeSeed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.CloneObject(sourcePage,pageDictionary,"UNMAPPED"));
        Equal(objects,target.Objects.Items.Count,"Failed page clone registered objects"); Equal(beforeSeed,target.DrawingVariables.HandleSeed,"Failed page clone allocated handles");
        var page = (DxfPlotSettingsObject)target.Objects.CloneObject(sourcePage,pageDictionary,"MIXED_PAGE",map);
        target.Layouts.Add(new Layout("MIXED_SHEET")).PlotSettings = (PlotSettings)page.Settings.Clone();
        target.Objects.CloneObject(source.Objects.GetWipeoutVariables(),target.NamedObjects,"ACAD_WIPEOUT_VARS",map);
        DxfMLeaderStyle? leaderStyle = null;
        if (version >= DxfVersion.AutoCad2007)
        {
            var original = (DxfMLeaderStyle)((DxfDictionary)source.NamedObjects["ACAD_MLEADERSTYLE"])["MIXED_LEADER"];
            leaderStyle = (DxfMLeaderStyle)target.Objects.CloneObject(original,ThirdDictionary(target,"ACAD_MLEADERSTYLE"),"MIXED_LEADER",map);
            map[original] = leaderStyle;
            var foreign = (MultiLeader)source.Entities.All.OfType<MultiLeader>().First().Clone();
            int entities = target.Entities.All.Count(); beforeSeed = target.DrawingVariables.HandleSeed;
            bool rejected; try { target.Entities.Add(foreign); rejected = false; } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected,"Same-name foreign MLEADER references were accepted"); Equal(entities,target.Entities.All.Count(),"Foreign MLEADER changed target"); Equal(beforeSeed,target.DrawingVariables.HandleSeed,"Foreign MLEADER allocated handles");
        }
        foreach (var entity in source.Entities.All)
        {
            var copy = (EntityObject)entity.Clone();
            if (copy is Text text) text.Style = font;
            if (copy is Dimension dimension) dimension.Style = dimStyle;
            if (copy is MultiLeader leader)
            {
                leader.Properties.Style = leaderStyle; leader.Properties.TextStyle = font; leader.Properties.LeaderLinetype = line; leader.Properties.Block = block.Record;
                if (leader.Context.MText != null) leader.Context.MText.Style = font;
                if (leader.Context.Block != null) leader.Context.Block.Block = block.Record;
                foreach (var attribute in leader.Properties.BlockAttributes) attribute.Definition = block.AttributeDefinitions["LABEL"];
            }
            target.Entities.Add(copy);
        }
        ThirdAssert(source,version); ThirdAssert(target,version);
        var sourceHandles = ThirdHandles(source); var targetHandles = ThirdHandles(target);
        foreach (string role in sourceHandles.Keys) Check(sourceHandles[role] != targetHandles[role],"Mapping accidentally retained source handle for " + role);
        target = ThirdRoundTrips(target,version,binary,"copy");
        // Value edits on the copied STYLE, page payload and SAT content stay isolated.
        target.TextStyles[ThirdFontName].FontStyle = FontStyle.Regular;
        Equal(ThirdFontBits,source.TextStyles[ThirdFontName].ExtendedFontData!.Flags,"Copied font edit mutated source");
        ((DxfPlotSettingsObject)((DxfDictionary)target.NamedObjects["ACAD_PLOTSETTINGS"])["MIXED_PAGE"]).Settings.WindowBottomLeft = Vector2.Zero;
        Equal(new Vector2(-10,-20),sourcePage.Settings.WindowBottomLeft,"Copied page edit mutated source");
        if (version <= DxfVersion.AutoCad2010)
        {
            var copiedBody = target.Blocks[ThirdBlockName].Entities.OfType<Body>().Single();
            var before = source.Blocks[ThirdBlockName].Entities.OfType<Body>().Single().SatLines.ToArray();
            copiedBody.SetSatLines(new[] {"opaque edited SAT cache"});
            Check(source.Blocks[ThirdBlockName].Entities.OfType<Body>().Single().SatLines.SequenceEqual(before),"Copied SAT edit mutated source");
        }
        File.WriteAllText(Path.Combine(ArtifactDirectory,$"third-mixed-map-{version}-{binary}.json"),JsonSerializer.Serialize(new {source=sourceHandles,target=targetHandles},new JsonSerializerOptions {WriteIndented=true}));
    }
}
