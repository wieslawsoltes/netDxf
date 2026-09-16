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
        /// <summary>Maximum combined scalar/display UTF-16 units submitted to one transaction.</summary>
        public const int MaximumFieldResultCharacters = 4194304;
        private bool editingFieldResults, fieldResultsReentered;

        /// <summary>Atomically applies explicit cached scalar results to distinct current FIELD snapshots.</summary>
        /// <returns>The number of FIELD payloads that changed.</returns>
        /// <remarks>Every FIELD ancestor of a selected field must also be selected so it is not silently
        /// left with a stale successful cache. Evaluator code, named data, ownership and object references
        /// remain fixed. Successful results set evaluation/cache/display state and clear the stored error.
        /// No host TEXT/MTEXT/ATTRIB/TABLE display cache is rewritten. No handles are allocated.</remarks>
        public int ApplyFieldResults(IEnumerable<DxfFieldResultEdit> results)
        {
            this.BeginFieldResults();
            try
            {
                if (results == null) throw new ArgumentNullException(nameof(results));
                var edits = new List<DxfFieldResultEdit>();
                long characters = 0;
                foreach (var edit in results)
                {
                    if (edit == null || edits.Count == MaximumFieldResultCount)
                        throw new ArgumentException("Null or excessive FIELD result requests.", nameof(results));
                    CountFieldResultCharacters(edit.Result, ref characters);
                    edits.Add(edit);
                }
                return this.CommitFieldResults(edits);
            }
            finally { this.EndFieldResults(); }
        }

        /// <summary>Evaluates a complete owned FIELD tree in child-first order through an explicit host callback.</summary>
        /// <param name="root">A registered FIELD root not owned by another FIELD.</param>
        /// <param name="evaluator">A host evaluator returning an explicit result for each field, or throwing on unsupported code.</param>
        /// <param name="evaluationContext">Nonzero public context mask (1–63); defaults to on-demand (32).</param>
        /// <returns>The number of changed FIELD payloads, published together after every callback succeeds.</returns>
        /// <remarks>Every field must admit its complete cache projection and enable the requested context.
        /// Children are evaluated once before their parent; callbacks see old live caches plus detached child
        /// results. Unknown/native evaluator code is never automatically executed. A late callback failure,
        /// caught reentry, changed registration/profile or invalid graph aborts all result publication.
        /// Independent document changes made by caller callbacks are not rolled back. Single-threaded API.</remarks>
        public int EvaluateFieldTree(DxfStoredField root, Func<DxfFieldEvaluationInput, DxfFieldResult> evaluator, int evaluationContext = 32)
        {
            this.BeginFieldResults();
            try
            {
                if (root == null) throw new ArgumentNullException(nameof(root));
                if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
                if (evaluationContext <= 0 || (evaluationContext & ~63) != 0) throw new ArgumentOutOfRangeException(nameof(evaluationContext));
                if (root.Owner is DxfStoredField) throw new ArgumentException("Select the complete FIELD ownership root, not a child.", nameof(root));
                this.ValidateFieldResultGraph();
                var order = new List<DxfStoredField>();
                var stack = new Stack<Tuple<DxfStoredField, int, bool>>();
                var seen = new HashSet<DxfStoredField>();
                var snapshots = new Dictionary<DxfStoredField, DxfFieldEvaluationSnapshot>();
                stack.Push(Tuple.Create(root, 1, false));
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
                long characters = 0;
                foreach (var field in order)
                {
                    var input = new DxfFieldEvaluationInput(field, snapshots[field], field.Children.Select(child => evaluated[child]).ToList());
                    DxfFieldResult result = evaluator(input);
                    if (this.fieldResultsReentered) throw new InvalidOperationException("Reentry invalidated FIELD evaluation.");
                    if (result == null) throw new InvalidOperationException("A FIELD evaluator must return an explicit result or throw.");
                    CountFieldResultCharacters(result, ref characters);
                    evaluated.Add(field, result); edits.Add(snapshots[field].WithResult(result));
                }
                return this.CommitFieldResults(edits);
            }
            finally { this.EndFieldResults(); }
        }
        private static void CountFieldResultCharacters(DxfFieldResult result, ref long characters)
        {
            characters += (result.Value as string)?.Length ?? 0;
            characters += (long)result.FormattedText.Length + result.ValueDisplayText.Length;
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
