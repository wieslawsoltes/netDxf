using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

internal static class ImportedDrawOrderVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("sortents/source construction preserves attribute and entity targets through history", SourceTargets));
    }

    private static void SourceTargets()
    {
        var document = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2013); var block = new Block("OrderedDefinition");
        var definition = new AttributeDefinition("LABEL"); block.AttributeDefinitions.Add(definition);
        var line = new Line(Vector2.Zero, new Vector2(10, 0)); block.Entities.Add(line);
        document.Entities.Add(new Insert(block, Vector3.Zero));
        var table = document.Objects.CreateSortentsTable(block.Record, Array.Empty<DxfSortOrderEntry>());
        var source = new ShadowSession(document); var editor = new EditorSession(source);
        var retained = source.Current; var edit = new ShadowGraphEdit(retained);
        ObjectId Entry(DxfObject target, string key) => edit.Create(typeof(DxfSortOrderEntry).FullName!, ShadowObjectKind.Object,
            new Dictionary<string, ShadowValue>
            {
                ["Target"] = new ShadowValue.Reference(Id(target)),
                ["Entity"] = target is EntityObject ? new ShadowValue.Reference(Id(target)) : ShadowValue.Null,
                ["SortHandle"] = ShadowValue.From(key)
            });
        var attributeEntry = Entry(definition, "0"); var entityEntry = Entry(line, "A");
        edit.ReplaceSequence(Id(table), "Entries", [new ShadowValue.Reference(attributeEntry), new ShadowValue.Reference(entityEntry)]);
        editor.Execute("Set draw order", edit.Build());
        void Check()
        {
            Require(table.Entries.Count == 2, "The draw-order collection was not reconstructed.");
            Require(ReferenceEquals(table.Entries[0].Target, definition) && table.Entries[0].Entity is null
                && table.Entries[0].SortHandle == "0", "The attribute-definition target was not preserved.");
            Require(ReferenceEquals(table.Entries[1].Target, line) && ReferenceEquals(table.Entries[1].Entity, line)
                && table.Entries[1].SortHandle == "A", "The graphical-entity target was not preserved.");
        }
        Check();
        for (var i = 0; i < 3; i++)
        {
            editor.Undo(); Require(table.Entries.Count == 0, "Undo retained draw-order entries.");
            editor.Redo(); Check();
        }
        Require(!retained.Read(Id(table))["Entries"].Items.Any(), "History mutated the retained snapshot.");
    }

    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
