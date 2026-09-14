#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;

namespace netDxf.IO
{
    /// <summary>Which exposed reference kinds a dependency traversal follows.</summary>
    [Flags]
    public enum DxfRawReferenceTraversal
    {
        /// <summary>Only the supplied root records.</summary>
        None = 0,
        /// <summary>Ordinary hard pointers.</summary>
        HardPointers = 1,
        /// <summary>Ordinary soft pointers.</summary>
        SoftPointers = 2,
        /// <summary>Hard ownership and extension dictionary links.</summary>
        HardOwnership = 4,
        /// <summary>Soft ownership links.</summary>
        SoftOwnership = 8,
        /// <summary>Common parent-owner links.</summary>
        ParentOwners = 16,
        /// <summary>Persistent reactor links.</summary>
        Reactors = 32,
        /// <summary>Extended-data handle links.</summary>
        XData = 64,
        /// <summary>Interpreted HEADER references, excluding HANDSEED.</summary>
        HeaderReferences = 128,
        /// <summary>Every interpreted reference category, excluding arbitrary and opaque slots.</summary>
        All = 255
    }

    /// <summary>A read-only traversal result, including unresolved and uninterpreted slots.</summary>
    public sealed class DxfRawDependencyClosure
    {
        internal DxfRawDependencyClosure(List<DxfRawRecord> records, List<DxfRawHandleOccurrence> unresolved,
            List<DxfRawHandleOccurrence> ambiguous, List<DxfRawHandleOccurrence> opaque)
        {
            records.Sort((a,b) => a.StartTagIndex.CompareTo(b.StartTagIndex));
            unresolved.Sort((a,b) => a.TagIndex.CompareTo(b.TagIndex));
            ambiguous.Sort((a,b) => a.TagIndex.CompareTo(b.TagIndex));
            opaque.Sort((a,b) => a.TagIndex.CompareTo(b.TagIndex));
            this.Records = new ReadOnlyCollection<DxfRawRecord>(records);
            this.UnresolvedReferences = new ReadOnlyCollection<DxfRawHandleOccurrence>(unresolved);
            this.AmbiguousReferences = new ReadOnlyCollection<DxfRawHandleOccurrence>(ambiguous);
            this.UninterpretedHandles = new ReadOnlyCollection<DxfRawHandleOccurrence>(opaque);
        }
        /// <summary>Gets the selected records in source order, including supplied roots.</summary>
        public IReadOnlyList<DxfRawRecord> Records { get; }
        /// <summary>Gets selected nonzero references with no definition.</summary>
        public IReadOnlyList<DxfRawHandleOccurrence> UnresolvedReferences { get; }
        /// <summary>Gets selected references with multiple definitions; none was chosen.</summary>
        public IReadOnlyList<DxfRawHandleOccurrence> AmbiguousReferences { get; }
        /// <summary>Gets opaque handle slots in selected records, which traversal did not interpret.</summary>
        public IReadOnlyList<DxfRawHandleOccurrence> UninterpretedHandles { get; }
        /// <summary>Gets whether all selected interpreted references resolved uniquely.</summary>
        /// <remarks>Not a certificate of complete semantic dependency closure, including opaque/private payloads.</remarks>
        public bool AreSelectedReferencesResolved { get { return this.UnresolvedReferences.Count == 0 && this.AmbiguousReferences.Count == 0; } }
    }

    public sealed partial class DxfRawHandleIndex
    {
        /// <summary>Traverses selected exposed references iteratively without choosing ambiguous identities.</summary>
        /// <param name="roots">Records from the indexed snapshot; duplicates are coalesced.</param>
        /// <param name="traversal">Reference kinds to follow; arbitrary handles are never followed.</param>
        /// <param name="cancellationToken">Cancellation during enumeration and graph traversal.</param>
        /// <returns>Resolved records plus explicit unresolved/ambiguous/uninterpreted evidence.</returns>
        /// <remarks>
        /// This follows outgoing links only. POLYLINE/BLOCK lexical aggregates, reverse ownership,
        /// class-specific dependencies and references hidden in opaque payloads require a schema.
        /// It does not extract a valid standalone drawing or delete/remap any data.
        /// </remarks>
        public DxfRawDependencyClosure GetDependencyClosure(IEnumerable<DxfRawRecord> roots,
            DxfRawReferenceTraversal traversal = DxfRawReferenceTraversal.HardPointers | DxfRawReferenceTraversal.HardOwnership,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if ((traversal & ~DxfRawReferenceTraversal.All) != 0) throw new ArgumentOutOfRangeException(nameof(traversal));
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<DxfRawRecord> selected = new HashSet<DxfRawRecord>();
            Queue<DxfRawRecord> pending = new Queue<DxfRawRecord>();
            List<DxfRawRecord> result = new List<DxfRawRecord>();
            int count = 0;
            foreach (DxfRawRecord root in roots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count++ >= this.options.MaximumOccurrences) throw new InvalidDataException("Raw dependency root enumeration budget exceeded.");
                this.CheckRecord(root);
                if (selected.Add(root)) { pending.Enqueue(root); result.Add(root); }
            }
            List<DxfRawHandleOccurrence> unresolved = new List<DxfRawHandleOccurrence>();
            List<DxfRawHandleOccurrence> ambiguous = new List<DxfRawHandleOccurrence>();
            List<DxfRawHandleOccurrence> opaque = new List<DxfRawHandleOccurrence>();
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DxfRawRecord record = pending.Dequeue();
                List<DxfRawHandleOccurrence> items;
                if (!this.byRecord.TryGetValue(record, out items)) continue;
                foreach (DxfRawHandleOccurrence item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Role == DxfRawHandleRole.Opaque) opaque.Add(item);
                    if (item.NumericHandle == 0 || (TraversalFor(item.Role) & traversal) == 0) continue;
                    List<DxfRawHandleOccurrence> targets;
                    if (!this.identities.TryGetValue(item.NumericHandle, out targets)) { unresolved.Add(item); continue; }
                    if (targets.Count != 1) { ambiguous.Add(item); continue; }
                    DxfRawRecord next = targets[0].Record;
                    if (selected.Add(next)) { pending.Enqueue(next); result.Add(next); }
                }
            }
            return new DxfRawDependencyClosure(result, unresolved, ambiguous, opaque);
        }
        private static DxfRawReferenceTraversal TraversalFor(DxfRawHandleRole role)
        {
            switch (role)
            {
                case DxfRawHandleRole.HardPointer: return DxfRawReferenceTraversal.HardPointers;
                case DxfRawHandleRole.SoftPointer: return DxfRawReferenceTraversal.SoftPointers;
                case DxfRawHandleRole.HardOwner:
                case DxfRawHandleRole.ExtensionDictionary: return DxfRawReferenceTraversal.HardOwnership;
                case DxfRawHandleRole.SoftOwner: return DxfRawReferenceTraversal.SoftOwnership;
                case DxfRawHandleRole.Owner: return DxfRawReferenceTraversal.ParentOwners;
                case DxfRawHandleRole.Reactor: return DxfRawReferenceTraversal.Reactors;
                case DxfRawHandleRole.XData: return DxfRawReferenceTraversal.XData;
                case DxfRawHandleRole.HeaderReference: return DxfRawReferenceTraversal.HeaderReferences;
                default: return DxfRawReferenceTraversal.None;
            }
        }

        /// <summary>Simultaneously renames unique identities and their interpreted exposed references.</summary>
        /// <param name="mapping">Existing nonzero source handles and distinct nonzero target handles.</param>
        /// <param name="cancellationToken">Cancellation during preflight and immutable tag copying.</param>
        /// <returns>An edited snapshot, or the original snapshot for a numeric no-op.</returns>
        /// <remarks>
        /// Numeric aliases are normalized for validation; swaps and permutations are simultaneous.
        /// Rejects duplicate/null identities, malformed controls, normalized duplicate mapping keys,
        /// collisions with unchanged identities, capture of formerly dangling references, and opaque
        /// handle slots touched by a source or target. Arbitrary group 320-329 values are not translated.
        /// An existing HANDSEED is raised past all identities when needed; absence is not invented.
        /// Changed handle tags use canonical uppercase hex. Unchanged tags retain their objects/values.
        /// This is not a class-specific clone/import/repair engine. Hidden handles in strings or binary
        /// payloads cannot be found here. No cross-document/version change or source mutation occurs.
        /// </remarks>
        public DxfRawDocument RemapHandles(IReadOnlyDictionary<string, string> mapping,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (mapping == null) throw new ArgumentNullException(nameof(mapping));
            cancellationToken.ThrowIfCancellationRequested();
            Dictionary<ulong, ulong> changes = new Dictionary<ulong, ulong>();
            HashSet<ulong> supplied = new HashSet<ulong>();
            int count = 0;
            foreach (KeyValuePair<string, string> pair in mapping)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (count++ >= this.options.MaximumOccurrences) throw new InvalidDataException("Raw handle mapping budget exceeded.");
                ulong source = ParseHandle(pair.Key, nameof(mapping)), target = ParseHandle(pair.Value, nameof(mapping));
                if (source == 0 || target == 0) throw new ArgumentException("Identity remapping cannot use the null handle.", nameof(mapping));
                if (!supplied.Add(source)) throw new ArgumentException("Mapping keys alias the same numeric source handle.", nameof(mapping));
                List<DxfRawHandleOccurrence> definitions;
                if (!this.identities.TryGetValue(source, out definitions) || definitions.Count != 1)
                    throw new ArgumentException("Each source must have exactly one indexed identity.", nameof(mapping));
                if (source != target) changes.Add(source, target);
            }
            if (changes.Count == 0) return this.document;
            foreach (DxfRawHandleDiagnostic diagnostic in this.diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (diagnostic.Kind == DxfRawHandleDiagnosticKind.DuplicateIdentity ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.MultipleIdentities ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.NullIdentity ||
                    diagnostic.Kind == DxfRawHandleDiagnosticKind.InvalidControlGroup)
                    throw new InvalidOperationException("Handle remapping requires unambiguous identities and valid common control framing.");
            }
            HashSet<ulong> targets = new HashSet<ulong>();
            foreach (KeyValuePair<ulong, ulong> change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!targets.Add(change.Value)) throw new ArgumentException("Multiple identities cannot share a target handle.", nameof(mapping));
                if (this.identities.ContainsKey(change.Value) && !changes.ContainsKey(change.Value))
                    throw new ArgumentException("A target collides with an unchanged object identity.", nameof(mapping));
                if (!this.identities.ContainsKey(change.Value) && this.incoming.ContainsKey(change.Value))
                    throw new ArgumentException("A target would capture a previously unresolved reference.", nameof(mapping));
            }
            ulong maximum = 0;
            foreach (ulong identity in this.identities.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ulong mapped;
                if (!changes.TryGetValue(identity, out mapped)) mapped = identity;
                if (mapped > maximum) maximum = mapped;
            }
            DxfRawHandleOccurrence seed = null;
            Dictionary<int, DxfTag> replacements = new Dictionary<int, DxfTag>();
            foreach (DxfRawHandleOccurrence item in this.occurrences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Role == DxfRawHandleRole.Opaque && (changes.ContainsKey(item.NumericHandle) || targets.Contains(item.NumericHandle)))
                    throw new InvalidOperationException("An opaque handle slot is affected; an application-specific remapping contract is required.");
                if (item.Role == DxfRawHandleRole.HeaderSeed)
                {
                    if (seed != null) throw new InvalidOperationException("Ambiguous repeated HANDSEED declarations cannot be repaired implicitly.");
                    seed = item; continue;
                }
                ulong value;
                if ((item.Role == DxfRawHandleRole.Identity || item.IsReference) && changes.TryGetValue(item.NumericHandle, out value))
                    replacements.Add(item.TagIndex, new DxfTag(item.Code, value.ToString("X", CultureInfo.InvariantCulture)));
            }
            if (seed != null && seed.NumericHandle <= maximum)
            {
                if (maximum == ulong.MaxValue) throw new OverflowException("No representable next HANDSEED remains after remapping.");
                replacements.Add(seed.TagIndex, new DxfTag(seed.Code, (maximum + 1).ToString("X", CultureInfo.InvariantCulture)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            DxfRawDocument result = this.document.WithTags(this.RemappedTags(replacements, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        private IEnumerable<DxfTag> RemappedTags(Dictionary<int, DxfTag> replacements, CancellationToken token)
        {
            for (int i = 0; i < this.document.Tags.Count; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                DxfTag replacement;
                yield return replacements.TryGetValue(i, out replacement) ? replacement : this.document.Tags[i];
            }
        }
    }
}
