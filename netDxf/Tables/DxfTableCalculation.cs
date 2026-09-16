// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.Tables
{
    /// <summary>Evaluates an explicit formula map as one bounded dependency graph without mutating any source cells.</summary>
    public static class DxfTableCalculation
    {
        /// <summary>Calculates all supplied formulas with memoization and circular-reference detection.</summary>
        /// <remarks>
        /// Formula addresses override resolver cells for this calculation only. Enumeration is completed
        /// before the resolver is called. Duplicate addresses, cycles, unknown functions, invalid operands,
        /// out-of-grid references and nonfinite results fail the complete calculation. Results are detached.
        /// This API does not interpret strings beginning with equals as formulas or execute native FIELD objects.
        /// </remarks>
        public static IReadOnlyDictionary<DxfTableCellAddress, double> Evaluate(int rowCount, int columnCount,
            Func<DxfTableCellAddress, object> values, IEnumerable<KeyValuePair<DxfTableCellAddress, string>> formulas)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (formulas == null) throw new ArgumentNullException(nameof(formulas));
            var compiled = new Dictionary<DxfTableCellAddress, DxfTableFormula>();
            var results = new Dictionary<DxfTableCellAddress, double>();
            var active = new HashSet<DxfTableCellAddress>();
            DxfTableFormula.Evaluation context = null;
            context = new DxfTableFormula.Evaluation(rowCount, columnCount, address =>
            {
                if (!compiled.TryGetValue(address, out DxfTableFormula formula)) return values(address);
                if (results.TryGetValue(address, out double old)) return old;
                if (active.Count >= DxfTableFormula.MaximumDepth) throw new InvalidOperationException("Table formula dependency depth exceeds its limit.");
                if (!active.Add(address)) throw new InvalidOperationException("Circular table formula reference: " + address);
                try { double result = formula.Evaluate(context); results.Add(address, result); return result; }
                finally { active.Remove(address); }
            });
            long characters = 0;
            foreach (var pair in formulas)
            {
                context.Check(pair.Key);
                if (compiled.Count >= 100000 || compiled.ContainsKey(pair.Key)) throw new ArgumentException("Formula addresses must be unique and the request must not exceed 100,000 formulas.", nameof(formulas));
                characters += pair.Value == null ? 0 : pair.Value.Length;
                if (characters > 1048576) throw new ArgumentException("Combined formula text exceeds one million characters.", nameof(formulas));
                compiled.Add(pair.Key, DxfTableFormula.Parse(pair.Value));
            }
            foreach (var address in compiled.Keys) context.Read(address);
            return new ReadOnlyDictionary<DxfTableCellAddress, double>(results);
        }
    }
}
