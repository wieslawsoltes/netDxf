// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Maximum fields in one explicit cached-result transaction.</summary>
        public const int MaximumFieldResultCount = 4096;
        /// <summary>Maximum owned FIELD depth in one evaluation, including the root.</summary>
        public const int MaximumFieldEvaluationDepth = 256;
        /// <summary>Maximum combined scalar, display and error-message UTF-16 units submitted to one transaction.</summary>
        public const int MaximumFieldResultCharacters = 4194304;
        /// <summary>Maximum combined binary-value bytes in one result transaction or evaluated forest.</summary>
        public const int MaximumFieldResultBytes = 16777216;
        private bool editingFieldResults, fieldResultsReentered;

        /// <summary>Atomically applies explicit cached scalar results or evaluator failures to distinct current FIELD snapshots.</summary>
        /// <returns>The number of FIELD payloads that changed.</returns>
        /// <remarks>Every FIELD ancestor of a selected field must also be selected so it is not silently
        /// left with a stale successful cache. Evaluator code, named data, ownership and object references
        /// remain fixed. Successful results set evaluation/cache/display state and clear the stored error.
        /// Failure results explicitly retain a bound cache or replace it with a caller-supplied fallback.
        /// No host TEXT/MTEXT/ATTRIB/TABLE display cache is rewritten. No handles are allocated.</remarks>
        public int ApplyFieldResults(IEnumerable<DxfFieldResultEdit> results)
        {
            this.BeginFieldResults();
            try
            {
                if (results == null) throw new ArgumentNullException(nameof(results));
                var edits = new List<DxfFieldResultEdit>();
                long characters = 0, binaryBytes = 0;
                foreach (var edit in results)
                {
                    if (edit == null || edits.Count == MaximumFieldResultCount)
                        throw new ArgumentException("Null or excessive FIELD result requests.", nameof(results));
                    CountFieldResultSize(edit.Result, ref characters, ref binaryBytes);
                    edits.Add(edit);
                }
                return this.CommitFieldResults(edits);
            }
            finally { this.EndFieldResults(); }
        }

        /// <summary>Evaluates a complete owned FIELD tree in child-first order through an explicit host callback.</summary>
        /// <param name="root">A registered FIELD root not owned by another FIELD.</param>
        /// <param name="evaluator">A host evaluator returning an explicit success or failure result, or throwing to abort.</param>
        /// <param name="evaluationContext">Nonzero supported context mask (1–63); defaults to on-demand (32).</param>
        /// <returns>The number of changed FIELD payloads, published together after every callback succeeds.</returns>
        /// <remarks>Uses the same transaction as EvaluateFieldTrees. Returning a failure result records a completed
        /// failed evaluation; throwing aborts all publication. No native evaluator is implicitly executed.</remarks>
        public int EvaluateFieldTree(DxfStoredField root, Func<DxfFieldEvaluationInput, DxfFieldResult> evaluator, int evaluationContext = 32)
        {
            return this.EvaluateFieldTrees(new[] { root }, evaluator, evaluationContext);
        }

        /// <summary>Evaluates distinct complete FIELD roots and publishes all of their results in one transaction.</summary>
        /// <param name="roots">Distinct registered ownership roots, enumerated and disposed before any evaluator callback.</param>
        /// <param name="evaluator">A host callback returning an explicit success or failure result for each field.</param>
        /// <param name="evaluationContext">Nonzero supported context mask (1–63); defaults to on-demand (32).</param>
        /// <returns>The number of changed FIELD payloads across the selected forest.</returns>
        /// <remarks>All trees are validated before evaluation begins. Root order and stored child order determine
        /// deterministic child-first callbacks, once per identity. Node and text limits apply to the whole forest.
        /// Every field must admit its complete cache projection and enable the context. Callbacks see old live caches
        /// and detached child results, including explicit failures. A late exception, caught reentry or invalid source
        /// state aborts every tree. Independent document changes made by caller callbacks are not rolled back.
        /// Single-threaded API; host entities and native external evaluators are not updated or invoked.</remarks>
        public int EvaluateFieldTrees(IEnumerable<DxfStoredField> roots,
            Func<DxfFieldEvaluationInput, DxfFieldResult> evaluator, int evaluationContext = 32)
        {
            this.BeginFieldResults();
            try
            {
                if (roots == null) throw new ArgumentNullException(nameof(roots));
                if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
                if (evaluationContext <= 0 || (evaluationContext & ~63) != 0) throw new ArgumentOutOfRangeException(nameof(evaluationContext));
                var rootList = new List<DxfStoredField>();
                var rootSet = new HashSet<DxfStoredField>();
                foreach (var root in roots)
                {
                    if (root == null) throw new ArgumentNullException(nameof(roots), "A FIELD root cannot be null.");
                    if (rootList.Count == MaximumFieldResultCount || !rootSet.Add(root))
                        throw new ArgumentException("FIELD roots must be distinct and within the evaluation limit.", nameof(roots));
                    rootList.Add(root);
                }
                this.ValidateFieldResultGraph();
                var order = new List<DxfStoredField>();
                var stack = new Stack<Tuple<DxfStoredField, int, bool>>();
                var seen = new HashSet<DxfStoredField>();
                var snapshots = new Dictionary<DxfStoredField, DxfFieldEvaluationSnapshot>();
                for (int i = rootList.Count - 1; i >= 0; i--)
                {
                    if (rootList[i].Owner is DxfStoredField)
                        throw new ArgumentException("Select complete FIELD ownership roots, not children.", nameof(roots));
                    stack.Push(Tuple.Create(rootList[i], 1, false));
                }
                while (stack.Count != 0)
                {
                    var current = stack.Pop(); var field = current.Item1;
                    if (current.Item3) { order.Add(field); continue; }
                    if (!seen.Add(field) || seen.Count > MaximumFieldResultCount || current.Item2 > MaximumFieldEvaluationDepth)
                        throw new InvalidOperationException("FIELD ownership repeats or exceeds the evaluation limits.");
                    this.CheckFieldResultTarget(field);
                    if ((field.Evaluation.StoredEvaluationOptions & evaluationContext) == 0)
                        throw new InvalidOperationException("The requested context is disabled for FIELD " + field.Handle);
                    snapshots.Add(field, field.Evaluation);
                    stack.Push(Tuple.Create(field, current.Item2, true));
                    for (int i = field.Children.Count - 1; i >= 0; i--)
                        stack.Push(Tuple.Create(field.Children[i], current.Item2 + 1, false));
                }
                var evaluated = new Dictionary<DxfStoredField, DxfFieldResult>();
                var edits = new List<DxfFieldResultEdit>();
                long characters = 0, binaryBytes = 0;
                foreach (var field in order)
                {
                    var input = new DxfFieldEvaluationInput(field, snapshots[field], field.Children.Select(child => evaluated[child]).ToList());
                    DxfFieldResult result = evaluator(input);
                    if (this.fieldResultsReentered) throw new InvalidOperationException("Reentry invalidated FIELD evaluation.");
                    if (result == null) throw new InvalidOperationException("A FIELD evaluator must return an explicit result or throw.");
                    CountFieldResultSize(result, ref characters, ref binaryBytes);
                    evaluated.Add(field, result); edits.Add(snapshots[field].WithResult(result));
                }
                return this.CommitFieldResults(edits);
            }
            finally { this.EndFieldResults(); }
        }
        private static void CountFieldResultSize(DxfFieldResult result, ref long characters, ref long binaryBytes)
        {
            if (result.Value is DxfFieldBinaryValue binary) binaryBytes += binary.Length;
            if (binaryBytes > MaximumFieldResultBytes)
                throw new ArgumentException("Combined FIELD result binary data exceeds its transaction limit.");
            characters += (result.Value as string)?.Length ?? 0;
            characters += (long)result.FormattedText.Length + result.ValueDisplayText.Length + result.ErrorMessage.Length;
            if (characters > MaximumFieldResultCharacters)
                throw new ArgumentException("Combined FIELD result text exceeds its transaction limit.");
        }
        private void BeginFieldResults()
        {
            if (this.editingFieldResults)
            { this.fieldResultsReentered = true; throw new InvalidOperationException("FIELD result transactions cannot be reentered."); }
            this.editingFieldResults = true; this.fieldResultsReentered = false;
        }
        private void EndFieldResults() { this.editingFieldResults = false; this.fieldResultsReentered = false; }
        private void ValidateFieldResultGraph()
        {
            if (this.fieldResultsReentered) throw new InvalidOperationException("Reentry invalidated FIELD result replacement.");
            var errors = this.Validate();
            if (errors.Count != 0) throw new InvalidOperationException("Cannot publish FIELD results in an invalid database: " + string.Join("; ", errors));
        }
        private void CheckFieldResultTarget(DxfStoredField field)
        {
            if (field.Database != this || !this.IsRegistered(field) || field.IsErased)
                throw new ArgumentException("FIELD result targets must remain registered in this document.", nameof(field));
            field.ValidateSource(this.Document);
            if (field.Evaluation == null) throw new NotSupportedException("FIELD evaluation/cache framing is not qualified for result editing.");
        }
        private int CommitFieldResults(List<DxfFieldResultEdit> edits)
        {
            this.ValidateFieldResultGraph();
            var selected = new HashSet<DxfStoredField>();
            foreach (var edit in edits)
            {
                var field = edit.Original.Field; this.CheckFieldResultTarget(field);
                if (!selected.Add(field)) throw new ArgumentException("FIELD result targets must be distinct.", nameof(edits));
                if (!ReferenceEquals(field.Evaluation, edit.Original)) throw new ArgumentException("A FIELD evaluation snapshot is stale.", nameof(edits));
            }
            foreach (var field in selected)
                if (field.Owner is DxfStoredField parent && !selected.Contains(parent))
                    throw new ArgumentException("Updating a child FIELD also requires every FIELD ancestor's explicit result.", nameof(edits));
            var prepared = new List<Tuple<DxfStoredField, DxfStoredField.PreparedResult>>();
            foreach (var edit in edits)
            {
                var next = edit.Original.Field.PrepareResult(edit);
                if (next != null) prepared.Add(Tuple.Create(edit.Original.Field, next));
            }
            // No caller code, graph lookup, parsing or allocation remains in publication.
            foreach (var item in prepared) item.Item1.PublishResult(item.Item2);
            return prepared.Count;
        }
    }
}
