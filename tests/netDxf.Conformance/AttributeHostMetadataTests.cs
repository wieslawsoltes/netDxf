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
    private static void RegisterAttributeHostMetadataTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"field-host/attribute-metadata/{version}/{binary}", () => AttributeHostMetadata(version, binary));
        foreach (bool binary in new[] { false, true })
        foreach (string fault in new[] { "detached", "removed", "foreign", "invalid-reactor", "duplicate-source", "discarded-source" })
            Run($"field-host/attribute-identity/{binary}/{fault}", () => AttributeHostIdentity(binary, fault));
    }

    private static Insert AttributeHostInsert(string name)
    {
        var block = new Block(name);
        block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Value = "value" });
        return new Insert(block);
    }

    private static void AttributeHostMetadata(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var insert = AttributeHostInsert("ATTRIBUTE_METADATA");
        doc.Entities.Add(insert); var attribute = insert.Attributes.Single();
        Check(doc.GetObjectByHandle(attribute.Handle) == null, "public owner-held handle lookup contract changed");
        var extension = new DxfDictionary(); var note = new DxfXRecord(); note.Data.Add(new DxfTag(1, "metadata Ω"));
        extension.Add("NOTE", note); doc.Objects.SetExtensionDictionary(attribute, extension);
        extension.PersistentReactors.Add(attribute);
        attribute.PersistentReactors.Add(doc.Layers["0"]);
        attribute.XData.Add(new XData(new ApplicationRegistry("ATTRIBUTE_HOST")) { XDataRecord = { new XDataRecord(XDataCode.String, "owned") } });
        Equal(0, doc.Objects.Validate().Count, "retained attribute was not an actual database owner");
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "attribute metadata output"); bytes.Position = 0;
        var loaded = DxfDocument.Load(bytes)!; var again = loaded.Entities.Inserts.Single().Attributes.Single();
        Check(loaded.GetObjectByHandle(again.Handle) == null, "metadata support changed public attribute lookup");
        Check(ReferenceEquals(again, again.ExtensionDictionary.Owner), "attribute extension reciprocal ownership");
        Check(ReferenceEquals(again, again.ExtensionDictionary.PersistentReactors.Single()), "reactor must resolve the exact retained attribute");
        Check(ReferenceEquals(loaded.Layers["0"], again.PersistentReactors.Single()), "attribute common reactor was lost");
        Equal("metadata Ω", ((DxfXRecord)again.ExtensionDictionary["NOTE"]).Data[0].Value, "attribute extension value");
        Equal("owned", again.XData["ATTRIBUTE_HOST"].XDataRecord[0].Value, "attribute XData value");
        Equal(0, loaded.Objects.Validate().Count, "loaded attribute metadata graph");
        using var opposite = new MemoryStream(); Check(loaded.Save(opposite, !binary), "opposite attribute metadata output"); opposite.Position = 0;
        Check(DxfDocument.Load(opposite)!.Entities.Inserts.Single().Attributes.Single().ExtensionDictionary != null, "opposite metadata reload");
    }

    private static void AttributeHostIdentity(bool binary, string fault)
    {
        if (fault is "duplicate-source" or "discarded-source")
        {
            var doc = FieldHostDocument(DxfVersion.AutoCad2018, binary, "ATTRIB");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "identity fixture"); output.Position = 0;
            var raw = DxfRawDocument.Load(output); var record = StoredFieldRecord(raw, "15B"); var tags = record.Tags.ToList();
            if (fault == "duplicate-source") tags[tags.FindIndex(t => t.Code == 5)] = new DxfTag(5, "14B");
            else tags[tags.FindIndex(t => t.Code == 2)] = new DxfTag(2, "");
            bool rejected = false;
            try { rejected = StoredFieldTryLoad(raw.WithRecord(record, tags), binary) == null; }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException) { rejected = true; }
            Check(rejected, "discarded/ambiguous attribute supplied a database owner identity"); return;
        }
        var destination = new DxfDocument(DxfVersion.AutoCad2018); var insert = AttributeHostInsert("IDENTITY");
        if (fault == "foreign") new DxfDocument(DxfVersion.AutoCad2018).Entities.Add(insert);
        else if (fault != "detached") destination.Entities.Add(insert);
        if (fault == "removed") Check(destination.Entities.Remove(insert), "remove attribute owner fixture");
        var attribute = insert.Attributes.Single(); int count = destination.Objects.Items.Count; long seed = StoredFieldSeed(destination);
        if (fault == "invalid-reactor")
        {
            attribute.PersistentReactors.Add(new Line(Vector3.Zero, Vector3.UnitX));
            Check(destination.Objects.Validate().Count > 0, "owner-held attribute common metadata was omitted from validation");
            using var bytes = new MemoryStream(); CheckSaveRejected(destination, bytes); Equal(0L, bytes.Length, "invalid attribute metadata wrote output");
        }
        else Throws<ArgumentException>(() => destination.Objects.SetExtensionDictionary(attribute, new DxfDictionary()));
        Equal(seed, StoredFieldSeed(destination), "attribute validation allocated handles");
        Equal(count, destination.Objects.Items.Count, "attribute validation registered metadata");
    }
}
