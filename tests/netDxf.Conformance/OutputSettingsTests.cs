using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RunOutputSettingsTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"output-settings/authored/{version}/{binary}", () => OutputSettingsAuthored(version, binary));
            Run($"output-settings/independent/{version}/{binary}", () => OutputSettingsIndependent(version, binary));
            Run($"output-settings/stored-factor/{version}/{binary}", () => OutputSettingsStoredFactor(version, binary));
        }
        Run("output-settings/clone-references-and-failures", OutputSettingsClone);
        Run("output-settings/clone-callback-recheck", OutputSettingsCloneCallbacks);
        Run("output-settings/validation-and-compatibility", OutputSettingsValidation);
        Run("output-settings/shade-reference-failure-and-retry", OutputSettingsShadeRetry);
        Run("output-settings/shade-reference-profile-promotion", OutputSettingsShadeProfiles);
        Run("output-settings/graphical-shade-reference-rejection", OutputSettingsGraphicalShade);
        Run("output-settings/fresh-invalid-preflight", OutputSettingsFreshPreflight);
        Run("output-settings/clone-exact-object-identity", OutputSettingsCloneIdentity);
        foreach (string kind in new[] { "PLOTSETTINGS", "LAYOUT", "WIPEOUTVARIABLES" })
            foreach (int scenario in Enumerable.Range(0, kind == "WIPEOUTVARIABLES" ? 2 : kind == "LAYOUT" ? 11 : 9))
            { string type = kind; int fault = scenario; Run($"output-settings/malformed/{type}/{fault}", () => OutputSettingsMalformed(type, fault)); }
    }
    private static PlotSettings OutputPayload(short scale)
    {
        return new PlotSettings { PageSetupName = "payload", PlotterName = @"Printer\U+0041 Żółć", PaperSizeName = "Custom 東京", ViewName = "", CurrentStyleSheet = @"C:\plot\style.ctb",
            PaperMargin = new PaperMargin(1.25,2.5,3.75,4.125), PaperSize = new Vector2(310.5,207.25), Origin = new Vector2(-7.75,8.125),
            WindowBottomLeft = new Vector2(-11.25,-22.5), WindowUpRight = new Vector2(33.75,44.125), PrintScaleNumerator = 2.5, PrintScaleDenominator = 17.75,
            Flags = (PlotFlags)687, PaperUnits = PlotPaperUnits.Milimeters, PaperRotation = PlotRotation.Degrees270, PlotType = PlotType.Window,
            StandardScaleType = scale, StandardScaleFactor = 0.03125, ShadePlotMode = ShadePlotMode.Hidden, ShadePlotResolutionMode = ShadePlotResolutionMode.Custom,
            ShadePlotDPI = 777, PaperImageOrigin = new Vector2(-0.625,1.875) };
    }
    private static void OutputSettingsAuthored(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));
        for(short code=0;code<=32;code++) doc.Objects.AddPlotSettings($"User-{code:00}", OutputPayload(code));
        var layout = doc.Layouts.Add(new Layout("UserSheet")); layout.PlotSettings = OutputPayload(25);
        var wipe = doc.Objects.SetWipeoutVariables(true); string wipeHandle=wipe.Handle;
        foreach(bool frame in new[]{false,true})
        {
            Check(ReferenceEquals(wipe,doc.Objects.SetWipeoutVariables(frame)),"Wipeout edit changed object identity.");
            using var output=new MemoryStream();Check(doc.Save(output,binary),"output settings save");
            if(frame) File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"output-settings-{version}-{binary}.dxf"),output.ToArray());
            output.Position=0;var loaded=DxfDocument.Load(output) ?? throw new Exception("Output settings load failed.");
            var dictionary=(DxfDictionary)loaded.NamedObjects["ACAD_PLOTSETTINGS"];Equal(33,dictionary.Count,"named page count");
            for(short code=0;code<=32;code++)
            {
                var item=(DxfPlotSettingsObject)dictionary[$"User-{code:00}"];var plot=item.Settings;
                Equal(code,plot.StandardScaleType,"all standard scale codes");Equal(code==0,plot.ScaleToFit,"legacy fit predicate");
                Equal((double?)0.03125,plot.StandardScaleFactor,"stored147 independent of custom ratio");
                Equal(new Vector2(-11.25,-22.5),plot.WindowBottomLeft,"lower-left window");Equal(new Vector2(33.75,44.125),plot.WindowUpRight,"upper-right window");
                Equal(@"Printer\U+0041 Żółć",plot.PlotterName,"literal printer");Check(item.PersistentReactors.Contains(dictionary),"page owner reactor");
            }
            var embedded=loaded.Layouts["UserSheet"].PlotSettings;Equal((short)25,embedded.StandardScaleType,"embedded75");Equal((double?)0.03125,embedded.StandardScaleFactor,"embedded147");
            Equal(new Vector2(-11.25,-22.5),embedded.WindowBottomLeft,"embedded lower corner");
            var result=loaded.Objects.GetWipeoutVariables();Check(result!=null && result.DisplayFrame==frame && result.Handle==wipeHandle,"wipeout root flag/identity");
            Equal(0,loaded.Objects.Validate().Count,"loaded output graph");
        }
    }
    private static string OutputFixture(DxfVersion version) => Path.Combine("tests","fixtures","output-settings",$"independent-output-settings-R{version.ToString().Substring(7)}.dxf");
    private static void OutputSettingsIndependent(DxfVersion version,bool binary)
    {
        string path=OutputFixture(version);using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine("tests","fixtures","output-settings","manifest.json")));
        var expected=manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(x=>x.GetProperty("file").GetString()==Path.GetFileName(path));
        byte[] bytes=File.ReadAllBytes(path);Equal(expected.GetProperty("sha256").GetString(),Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),"external producer hash");
        using var input=new MemoryStream(bytes);var doc=DxfDocument.Load(input) ?? throw new Exception("Independent page load failed.");
        var dictionary=(DxfDictionary)doc.NamedObjects["ACAD_PLOTSETTINGS"];Equal(33,dictionary.Count,"independent page count");
        for(short code=0;code<=32;code++)
        {
            var page=(DxfPlotSettingsObject)dictionary[$"Independent-{code:00}"];Equal(code,page.Settings.StandardScaleType,"independent75");Equal((double?)0.03125,page.Settings.StandardScaleFactor,"independent147");
            Equal(new Vector2(-11.25,-22.5),page.Settings.WindowBottomLeft,"independent window LL");Equal(new Vector2(33.75,44.125),page.Settings.WindowUpRight,"independent window UR");
        }
        var shade=expected.GetProperty("shade_handle");var embedded=doc.Layouts["IndependentSheet"].PlotSettings;
        if(shade.ValueKind!=JsonValueKind.Null)
        {
            DxfObject target=doc.GetObjectByHandle(shade.GetString());Check(target.CodeName=="VISUALSTYLE","independent actual shade target");
            Check(ReferenceEquals(target,embedded.ShadePlotObject) && ReferenceEquals(target,((DxfPlotSettingsObject)dictionary["Independent-07"]).Settings.ShadePlotObject),"deferred333 resolution");
        }
        Equal(0,doc.Objects.Validate().Count,"independent graph");using var output=new MemoryStream();Check(doc.Save(output,binary),"independent page save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"independent-output-settings-{version}-{binary}.dxf"),output.ToArray());
    }
    private static void OutputSettingsClone()
    {
        var source=DxfDocument.Load(OutputFixture(DxfVersion.AutoCad2018))!;var target=DxfDocument.Load(OutputFixture(DxfVersion.AutoCad2018))!;
        var original=(DxfPlotSettingsObject)((DxfDictionary)source.NamedObjects["ACAD_PLOTSETTINGS"])["Independent-07"];
        var destination=(DxfDictionary)target.NamedObjects["ACAD_PLOTSETTINGS"];int before=target.Objects.Items.Count;string seed=target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(()=>target.Objects.CloneObject(original,destination,"MISSING"));Equal(before,target.Objects.Items.Count,"failed clone registration");Equal(seed,target.DrawingVariables.HandleSeed,"failed clone seed");
        var originalRegistry=original.XData["QA_OUTPUT_SETTINGS"].ApplicationRegistry;var registryOwner=originalRegistry.Owner;string registryHandle=originalRegistry.Handle;
        var replacement=target.GetObjectByHandle(original.Settings.ShadePlotObject.Handle);
        var clone=(DxfPlotSettingsObject)target.Objects.CloneObject(original,destination,"COPY",new Dictionary<DxfObject,DxfObject>{{original.Settings.ShadePlotObject,replacement}});
        Check(ReferenceEquals(clone.Settings.ShadePlotObject,replacement),"clone shade mapping");Check(ReferenceEquals(clone.Owner,destination)&&clone.PersistentReactors.Contains(destination),"clone owner mapping");
        Equal(destination.Handle,(string)clone.XData["QA_OUTPUT_SETTINGS"].XDataRecord[1].Value,"clone owner XData mapping");
        clone.Settings.WindowBottomLeft=new Vector2(99,88);Equal(new Vector2(-11.25,-22.5),original.Settings.WindowBottomLeft,"clone payload independence");
        foreach(bool binary in new[]{false,true}){using var output=new MemoryStream();Check(target.Save(output,binary),"clone save");output.Position=0;Check(DxfDocument.Load(output)!=null,"clone reload");}
        Check(originalRegistry.Owner==registryOwner && originalRegistry.Handle==registryHandle && ReferenceEquals(source.GetObjectByHandle(registryHandle),originalRegistry),"clone transferred source registry");
        Throws<ArgumentException>(()=>target.Objects.CloneObject(original,destination,"COPY"));
        var wipe=source.Objects.GetWipeoutVariables();var app=new DxfDictionary();target.NamedObjects.Add("APP_COPY",app);var copiedWipe=(DxfWipeoutVariables)target.Objects.CloneObject(wipe,app,"FRAME");
        Equal(wipe.DisplayFrame,copiedWipe.DisplayFrame,"single object scalar clone");Check(copiedWipe.PersistentReactors.Contains(app),"single object owner reactor mapping");
    }
    private static void OutputSettingsCloneCallbacks()
    {
        var source=new DxfDocument();var item=source.Objects.SetWipeoutVariables(true);var target=new DxfDocument();int after=0;string seed="";var installed=new DxfDictionaryVariable();
        var callbacks=new ContainerCallbackMappings(()=>{target.NamedObjects.Add("COPY",installed);after=target.Objects.Items.Count;seed=target.DrawingVariables.HandleSeed;});
        Throws<ArgumentException>(()=>target.Objects.CloneObject(item,target.NamedObjects,"COPY",callbacks));
        Equal(after,target.Objects.Items.Count,"callback collision registration");Equal(seed,target.DrawingVariables.HandleSeed,"callback collision allocation");Check(ReferenceEquals(installed,target.NamedObjects["COPY"]),"callback-installed object replaced");
        var foreign=new DxfXRecord();source.NamedObjects.Add("EXTERNAL",foreign);item.PersistentReactors.Add(foreign);var mapped=new DxfXRecord();target.NamedObjects.Add("TARGET",mapped);
        var throwingLookup=new ObjectMappingWithThrowingLookup(foreign,mapped);var clone=target.Objects.CloneObject(item,target.NamedObjects,"RETRY",throwingLookup);Equal(0,throwingLookup.LookupCalls,"caller lookup after snapshot");Check(clone.PersistentReactors.Contains(mapped),"snapshot external mapping");
    }
    private static void OutputSettingsValidation()
    {
        var plot=OutputPayload(25);plot.ScaleToFit=false;Equal((short)25,plot.StandardScaleType,"false retains explicit standard code");plot.ScaleToFit=true;Equal((short)0,plot.StandardScaleType,"true selects fit");plot.ScaleToFit=false;Equal((short)16,plot.StandardScaleType,"legacy false selects1:1");
        Throws<ArgumentOutOfRangeException>(()=>plot.StandardScaleType=33);Throws<ArgumentOutOfRangeException>(()=>plot.StandardScaleFactor=double.NaN);
        var copied=(PlotSettings)plot.Clone();Equal(plot.StandardScaleFactor,copied.StandardScaleFactor,"legacy clone147");
        var doc=new DxfDocument();int count=doc.Objects.Items.Count;string seed=doc.DrawingVariables.HandleSeed;
        plot.PaperSize=new Vector2(double.NaN,1);Throws<ArgumentException>(()=>doc.Objects.AddPlotSettings("BAD",plot));Equal(count,doc.Objects.Items.Count,"invalid author registration");Equal(seed,doc.DrawingVariables.HandleSeed,"invalid author seed");
        plot=OutputPayload(1);Throws<ArgumentException>(()=>doc.Objects.AddPlotSettings("bad\uD800",plot));Equal(count,doc.Objects.Items.Count,"invalid name registration");
        var page=doc.Objects.AddPlotSettings("PAGE",plot);plot.WindowBottomLeft=Vector2.Zero;Equal(new Vector2(-11.25,-22.5),page.Settings.WindowBottomLeft,"author copied input");
        var foreign=new DxfDocument();var foreignObject=new DxfXRecord();foreign.NamedObjects.Add("FOREIGN",foreignObject);page.Settings.ShadePlotObject=foreignObject;
        using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"foreign shade wrote output");page.Settings.ShadePlotObject=null;
        var embedded=doc.Layouts.First().PlotSettings;embedded.ShadePlotObject=new DxfXRecord();using var embeddedOutput=new MemoryStream();CheckSaveRejected(doc,embeddedOutput);Equal(0L,embeddedOutput.Length,"detached embedded shade output");embedded.ShadePlotObject=null;
        embedded.PlotterName="a\nb";using var text=new MemoryStream();CheckSaveRejected(doc,text);Equal(0L,text.Length,"embedded framing output");
    }
    private static void OutputSettingsMalformed(string kind,int scenario)
    {
        var doc=new DxfDocument(DxfVersion.AutoCad2018);var page=doc.Objects.AddPlotSettings("PAGE",OutputPayload(25));var layout=doc.Layouts.Add(new Layout("SHEET"));layout.PlotSettings=OutputPayload(25);var wipe=doc.Objects.SetWipeoutVariables(false);
        string handle=kind=="PLOTSETTINGS"?page.Handle:kind=="LAYOUT"?layout.Handle:wipe.Handle;
        using var valid=new MemoryStream();Check(doc.Save(valid,true),"malformed seed");valid.Position=0;var raw=DxfRawDocument.Load(valid);
        raw=ObjectStoreReplaceRecord(raw,handle,tags=>{
            int start=tags.FindIndex(t=>t.Code==100&&(string)t.Value==(kind=="WIPEOUTVARIABLES"?"AcDbWipeoutVariables":"AcDbPlotSettings"));
            int Find(short code)=>tags.FindIndex(start+1,t=>t.Code==code);
            if (kind == "LAYOUT" && scenario >= 9)
            {
                int boundary = tags.FindIndex(start + 1, t => t.Code == 100);
                if (scenario == 9) tags[boundary] = new DxfTag(100, "PrivateUnsupportedLayout");
                else tags.RemoveRange(boundary, tags.Count - boundary);
                return tags;
            }
            if(kind=="WIPEOUTVARIABLES"){if(scenario==0)tags[Find(70)]=new DxfTag(70,(short)2);else tags.Insert(start+1,new DxfTag(70,(short)0));}
            else switch(scenario){case 0:tags[Find(75)]=new DxfTag(75,(short)33);break;case 1:tags[Find(143)]=new DxfTag(143,0.0);break;case 2:tags[Find(142)]=new DxfTag(142,-1.0);break;case 3:tags[Find(72)]=new DxfTag(72,(short)9);break;case 4:tags[Find(73)]=new DxfTag(73,(short)9);break;case 5:tags[Find(78)]=new DxfTag(78,(short)99);break;case 6:tags.Insert(start+1,new DxfTag(75,(short)1));break;case 7:tags.Insert(start+1,new DxfTag(333,"DEADBEEF"));break;case 8:tags[Find(2)]=new DxfTag(2,"bad\\U+000Atext");break;}
            return tags;});
        foreach(bool binary in new[]{false,true}){using var output=new MemoryStream();raw.Save(output,binary);output.Position=0;bool rejected;try{rejected=DxfDocument.Load(output)==null;}catch(FormatException){rejected=true;}Check(rejected,"Malformed output settings accepted.");}
    }
    private static void OutputSettingsStoredFactor(DxfVersion version, bool binary)
    {
        foreach (double factor in new[] { 0.0, -0.5, 0.03125 })
        {
            var doc = new DxfDocument(version);
            var plot = OutputPayload(0); plot.StandardScaleFactor = factor;
            var page = doc.Objects.AddPlotSettings("FACTOR", plot);
            var sheet = doc.Layouts.Add(new Layout("FACTOR_SHEET")); sheet.PlotSettings = (PlotSettings)plot.Clone();
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "stored factor save");
            stream.Position = 0; var raw = DxfRawDocument.Load(stream);
            foreach (string handle in new[] { page.Handle, sheet.Handle })
            {
                var record = raw.Sections.SelectMany(section => section.Records).Single(item => item.Tags.Any(tag => tag.Code == 5 && (string)tag.Value == handle));
                double stored = (double)record.Tags.Single(tag => tag.Code == 147).Value;
                Equal(BitConverter.DoubleToInt64Bits(factor), BitConverter.DoubleToInt64Bits(stored), "raw147 bits changed");
            }
            stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
            var named = ((DxfPlotSettingsObject)((DxfDictionary)loaded.NamedObjects["ACAD_PLOTSETTINGS"])["FACTOR"]).Settings;
            foreach (var settings in new[] { named, loaded.Layouts["FACTOR_SHEET"].PlotSettings })
            {
                Equal((double?)factor, settings.StandardScaleFactor, "finite explicit147 retention");
                Equal((short)0, settings.StandardScaleType, "fit code changed");
                Equal(2.5, settings.PrintScaleNumerator, "custom numerator changed");
                Equal(17.75, settings.PrintScaleDenominator, "custom denominator changed");
            }
        }
    }
    private static void OutputSettingsShadeRetry()
    {
        foreach (bool binary in new[] { false, true }) foreach (bool embedded in new[] { false, true }) foreach (bool foreign in new[] { false, true })
        {
            var doc = new DxfDocument();
            var page = doc.Objects.AddPlotSettings("PAGE", OutputPayload(25));
            var layout = doc.Layouts.Add(new Layout("SHEET")); layout.PlotSettings = OutputPayload(25);
            using (var initial = new MemoryStream()) Check(doc.Save(initial, binary), "retry baseline save");
            var settings = embedded ? layout.PlotSettings : page.Settings;
            var badTarget = new DxfXRecord(); var other = new DxfDocument();
            if (foreign) other.NamedObjects.Add("SHADE", badTarget);
            settings.ShadePlotObject = badTarget;
            int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
            using var rejected = new MemoryStream(); CheckSaveRejected(doc, rejected);
            Equal(0L, rejected.Length, "invalid reference emitted bytes");
            Equal(count, doc.Objects.Items.Count, "invalid reference registered objects");
            Equal(seed, doc.DrawingVariables.HandleSeed, "invalid reference allocated handles");
            Check(ReferenceEquals(settings.ShadePlotObject, badTarget), "failed save changed reference");
            settings.ShadePlotObject = null;
            using var retry = new MemoryStream(); Check(doc.Save(retry, binary), "corrected reference retry save");
            retry.Position = 0; Check(DxfDocument.Load(retry) != null, "corrected reference retry load");
        }
    }
    private static void OutputSettingsShadeProfiles()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 }) foreach (bool binary in new[] { false, true }) foreach (bool embedded in new[] { false, true })
        {
            var doc = DxfDocument.Load(OutputFixture(DxfVersion.AutoCad2018))!;
            var page = ((DxfPlotSettingsObject)((DxfDictionary)doc.NamedObjects["ACAD_PLOTSETTINGS"])["Independent-07"]).Settings;
            var layout = doc.Layouts["IndependentSheet"].PlotSettings;
            if (embedded) page.ShadePlotObject = null; else layout.ShadePlotObject = null;
            doc.DrawingVariables.AcadVer = version;
            string seed = doc.DrawingVariables.HandleSeed; int count = doc.Objects.Items.Count;
            using var rejected = new MemoryStream(); CheckSaveRejected(doc, rejected);
            Equal(0L, rejected.Length, "unqualified shade profile emitted bytes");
            Equal(seed, doc.DrawingVariables.HandleSeed, "unqualified shade profile allocated handles");
            Equal(count, doc.Objects.Items.Count, "unqualified shade profile registered objects");
            doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2007;
            using var promoted = new MemoryStream(); Check(doc.Save(promoted, binary), "promoted shade save");
            promoted.Position = 0; var raw = DxfRawDocument.Load(promoted);
            raw = DxfRawDocument.Create(raw.Tags.Select(tag => tag.Code == 1 && tag.Value is string value && value == "AC1021" ? new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1015" : "AC1018") : tag));
            using var older = new MemoryStream(); raw.Save(older, binary); older.Position = 0;
            var retained = DxfDocument.Load(older)!;
            var settings = embedded ? retained.Layouts["IndependentSheet"].PlotSettings : ((DxfPlotSettingsObject)((DxfDictionary)retained.NamedObjects["ACAD_PLOTSETTINGS"])["Independent-07"]).Settings;
            Check(settings.ShadePlotObject != null, "older profile lost retained shade reference");
            using var retainedRejected = new MemoryStream(); CheckSaveRejected(retained, retainedRejected); Equal(0L, retainedRejected.Length, "retained unqualified shade emitted bytes");
            retained.DrawingVariables.AcadVer = DxfVersion.AutoCad2007;
            using var retry = new MemoryStream(); Check(retained.Save(retry, binary), "retained shade promotion retry");
        }
    }
    private static void OutputSettingsGraphicalShade()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var block = new netDxf.Blocks.Block("ATTRIBUTES");
        var definition = new AttributeDefinition("TAG") { Value = "sentinel" }; block.AttributeDefinitions.Add(definition);
        var insert = new Insert(block); doc.Entities.Add(insert);
        var page = doc.Objects.AddPlotSettings("PAGE", OutputPayload(25));
        var layout = doc.Layouts.Add(new Layout("SHEET")); layout.PlotSettings = OutputPayload(25);
        var validTarget = new DxfDictionaryVariable(); doc.NamedObjects.Add("SHADE", validTarget);
        page.Settings.ShadePlotObject = validTarget;
        foreach (DxfObject graphical in new DxfObject[] { definition, insert.Attributes.Single() })
        {
            Throws<ArgumentException>(() => page.Settings.ShadePlotObject = graphical);
            Check(ReferenceEquals(page.Settings.ShadePlotObject, validTarget), "rejected graphical target changed existing setting");
            int count = doc.Objects.Items.Count; string seed = doc.DrawingVariables.HandleSeed;
            Throws<ArgumentException>(() => doc.Objects.CloneObject(page, doc.NamedObjects, "BAD_CLONE", new Dictionary<DxfObject,DxfObject> { { validTarget, graphical } }));
            Equal(count, doc.Objects.Items.Count, "graphical clone target registered objects"); Equal(seed, doc.DrawingVariables.HandleSeed, "graphical clone target allocated handles");
            foreach (bool embedded in new[] { false, true }) foreach (bool binary in new[] { false, true })
            {
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "graphical source save"); stream.Position = 0;
                var raw = DxfRawDocument.Load(stream);
                raw = ObjectStoreReplaceRecord(raw, embedded ? layout.Handle : page.Handle, tags =>
                {
                    int start = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbPlotSettings");
                    int end = tags.FindIndex(start + 1, t => t.Code == 100 || t.Code == 1001); if (end < 0) end = tags.Count;
                    int reference = tags.FindIndex(start + 1, end - start - 1, t => t.Code == 333);
                    if (reference < 0) tags.Insert(end, new DxfTag(333, graphical.Handle)); else tags[reference] = new DxfTag(333, graphical.Handle);
                    return tags;
                });
                using var malformed = new MemoryStream(); raw.Save(malformed, binary); malformed.Position = 0;
                bool rejected; try { rejected = DxfDocument.Load(malformed) == null; } catch (FormatException) { rejected = true; }
                Check(rejected, "graphical incoming333 accepted");
            }
        }
    }
    private static void OutputSettingsFreshPreflight()
    {
        var databaseField = typeof(DxfDocument).GetField("objectDatabase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (bool binary in new[] { false, true }) foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004, DxfVersion.AutoCad2018 })
        {
            var doc = new DxfDocument(version); var settings = doc.Layouts.First().PlotSettings;
            if (version < DxfVersion.AutoCad2007) settings.ShadePlotObject = doc.Layers["0"];
            else settings.PaperSize = new Vector2(double.NaN, 10);
            string seed = doc.DrawingVariables.HandleSeed;
            Check(databaseField.GetValue(doc) == null, "fresh fixture initialized object database");
            using var rejected = new MemoryStream(); CheckSaveRejected(doc, rejected);
            Equal(0L, rejected.Length, "invalid first save emitted bytes"); Equal(seed, doc.DrawingVariables.HandleSeed, "invalid first save allocated handles");
            Check(databaseField.GetValue(doc) == null, "invalid first save initialized object database");
            settings.ShadePlotObject = null; settings.PaperSize = new Vector2(210, 297);
            using var retry = new MemoryStream(); Check(doc.Save(retry, binary), "corrected first-save retry");
        }
    }
    private static void OutputSettingsCloneIdentity()
    {
        foreach (string kind in new[] { "object", "dictionary", "extension" })
        {
            var source = new DxfDocument(); var target = new DxfDocument(); var third = new DxfDocument();
            var sourceStyle = source.TextStyles.Add(new TextStyle("SAME_NAME", "txt.shx"));
            var decoyStyle = third.TextStyles.Add(new TextStyle("SAME_NAME", "txt.shx"));
            Check(sourceStyle.Equals(decoyStyle), "decoy fixture must compare equal by table name");
            var targetStyle = target.TextStyles.Add(new TextStyle("TARGET", "txt.shx")); var otherTarget = target.TextStyles.Add(new TextStyle("OTHER", "txt.shx"));
            var sourceOwner = new Line(Vector3.Zero, Vector3.UnitX); source.Entities.Add(sourceOwner);
            var targetOwner = new Line(Vector3.Zero, Vector3.UnitX); target.Entities.Add(targetOwner);
            var graph = new DxfDictionary(); var buffer = new DxfIdBuffer(); buffer.References.Add(sourceStyle); buffer.PersistentReactors.Add(sourceStyle);
            var data = new XData(new ApplicationRegistry("QA_IDENTITY")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, sourceStyle.Handle)); buffer.XData.Add(data);
            graph.Add("BUFFER", buffer);
            if (kind == "extension") source.Objects.SetExtensionDictionary(sourceOwner, graph); else source.NamedObjects.Add("GRAPH", graph);
            var destination = target.NamedObjects; int count = target.Objects.Items.Count; string seed = target.DrawingVariables.HandleSeed;
            DxfDatabaseObject Copy(IReadOnlyDictionary<DxfObject,DxfObject> mapping, string name = "COPY") => kind == "object"
                ? target.Objects.CloneObject(buffer, destination, name, mapping)
                : kind == "dictionary" ? target.Objects.Clone(graph, destination, name, mapping)
                : target.Objects.CloneExtensionDictionary(sourceOwner, targetOwner, mapping);
            Throws<InvalidOperationException>(() => Copy(new Dictionary<DxfObject,DxfObject> { { decoyStyle, targetStyle } }));
            Equal(count, target.Objects.Items.Count, "decoy mapping registered clones"); Equal(seed, target.DrawingVariables.HandleSeed, "decoy mapping allocated handles");
            Check(!destination.Contains("COPY") && targetOwner.ExtensionDictionary == null, "decoy mapping occupied destination");
            var exact = new Dictionary<DxfObject,DxfObject>(ReferenceEqualityComparer.Instance) { { sourceStyle, targetStyle }, { decoyStyle, otherTarget } };
            var copied = Copy(exact); var result = copied as DxfIdBuffer ?? (DxfIdBuffer)((DxfDictionary)copied)["BUFFER"];
            Check(ReferenceEquals(result.References[0], targetStyle) && result.PersistentReactors.Contains(targetStyle), "exact mapping did not select source identity");
            Equal(targetStyle.Handle, (string)result.XData["QA_IDENTITY"].XDataRecord[0].Value, "exact XData mapping differs");
            Check(ReferenceEquals(buffer.References[0], sourceStyle), "clone changed source identity");
            if (kind == "extension") { targetOwner = new Line(Vector3.Zero, Vector3.UnitY); target.Entities.Add(targetOwner); }
            var renamedCopy = Copy(new OutputRenamingMapping(sourceStyle, targetStyle, () => sourceStyle.Name = "RENAMED"), "RENAMED_COPY");
            var renamedBuffer = renamedCopy as DxfIdBuffer ?? (DxfIdBuffer)((DxfDictionary)renamedCopy)["BUFFER"];
            Check(ReferenceEquals(renamedBuffer.References[0], targetStyle), "renaming enumerated source key broke exact identity mapping");
        }
        var ownerSource = new DxfDocument(); var ownerTarget = new DxfDocument(); var ownerThird = new DxfDocument();
        var sourceTableOwner = ownerSource.TextStyles.Add(new TextStyle("OWNER", "txt.shx")); var decoyOwner = ownerThird.TextStyles.Add(new TextStyle("OWNER", "txt.shx"));
        var targetTableOwner = ownerTarget.TextStyles.Add(new TextStyle("OWNER", "txt.shx")); var unrelated = ownerTarget.TextStyles.Add(new TextStyle("UNRELATED", "txt.shx"));
        var extension = new DxfDictionary(); var ownerBuffer = new DxfIdBuffer(); ownerBuffer.References.Add(sourceTableOwner); extension.Add("OWNER", ownerBuffer);
        ownerSource.Objects.SetExtensionDictionary(sourceTableOwner, extension);
        var cloned = ownerTarget.Objects.CloneExtensionDictionary(sourceTableOwner, targetTableOwner, new Dictionary<DxfObject,DxfObject> { { decoyOwner, unrelated } });
        Check(ReferenceEquals(((DxfIdBuffer)cloned["OWNER"]).References[0], targetTableOwner), "equal-name decoy replaced automatic exact owner mapping");
    }
    private sealed class OutputRenamingMapping : IReadOnlyDictionary<DxfObject,DxfObject>
    {
        private readonly DxfObject source;
        private readonly DxfObject target;
        private readonly Action afterYield;
        public OutputRenamingMapping(DxfObject source, DxfObject target, Action afterYield)
        { this.source = source; this.target = target; this.afterYield = afterYield; }
        public int Count => 1;
        public IEnumerable<DxfObject> Keys => new[] { this.source };
        public IEnumerable<DxfObject> Values => new[] { this.target };
        public DxfObject this[DxfObject key] => throw new InvalidOperationException("Caller lookup must not be used.");
        public bool ContainsKey(DxfObject key) => throw new InvalidOperationException("Caller lookup must not be used.");
        public bool TryGetValue(DxfObject key, out DxfObject value) => throw new InvalidOperationException("Caller lookup must not be used.");
        public IEnumerator<KeyValuePair<DxfObject,DxfObject>> GetEnumerator()
        { yield return new KeyValuePair<DxfObject,DxfObject>(this.source, this.target); this.afterYield(); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
