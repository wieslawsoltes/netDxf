using System.Globalization;
using netDxf;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static DxfTableCellAddress Address(string text) => DxfTableCellAddress.Parse(text);
    private static void RegisterTableCalculationTests()
    {
        foreach (string culture in new[] { "en-US", "pl-PL", "tr-TR" })
        {
            foreach (var pair in new[] {
                ("=1+2*3", 7d), ("=(1+2)*3", 9d), ("=2^3^2", 512d), ("=-2^2", -4d),
                ("=(-2)^2", 4d), ("=2^-3", 0.125d), ("=1e2+.5", 100.5d), ("=SUM(1,2,3)", 6d),
                ("=average(2,4)", 3d), ("=MIN(5,-2,6)", -2d), ("=MAX(5,-2,6)", 6d),
                ("=SUM(1e16,1,-1e16)", 1d), ("=SUM(A1:B2)", 5d), ("=AVERAGE(B2:A1)", 2.5d),
                ("=COUNT(A1:B2)", 4d), ("=COUNT(A1,B1)", 2d), ("=$A$1+$B$2", 5d),
                ("=SUM(A1:B2,MAX(7,8))", 13d) })
                Run($"table-calculation/formula/{culture}/{pair.Item1}", () =>
                {
                    var prior = CultureInfo.CurrentCulture;
                    try
                    {
                        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                        object Cell(DxfTableCellAddress a) => a == Address("A1") ? 2 : a == Address("B2") ? 3d : a == Address("B1") ? "text" : null!;
                        Equal(pair.Item2, DxfTableFormula.Parse(pair.Item1).Evaluate(2, 2, Cell), "formula result");
                    }
                    finally { CultureInfo.CurrentCulture = prior; }
                });
        }
        foreach (string text in new[] { "A1", "Z1", "AA1", "ZZ999", "AAA1000", "FXSHRXX2147483648", "$a$1" })
            Run($"table-calculation/address/{text}", () => Equal(Address(text), Address(Address(text).ToString()), "address roundtrip"));
        foreach (string text in new[] { "", "A0", "A01", "0A", "A-1", "A1x", "$", "A$", "FXSHRXY1", "A2147483649", "Ａ1" })
            Run($"table-calculation/address-rejection/{text}", () => Throws<FormatException>(() => Address(text)));
        foreach (string expression in new[] { "", "1+2", "=", "=1+", "=()", "=SUM()", "=A1:B2", "=1,2", "=2(3)", "=SUM(1:2)", "=A0", "=1e", "=1e9999", "=(1", "=1)", "=1\n+2" })
            Run($"table-calculation/parse-rejection/{expression}", () => Throws<FormatException>(() => DxfTableFormula.Parse(expression)));
        Run("table-calculation/unknown-function", () => Throws<NotSupportedException>(() => DxfTableFormula.Parse("=EXEC(1)")));
        foreach (string expression in new[] { "=1/0", "=0^-1", "=(-1)^.5", "=1e308*2" })
            Run($"table-calculation/arithmetic-rejection/{expression}", () => Throws<ArithmeticException>(() => DxfTableFormula.Parse(expression).Evaluate(1, 1, _ => 0)));
        foreach (object? value in new object?[] { null, "3", true, new object(), double.NaN })
            Run($"table-calculation/no-coercion/{value?.GetType().Name ?? "null"}/{value}", () =>
            {
                bool rejected = false;
                try { DxfTableFormula.Parse("=A1+1").Evaluate(1, 1, _ => value!); }
                catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException || e is ArithmeticException) { rejected = true; }
                Check(rejected, "unsupported scalar was coerced");
            });
        foreach (string function in new[] { "AVERAGE", "MIN", "MAX" })
            Run($"table-calculation/empty/{function}", () => Throws<InvalidOperationException>(() => DxfTableFormula.Parse($"={function}(A1:A2)").Evaluate(2, 1, _ => null!)));
        Run("table-calculation/empty/sum", () => Equal(0d, DxfTableFormula.Parse("=SUM(A1:A2)").Evaluate(2, 1, _ => null!), "empty sum"));
        Run("table-calculation/bounds", () => Throws<ArgumentOutOfRangeException>(() => DxfTableFormula.Parse("=B1").Evaluate(1, 1, _ => 0)));
        Run("table-calculation/oversize-grid", () => Throws<ArgumentOutOfRangeException>(() => DxfTableFormula.Parse("=1").Evaluate(1001, 1000, _ => 0)));
        Run("table-calculation/deep-unary", () => Throws<FormatException>(() => DxfTableFormula.Parse("=" + new string('-', 300) + "1")));
        Run("table-calculation/deep-parentheses", () => Throws<FormatException>(() => DxfTableFormula.Parse("=" + new string('(', 300) + "1" + new string(')', 300))));
        Run("table-calculation/deep-left-tree", () => Throws<FormatException>(() => DxfTableFormula.Parse("=1" + string.Concat(Enumerable.Repeat("+1", 300)))));
        Run("table-calculation/operation-budget", () => Throws<InvalidOperationException>(() => DxfTableFormula.Parse("=SUM(A1:A1000000)").Evaluate(1000000, 1, _ => 0)));
        Run("table-calculation/memoized", () =>
        {
            int calls = 0;
            Equal(9d, DxfTableFormula.Parse("=A1+A1+A1").Evaluate(1, 1, _ => { calls++; return 3; }), "memoized result");
            Equal(1, calls, "repeated resolver invocation");
        });
        Run("table-calculation/dependencies", () =>
        {
            var formulas = new Dictionary<DxfTableCellAddress, string> { [Address("C1")] = "=B1+A1", [Address("B1")] = "=A1*2", [Address("A1")] = "=3" };
            var result = DxfTableCalculation.Evaluate(1, 3, _ => throw new Exception("formula addresses must not invoke cell resolver"), formulas);
            Equal(9d, result[Address("C1")], "topological result");
            Throws<NotSupportedException>(() => ((IDictionary<DxfTableCellAddress, double>)result).Clear());
        });
        Run("table-calculation/circular", () => Throws<InvalidOperationException>(() => DxfTableCalculation.Evaluate(1, 2, _ => 0,
            new Dictionary<DxfTableCellAddress, string> { [Address("A1")] = "=B1+1", [Address("B1")] = "=A1+1" })));
        Run("table-calculation/self-count", () => Equal(1d, DxfTableCalculation.Evaluate(1, 1, _ => 0,
            new Dictionary<DxfTableCellAddress, string> { [Address("A1")] = "=COUNT(A1)" })[Address("A1")], "COUNT depends on address, not value"));
        Run("table-calculation/duplicate-address", () => Throws<ArgumentException>(() => DxfTableCalculation.Evaluate(1, 1, _ => throw new Exception("must materialize first"),
            new[] { new KeyValuePair<DxfTableCellAddress, string>(Address("A1"), "=1"), new KeyValuePair<DxfTableCellAddress, string>(Address("A1"), "=2") })));
        for (int n = 1; n <= 100; n++)
        {
            int value = n;
            Run($"table-calculation/independent-arithmetic/{n}", () => Equal((double)(value * (value + 1) / 2),
                DxfTableFormula.Parse($"={value}*({value}+1)/2").Evaluate(1, 1, _ => 0), "integer triangular number"));
        }
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
            Run($"table-calculation/native-grid/{file}/{binary}", () => CheckNativeContentGrid(file, binary));
        foreach (bool binary in new[] { false, true })
        {
            Run($"table-calculation/apply/{binary}", () => ApplyContentFormula(binary));
            foreach (string fault in new[] { "fractional-int", "cycle", "unknown", "dispose", "reentry" })
                Run($"table-calculation/atomic/{binary}/{fault}", () => RejectContentFormula(binary, fault));
        }
    }
    private static void CheckNativeContentGrid(string file, bool binary)
    {
        var doc = ConsumerLoad(file, binary);
        foreach (var content in doc.Objects.Items.OfType<DxfStoredTableContent>())
        {
            var payload = content.Payload; var grid = content.GetGrid();
            Equal(content.RowCount!.Value, grid.Rows.Count, "row count");
            Equal(content.ColumnCount!.Value, grid.Columns.Count, "column count");
            Equal(grid.Rows.Count * grid.Columns.Count, grid.Cells.Count, "cell inventory");
            Check(grid.Cells.SelectMany(c => c.Contents).SequenceEqual(content.StoredValues), "scalar addresses changed stored order/identity");
            Check(ReferenceEquals(payload, content.Payload) && ReferenceEquals(payload, grid.Payload), "read mutated source snapshot");
            foreach (var cell in grid.Cells) Check(ReferenceEquals(cell, grid[cell.Address]), "indexer mismatch");
            Throws<NotSupportedException>(() => ((IList<DxfTableContentCell>)grid.Cells).Clear());
        }
    }
    private static DxfDocument FormulaDocument(bool binary, bool integer = false)
    {
        return ConsumerLoad("acad_table_simple.dxf", binary, raw =>
        {
            var content = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "TABLECONTENT");
            var tags = content.Tags.ToList();
            for (int i = 0; i + 5 < tags.Count; i++)
            {
                if (tags[i].Code != 1 || !Equals(tags[i].Value, "CELLCONTENT_BEGIN")) continue;
                if (tags[i + 4].Code != 90 || tags[i + 5].Code != 1) continue;
                tags[i + 4] = new DxfTag(90, integer ? 1 : 2);
                tags[i + 5] = integer ? new DxfTag(91, 0) : new DxfTag(140, 0d);
            }
            return raw.WithRecord(content, tags);
        });
    }
    private static Dictionary<DxfTableCellAddress, string> FormulaRequest() => new()
    { [Address("A1")] = "=7", [Address("A2")] = "=A1*3", [Address("A3")] = "=SUM(A1:A2)" };
    private static void ApplyContentFormula(bool binary)
    {
        var doc = FormulaDocument(binary); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().First();
        var old = content.GetGrid(); long seed = OwnershipSeed(doc);
        TableContentSave(doc, binary, $"table-calculation-before-{binary}.dxf");
        content.ApplyFormulaResults(FormulaRequest());
        Equal(7d, content.GetGrid()[Address("A1")].Contents.Single().Value, "first calculated cell");
        Equal(21d, content.GetGrid()[Address("A2")].Contents.Single().Value, "dependent cell");
        Equal(28d, content.GetGrid()[Address("A3")].Contents.Single().Value, "aggregate cell");
        Equal(0d, old[Address("A1")].Contents.Single().Value, "old grid mutated");
        Equal(seed, OwnershipSeed(doc), "formula allocated handles");
        var stale = old[Address("A1")].Contents.Single().WithValue(10d, "10");
        Throws<ArgumentException>(() => content.ReplaceContent(content.Name, content.Description, content.TableStyle, new[] { stale }));
        var reload = TableContentLoad(TableContentSave(doc, binary, $"table-calculation-after-{binary}.dxf"));
        Equal(28d, reload.Objects.Items.OfType<DxfStoredTableContent>().First().GetGrid()[Address("A3")].Contents.Single().Value, "roundtrip calculated result");
    }
    private static void RejectContentFormula(bool binary, string fault)
    {
        var doc = FormulaDocument(binary, fault == "fractional-int"); var content = doc.Objects.Items.OfType<DxfStoredTableContent>().First();
        var before = content.Payload; var request = FormulaRequest();
        if (fault == "fractional-int") request[Address("A3")] = "=1/2";
        if (fault == "cycle") request[Address("A3")] = "=A3+1";
        if (fault == "unknown") request[Address("A3")] = "=PRIVATE(1)";
        IEnumerable<KeyValuePair<DxfTableCellAddress, string>> Enumerate()
        {
            try
            {
                foreach (var pair in request) yield return pair;
                if (fault == "reentry")
                {
                    try { content.ApplyFormulaResults(FormulaRequest()); }
                    catch (InvalidOperationException) { }
                }
            }
            finally { if (fault == "dispose") throw new InvalidOperationException("test dispose failure"); }
        }
        bool rejected = false;
        try { content.ApplyFormulaResults(Enumerate()); }
        catch (Exception e) when (e is InvalidOperationException || e is NotSupportedException) { rejected = true; }
        Check(rejected, "invalid request was accepted");
        Check(ReferenceEquals(before, content.Payload), "formula failure partially published");
        content.ApplyFormulaResults(FormulaRequest());
    }
}
