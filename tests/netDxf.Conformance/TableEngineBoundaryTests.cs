using netDxf;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterTableEngineBoundaryTests()
    {
        Run("table-engine/count-numeric-only", () =>
        {
            object?[] values = { null, "12", 0, -1.5, "=3", 4 };
            Equal(3d, DxfTableFormula.Parse("=COUNT(A1:A6)").Evaluate(6, 1, a => values[a.Row]!), "COUNT must ignore nonnumeric cells");
            Equal(0d, DxfTableFormula.Parse("=COUNT(A1:A2)").Evaluate(2, 1, _ => "text"), "empty numeric count");
        });
        Run("table-engine/count-dependencies", () =>
        {
            int calls = 0;
            var values = DxfTableCalculation.Evaluate(1, 4, _ => { calls++; return 2; },
                new Dictionary<DxfTableCellAddress, string> { [Address("A1")] = "=COUNT(B1:D1)", [Address("B1")] = "=C1*2" });
            Equal(3d, values[Address("A1")], "numeric dependency count");
            Equal(4d, values[Address("B1")], "COUNT must calculate referenced formulas");
            Equal(2, calls, "COUNT dependency memoization");
        });
        Run("table-engine/count-cycle", () => Throws<InvalidOperationException>(() => DxfTableCalculation.Evaluate(1, 2, _ => 0,
            new Dictionary<DxfTableCellAddress, string> { [Address("A1")] = "=COUNT(B1)", [Address("B1")] = "=COUNT(A1)" })));
        Run("table-engine/formula-depth-budget", () =>
        {
            var formulas = Enumerable.Range(0, 129).Select(n => new KeyValuePair<DxfTableCellAddress, string>(
                new DxfTableCellAddress(n, 0), n == 128 ? "=1" : "=A" + (n + 2))).ToArray();
            Throws<InvalidOperationException>(() => DxfTableCalculation.Evaluate(129, 1, _ => 0, formulas));
        });
        Run("table-engine/formula-text-budget", () =>
        {
            var formulas = Enumerable.Range(0, 300).Select(n => new KeyValuePair<DxfTableCellAddress, string>(
                new DxfTableCellAddress(n, 0), "=1" + new string(' ', 4000))).ToArray();
            Throws<ArgumentException>(() => DxfTableCalculation.Evaluate(300, 1, _ => throw new Exception("resolver called before bounded materialization"), formulas));
        });
        foreach (string literal in new[] { "%%d", "%%c", "%<1>%", "end>%" })
            Run($"table-engine/literal-rejection/{literal}", () =>
            {
                var doc = ConsumerLoad("acad_table_simple.dxf", false);
                var content = doc.Objects.Items.OfType<DxfStoredTableContent>().First();
                var before = content.Payload;
                var style = DxfCellStyleResolver.Resolve(LayoutDefinition(doc), Array.Empty<DxfCellStyleFormatDefinition>());
                Throws<NotSupportedException>(() => DxfTableLayout.Create(content.GetGrid(), _ => style, _ => literal, (_, _, _) => 1, false));
                Check(ReferenceEquals(before, content.Payload), "native text rejection modified source");
            });
        foreach (bool selected in new[] { false, true })
            Run($"table-engine/missing-margin/{selected}", () =>
            {
                var doc = new DxfDocument(); var original = LayoutDefinition(doc);
                var overrideFormat = LayoutDefinition(doc, selected ? (int)DxfCellProperty.MarginLeft : (int)DxfCellProperty.TextHeight, 1);
                var withoutMargins = new DxfCellStyleFormatDefinition(5, 1, overrideFormat.Values, overrideFormat.Content,
                    overrideFormat.TextStyle, 0, null, Array.Empty<DxfCellGridFormatDefinition>());
                if (selected) Throws<NotSupportedException>(() => DxfCellStyleResolver.Resolve(original, new[] { withoutMargins }));
                else Equal(3d, DxfCellStyleResolver.Resolve(original, new[] { withoutMargins }).Format.Content.TextHeight, "unselected absent margins need not override base");
            });
        Run("table-engine/overlapping-edge-masks", () =>
        {
            var doc = new DxfDocument(); var original = LayoutDefinition(doc);
            var overlapping = new DxfCellStyleFormatDefinition(5, 1, original.Values, original.Content, original.TextStyle, 1,
                original.Margins, new[] { original.Borders[0], original.Borders[0] });
            Throws<NotSupportedException>(() => DxfCellStyleResolver.Resolve(original, new[] { overlapping }));
        });
        foreach (bool binary in new[] { false, true })
        foreach (string fault in new[] { "zero-width", "negative-height", "overlapping-merges", "content-count" })
            Run($"table-engine/grid-rejection/{binary}/{fault}", () =>
            {
                var doc = ConsumerLoad("acad_table_simple.dxf", binary, raw =>
                {
                    var content = raw.Sections.SelectMany(section => section.Records).First(record => record.Name == "TABLECONTENT");
                    var tags = content.Tags.ToList();
                    if (fault == "zero-width" || fault == "negative-height")
                    {
                        string frame = fault == "zero-width" ? "TABLECOLUMN_BEGIN" : "TABLEROW_BEGIN";
                        int index = tags.FindIndex(tag => tag.Code == 1 && Equals(tag.Value, frame));
                        tags[index + 2] = new DxfTag(40, fault == "zero-width" ? 0d : -1d);
                    }
                    else if (fault == "content-count")
                    {
                        int index = tags.FindIndex(tag => tag.Code == 1 && Equals(tag.Value, "LINKEDTABLEDATACELL_BEGIN"));
                        int count = tags.FindIndex(index + 1, tag => tag.Code == 95);
                        tags[count] = new DxfTag(95, (int)tags[count].Value + 1);
                    }
                    else
                    {
                        int formatted = tags.FindIndex(tag => tag.Code == 100 && Equals(tag.Value, "AcDbFormattedTableData"));
                        int end = tags.FindIndex(formatted + 1, tag => tag.Code == 309 && Equals(tag.Value, "TABLEFORMAT_END"));
                        int count = (int)tags[end + 1].Value;
                        Check(count > 0, "fixture must contain a merge");
                        var rectangle = tags.Skip(end + 2).Take(4).ToArray();
                        tags[end + 1] = new DxfTag(90, count + 1);
                        tags.InsertRange(end + 2 + 4 * count, rectangle);
                    }
                    return raw.WithRecord(content, tags);
                });
                var stored = doc.Objects.Items.OfType<DxfStoredTableContent>().First();
                var before = stored.Payload;
                Throws<NotSupportedException>(() => stored.GetGrid());
                Check(ReferenceEquals(before, stored.Payload), "grid rejection modified source");
            });
    }
}
