using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string BoundDisplayName = "BOUND_TABLE_Ω";
    private static void RegisterTableDisplayBindingTests()
    {
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
        {
            Run($"table-display/bind/{file}/{binary}", () => TableDisplayBinding(file, binary));
            foreach (string fault in new[] { "null", "detached", "foreign", "removed", "layout", "attributes", "nested", "profile", "invalid-graph", "name-pointer-mismatch", "private-packet" })
                Run($"table-display/rejection/{file}/{binary}/{fault}", () => TableDisplayRejection(file, binary, fault));
        }
    }

    private static StoredTable FirstDisplayTable(DxfDocument doc) =>
        doc.Blocks.SelectMany(b => b.Entities).OfType<StoredTable>().First(t => t.StoredBackingContent != null);

    private static Block CreateBoundDisplay(DxfDocument doc, StoredTable table)
    {
        var content = new DxfCellContentFormatValues(0, 0, 2, 0, "", 0, 1, 1, 7, .01);
        var definition = new DxfCellStyleFormatDefinition(5, 1,
            new DxfCellStyleFormatValues(0, 0, 257, 1), content, doc.TextStyles["Standard"], 1,
            new DxfCellMargins(0, 0, 0, 0, 0, 0), new[] {
                new DxfCellGridFormatDefinition(63, new DxfCellGridFormatValues(0, 1, 7, -1, 0, 0), doc.Linetypes["Continuous"]) });
        var resolved = DxfCellStyleResolver.Resolve(definition, Array.Empty<DxfCellStyleFormatDefinition>());
        var layout = DxfTableLayout.Create(table.StoredBackingContent.GetGrid(), _ => resolved,
            cell => cell.Address.ToString(), (_, _, _) => .01, false);
        return layout.BuildDisplayBlock(BoundDisplayName);
    }

    private static void TableDisplayBinding(string file, bool binary)
    {
        var doc = ConsumerLoad(file, binary); var table = FirstDisplayTable(doc);
        var originalBlock = table.DisplayBlock ?? throw new InvalidOperationException("Qualified source display block not exposed.");
        var oldPayload = table.Payload; var priorWire = OwnershipTagValues(oldPayload).ToArray();
        var oldReferences = table.References; var refs = oldReferences.ToArray();
        var contents = doc.Objects.Items.OfType<DxfStoredTableContent>().Select(c => c.Payload).ToArray();
        byte[]? proxy = table.ProxyGraphics;
        table.ReplaceDisplayBlock(originalBlock);
        Check(ReferenceEquals(oldPayload, table.Payload), "same display identity replaced the payload");
        Check(proxy == null ? table.ProxyGraphics == null : proxy.SequenceEqual(table.ProxyGraphics), "same display identity cleared its proxy");
        var target = doc.Blocks.Add(CreateBoundDisplay(doc, table));
        TableContentSave(doc, binary, $"table-display-before-{file}-{binary}.dxf");
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        table.ReplaceDisplayBlock(target);
        Equal(seed, OwnershipSeed(doc), "display rebind allocated handles");
        Equal(count, doc.Objects.Items.Count, "display rebind changed database membership");
        Check(ReferenceEquals(target, table.DisplayBlock) && table.ProxyGraphics == null, "display identity or stale proxy not updated");
        Check(!ReferenceEquals(oldPayload, table.Payload) && priorWire.SequenceEqual(OwnershipTagValues(oldPayload)), "old TABLE payload changed");
        Check(oldReferences.SequenceEqual(refs), "old reference membership changed");
        Equal(2, table.References.Count(r => ReferenceEquals(r, target.Record)), "name and handle reference multiplicity");
        Equal(0, table.References.Count(r => ReferenceEquals(r, originalBlock.Record)), "former display uses were not released");
        Check(!doc.Blocks.Remove(target), "selected display block was removable");
        Check(contents.SequenceEqual(doc.Objects.Items.OfType<DxfStoredTableContent>().Select(c => c.Payload)), "display operation changed backing contents");
        var payload = table.Payload;
        table.ReplaceDisplayBlock(target);
        Check(ReferenceEquals(payload, table.Payload), "repeat display selection is not a no-op");
        TableContentSave(doc, binary, $"table-display-after-{file}-{binary}.dxf");
        var reload = TableContentLoad(TableContentSave(doc, !binary));
        var again = reload.Blocks.SelectMany(b => b.Entities).OfType<StoredTable>().Single(t => t.Handle == table.Handle);
        Equal(BoundDisplayName, again.DisplayBlock.Name, "cross-transport display binding");
        Equal(2, again.References.Count(r => ReferenceEquals(r, again.DisplayBlock.Record)), "cross-transport reference count");
        Check(again.ProxyGraphics == null && !reload.Blocks.Remove(again.DisplayBlock), "reloaded display protections");
        target.Name = "RENAMED_DISPLAY_Ż";
        table.ReplaceDisplayBlock(target);
        Check(ReferenceEquals(payload, table.Payload), "same renamed identity rewrote source lexical tags");
        reload = TableContentLoad(TableContentSave(doc, binary));
        again = reload.Blocks.SelectMany(b => b.Entities).OfType<StoredTable>().Single(t => t.Handle == table.Handle);
        Equal(target.Name, again.DisplayBlock.Name, "bound display rename");
        // A second replacement releases the two uses of the first generated block.
        var second = doc.Blocks.Add(new Block("SECOND_DISPLAY", new[] { new Line(Vector3.Zero, Vector3.UnitX) }));
        table.ReplaceDisplayBlock(second);
        Check(doc.Blocks.Remove(target), "second replacement did not release the old generated display");
        Equal(0, doc.Objects.Validate().Count, "display rebind left an invalid source graph");
    }

    private static void TableDisplayRejection(string file, bool binary, string fault)
    {
        var doc = ConsumerLoad(file, binary, raw =>
        {
            if (fault != "name-pointer-mismatch" && fault != "private-packet") return raw;
            var record = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "ACAD_TABLE");
            var tags = record.Tags.ToList();
            if (fault == "name-pointer-mismatch") tags[tags.FindIndex(t => t.Code == 343)] = new DxfTag(343, "0");
            else tags.AddRange(new[] { new DxfTag(102, "{PRIVATE_DISPLAY"), new DxfTag(1, "hidden"), new DxfTag(102, "}") });
            return raw.WithRecord(record, tags);
        });
        var table = FirstDisplayTable(doc);
        var target = new Block("REJECTED_DISPLAY", new[] { new Line(Vector3.Zero, Vector3.UnitX) });
        if (fault == "attributes") target.AttributeDefinitions.Add(new AttributeDefinition("TAG"));
        if (fault == "nested") target.Entities.Add(new Insert(new Block("NESTED")));
        if (fault == "foreign") target = new DxfDocument().Blocks.Add(target);
        else if (fault == "layout") target = doc.Blocks["*Model_Space"];
        else if (fault != "detached" && fault != "null") doc.Blocks.Add(target);
        if (fault == "removed") Check(doc.Blocks.Remove(target), "remove unused target setup");
        if (fault == "invalid-graph") table.PersistentReactors.Add(new Line(Vector3.Zero, Vector3.UnitX));
        var profile = doc.DrawingVariables.AcadVer;
        if (fault == "profile") doc.DrawingVariables.AcadVer = netDxf.Header.DxfVersion.AutoCad2000;
        var before = table.Payload; var refs = table.References.ToArray(); var block = table.DisplayBlock;
        var proxy = table.ProxyGraphics; long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        bool rejected = false;
        try { table.ReplaceDisplayBlock(fault == "null" ? null! : target); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { rejected = true; }
        Check(rejected, "invalid display replacement accepted");
        Check(ReferenceEquals(before, table.Payload) && ReferenceEquals(block, table.DisplayBlock) && refs.SequenceEqual(table.References), "rejected display mutated the source");
        Check(proxy == null ? table.ProxyGraphics == null : proxy.SequenceEqual(table.ProxyGraphics), "rejected display cleared proxy data");
        Equal(seed, OwnershipSeed(doc), "rejected display allocated handles"); Equal(count, doc.Objects.Items.Count, "rejected display changed database objects");
        doc.DrawingVariables.AcadVer = profile;
        if (fault == "invalid-graph") table.PersistentReactors.Clear();
    }
}
