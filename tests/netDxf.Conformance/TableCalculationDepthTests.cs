using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterTableCalculationDepthTests()
    {
        foreach (int signs in new[] { 8, 16, 64, 120 })
        {
            // Every individual expression and dependency count is admissible; only
            // their combined nesting can exceed the separate shared stack budget.
            int accepted = DxfTableFormula.MaximumEvaluationDepth / (signs + 1);
            Run($"table-calculation/combined-depth/{signs}/accepted", () =>
            {
                var results = DxfTableCalculation.Evaluate(accepted, 1, _ => throw new Exception("unexpected resolver"),
                    DeepCalculation(accepted, signs));
                Equal(accepted, results.Count, "all formula results computed");
                Check(results.Values.All(result => result == 1), "deep calculation result");
            });
            Run($"table-calculation/combined-depth/{signs}/rejected", () =>
            {
                Throws<InvalidOperationException>(() => DxfTableCalculation.Evaluate(accepted + 1, 1, _ => null!,
                    DeepCalculation(accepted + 1, signs)));
                // The same immutable source requests remain usable for a shorter calculation.
                var results = DxfTableCalculation.Evaluate(accepted, 1, _ => null!, DeepCalculation(accepted, signs));
                Equal(accepted, results.Count, "failed calculation leaked evaluation depth");
            });
        }
    }

    private static IEnumerable<KeyValuePair<DxfTableCellAddress, string>> DeepCalculation(int rows, int signs)
    {
        for (int row = 0; row < rows; row++)
        {
            string operand = row + 1 == rows ? "1" : new DxfTableCellAddress(row + 1, 0).ToString();
            yield return new KeyValuePair<DxfTableCellAddress, string>(new DxfTableCellAddress(row, 0),
                "=" + new string('+', signs) + operand);
        }
    }
}
