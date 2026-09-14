using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    // Intentionally registered separately: these exercise interactions among the feature modules.
    private static void RegisterMixedModuleIntegrationTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"mixed-modules/roundtrip/{v}/{b}", () => MixedModuleRoundTrip(v, b));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"mixed-modules/cross-document/{v}/{b}", () => MixedModuleClone(v, b));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"mixed-modules/reference-lifecycle/{v}/{b}", () => MixedModuleLifecycle(v, b));
        }
    }

    private static readonly byte[] MixedProxyBytes = { 0, 1, 2, 255, 10, 13, 0 };
    private static readonly Vector2[] MixedPoints = { new(0, 0), new(5, 0), new(5, 4), new(0, 4) };
    private static readonly double[] MixedBulges = { .5, 0, -.25, 0 };
    private static readonly double?[] MixedStarts = { null, 0, 2, null };
    private static readonly double?[] MixedEnds = { 1, null, 3, null };
    private static readonly int[] MixedIds = { 0, -17, int.MaxValue, 42 };

    private static XData MixedData(string label, DxfObject reference)
    {
        var data = new XData(new ApplicationRegistry("MIXED_MODULES"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, label));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, reference.Handle));
        return data;
    }

    private static DxfDocument BuildMixedModuleDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        var frame = new UCS("MIX_FRAME") { Origin = new Vector3(3, 4, 5), Elevation = 4.5, Flags = UcsFlags.Referenced };
        frame.SetOrthographicOrigin(UcsOrthographicType.Top, new Vector3(0, 0, 1)); doc.UCSs.Add(frame);
        doc.Views.Add(new View("MIX_VIEW") { Ucs = new ViewUcs { NamedUcs = frame, BaseUcs = frame, OrthographicType = 1,
            Origin = new Vector3(6, 7, 8), XAxis = new Vector3(0, 2, 0), YAxis = new Vector3(-3, 0, 0), Elevation = 1.25 } });
        doc.VPorts.Add(new VPort("MIX_PORT") { NamedUcs = frame, BaseUcs = frame, UcsOrthographicType = 1,
            UcsOrigin = new Vector3(-2, -3, -4), UcsElevation = -2.5 });
        var original = new Polyline2D(Enumerable.Range(0, 4).Select(i => new Polyline2DVertex(MixedPoints[i], MixedBulges[i])
        { StartWidthOverride = MixedStarts[i], EndWidthOverride = MixedEnds[i], VertexIdentifier = version >= DxfVersion.AutoCad2013 ? MixedIds[i] : null }), true)
        {
            Layer = new Layer("MIX_ORIGINAL"), ConstantWidth = 1.5, Elevation = 2,
            ColorName = version >= DxfVersion.AutoCad2004 ? "BOOK$Mixed" : null,
            ShadowMode = version >= DxfVersion.AutoCad2007 ? EntityShadowMode.CastAndReceive : null,
            ProxyGraphics = MixedProxyBytes
        };
        var reverse = (Polyline2D)original.Clone(); reverse.Layer = new Layer("MIX_REVERSED"); reverse.Reverse();
        // Changing common metadata on the clone must not touch the source, even after geometry editing.
        reverse.ColorName = "temporary"; Equal(version >= DxfVersion.AutoCad2004 ? "BOOK$Mixed" : null, original.ColorName, "Mixed clone shares color-name state");
        reverse.ColorName = original.ColorName;
        reverse.ProxyGraphics = new byte[] { 9 }; Check(original.ProxyGraphics.SequenceEqual(MixedProxyBytes), "Mixed clone shares proxy state"); reverse.ProxyGraphics = MixedProxyBytes;
        var line = new Line(new Vector3(20, 30, 40), new Vector3(50, 60, 70));
        var host = new Block("MIX_HOST", new EntityObject[] { original, reverse, line });
        var insert = new Insert(host, new Vector3(10, 0, 0)); doc.Entities.Add(insert);
        var filter = new DxfSpatialFilter { Normal = new Vector3(0, 0, 2), Origin = new Vector3(1, 2, 3), FrontClippingDistance = 2.5,
            InverseInsertTransform = new Matrix4(1, 0, 0, -10, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1) };
        filter.SetBoundary(new[] { new Vector2(-1, -2), new Vector2(3, 4) }); filter.XData.Add(MixedData("filter", original)); doc.Objects.SetSpatialFilter(insert, filter);
        // Every eligible object shares the same host extension dictionary, in either helper-creation order.
        if (version >= DxfVersion.AutoCad2010 && version != DxfVersion.AutoCad2013) MixedAddGeoData(doc, host, original);
        if (version >= DxfVersion.AutoCad2004)
            doc.Objects.CreateSortentsTable(host.Record, new[] { new DxfSortOrderEntry(reverse, "0"), new DxfSortOrderEntry(line, original.Handle), new DxfSortOrderEntry(original, "FFFFFFFFFFFFFFFF") });
        if (version == DxfVersion.AutoCad2013) MixedAddGeoData(doc, host, original);
        if (host.Record.ExtensionDictionary == null) doc.Objects.SetExtensionDictionary(host.Record, new DxfDictionary());
        var links = new DxfIdBuffer();
        foreach (DxfObject? reference in new DxfObject?[] { original, reverse, line, frame, doc.Views["MIX_VIEW"], doc.VPorts["MIX_PORT"], insert, null, original }) links.References.Add(reference);
        var geo = doc.Objects.GetGeoData(host.Record); if (geo != null) links.References.Add(geo);
        links.XData.Add(MixedData("links", original)); (host.Record.ExtensionDictionary ?? throw new InvalidOperationException("Mixed host extension was not attached.")).Add("MIX_REFERENCES", links);
        return doc;
    }

    private static void MixedAddGeoData(DxfDocument doc, Block host, Polyline2D original)
    {
        var geo = new DxfGeoData(host.Record) { CoordinateType = DxfGeoCoordinateType.LocalGrid, DesignPoint = new Vector3(1, 2, 3), ReferencePoint = new Vector3(100, 200, 300),
            UpDirection = new Vector3(0, 0, 2), NorthDirection = new Vector2(0, 3), ScaleEstimation = DxfGeoScaleEstimation.UserScale, UserScaleFactor = 1.00025,
            SeaLevelCorrection = true, SeaLevelElevation = 12.5, CoordinateProjectionRadius = 6378137, CoordinateSystemDefinition = "LOCAL_MIXED\nuninterpreted" };
        geo.SetMesh(new[] { new DxfGeoMeshPoint(new Vector2(0, 0), new Vector2(100, 200)), new DxfGeoMeshPoint(new Vector2(5, 0), new Vector2(110, 200)), new DxfGeoMeshPoint(new Vector2(5, 4), new Vector2(110, 212)) },
            new[] { new DxfGeoMeshFace(0, 1, 2) });
        geo.XData.Add(MixedData("geo", original)); doc.Objects.SetGeoData(geo);
    }

    private static Polyline2D MixedPolyline(Block host, bool reversed) => host.Entities.OfType<Polyline2D>().Single(p => p.Layer.Name == (reversed ? "MIX_REVERSED" : "MIX_ORIGINAL"));
    private static DxfIdBuffer MixedLinks(Block host) => (DxfIdBuffer)host.Record.ExtensionDictionary["MIX_REFERENCES"];
    private static DxfSpatialFilter MixedFilter(Insert insert) => (DxfSpatialFilter)((DxfDictionary)insert.ExtensionDictionary["ACAD_FILTER"])["SPATIAL"];
    private static void MixedReferenceData(DxfDocument doc, DxfDatabaseObject item, DxfObject expected)
    {
        string handle = (string)item.XData["MIXED_MODULES"].XDataRecord[1].Value;
        Check(ReferenceEquals(doc.GetObjectByHandle(handle), expected), "Mixed XData reference escaped its destination graph.");
    }

    private static void AssertMixedModuleDocument(DxfDocument doc, string hostName, bool tablesPresent, string? preservedSortKey = null)
    {
        DxfVersion version = doc.DrawingVariables.AcadVer; var host = doc.Blocks[hostName]; var original = MixedPolyline(host, false); var reverse = MixedPolyline(host, true);
        foreach (bool reversed in new[] { false, true })
        {
            var polyline = reversed ? reverse : original; Equal((double?)1.5, polyline.ConstantWidth, "Mixed constant width"); Equal(2.0, polyline.Elevation, "Mixed elevation"); Check(polyline.IsClosed, "Mixed closure");
            Equal(version >= DxfVersion.AutoCad2004 ? "BOOK$Mixed" : null, polyline.ColorName, "Mixed common color name");
            Equal(version >= DxfVersion.AutoCad2007 ? (EntityShadowMode?)EntityShadowMode.CastAndReceive : null, polyline.ShadowMode, "Mixed shadow presence");
            Check(polyline.ProxyGraphics.SequenceEqual(MixedProxyBytes), "Mixed proxy payload");
            for (int i = 0; i < 4; i++)
            {
                int point = reversed ? 3 - i : i, edge = reversed ? (6 - i) % 4 : i; var vertex = polyline.Vertexes[i];
                Equal(MixedPoints[point], vertex.Position, "Mixed point/reverse association"); Equal(reversed ? -MixedBulges[edge] : MixedBulges[edge], vertex.Bulge, "Mixed bulge association");
                Equal(reversed ? MixedEnds[edge] : MixedStarts[edge], vertex.StartWidthOverride, "Mixed optional start width");
                Equal(reversed ? MixedStarts[edge] : MixedEnds[edge], vertex.EndWidthOverride, "Mixed optional end width");
                Equal(version >= DxfVersion.AutoCad2013 ? (int?)MixedIds[point] : null, vertex.VertexIdentifier, "Mixed vertex identifier");
                Equal(1.5, polyline.GetEffectiveStartWidth(i), "Mixed effective width");
            }
        }
        var line = host.Entities.OfType<Line>().Single(); Equal(new Vector3(20, 30, 40), line.StartPoint, "Mixed following line start"); Equal(new Vector3(50, 60, 70), line.EndPoint, "Mixed following line end");
        var insert = doc.Entities.Inserts.Single(); Check(ReferenceEquals(insert.Block, host), "Mixed block ownership"); Equal(new Vector3(10, 0, 0), insert.Position, "Mixed insert position");
        var filter = MixedFilter(insert); Equal((double?)2.5, filter.FrontClippingDistance, "Mixed front distance versus matrix"); Equal(null, filter.BackClippingDistance, "Mixed absent back distance");
        Equal(new Vector3(0, 0, 2), filter.Normal, "Mixed filter normal magnitude"); Equal(new Vector3(1, 2, 3), filter.Origin, "Mixed filter origin"); Equal(-10.0, filter.InverseInsertTransform.M14, "Mixed inverse matrix translation");
        Equal(Matrix4.Identity, filter.ClipBoundaryTransform, "Mixed boundary matrix"); Check(filter.Boundary.SequenceEqual(new[] { new Vector2(-1, -2), new Vector2(3, 4) }), "Mixed filter boundary");
        MixedReferenceData(doc, filter, original);
        var extension = host.Record.ExtensionDictionary; var geo = doc.Objects.GetGeoData(host.Record);
        if (version >= DxfVersion.AutoCad2010)
        {
            Check(geo != null && ReferenceEquals(geo.Owner, extension) && ReferenceEquals(geo.HostBlock, host.Record), "Mixed GEODATA host/owner mapping");
            Equal(new Vector3(1, 2, 3), geo!.DesignPoint, "Mixed design point"); Equal(new Vector3(100, 200, 300), geo.ReferencePoint, "Mixed geographic reference point");
            Equal(new Vector3(0, 0, 2), geo.UpDirection, "Mixed geographic up direction"); Equal(new Vector2(0, 3), geo.NorthDirection, "Mixed geographic north direction");
            Equal("LOCAL_MIXED\nuninterpreted", geo.CoordinateSystemDefinition, "Mixed coordinate text"); Equal(3, geo.MeshPoints.Count, "Mixed mesh points"); Equal(1, geo.MeshFaces.Count, "Mixed mesh faces");
            Equal(new Vector2(110, 212), geo.MeshPoints[2].Target, "Mixed mesh target"); Equal(2, geo.MeshFaces[0].Third, "Mixed mesh face index"); MixedReferenceData(doc, geo, original);
        }
        else Check(geo == null, "Mixed older profile invented GEODATA");
        if (version >= DxfVersion.AutoCad2004)
        {
            var sort = (DxfSortentsTable)extension["ACAD_SORTENTS"]; Check(ReferenceEquals(sort.Owner, extension) && ReferenceEquals(sort.BlockRecord, host.Record), "Mixed SORTENTS host/owner mapping");
            Check(sort.Entries.Select(e => e.Entity).SequenceEqual(new EntityObject[] { reverse, line, original }), "Mixed sort entity mapping/order");
            Check(sort.Entries.Select(e => e.SortHandle).SequenceEqual(new[] { "0", preservedSortKey ?? original.Handle, "FFFFFFFFFFFFFFFF" }), "Mixed opaque sort keys changed");
        }
        var links = MixedLinks(host); var expected = new List<DxfObject?> { original, reverse, line };
        if (tablesPresent)
        {
            var frame = doc.UCSs["MIX_FRAME"]; var view = doc.Views["MIX_VIEW"]; var port = doc.VPorts["MIX_PORT"];
            Check(ReferenceEquals(view.Ucs.NamedUcs, frame) && ReferenceEquals(view.Ucs.BaseUcs, frame) && ReferenceEquals(port.NamedUcs, frame) && ReferenceEquals(port.BaseUcs, frame), "Mixed VIEW/VPORT UCS identities");
            Equal(4, frame.GetReferences().Sum(r => r.Uses), "Mixed VIEW/VPORT reference multiplicity");
            Equal(new Vector3(0, 2, 0), view.Ucs.XAxis, "Mixed VIEW stored X axis"); Equal(new Vector3(-3, 0, 0), view.Ucs.YAxis, "Mixed VIEW stored Y axis");
            expected.AddRange(new DxfObject[] { frame, view, port });
        }
        else Check(!doc.UCSs.Contains("MIX_FRAME") && !doc.Views.Contains("MIX_VIEW") && !doc.VPorts.Contains("MIX_PORT"), "Removed mixed table records survived");
        expected.AddRange(new DxfObject?[] { insert, null, original }); if (geo != null) expected.Add(geo);
        Check(links.References.Count == expected.Count && links.References.Zip(expected).All(pair => ReferenceEquals(pair.First, pair.Second)), "Mixed IDBUFFER ordered references or null slot changed");
        MixedReferenceData(doc, links, original); Equal(0, doc.Objects.Validate().Count, "Mixed database graph validation");
    }

    private static DxfDocument MixedExport(DxfDocument doc, bool binary, string scenario, string hostName, bool tablesPresent, string? sortKey = null)
    {
        AssertMixedModuleDocument(doc, hostName, tablesPresent, sortKey);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Mixed output save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mixed-{scenario}-{doc.DrawingVariables.AcadVer}-{binary}.dxf"), output.ToArray());
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Mixed output reload failed."); AssertMixedModuleDocument(loaded, hostName, tablesPresent, sortKey);
        using var alternate = new MemoryStream(); Check(loaded.Save(alternate, !binary), "Mixed alternate-transport save"); alternate.Position = 0;
        AssertMixedModuleDocument(DxfDocument.Load(alternate) ?? throw new InvalidOperationException("Mixed alternate-transport reload failed."), hostName, tablesPresent, sortKey);
        return loaded;
    }

    private static void MixedModuleRoundTrip(DxfVersion version, bool binary) => MixedExport(BuildMixedModuleDocument(version), binary, "roundtrip", "MIX_HOST", true);

    private static void MixedModuleClone(DxfVersion version, bool binary)
    {
        var source = BuildMixedModuleDocument(version); var sourceHost = source.Blocks["MIX_HOST"]; var sourceInsert = source.Entities.Inserts.Single();
        var target = new DxfDocument(version);
        for (int i = 0; i < 128; i++) target.Layers.Add(new Layer("MIX_PAD_" + i.ToString("D3")));
        var frame = (UCS)source.UCSs["MIX_FRAME"].Clone(); target.UCSs.Add(frame);
        var view = (View)source.Views["MIX_VIEW"].Clone(); view.Ucs.NamedUcs = frame; view.Ucs.BaseUcs = frame; target.Views.Add(view);
        var port = (VPort)source.VPorts["MIX_PORT"].Clone(); port.NamedUcs = frame; port.BaseUcs = frame; target.VPorts.Add(port);
        var sourceEntities = sourceHost.Entities.ToArray(); var targetEntities = sourceEntities.Select(e => (EntityObject)e.Clone()).ToArray();
        var host = new Block("MIX_HOST_COPY", targetEntities); var insert = new Insert(host, sourceInsert.Position); target.Entities.Add(insert);
        var mappings = new Dictionary<DxfObject, DxfObject> { [sourceHost.Record] = host.Record, [sourceInsert] = insert,
            [source.UCSs["MIX_FRAME"]] = frame, [source.Views["MIX_VIEW"]] = view, [source.VPorts["MIX_PORT"]] = port };
        for (int i = 0; i < sourceEntities.Length; i++) mappings.Add(sourceEntities[i], targetEntities[i]);
        Check(mappings.All(pair => pair.Key.Handle != pair.Value.Handle), "Mixed clone fixture does not distinguish source/destination handles");
        int before = target.Objects.Items.Count; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.CloneExtensionDictionary(sourceHost.Record, host.Record));
        Equal(before, target.Objects.Items.Count, "Failed mixed clone registered objects"); Equal(seed, target.DrawingVariables.HandleSeed, "Failed mixed clone allocated handles"); Check(host.Record.ExtensionDictionary == null, "Failed mixed clone published its root");
        target.Objects.CloneExtensionDictionary(sourceHost.Record, host.Record, mappings);
        target.Objects.CloneExtensionDictionary(sourceInsert, insert, mappings);
        foreach (var reference in MixedLinks(host).References.Where(r => r != null)) Check(ReferenceEquals(target.GetObjectByHandle(reference.Handle), reference), "Mixed clone contains a source-document object");
        foreach (var pair in mappings) Check(!ReferenceEquals(pair.Key, pair.Value), "Mixed clone retained a source identity");
        string sortKey = MixedPolyline(sourceHost, false).Handle;
        MixedExport(target, binary, "clone", "MIX_HOST_COPY", true, sortKey);
        // Editing the source after the copy must not alias either common bytes or owned metadata.
        MixedPolyline(sourceHost, false).ProxyGraphics = new byte[] { 99 }; source.Objects.GetGeoData(sourceHost.Record).CoordinateSystemDefinition = "changed source";
        Check(MixedPolyline(host, false).ProxyGraphics.SequenceEqual(MixedProxyBytes), "Cross-document proxy payload aliases source");
        Equal("LOCAL_MIXED\nuninterpreted", target.Objects.GetGeoData(host.Record).CoordinateSystemDefinition, "Cross-document GEODATA aliases source");
    }

    private static void MixedModuleLifecycle(DxfVersion version, bool binary)
    {
        var doc = BuildMixedModuleDocument(version); var host = doc.Blocks["MIX_HOST"]; var frame = doc.UCSs["MIX_FRAME"];
        var view = doc.Views["MIX_VIEW"]; var port = doc.VPorts["MIX_PORT"]; var links = MixedLinks(host);
        // IDBUFFER references are explicit graph data; clear the table slots before removing their targets.
        for (int i = links.References.Count - 1; i >= 0; i--) if (links.References[i] is UCS || links.References[i] is View || links.References[i] is VPort) links.References.RemoveAt(i);
        Equal(4, frame.GetReferences().Sum(r => r.Uses), "Mixed initial reference count"); Check(!doc.UCSs.Remove(frame), "Mixed referenced UCS was removed");
        Check(doc.Views.Remove(view), "Mixed VIEW removal failed"); Equal(2, frame.GetReferences().Sum(r => r.Uses), "Mixed VIEW removal erased VPORT uses");
        Check(!doc.UCSs.Remove(frame), "Mixed VPORT references did not retain UCS"); Check(doc.VPorts.Remove(port), "Mixed VPORT removal failed");
        Check(!frame.HasReferences() && doc.UCSs.Remove(frame), "Mixed last reference did not release UCS");
        MixedExport(doc, binary, "lifecycle", "MIX_HOST", false);
    }
}
