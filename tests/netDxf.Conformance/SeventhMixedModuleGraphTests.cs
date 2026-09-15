using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private sealed record SeventhGraph(DxfDictionary Folder, DxfStoredField Field, UCS Base, UCS Child,
        DxfStoredDimAssoc Association, EntityObject Geometry, DxfStoredTableContent Content,
        Section? Section, DxfSectionSettings? Settings, DxfSun? Sun, StoredTable? Table);

    private static SeventhGraph SeventhParts(DxfDocument doc)
    {
        var folder = (DxfDictionary)doc.NamedObjects["SEVENTH_MIXED"];
        var field = (DxfStoredField)folder["FIELD"]; var targets = field.ReferencedObjects;
        bool extended = targets.Count == 13;
        return new SeventhGraph(folder, field, (UCS)targets[0], (UCS)targets[1], (DxfStoredDimAssoc)targets[2],
            (EntityObject)targets[4], (DxfStoredTableContent)targets[5], extended ? (Section)targets[9] : null,
            extended ? (DxfSectionSettings)targets[10] : null, extended ? (DxfSun)targets[11] : null,
            extended ? (StoredTable)targets[12] : null);
    }

    private static DxfDocument SeventhAttachGraph(DxfDocument doc, bool binary)
    {
        doc = SeventhAppendDimAssoc(doc, binary);
        var association = (DxfStoredDimAssoc)doc.GetObjectByHandle(StoredDimAssocHandle(doc.DrawingVariables.AcadVer, "452"));
        var geometry = association.PointReferences[0].Geometry;
        var content = (DxfStoredTableContent)doc.GetObjectByHandle(doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2004 ? "747" : "116");
        var parent = doc.UCSs.Add(new UCS("SEVENTH_BASE") { Origin = new Vector3(1.25, -2.5, 3.75), Elevation = 4.125 });
        var child = doc.UCSs.Add(new UCS("SEVENTH_CHILD")); child.SetOrthographicBase(5, parent);
        child.SetOrthographicOrigin(UcsOrthographicType.Right, new Vector3(7, 8, 9));
        var folder = new DxfDictionary(); doc.NamedObjects.Add("SEVENTH_MIXED", folder);
        var prefix = new DxfXRecord(); prefix.Data.Add(new DxfTag(1, "mutable sibling survives refused clone/erase")); folder.Add("PREFIX", prefix);
        var placeholder = new DxfXRecord(); folder.Add("FIELD", placeholder);
        var targets = new List<DxfObject?> { parent, child, association, association.Dimension, geometry, content, content.TableStyle, geometry, null };

        if (doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2018)
        {
            var section = new Section { Name = "Seventh shared geometry", State = 4, Flags = 17, VerticalDirection = Vector3.UnitZ,
                TopHeight = 5.25, BottomHeight = -2.75, IndicatorTransparency = 31, StoredNativeIndicatorColor = 6 };
            section.Vertices.Add(new Vector3(1, 2, 3)); section.Vertices.Add(new Vector3(4, 5, 6)); doc.Entities.Add(section);
            var settings = new DxfSectionSettings { SectionType = 4 };
            settings.SetTypeSettings(new[] { new DxfSectionTypeSettings(4, 17,
                new DxfObject[] { section, settings, association.Dimension, geometry, content, parent }, section.Owner.Record,
                "seventh-inert.dwg", new[] { new DxfSectionGeometrySettings { SectionType = 4, GeometryValue = 8, Flags = 33,
                    ColorCode = 62, ColorIndex = 6, LayerName = "0", FaceTransparency = 21, EdgeTransparency = 35 } }) });
            doc.Objects.SetSectionSettings(section, settings);
            var host = doc.Views.Add(new View("SEVENTH_SUN_OWNER")); var sun = new DxfSun { Intensity = 1.625, JulianDay = 2455826, StoredTime = 43200000 };
            doc.Objects.SetSun(host, sun);
            var extension = new DxfDictionary(); var links = new DxfXRecord();
            links.Data.Add(new DxfTag(330, host.Handle)); links.Data.Add(new DxfTag(331, sun.Handle));
            links.Data.Add(new DxfTag(340, parent.Handle)); links.Data.Add(new DxfTag(340, content.Handle));
            links.Data.Add(new DxfTag(340, association.Handle)); links.Data.Add(new DxfTag(310, new byte[] { 7, 0, 255 }));
            extension.Add("LINKS", links); extension.Add("ALIAS", links); doc.Objects.SetExtensionDictionary(sun, extension);
            targets.AddRange(new DxfObject[] { section, settings, sun, doc.Entities.StoredTables.Single() });
        }

        using var bytes = new MemoryStream(SeventhBytes(doc, binary)); var raw = DxfRawDocument.Load(bytes);
        var record = SeventhRecord(raw, placeholder.Handle);
        var payload = new List<DxfTag> { new(100, "AcDbField"), new(1, "SeventhStored"), new(2, "no evaluation: \\U+03A9"),
            new(90, 0), new(97, targets.Count) };
        payload.AddRange(targets.Select(target => new DxfTag(331, target?.Handle ?? "0")));
        payload.AddRange(new DxfTag[] { new(91, 63), new(92, 0), new(93, 0), new(7, "ACFD_FIELD_VALUE"), new(90, 0),
            new(91, 0), new(301, "####"), new(310, new byte[] { 0, 127, 255 }), new(320, "F0F0F0") });
        raw = raw.WithRecord(record, record.Tags.TakeWhile(tag => tag.Code != 100)
            .Select(tag => tag.Code == 0 ? new DxfTag(0, "FIELD") : tag).Concat(payload));
        return SeventhLoad(raw, binary);
    }

    private static void SeventhAssert(DxfDocument doc, bool released = false)
    {
        var p = SeventhParts(doc); Equal(0, doc.Objects.Validate().Count, "Seventh mixed database validation");
        Equal(released ? (short)0 : (short)5, p.Child.OrthographicViewType, "Mutable UCS type");
        Check(ReferenceEquals(p.Child.BaseUcs, released ? null : p.Base), "UCS actual base target");
        Equal(new Vector3(7, 8, 9), p.Child.OrthographicOrigins[UcsOrthographicType.Right], "Independent UCS origin override");
        Check(p.Base.GetReferences().Any(reference => ReferenceEquals(reference.Reference, p.Field)), "FIELD keeps the UCS target registered");
        Check(p.Base.GetReferences().Any(reference => ReferenceEquals(reference.Reference, p.Child)) == !released, "Mutable UCS reference bookkeeping differs");
        Check(ReferenceEquals(p.Field.ReferencedObjects[3], p.Association.Dimension) && ReferenceEquals(p.Field.ReferencedObjects[7], p.Geometry) && p.Field.ReferencedObjects[8] == null,
            "FIELD native association, repeated geometry and null identities");
        Check(p.Association.PointReferences.All(point => ReferenceEquals(point.Geometry, p.Geometry)), "Native four-point geometry association");
        Check(ReferenceEquals(p.Association.Dimension.ExtensionDictionary, p.Association.Owner) &&
            ReferenceEquals(((DxfDictionary)p.Association.Owner)["ACAD_DIMASSOC"], p.Association), "DIMASSOC reciprocal extension edge");
        Check(ReferenceEquals(p.Field.ReferencedObjects[6], p.Content.TableStyle), "FIELD and TABLECONTENT share the actual style object");
        Equal(doc.DrawingVariables.AcadVer, p.Content.SourceVersion, "TABLECONTENT source profile");
        Equal(doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2004 ? 3 : 5, p.Content.ColumnCount!.Value, "Native TABLECONTENT columns");
        Equal(doc.DrawingVariables.AcadVer == DxfVersion.AutoCad2004 ? 7 : 10, p.Content.RowCount!.Value, "Native TABLECONTENT rows");
        if (p.Section != null)
        {
            Equal("Seventh shared geometry", p.Section.Name, "SECTION authored name");
            Equal(4, p.Section.State, "SECTION state"); Equal(17, p.Section.Flags, "SECTION flags");
            Equal(Vector3.UnitZ, p.Section.VerticalDirection, "SECTION stored direction");
            Equal(5.25, p.Section.TopHeight, "SECTION top height"); Equal(-2.75, p.Section.BottomHeight, "SECTION bottom height");
            Equal((short)31, p.Section.IndicatorTransparency, "SECTION indicator transparency");
            Check(p.Section.Vertices.SequenceEqual(new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) }), "SECTION stored vertices");
            Check(ReferenceEquals(p.Section.GeometrySettings, p.Settings) && ReferenceEquals(p.Settings!.Owner, p.Section), "SECTION settings ownership");
            var refs = p.Settings!.TypeSettings.Single().SourceObjects;
            Check(ReferenceEquals(refs[2], p.Association.Dimension) && ReferenceEquals(refs[3], p.Geometry) && ReferenceEquals(refs[4], p.Content), "SECTION shares native targets");
            var appearance = p.Settings.TypeSettings.Single().GeometrySettings.Single();
            Check(appearance.GeometryValue == 8 && appearance.Flags == 33 && appearance.ColorCode == 62 && appearance.ColorIndex == 6 &&
                appearance.LayerName == "0" && appearance.FaceTransparency == 21 && appearance.EdgeTransparency == 35, "SECTION stored geometry appearance");
            Check(ReferenceEquals(((View)p.Sun!.Owner).Sun, p.Sun), "SUN reciprocal host slot");
            Check(ReferenceEquals(p.Table!.StoredBackingContent, p.Content) && ReferenceEquals(p.Table.BackingContent, p.Content), "Stored TABLE keeps typed backing identity");
        }
    }
}
