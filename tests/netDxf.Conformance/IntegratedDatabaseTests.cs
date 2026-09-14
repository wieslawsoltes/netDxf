using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string IntegratedConfiguration = "Integrated plan";
    private const string IntegratedApplication = "INTEGRATED_META";

    private static void RegisterIntegratedDatabaseTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"integration/database-roundtrip/{v}/{b}", () => IntegratedDatabaseRoundTrip(v, b));
            Run($"integration/cross-document-mapping/{v}/{b}", () => IntegratedDatabaseMapping(v, b));
        }
    }

    private static DxfDocument CreateIntegratedDatabase(DxfVersion version, bool offsetHandles = false)
    {
        var document = new DxfDocument(version);
        // Different allocation histories make omitted cross-document remapping observable.
        if (offsetHandles)
            for (int i = 0; i < 7; i++) document.Layers.Add(new Layer("Integration allocation offset " + i));
        var left = new VPort(IntegratedConfiguration) { LowerLeftCorner = Vector2.Zero, UpperRightCorner = new Vector2(0.5, 1), ViewCenter = new Vector2(3, 4) };
        var right = new VPort(IntegratedConfiguration) { LowerLeftCorner = new Vector2(0.5, 0), UpperRightCorner = new Vector2(1, 1), ViewCenter = new Vector2(8, 9) };
        document.VPorts.AddRecord(left); document.VPorts.AddRecord(right);
        var source = new MText("LeftRight")
        {
            Position = new Vector3(2, 3, 0), Height = 2,
            Columns = new MTextColumns
            {
                Storage = MTextColumnStorage.Embedded, Type = MTextColumnType.Static,
                Count = 2, Width = 12, Gutter = 2, DefinedHeight = 20, TotalHeight = 20
            }
        };
        MText main;
        if (version >= DxfVersion.AutoCad2018) { main = source; document.Entities.Add(main); }
        else
        {
            var columns = source.ConvertToLinkedColumns(new[] { "Left", "Right" });
            main = columns[0]; foreach (MText column in columns) document.Entities.Add(column);
        }
        var graph = new DxfDictionary();
        var state = new DxfXRecord();
        var mode = new DxfDictionaryVariable { Value = "original" };
        graph.Add("STATE", state); graph.Add("STATE_ALIAS", state, false); graph.Add("MODE", mode);
        document.NamedObjects.Add("INTEGRATED", graph);
        state.Data.Add(new DxfTag(1, "view and text state"));
        state.Data.Add(new DxfTag(330, left.Handle));
        state.Data.Add(new DxfTag(340, right.Handle));
        state.Data.Add(new DxfTag(340, main.Handle));
        state.Data.Add(new DxfTag(330, mode.Handle));
        foreach (MText linked in main.Columns!.LinkedColumns) state.Data.Add(new DxfTag(340, linked.Handle));
        state.Data.Add(new DxfTag(320, "FEDCBA"));
        state.PersistentReactors.Add(right);
        left.PersistentReactors.Add(state);
        main.PersistentReactors.Add(right);
        IntegratedAddXData(left, "left viewport", state);
        IntegratedAddXData(right, "right viewport", main);
        IntegratedAddXData(main, "main text", right);
        IntegratedAddXData(graph, "named graph", mode);
        IntegratedAddXData(state, "state record", left);
        IntegratedAttachMetadata(document, left, state, main);
        IntegratedAttachMetadata(document, right, state, left);
        IntegratedAttachMetadata(document, main, state, right);
        return document;
    }

    private static void IntegratedAddXData(DxfObject owner, string label, DxfObject target)
    {
        var data = new XData(new ApplicationRegistry(IntegratedApplication));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, label));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, target.Handle));
        owner.XData.Add(data);
    }

    private static void IntegratedAttachMetadata(DxfDocument document, DxfObject owner, DxfXRecord state, DxfObject peer)
    {
        var extension = new DxfDictionary();
        var record = new DxfXRecord();
        record.Data.Add(new DxfTag(1, "cross-module extension"));
        record.Data.Add(new DxfTag(330, state.Handle));
        record.Data.Add(new DxfTag(340, peer.Handle));
        extension.Add("LINKS", record);
        document.Objects.SetExtensionDictionary(owner, extension);
    }

    private static void IntegratedAssertPointer(DxfDocument document, DxfTag tag, DxfObject target, string message)
    {
        Equal(target.Handle, (string)tag.Value, message + " handle");
        Check(ReferenceEquals(target, document.GetObjectByHandle((string)tag.Value)), message + " object identity");
    }

    private static void IntegratedAssertXData(DxfDocument document, DxfObject owner, string label, DxfObject target)
    {
        var data = owner.XData[IntegratedApplication];
        Equal(label, (string)data.XDataRecord[0].Value, "External XData label");
        Equal(target.Handle, (string)data.XDataRecord[1].Value, "External XData handle");
        Check(ReferenceEquals(target, document.GetObjectByHandle((string)data.XDataRecord[1].Value)), "External XData target identity");
        Check(ReferenceEquals(document.ApplicationRegistries[IntegratedApplication], data.ApplicationRegistry), "External XData registry was not canonicalized");
    }

    private static void IntegratedAssertExtension(DxfDocument document, DxfObject owner, DxfXRecord state, DxfObject peer)
    {
        DxfDictionary extension = owner.ExtensionDictionary ?? throw new InvalidOperationException("Cross-module extension dictionary missing");
        Check(ReferenceEquals(extension.Owner, owner), "Extension dictionary owner identity");
        Check(ReferenceEquals(extension, document.GetObjectByHandle(extension.Handle)), "Extension dictionary registration");
        var record = (DxfXRecord)extension["LINKS"];
        Check(ReferenceEquals(record.Owner, extension), "Extension XRECORD ownership");
        IntegratedAssertPointer(document, record.Data[1], state, "Extension state reference");
        IntegratedAssertPointer(document, record.Data[2], peer, "Extension peer reference");
    }

    private static void IntegratedAssertGraph(DxfDocument document, DxfDictionary graph, VPort left, VPort right, MText main, string modeValue)
    {
        var state = (DxfXRecord)graph["STATE"];
        var mode = (DxfDictionaryVariable)graph["MODE"];
        Check(ReferenceEquals(state, graph["STATE_ALIAS"]), "Named alias identity");
        Check(ReferenceEquals(state.Owner, graph), "Named XRECORD ownership");
        Equal(modeValue, mode.Value, "Independent graph state");
        IntegratedAssertPointer(document, state.Data[1], left, "XRECORD soft viewport reference");
        IntegratedAssertPointer(document, state.Data[2], right, "XRECORD hard duplicate viewport reference");
        IntegratedAssertPointer(document, state.Data[3], main, "XRECORD hard MTEXT reference");
        IntegratedAssertPointer(document, state.Data[4], mode, "XRECORD internal reference");
        for (int i = 0; i < main.Columns!.LinkedColumns.Count; i++)
            IntegratedAssertPointer(document, state.Data[5 + i], main.Columns.LinkedColumns[i], "XRECORD linked-column reference");
        Equal("FEDCBA", (string)state.Data.Last().Value, "Arbitrary handle must remain opaque");
        Check(ReferenceEquals(right, state.PersistentReactors.Single()), "Cross-module graph reactor identity");
        IntegratedAssertXData(document, graph, "named graph", mode);
        IntegratedAssertXData(document, state, "state record", left);
    }

    private static void IntegratedAssertDatabase(DxfDocument document, DxfVersion version)
    {
        var tiles = document.VPorts.GetConfiguration(IntegratedConfiguration);
        Equal(2, tiles.Count, "Repeated-name VPORT tile count");
        var left = tiles[0]; var right = tiles[1];
        Check(!ReferenceEquals(left, right) && left.Handle != right.Handle, "Repeated-name viewport identity collapsed");
        Equal(new Vector2(3, 4), left.ViewCenter, "First tile was reordered");
        Equal(new Vector2(8, 9), right.ViewCenter, "Second tile was reordered");
        var main = document.Entities.MTexts.Single(m => m.Columns != null);
        var columns = main.Columns!;
        Equal(2, columns.Count, "MTEXT column count");
        Equal(12.0, columns.Width, "MTEXT column width");
        Equal(2.0, columns.Gutter, "MTEXT gutter");
        if (version >= DxfVersion.AutoCad2018)
        {
            Equal(MTextColumnStorage.Embedded, columns.Storage, "Embedded column storage");
            Equal("LeftRight", main.Value, "Embedded combined text");
            Equal(1, document.Entities.MTexts.Count(), "Embedded MTEXT entity count");
        }
        else
        {
            Equal(MTextColumnStorage.LegacyLinked, columns.Storage, "Legacy column storage");
            Equal("Left", main.Value, "Legacy first text");
            var linked = columns.LinkedColumns.Single();
            Equal("Right", linked.Value, "Legacy linked text");
            Check(ReferenceEquals(linked, document.Entities.MTexts.Single(m => m.Columns == null)), "Legacy link does not reference the actual document entity");
            Check(ReferenceEquals(main.Owner, linked.Owner), "Legacy columns have different block owners");
        }
        var graph = (DxfDictionary)document.NamedObjects["INTEGRATED"];
        var state = (DxfXRecord)graph["STATE"];
        IntegratedAssertGraph(document, graph, left, right, main, "original");
        IntegratedAssertExtension(document, left, state, main);
        IntegratedAssertExtension(document, right, state, left);
        IntegratedAssertExtension(document, main, state, right);
        Check(ReferenceEquals(state, left.PersistentReactors.Single()), "VPORT reactor to named object lost");
        Check(ReferenceEquals(right, main.PersistentReactors.Single()), "MTEXT reactor to table record lost");
        IntegratedAssertXData(document, left, "left viewport", state);
        IntegratedAssertXData(document, right, "right viewport", main);
        IntegratedAssertXData(document, main, "main text", right);
        Equal(0, document.Objects.Validate().Count, "Integrated database graph validation");
    }

    private static DxfDocument IntegratedPersist(DxfDocument document, bool binary, string artifact)
    {
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Integrated document save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, artifact), output.ToArray());
        output.Position = 0;
        return DxfDocument.Load(output) ?? throw new InvalidOperationException("Integrated document reload failed");
    }

    private static void IntegratedDatabaseRoundTrip(DxfVersion version, bool binary)
    {
        var document = CreateIntegratedDatabase(version);
        string[]? stableHandles = null;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            IntegratedAssertDatabase(document, version);
            string[] handles = document.VPorts.GetConfiguration(IntegratedConfiguration).Select(p => p.Handle)
                .Concat(document.Entities.MTexts.Select(m => m.Handle)).ToArray();
            if (stableHandles == null) stableHandles = handles;
            else Check(stableHandles.SequenceEqual(handles), "Table/entity handles changed across mixed persistence");
            document = IntegratedPersist(document, cycle == 1 ? !binary : binary, $"integrated-database-{version}-{binary}-{cycle}.dxf");
        }
        IntegratedAssertDatabase(document, version);
        var source = (DxfDictionary)document.NamedObjects["INTEGRATED"];
        var copy = document.Objects.Clone(source, document.NamedObjects, "LOCAL_COPY");
        ((DxfDictionaryVariable)copy["MODE"]).Value = "local copy";
        var tiles = document.VPorts.GetConfiguration(IntegratedConfiguration);
        var main = document.Entities.MTexts.Single(m => m.Columns != null);
        IntegratedAssertGraph(document, copy, tiles[0], tiles[1], main, "local copy");
        IntegratedAssertDatabase(document, version);
        document = IntegratedPersist(document, !binary, $"integrated-local-clone-{version}-{binary}.dxf");
        tiles = document.VPorts.GetConfiguration(IntegratedConfiguration); main = document.Entities.MTexts.Single(m => m.Columns != null);
        IntegratedAssertGraph(document, (DxfDictionary)document.NamedObjects["LOCAL_COPY"], tiles[0], tiles[1], main, "local copy");
        IntegratedAssertDatabase(document, version);
    }

    private static void IntegratedDatabaseMapping(DxfVersion version, bool binary)
    {
        var source = CreateIntegratedDatabase(version);
        source = IntegratedPersist(source, binary, $"integrated-mapping-source-{version}-{binary}.dxf");
        IntegratedAssertDatabase(source, version);
        var target = CreateIntegratedDatabase(version, offsetHandles: true);
        var sourceTiles = source.VPorts.GetConfiguration(IntegratedConfiguration);
        var targetTiles = target.VPorts.GetConfiguration(IntegratedConfiguration);
        var sourceText = source.Entities.MTexts.Single(m => m.Columns != null);
        var targetText = target.Entities.MTexts.Single(m => m.Columns != null);
        Check(sourceTiles[0].Handle != targetTiles[0].Handle && sourceTiles[1].Handle != targetTiles[1].Handle, "Viewport mappings must change both external handles");
        Check(sourceText.Handle != targetText.Handle, "MTEXT mapping must change its external handle");
        var mappings = new Dictionary<DxfObject, DxfObject>
        {
            [sourceTiles[0]] = targetTiles[0], [sourceTiles[1]] = targetTiles[1], [sourceText] = targetText
        };
        for (int i = 0; i < sourceText.Columns!.LinkedColumns.Count; i++)
        {
            Check(sourceText.Columns.LinkedColumns[i].Handle != targetText.Columns!.LinkedColumns[i].Handle, "Linked MTEXT mapping must change its external handle");
            mappings.Add(sourceText.Columns.LinkedColumns[i], targetText.Columns.LinkedColumns[i]);
        }
        var original = (DxfDictionary)source.NamedObjects["INTEGRATED"];
        int count = target.Objects.Items.Count;
        Throws<InvalidOperationException>(() => target.Objects.Clone(original, target.NamedObjects, "MISSING_MAP"));
        Equal(count, target.Objects.Items.Count, "Rejected graph copy changed destination registration");
        var copy = target.Objects.Clone(original, target.NamedObjects, "IMPORTED", mappings);
        Check(copy["STATE"].Handle != original["STATE"].Handle && copy["MODE"].Handle != original["MODE"].Handle, "Cloned internal objects require distinct destination handles");
        ((DxfDictionaryVariable)copy["MODE"]).Value = "imported copy";
        IntegratedAssertGraph(target, copy, targetTiles[0], targetTiles[1], targetText, "imported copy");
        IntegratedAssertDatabase(source, version);
        IntegratedAssertDatabase(target, version);
        target = IntegratedPersist(target, !binary, $"integrated-mapped-clone-{version}-{binary}.dxf");
        targetTiles = target.VPorts.GetConfiguration(IntegratedConfiguration); targetText = target.Entities.MTexts.Single(m => m.Columns != null);
        IntegratedAssertGraph(target, (DxfDictionary)target.NamedObjects["IMPORTED"], targetTiles[0], targetTiles[1], targetText, "imported copy");
        IntegratedAssertDatabase(target, version);
    }
}
