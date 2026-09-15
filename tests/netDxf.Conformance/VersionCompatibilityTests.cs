using System.Collections.Generic;
using System.Reflection;
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
    private static readonly string[] CompatibilityFeatures = {
        "color", "shadow", "mesh", "helix", "light", "section", "vertex-id", "background", "frame",
        "columns-direct", "columns-embedded", "columns-linked", "hatch-fit", "mleader", "mleader-optional", "mleader-extend",
        "sat", "sat-history", "camera", "live-section", "live-section-null", "sun-view", "sun-vport", "sun-viewport",
        "shade-layout", "shade-page", "datatable", "geodata", "lightlist", "sun", "mleaderstyle", "sectionsettings", "sortents" };
    private static void RegisterVersionCompatibilityTests()
    {
        foreach (DxfVersion target in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            foreach (string feature in CompatibilityFeatures)
                Run($"version-compatibility/writer/{feature}/{target}/{binary}", () => CompatibilityWriter(feature, target, binary));
            foreach (string feature in new[] { "table", "tablestyle", "tablecontent", "tablegeometry", "cellstylemap-r2013", "cellstylemap-r2018", "field", "dimassoc", "section-manager", "sunstudy", "polyline-records" })
                Run($"version-compatibility/stored/{feature}/{target}/{binary}", () => CompatibilityStored(feature, target, binary));
            Run($"version-compatibility/omissions/{target}/{binary}", () => CompatibilityOmissions(target, binary));
            Run($"version-compatibility/defined-height/{target}/{binary}", () => CompatibilityDefinedHeight(target, binary));
        }
        foreach (string placement in new[] { "model", "paper", "unused-block", "nested-block", "attdef", "attrib" })
            Run($"version-compatibility/traversal/{placement}", () => CompatibilityPlacement(placement));
        foreach (bool binary in new[] { false, true }) {
            foreach (string feature in new[] { "table", "tablestyle", "tablecontent", "tablegeometry", "field", "dimassoc", "section-manager", "polyline-records" })
                Run($"version-compatibility/upgrade/{feature}/{binary}", () => CompatibilityStored(feature, DxfVersion.AutoCad2018, binary, feature == "field" ? DxfVersion.AutoCad2000 : DxfVersion.AutoCad2010));
            foreach (string host in new[] { "VIEW", "VPORT", "VIEWPORT" }) foreach (DxfVersion target in SupportedVersions)
                Run($"version-compatibility/sun-null/{host}/{target}/{binary}", () => CompatibilitySunNull(host, target, binary));
            foreach (DxfVersion target in SupportedVersions)
                Run($"version-compatibility/polygonmesh/{target}/{binary}", () => CompatibilityPolygonMesh(target, binary));
        }
        Run("version-compatibility/pristine-purity", CompatibilityPristine);
        Run("version-compatibility/live-reference-snapshot", CompatibilitySnapshot);
        Run("version-compatibility/metadata-callbacks", CompatibilityCallbacks);
        Run("version-compatibility/bounded-not-save-promise", CompatibilityBounded);
        foreach (DxfVersion target in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"version-compatibility/opaque-exclusion/{target}/{binary}", () => CompatibilityOpaque(target, binary));
    }

    private sealed class CompatibilityState
    {
        private readonly DxfDocument document;
        private readonly object? database;
        private readonly DxfVersion version;
        private readonly long seed;
        private readonly string headerSeed;
        private readonly KeyValuePair<string, DxfObject>[] registered;
        private readonly int apps, classes;
        internal CompatibilityState(DxfDocument document)
        {
            this.document = document; this.database = Database(document); this.version = document.DrawingVariables.AcadVer;
            this.seed = OwnershipSeed(document); this.headerSeed = document.DrawingVariables.HandleSeed;
            this.registered = Registered(document).ToArray(); this.apps = document.ApplicationRegistries.Count; this.classes = document.Classes.Count;
        }
        internal static object? Database(DxfDocument document) => typeof(DxfDocument).GetField("objectDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document);
        internal static IEnumerable<KeyValuePair<string,DxfObject>> Registered(DxfDocument document) => (IEnumerable<KeyValuePair<string,DxfObject>>)typeof(DxfDocument).GetField("AddedObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;
        internal void CheckUnchanged()
        {
            Check(ReferenceEquals(this.database, Database(this.document)), "Analysis created/replaced lazy object database");
            Equal(this.version, this.document.DrawingVariables.AcadVer, "Analysis changed AcadVer");
            Equal(this.seed, OwnershipSeed(this.document), "Analysis allocated handles");
            Equal(this.headerSeed, this.document.DrawingVariables.HandleSeed, "Analysis changed header handle seed");
            var current = Registered(this.document).ToArray();
            Check(this.registered.Length == current.Length && this.registered.Zip(current, (before, after) => before.Key == after.Key && ReferenceEquals(before.Value, after.Value)).All(equal => equal), "Analysis changed registered object identities/order");
            Equal(this.apps, this.document.ApplicationRegistries.Count, "Analysis changed APPIDs");
            Equal(this.classes, this.document.Classes.Count, "Analysis changed CLASS definitions");
        }
    }
    private static DxfVersionCompatibilityReport CompatibilityAnalyze(DxfDocument document, DxfVersion target)
    {
        var state = new CompatibilityState(document); var report = document.AnalyzeVersionCompatibility(target); state.CheckUnchanged();
        Equal(document.DrawingVariables.AcadVer, report.SourceVersion, "Captured source profile"); Equal(target, report.TargetVersion, "Requested profile");
        var second = document.AnalyzeVersionCompatibility(target); state.CheckUnchanged();
        Check(report.Diagnostics.Select(CompatibilityDescription).SequenceEqual(second.Diagnostics.Select(CompatibilityDescription)), "Analysis order/snapshot not repeatable");
        return report;
    }
    private static string CompatibilityDescription(DxfVersionCompatibilityDiagnostic item) => $"{item.Code}|{item.Kind}|{item.SourceHandle}|{item.SourceCodeName}|{item.PropertyPath}|{item.Message}";
    private static bool CompatibilitySave(DxfDocument document, DxfVersion target, bool binary, string stem, out byte[] bytes)
    {
        document.DrawingVariables.AcadVer = target;
        using var stream = new MemoryStream(); bool saved;
        try { saved = document.Save(stream, binary); }
        catch (Exception error) when (error is NotSupportedException or InvalidOperationException or ArgumentException or DxfVersionNotSupportedException or InvalidDataException) { saved = false; }
        bytes = stream.ToArray();
        if (saved) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"compatibility-{stem}-{target}-{binary}.dxf"), bytes);
        return saved;
    }
    private static void CompatibilityWriter(string feature, DxfVersion target, bool binary)
    {
        DxfDocument document = CompatibilityFeature(feature); var report = CompatibilityAnalyze(document, target);
        bool reject = feature switch {
            "color" or "datatable" or "sortents" => target < DxfVersion.AutoCad2004,
            "mesh" or "hatch-fit" or "mleader-optional" or "sun-view" or "geodata" => target < DxfVersion.AutoCad2010,
            "vertex-id" or "mleader-extend" => target < DxfVersion.AutoCad2013,
            "frame" or "columns-embedded" => target < DxfVersion.AutoCad2018,
            "columns-linked" or "sat" => target >= (feature == "sat" ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018),
            "sat-history" => target < DxfVersion.AutoCad2007 || target >= DxfVersion.AutoCad2013,
            _ => target < DxfVersion.AutoCad2007 };
        Equal(reject, report.HasKnownRejections, feature + " boundary prediction");
        Equal(!reject, CompatibilitySave(document, target, binary, feature, out _), feature + " actual writer versus report");
        if (feature == "hatch-fit" && reject) Equal(3, report.Diagnostics.Count(d => d.Code == "HATCH_SPLINE_FIT_PROFILE"), "Each HATCH fit/tangent property reported separately");
        if (feature == "mleader-optional" && target < DxfVersion.AutoCad2010) Equal(6, report.Diagnostics.Count(d => d.Code == "MULTILEADER_OPTIONAL_FIELD_PROFILE"), "All six R2010 MLeader fields reported separately");
    }
    private static DxfDocument CompatibilityFeature(string feature)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018);
        switch (feature)
        {
            case "color": document.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { ColorName = "" }); break;
            case "shadow": document.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX) { ShadowMode = EntityShadowMode.CastAndReceive }); break;
            case "mesh": document.Entities.Add(new Mesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { new[] { 0, 1, 2 } })); break;
            case "helix": document.Entities.Add(NewHelixFixture()); break;
            case "light": document.Entities.Add(new Light()); break;
            case "section": document.Entities.Add(SectionExample()); break;
            case "vertex-id": var polyline = new Polyline2D(new[] { Vector2.Zero, Vector2.UnitX }); polyline.Vertexes[0].VertexIdentifier = 0; document.Entities.Add(polyline); break;
            case "background": document.Entities.Add(new MText("background") { BackgroundFill = ExpectedBackground() }); break;
            case "frame": document.Entities.Add(new MText("frame") { BackgroundFill = MTextBackgroundFill.CreateTextFrame() }); break;
            case "columns-direct": document.Entities.Add(new MText("direct") { Columns = ColumnModel(0, MTextColumnStorage.Direct) }); break;
            case "columns-embedded": document.Entities.Add(new MText("embedded") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) }); break;
            case "columns-linked":
                document.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
                var columns = new MText("FIRSTSECONDTHIRD") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
                document.Entities.Add(columns.ConvertToLinkedColumns(new[] { "FIRST", "SECOND", "THIRD" })); break;
            case "hatch-fit": document.Entities.Add((Hatch)NewHatchSplineFit().Clone()); break;
            case "mleader": case "mleader-optional": case "mleader-extend":
                var pair = NewMLeader(DxfVersion.AutoCad2018); document = pair.Item1; var leader = pair.Item2; document.Entities.Add(leader);
                if (feature == "mleader-optional") {
                    leader.Properties.TextAttachmentDirection = 0; leader.Properties.TextBottomAttachment = 0; leader.Properties.TextTopAttachment = 0;
                    leader.Context.TopAttachment = 0; leader.Context.BottomAttachment = 0; leader.Context.Leaders.Add(new MLeaderNode { AttachmentDirection = 0 }); }
                if (feature == "mleader-extend") leader.Properties.LeaderExtendToText = false; break;
            case "sat": case "sat-history":
                document.DrawingVariables.AcadVer = DxfVersion.AutoCad2010; var solid = new Solid3D(); solid.SetEncodedSatChunks(new[] { new AcisSatChunk(1, "") });
                if (feature == "sat-history") solid.HistoryHandle = "0"; document.Entities.Add(solid); break;
            case "camera": document.Views.Add(new View("CAMERA") { IsCameraPlottable = true }); break;
            case "live-section": case "live-section-null":
                Section? section = null; if (feature == "live-section") { section = SectionExample(); document.Entities.Add(section); }
                document.Views.Add(new View("LIVE") { LiveSection = section }); break;
            case "sun-view": return SunSetup(DxfVersion.AutoCad2018, "VIEW").Document;
            case "sun-vport": return SunSetup(DxfVersion.AutoCad2018, "VPORT").Document;
            case "sun-viewport": return SunSetup(DxfVersion.AutoCad2018, "VIEWPORT").Document;
            case "shade-layout": case "shade-page":
                var target = new DxfXRecord(); document.Objects.Root.Add("SHADE", target);
                if (feature == "shade-layout") document.Layouts.First().PlotSettings.ShadePlotObject = target;
                else { var page = new DxfPlotSettingsObject(); page.Settings.ShadePlotObject = target; document.Objects.Root.Add("PAGE", page); } break;
            case "datatable": document.Objects.Root.Add("DATA", new DxfDataTable()); break;
            case "geodata": MakeGeoData(document); break;
            case "lightlist": document.Objects.Root.Add("LIGHTS", new DxfLightList(1)); break;
            case "sun": return SunSetup(DxfVersion.AutoCad2018).Document;
            case "mleaderstyle": var style = new DxfMLeaderStyle(); style.Properties.TextStyle = document.TextStyles["Standard"]; document.Objects.AddMLeaderStyle("STYLE", style); break;
            case "sectionsettings": document.Objects.Root.Add("SETTINGS", new DxfSectionSettings()); break;
            case "sortents": document.Objects.CreateSortentsTable(document.Blocks[Block.DefaultModelSpaceName].Record, Array.Empty<DxfSortOrderEntry>()); break;
            default: throw new ArgumentException(feature);
        }
        return document;
    }
    private static void CompatibilityStored(string feature, DxfVersion target, bool binary, DxfVersion source = DxfVersion.AutoCad2018)
    {
        DxfDocument document = feature switch {
            "table" => StoredTableCarrier(source, StoredTableTestPayload(), binary, out _),
            "tablestyle" => TableStyleLoad(TableStylePacket(), binary, source),
            "tablecontent" => TableContentLoad(TableContentRawBytes(TableContentMinimal(source), binary)),
            "tablegeometry" => TableGeometryLoad(TableGeometryRaw(source), binary),
            "cellstylemap-r2013" => TableContentLoad(TableContentRawBytes(StoredTableSource("acad_table_simple.dxf"), binary)),
            "cellstylemap-r2018" => TableContentLoad(TableContentRawBytes(StoredTableSource("acad_table_with_blk_ref.dxf"), binary)),
            "field" => StoredFieldCarrier(source, binary, out _),
            "dimassoc" => StoredDimAssocLoad(StoredDimAssocInput(source, binary)),
            "section-manager" => SectionManagerLoad(SectionManagerFixture(source), binary),
            "sunstudy" => LoadSunStudy(SunStudySource(), binary)!,
            "polyline-records" => StoredDimAssocLoad(PolylineRecordInput(source, binary)),
            _ => throw new ArgumentException(feature) };
        source = document.DrawingVariables.AcadVer;
        var report = CompatibilityAnalyze(document, target);
        Equal(target != source, report.HasKnownRejections, "Stored profile prediction");
        if (target != source) {
            Type expected = feature switch { "table" => typeof(StoredTable), "tablestyle" => typeof(DxfTableStyle), "tablecontent" => typeof(DxfStoredTableContent),
                "tablegeometry" => typeof(DxfStoredTableGeometry), "cellstylemap-r2013" or "cellstylemap-r2018" => typeof(DxfStoredCellStyleMap),
                "field" => typeof(DxfStoredField), "dimassoc" => typeof(DxfStoredDimAssoc), "section-manager" => typeof(DxfStoredSectionManager),
                "sunstudy" => typeof(DxfStoredSunStudy), _ => typeof(Polyline3DRecord) };
            Check(report.Diagnostics.Any(d => d.Code == "STORED_SOURCE_PROFILE" && d.SourceObject.GetType() == expected), "Specific stored-type diagnostic absent");
        }
        Equal(target == source, CompatibilitySave(document, target, binary, feature, out _), "Stored profile actual writer");
    }
    private static void CompatibilitySunNull(string kind, DxfVersion target, bool binary)
    {
        var setup = SunSetup(DxfVersion.AutoCad2018, kind); using var seed = new MemoryStream(); Check(setup.Document.Save(seed, binary), "Null SUN seed"); seed.Position = 0;
        var raw = DxfRawDocument.Load(seed); var sun = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN");
        var host = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == setup.Host.Handle));
        raw = raw.WithRecord(host, host.Tags.Select(t => t.Code == 361 ? new DxfTag(361, "0") : t));
        raw = raw.WithoutRecord(raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "SUN"));
        var document = SunLoad(raw); var report = CompatibilityAnalyze(document, target);
        bool rejected = target < (kind == "VIEW" ? DxfVersion.AutoCad2010 : DxfVersion.AutoCad2007);
        Equal(rejected, report.HasKnownRejections, "Explicit-null SUN profile prediction");
        Equal(rejected ? 1 : 0, report.Diagnostics.Count(d => d.Code == "SUN_OWNER_PROFILE"), "Explicit-null host diagnostic");
        Equal(!rejected, CompatibilitySave(document, target, binary, "null-sun-" + kind, out _), "Actual explicit-null SUN writer");
    }
    private static void CompatibilityPolygonMesh(DxfVersion target, bool binary)
    {
        var seed = new DxfDocument(DxfVersion.AutoCad2010); seed.Entities.Add(new PolygonMesh(2, 2, new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitX + Vector3.UnitY }));
        using var stream = new MemoryStream(); Check(seed.Save(stream, binary), "PolygonMesh source save"); stream.Position = 0; var document = DxfDocument.Load(stream)!;
        var report = CompatibilityAnalyze(document, target); bool rejected = target != DxfVersion.AutoCad2010;
        Equal(rejected, report.HasKnownRejections, "PolygonMesh source-profile diagnostic");
        Equal(rejected ? 5 : 0, report.Diagnostics.Count(d => d.SourceObject is PolygonMeshRecord), "Each actual child record diagnosed");
        Equal(!rejected, CompatibilitySave(document, target, binary, "polygonmesh", out _), "PolygonMesh actual profile writer");
    }
    private static void CompatibilityOmissions(DxfVersion target, bool binary)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.DrawingVariables.LastSavedBy = "Compatibility author";
        document.Classes.Add(new DxfClass("COMPATIBILITY_CLASS", "CompatibilityCpp", "Tests") { InstanceCount = 0 });
        var boundary = new HatchBoundaryPath(new[] { new HatchBoundaryPath.Polyline { IsClosed = true, Vertexes = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY } } });
        var hatch = new Hatch(new HatchGradientPattern(), new[] { boundary }, false); document.Entities.Add(hatch);
        var report = CompatibilityAnalyze(document, target);
        Equal(target == DxfVersion.AutoCad2000, report.HasKnownLosses, "Omission flags"); Check(!report.HasKnownRejections, "Omission should not promise rejection");
        Equal(target == DxfVersion.AutoCad2000 ? 3 : 0, report.Diagnostics.Count, "Independent omission inventory");
        Check(CompatibilitySave(document, target, binary, "omissions", out byte[] bytes), "Omission writer save");
        using var input = new MemoryStream(bytes); var raw = DxfRawDocument.Load(input);
        bool retained = target > DxfVersion.AutoCad2000;
        Equal(retained, raw.Sections.Single(s => s.Name == "HEADER").Records.SelectMany(r => r.Tags).Any(t => t.Code == 9 && (string)t.Value == "$LASTSAVEDBY"), "Actual LastSavedBy field");
        Equal(retained, raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "HATCH").Tags.Any(t => t.Code == 450), "Actual gradient field");
        Equal(retained, raw.Sections.Single(s => s.Name == "CLASSES").Records.Single(r => r.Tags.Any(t => t.Code == 1 && (string)t.Value == "COMPATIBILITY_CLASS")).Tags.Any(t => t.Code == 91), "Actual class count field");
    }
    private static void CompatibilityDefinedHeight(DxfVersion target, bool binary)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.Entities.Add(new MText("height") { DefinedHeight = 17.25 });
        var report = CompatibilityAnalyze(document, target); Check(!report.HasKnownRejections && !report.Diagnostics.Any(item => item.SourceObject is MText), "Representable defined height falsely diagnosed");
        Check(CompatibilitySave(document, target, binary, "defined-height", out byte[] bytes), "Defined height save");
        using var input = new MemoryStream(bytes); var loaded = DxfDocument.Load(input)!; Near(17.25, loaded.Entities.MTexts.Single().DefinedHeight!.Value, "Defined height retained through older XData representation");
    }
    private static void CompatibilityPlacement(string placement)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); DxfObject source;
        var line = new Line(Vector3.Zero, Vector3.UnitX) { ColorName = "", ShadowMode = EntityShadowMode.CastAndReceive };
        if (placement == "attdef" || placement == "attrib") {
            var block = new Block("ATTRIBUTE_BLOCK"); var definition = new AttributeDefinition("TAG"); block.AttributeDefinitions.Add(definition); document.Blocks.Add(block);
            if (placement == "attdef") { definition.ColorName = ""; definition.ShadowMode = EntityShadowMode.CastAndReceive; source = definition; }
            else { var insert = new Insert(block); document.Entities.Add(insert); var attribute = insert.Attributes.Single(); attribute.ColorName = ""; attribute.ShadowMode = EntityShadowMode.CastAndReceive; source = attribute; }
        }
        else {
            source = line;
            if (placement == "model") document.Entities.Add(line);
            else if (placement == "paper") { var layout = new Layout("CompatibilityPaper"); document.Layouts.Add(layout); layout.AssociatedBlock.Entities.Add(line); }
            else { var block = new Block("UNUSED_BLOCK"); block.Entities.Add(line); document.Blocks.Add(block); if (placement == "nested-block") { var outer = new Block("OUTER_UNUSED"); outer.Entities.Add(new Insert(block)); document.Blocks.Add(outer); } }
        }
        var report = CompatibilityAnalyze(document, DxfVersion.AutoCad2000);
        var matches = report.Diagnostics.Where(item => ReferenceEquals(item.SourceObject, source)).ToArray(); Equal(2, matches.Length, "All placements and explicit defaults inspected");
        Check(matches.Select(item => item.PropertyPath).OrderBy(path => path).SequenceEqual(new[] { "ColorName", "ShadowMode" }), "Public property paths");
    }
    private static void CompatibilityPristine()
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); Check(CompatibilityState.Database(document) == null, "Pristine document already has object DB");
        foreach (DxfVersion target in SupportedVersions) CompatibilityAnalyze(document, target);
        var state = new CompatibilityState(document);
        Throws<ArgumentOutOfRangeException>(() => document.AnalyzeVersionCompatibility(DxfVersion.Unknown));
        Throws<ArgumentOutOfRangeException>(() => document.AnalyzeVersionCompatibility((DxfVersion)int.MaxValue));
        var old = document.AnalyzeVersionCompatibility(DxfVersion.AutoCad12); Equal(1, old.Diagnostics.Count, "Unsupported profile bounded rule"); Equal("DXF_WRITER_PROFILE_UNSUPPORTED", old.Diagnostics[0].Code, "Unsupported rule"); state.CheckUnchanged();
    }
    private static void CompatibilitySnapshot()
    {
        var document = CompatibilityFeature("color"); var source = document.Entities.Lines.Single(); var report = CompatibilityAnalyze(document, DxfVersion.AutoCad2000); var item = report.Diagnostics.Single(d => d.Code == "ENTITY_COLOR_NAME_PROFILE");
        string captured = CompatibilityDescription(item); Check(ReferenceEquals(source, item.SourceObject), "Live exact source identity");
        Throws<NotSupportedException>(() => ((IList<DxfVersionCompatibilityDiagnostic>)report.Diagnostics).Clear());
        source.ColorName = null; document.Entities.Remove(source); document.DrawingVariables.AcadVer = DxfVersion.AutoCad2004;
        Equal(captured, CompatibilityDescription(item), "Captured diagnostic changed after live edit/removal"); Check(ReferenceEquals(source, item.SourceObject), "Source reference was replaced by clone");
        Equal(DxfVersion.AutoCad2018, report.SourceVersion, "Captured source version changed"); Check(report.HasKnownRejections, "Old summary changed");
        Check(!CompatibilityAnalyze(document, DxfVersion.AutoCad2000).HasKnownRejections, "Fresh report retained removed feature");
    }
    private static void CompatibilityCallbacks()
    {
        var document = CompatibilityFeature("columns-linked"); var application = new MetadataCallbackRegistry("COMPATIBILITY_CALLBACK");
        document.ApplicationRegistries.Add(application); var text = document.Entities.MTexts.First(); text.XData.Add(new XData(application));
        int calls = 0; application.Callback = () => { calls++; throw new InvalidOperationException("Analysis invoked clone callback"); };
        foreach (DxfVersion target in SupportedVersions) CompatibilityAnalyze(document, target);
        Equal(0, calls, "Analysis metadata callbacks");
    }
    private static void CompatibilityOpaque(DxfVersion target, bool binary)
    {
        var raw = TableContentMinimal(DxfVersion.AutoCad2018); var packet = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "TABLECONTENT");
        raw = raw.WithRecord(packet, packet.Tags.Concat(new[] { new DxfTag(100, "PrivateCompatibilitySubclass"), new DxfTag(300, "PRIVATE-SCHEMA") }));
        var document = TableContentLoad(TableContentRawBytes(raw, binary)); Check(document.Objects.Items.OfType<DxfOpaqueObject>().Any(item => item.CodeName == "TABLECONTENT"), "Private control must remain opaque");
        var report = CompatibilityAnalyze(document, target); Check(!report.HasKnownRejections && !report.Diagnostics.Any(item => item.SourceObject is DxfOpaqueObject), "Invented opaque profile legality");
        Check(CompatibilitySave(document, target, binary, "opaque-exclusion", out byte[] bytes), "Current opaque writer control");
        using var input = new MemoryStream(bytes); Check(DxfRawDocument.Load(input).Sections.SelectMany(s => s.Records).Any(r => r.Name == "TABLECONTENT" && r.Tags.Any(t => t.Code == 300 && (string)t.Value == "PRIVATE-SCHEMA")), "Opaque private packet lost");
    }
    private static void CompatibilityBounded()
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.Classes.Add(new DxfClass("RASTERVARIABLES", "WrongClass", "Private"));
        var report = CompatibilityAnalyze(document, DxfVersion.AutoCad2018); Check(!report.HasKnownRejections, "General schema error was presented as version restriction");
        Check(!CompatibilitySave(document, DxfVersion.AutoCad2018, false, "invalid-class", out _), "Bounded test must demonstrate another Save failure");
    }
}
