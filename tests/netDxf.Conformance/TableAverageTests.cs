// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
using System.Text.Json;
using netDxf;
using netDxf.Tables;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterTableAverageTests()
    {
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        for (int scenario = 0; scenario < 2; scenario++)
        {
            int index = scenario;
            Run($"table-average/field/{version}/{binary}/{index}", () => AverageField(version, binary, index));
        }
        Run("table-average/binary64-corpus", () =>
        {
            var rows = new List<object>();
            try
            {
                TableAverageCases.VerifyAll((item, values) => rows.Add(new {
                    id = item.Id, inputs = item.Inputs.Select(TableAverageCases.Hex).ToArray(),
                    results = values.Select(TableAverageCases.Hex).ToArray() }));
            }
            finally
            {
                File.WriteAllText(Path.Combine(ArtifactDirectory, "table-average.json"),
                    JsonSerializer.Serialize(new { schema = 1, observations = rows }));
            }
            Equal(16388, rows.Count, "AVERAGE observation inventory");
        });
        Run("table-average/policies", TableAverageCases.VerifyPolicies);
        Run("table-average/resource-and-dependency-bounds", () =>
        {
            Throws<InvalidOperationException>(() => DxfTableFormula.Parse("=AVERAGE(A1:A1000000)")
                .Evaluate(1000000, 1, _ => 0));
            Throws<InvalidOperationException>(() => DxfTableCalculation.Evaluate(1, 1, _ => 0,
                new Dictionary<DxfTableCellAddress, string> { [Address("A1")] = "=AVERAGE(A1)" }));
            Equal(double.MaxValue, DxfTableFormula.Parse("=AVERAGE(A1:A10000)")
                .Evaluate(10000, 1, _ => double.MaxValue), "Bounded large finite mean");
        });
        foreach (bool binary in new[] { false, true })
            Run("table-average/persistent-table/" + binary, () => AverageTable(binary));
    }
    private static void AverageField(DxfVersion version, bool binary, int scenario)
    {
        string expression = scenario == 0 ? "AVERAGE(1e308,1e308)" : "AVERAGE(1e308,1,-1e308)";
        double expected = scenario == 0 ? 1e308 : 1d / 3;
        string code = "\\AcExpr (" + expression + ") \\f \"%lu2%pr8\"";
        var doc = FieldResultDocument(version, binary, raw =>
        {
            var record = StoredFieldRecord(raw, "14F"); var tags = record.Tags.ToList();
            int at = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbField"));
            tags[at + 1] = new DxfTag(1, "AcExpr");
            tags[at + 2] = new DxfTag(2, code);
            while (tags[at + 3].Code == 3) tags.RemoveAt(at + 3);
            return raw.WithRecord(record, tags);
        });
        var root = ResultRoot(doc); var child = root.Children.Single();
        string rootHandle = root.Handle, childHandle = child.Handle;
        string prefix = $"average-field-{version}-{binary}-{scenario}";
        SaveFieldResults(doc, binary, prefix + "-source.dxf");
        long seed = StoredFieldSeed(doc);
        var evaluator = new DxfStandardFieldEvaluator();
        Equal(2, doc.Objects.EvaluateFieldTree(root, evaluator.EvaluateOrThrow), "Mean FIELD update count");
        Equal(seed, StoredFieldSeed(doc), "Mean FIELD evaluation allocated handles");
        string display = DxfValueFormat.Parse("%lu2%pr8").Format(expected);
        for (int stage = 0; stage < 2; stage++)
        {
            Equal(TableAverageCases.Bits(expected), TableAverageCases.Bits((double)child.Evaluation.Value), "Mean FIELD cache bits");
            Equal(code, child.FieldCode, "Mean FIELD code changed");
            Equal(display, child.Evaluation.FormattedText, "Mean child display");
            Equal(display, root.Evaluation.FormattedText, "Mean parent display");
            var payload = child.Payload; seed = StoredFieldSeed(doc);
            Equal(0, doc.Objects.EvaluateFieldTree(root, evaluator.EvaluateOrThrow), "Repeated mean FIELD was not a no-op");
            Check(ReferenceEquals(payload, child.Payload), "No-op mean FIELD replaced payload");
            Equal(seed, StoredFieldSeed(doc), "No-op mean FIELD allocated handles");
            bool output = stage == 0 ? binary : !binary;
            SaveFieldResults(doc, output, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf"));
            using var stream = new MemoryStream(); Check(doc.Save(stream, output), "Mean FIELD save"); stream.Position = 0;
            doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Mean FIELD reload");
            root = (DxfStoredField)doc.GetObjectByHandle(rootHandle); child = (DxfStoredField)doc.GetObjectByHandle(childHandle);
            Check(ReferenceEquals(child.Owner, root) && doc.Objects.Validate().Count == 0, "Mean FIELD ownership");
        }
        Equal(TableAverageCases.Bits(expected), TableAverageCases.Bits((double)child.Evaluation.Value), "Resaved mean FIELD cache bits");
    }
    private static void AverageTable(bool binary)
    {
        var doc = FormulaDocument(binary);
        var content = doc.Objects.Items.OfType<netDxf.Objects.DxfStoredTableContent>().First();
        var source = content.GetGrid();
        TableContentSave(doc, binary, $"table-average-{binary}-source.dxf");
        long seed = OwnershipSeed(doc);
        content.ApplyFormulaResults(new Dictionary<DxfTableCellAddress, string> {
            [Address("A1")] = "=1e308", [Address("A2")] = "=1e308", [Address("A3")] = "=AVERAGE(A1:A2)" });
        Equal(seed, OwnershipSeed(doc), "Mean evaluation allocated handles");
        Equal(0d, source[Address("A1")].Contents.Single().Value, "Mean evaluation changed old immutable grid");
        for (int stage = 0; stage < 2; stage++)
        {
            for (int row = 1; row <= 3; row++)
                Equal(1e308, content.GetGrid()[Address("A" + row)].Contents.Single().Value, "Stored mean mismatch");
            var payload = content.Payload;
            Throws<ArithmeticException>(() => content.ApplyFormulaResults(new Dictionary<DxfTableCellAddress, string> {
                [Address("A1")] = "=7", [Address("A2")] = "=AVERAGE(A1,1e308*2)" }));
            Check(ReferenceEquals(payload, content.Payload), "Late mean failure partially published table values");
            doc = TableContentLoad(TableContentSave(doc, stage == 0 ? binary : !binary,
                $"table-average-{binary}-{(stage == 0 ? "output" : "resave")}.dxf"));
            content = doc.Objects.Items.OfType<netDxf.Objects.DxfStoredTableContent>().First();
            Check(doc.Objects.Validate().Count == 0, "Mean table graph changed");
        }
        Equal(1e308, content.GetGrid()[Address("A3")].Contents.Single().Value, "Resaved mean mismatch");
    }
}
