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
    /// <summary>Immutable, context-aware identity and reference index over one raw snapshot.</summary>
    /// <remarks>
    /// Build explicitly when needed. Indexing never changes tags, resolves external files, executes
    /// application data or infers handles embedded in strings/binary payloads. Numeric aliases share
    /// a lookup key, while original tag spelling is retained. Duplicate definitions are never overwritten.
    /// Unknown group-102 data, XRECORD payloads and embedded-object tails are opaque. This is structural evidence, not a
    /// complete class schema, drawing validator or proof of dependency-closed editing.
    /// </remarks>
    public sealed partial class DxfRawHandleIndex
    {
        private static readonly IReadOnlyList<DxfRawHandleOccurrence> Empty =
            new ReadOnlyCollection<DxfRawHandleOccurrence>(new DxfRawHandleOccurrence[0]);
        private readonly DxfRawDocument document;
        private readonly Dictionary<ulong, List<DxfRawHandleOccurrence>> identities = new Dictionary<ulong, List<DxfRawHandleOccurrence>>();
        private readonly Dictionary<ulong, List<DxfRawHandleOccurrence>> incoming = new Dictionary<ulong, List<DxfRawHandleOccurrence>>();
        private readonly Dictionary<DxfRawRecord, List<DxfRawHandleOccurrence>> byRecord = new Dictionary<DxfRawRecord, List<DxfRawHandleOccurrence>>();
        private readonly HashSet<DxfRawRecord> records = new HashSet<DxfRawRecord>();
        private readonly List<DxfRawHandleOccurrence> occurrences = new List<DxfRawHandleOccurrence>();
        private readonly List<DxfRawHandleDiagnostic> diagnostics = new List<DxfRawHandleDiagnostic>();
        private readonly DxfRawHandleIndexOptions options;

        private DxfRawHandleIndex(DxfRawDocument document, DxfRawHandleIndexOptions options, CancellationToken token)
        {
            this.document = document; this.options = options;
            foreach (DxfRawSection section in document.Sections)
            {
                token.ThrowIfCancellationRequested();
                int cursor = section.StartTagIndex;
                string table = null;
                foreach (DxfRawRecord record in section.Records)
                {
                    this.records.Add(record);
                    this.ReadUnscoped(cursor, record.StartTagIndex, token);
                    if (Eq(section.Name, "TABLES") && Eq(record.Name, "TABLE"))
                    {
                        table = null;
                        foreach (DxfTag tag in record.Content)
                            if (tag.Code == 2) { table = (string)tag.RawValue; break; }
                    }
                    this.ReadRecord(record, table, token);
                    if (Eq(record.Name, "ENDTAB")) table = null;
                    cursor = record.EndTagIndex;
                }
                this.ReadUnscoped(cursor, section.EndTagIndex, token);
            }
            this.ResolveDiagnostics(token);
            this.FindOwnerCycles(token);
            this.Occurrences = new ReadOnlyCollection<DxfRawHandleOccurrence>(this.occurrences);
            this.Diagnostics = new ReadOnlyCollection<DxfRawHandleDiagnostic>(this.diagnostics);
        }

        /// <summary>Builds a bounded index; failures and cancellation leave the document untouched.</summary>
        /// <param name="document">Immutable raw document.</param>
        /// <param name="options">Optional memory budgets.</param>
        /// <param name="cancellationToken">Cancellation during scanning and graph analysis.</param>
        /// <returns>An immutable index belonging to this exact snapshot.</returns>
        public static DxfRawHandleIndex Create(DxfRawDocument document, DxfRawHandleIndexOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            cancellationToken.ThrowIfCancellationRequested();
            return new DxfRawHandleIndex(document, options ?? new DxfRawHandleIndexOptions(), cancellationToken);
        }
        /// <summary>Gets exposed handle slots in original tag order, including opaque slots.</summary>
        public IReadOnlyList<DxfRawHandleOccurrence> Occurrences { get; }
        /// <summary>Gets deterministic structural diagnostics; absence does not certify a DXF schema.</summary>
        public IReadOnlyList<DxfRawHandleDiagnostic> Diagnostics { get; }

        /// <summary>Finds all definitions of a numeric handle, never choosing among duplicates.</summary>
        /// <param name="handle">One through sixteen hexadecimal digits.</param>
        /// <returns>Immutable identity occurrences, or an empty list.</returns>
        public IReadOnlyList<DxfRawHandleOccurrence> FindDefinitions(string handle)
        { return Lookup(this.identities, ParseHandle(handle, nameof(handle))); }
        /// <summary>Finds interpreted references to a numeric handle, including unresolved references.</summary>
        /// <param name="handle">One through sixteen hexadecimal digits.</param>
        /// <returns>Immutable occurrences; arbitrary and opaque slots are excluded.</returns>
        public IReadOnlyList<DxfRawHandleOccurrence> FindReferences(string handle)
        { return Lookup(this.incoming, ParseHandle(handle, nameof(handle))); }
        /// <summary>Gets all handle slots in one record of this exact snapshot.</summary>
        /// <param name="record">A record from the indexed document.</param>
        /// <returns>Immutable occurrences in source order.</returns>
        public IReadOnlyList<DxfRawHandleOccurrence> GetOccurrences(DxfRawRecord record)
        {
            this.CheckRecord(record);
            List<DxfRawHandleOccurrence> value;
            return this.byRecord.TryGetValue(record, out value) ? new ReadOnlyCollection<DxfRawHandleOccurrence>(value) : Empty;
        }
        private void CheckRecord(DxfRawRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!this.records.Contains(record)) throw new ArgumentException("The record belongs to another raw snapshot.", nameof(record));
        }
        private static IReadOnlyList<DxfRawHandleOccurrence> Lookup(Dictionary<ulong, List<DxfRawHandleOccurrence>> table, ulong handle)
        {
            List<DxfRawHandleOccurrence> value;
            return table.TryGetValue(handle, out value) ? new ReadOnlyCollection<DxfRawHandleOccurrence>(value) : Empty;
        }
        private static ulong ParseHandle(string value, string parameter)
        {
            if (value == null) throw new ArgumentNullException(parameter);
            ulong result;
            if (value.Length < 1 || value.Length > 16 || !ulong.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result))
                throw new ArgumentException("Expected one through sixteen hexadecimal digits.", parameter);
            // NumberStyles.AllowHexSpecifier does not admit whitespace or signs.
            return result;
        }
        private static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private void ReadUnscoped(int start, int end, CancellationToken token)
        {
            for (int i = start; i < end; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                DxfTag tag = this.document.Tags[i];
                if (tag.ValueType == DxfTagValueType.Handle) this.Add(null, i, tag, DxfRawHandleRole.Opaque, null, null);
            }
        }
        private void ReadRecord(DxfRawRecord record, string table, CancellationToken token)
        {
            bool header = Eq(record.SectionName, "HEADER");
            bool database = Eq(record.SectionName, "ENTITIES") || Eq(record.SectionName, "BLOCKS") ||
                Eq(record.SectionName, "TABLES") || Eq(record.SectionName, "OBJECTS");
            bool dimstyle = Eq(record.SectionName, "TABLES") && Eq(table, "DIMSTYLE") && Eq(record.Name, "DIMSTYLE");
            bool xrecord = Eq(record.SectionName, "OBJECTS") && Eq(record.Name, "XRECORD");
            bool payload = false, embedded = false, uncertain = false;
            string subclass = null, application = null;
            List<string> groups = new List<string>();
            for (int i = record.StartTagIndex + 1; i < record.EndTagIndex; i++)
            {
                if ((i & 255) == 0) token.ThrowIfCancellationRequested();
                DxfTag tag = this.document.Tags[i];
                if (tag.Code == 999) continue;
                // An embedded object's private grammar can reuse all nonzero codes.
                // Without that grammar, even 100/102/1001-looking fields in its tail
                // cannot be promoted to the enclosing object's structural links.
                if (database && !payload && groups.Count == 0 && tag.Code == 101 &&
                    Eq((string)tag.RawValue, "Embedded Object"))
                {
                    payload = true;
                    embedded = true;
                    application = null;
                    continue;
                }
                // XRECORD's normal-code payload is application data, including code 102.
                // Once reached, do not reinterpret its scalar values as common control structure.
                if (!payload && tag.Code == 102)
                {
                    string control = (string)tag.RawValue;
                    if (control.StartsWith("{", StringComparison.Ordinal)) groups.Add(control.Substring(1));
                    else if (control == "}" && groups.Count != 0) groups.RemoveAt(groups.Count - 1);
                    else
                    {
                        this.Diagnostic(DxfRawHandleDiagnosticKind.InvalidControlGroup, record, i, null, "Unmatched or invalid group-102 control string.");
                        uncertain = true;
                    }
                    continue;
                }
                if (!embedded && groups.Count == 0)
                {
                    if (!payload && tag.Code == 100)
                    {
                        subclass = (string)tag.RawValue;
                        if (xrecord && Eq(subclass, "AcDbXrecord")) payload = true;
                    }
                    if (tag.Code == 1001) application = (string)tag.RawValue;
                    else if (tag.Code < 1000) application = null;
                }
                if (tag.ValueType != DxfTagValueType.Handle) continue;
                DxfRawHandleRole role = DxfRawHandleRole.Opaque;
                string context = embedded ? "Embedded Object" : groups.Count != 0 ? groups[groups.Count - 1] : application;
                if (!uncertain && !embedded)
                {
                    if (groups.Count != 0)
                    {
                        if (database && groups.Count == 1 && Eq(groups[0], "ACAD_REACTORS") && tag.Code == 330)
                            role = DxfRawHandleRole.Reactor;
                        else if (database && groups.Count == 1 && Eq(groups[0], "ACAD_XDICTIONARY") && tag.Code == 360)
                            role = DxfRawHandleRole.ExtensionDictionary;
                    }
                    else if (header)
                    {
                        if (Eq(record.Name, "$HANDSEED") && tag.Code == 5) role = DxfRawHandleRole.HeaderSeed;
                        else if (tag.HandleKind == DxfHandleKind.Arbitrary) role = DxfRawHandleRole.Arbitrary;
                        else if (RoleForCode(tag.HandleKind) != DxfRawHandleRole.Opaque) role = DxfRawHandleRole.HeaderReference;
                    }
                    else if (database)
                    {
                        if (application != null) role = tag.Code == 1005 ? DxfRawHandleRole.XData : DxfRawHandleRole.Opaque;
                        else if (!payload)
                        {
                            if (subclass == null && tag.Code == (dimstyle ? 105 : 5)) role = DxfRawHandleRole.Identity;
                            else if (subclass == null && tag.Code == 330) role = DxfRawHandleRole.Owner;
                            else role = RoleForCode(tag.HandleKind);
                        }
                    }
                }
                this.Add(record, i, tag, role, context, subclass);
            }
            if (groups.Count != 0)
                this.Diagnostic(DxfRawHandleDiagnosticKind.InvalidControlGroup, record, record.EndTagIndex - 1, null, "Unterminated group-102 control block.");
        }
        private static DxfRawHandleRole RoleForCode(DxfHandleKind kind)
        {
            switch (kind)
            {
                case DxfHandleKind.SoftPointer: return DxfRawHandleRole.SoftPointer;
                case DxfHandleKind.HardPointer: return DxfRawHandleRole.HardPointer;
                case DxfHandleKind.SoftOwner: return DxfRawHandleRole.SoftOwner;
                case DxfHandleKind.HardOwner: return DxfRawHandleRole.HardOwner;
                case DxfHandleKind.Arbitrary: return DxfRawHandleRole.Arbitrary;
                default: return DxfRawHandleRole.Opaque;
            }
        }
        private void Add(DxfRawRecord record, int i, DxfTag tag, DxfRawHandleRole role, string context, string subclass)
        {
            if (this.occurrences.Count >= this.options.MaximumOccurrences) throw new InvalidDataException("Raw handle occurrence budget exceeded.");
            DxfRawHandleOccurrence item = new DxfRawHandleOccurrence(record, i, tag, role, context, subclass);
            this.occurrences.Add(item);
            if (record != null)
            {
                List<DxfRawHandleOccurrence> recordItems;
                if (!this.byRecord.TryGetValue(record, out recordItems)) this.byRecord.Add(record, recordItems = new List<DxfRawHandleOccurrence>());
                recordItems.Add(item);
            }
            Dictionary<ulong, List<DxfRawHandleOccurrence>> table = role == DxfRawHandleRole.Identity ? this.identities : item.IsReference ? this.incoming : null;
            if (table != null)
            {
                List<DxfRawHandleOccurrence> items;
                if (!table.TryGetValue(item.NumericHandle, out items)) table.Add(item.NumericHandle, items = new List<DxfRawHandleOccurrence>());
                items.Add(item);
            }
        }
        private void Diagnostic(DxfRawHandleDiagnosticKind kind, DxfRawRecord record, int i, string handle, string message)
        {
            if (this.diagnostics.Count >= this.options.MaximumDiagnostics) throw new InvalidDataException("Raw handle diagnostic budget exceeded.");
            this.diagnostics.Add(new DxfRawHandleDiagnostic(kind, record, i, handle, message));
        }
        private void ResolveDiagnostics(CancellationToken token)
        {
            HashSet<DxfRawRecord> seenIdentity = new HashSet<DxfRawRecord>();
            HashSet<DxfRawRecord> seenOwner = new HashSet<DxfRawRecord>();
            foreach (DxfRawHandleOccurrence item in this.occurrences)
            {
                token.ThrowIfCancellationRequested();
                if (item.Role == DxfRawHandleRole.Identity)
                {
                    if (item.NumericHandle == 0) this.Diagnostic(DxfRawHandleDiagnosticKind.NullIdentity, item.Record, item.TagIndex, item.CanonicalHandle, "An object identity cannot be the null handle.");
                    if (this.identities[item.NumericHandle].Count > 1) this.Diagnostic(DxfRawHandleDiagnosticKind.DuplicateIdentity, item.Record, item.TagIndex, item.CanonicalHandle, "Duplicate numeric object identity.");
                    if (!seenIdentity.Add(item.Record)) this.Diagnostic(DxfRawHandleDiagnosticKind.MultipleIdentities, item.Record, item.TagIndex, item.CanonicalHandle, "More than one identity slot in the record.");
                }
                if (item.Role == DxfRawHandleRole.Owner && !seenOwner.Add(item.Record))
                    this.Diagnostic(DxfRawHandleDiagnosticKind.MultipleOwners, item.Record, item.TagIndex, item.CanonicalHandle, "More than one common owner slot in the record.");
                if (!item.IsReference || item.NumericHandle == 0) continue;
                List<DxfRawHandleOccurrence> matches;
                if (!this.identities.TryGetValue(item.NumericHandle, out matches))
                    this.Diagnostic(DxfRawHandleDiagnosticKind.UnresolvedReference, item.Record, item.TagIndex, item.CanonicalHandle, "Nonzero reference has no indexed identity.");
                else if (matches.Count != 1)
                    this.Diagnostic(DxfRawHandleDiagnosticKind.AmbiguousReference, item.Record, item.TagIndex, item.CanonicalHandle, "Reference has more than one indexed definition.");
            }
        }
        private void FindOwnerCycles(CancellationToken token)
        {
            // An iterative functional-graph traversal avoids stack overflow on deep block trees.
            Dictionary<DxfRawRecord, DxfRawHandleOccurrence> owners = new Dictionary<DxfRawRecord, DxfRawHandleOccurrence>();
            HashSet<DxfRawRecord> multiple = new HashSet<DxfRawRecord>();
            foreach (DxfRawHandleOccurrence item in this.occurrences)
                if (item.Role == DxfRawHandleRole.Owner)
                {
                    if (owners.ContainsKey(item.Record)) multiple.Add(item.Record);
                    else owners.Add(item.Record, item);
                }
            foreach (DxfRawRecord record in multiple) owners.Remove(record);
            HashSet<DxfRawRecord> finished = new HashSet<DxfRawRecord>();
            foreach (DxfRawHandleOccurrence seed in this.occurrences)
            {
                token.ThrowIfCancellationRequested();
                if (seed.Role != DxfRawHandleRole.Identity || seed.NumericHandle == 0 ||
                    this.identities[seed.NumericHandle].Count != 1 || finished.Contains(seed.Record)) continue;
                List<DxfRawRecord> path = new List<DxfRawRecord>();
                Dictionary<DxfRawRecord, int> current = new Dictionary<DxfRawRecord, int>();
                DxfRawRecord node = seed.Record;
                while (node != null && !finished.Contains(node))
                {
                    token.ThrowIfCancellationRequested();
                    int cycle;
                    if (current.TryGetValue(node, out cycle))
                    {
                        for (int j = cycle; j < path.Count; j++)
                        {
                            DxfRawHandleOccurrence owner = owners[path[j]];
                            this.Diagnostic(DxfRawHandleDiagnosticKind.OwnerCycle, path[j], owner.TagIndex, owner.CanonicalHandle, "Cycle among common owner links.");
                        }
                        break;
                    }
                    current.Add(node, path.Count); path.Add(node);
                    DxfRawHandleOccurrence next; List<DxfRawHandleOccurrence> definitions;
                    node = owners.TryGetValue(node, out next) && next.NumericHandle != 0 &&
                        this.identities.TryGetValue(next.NumericHandle, out definitions) && definitions.Count == 1
                        ? definitions[0].Record : null;
                }
                foreach (DxfRawRecord done in path) finished.Add(done);
            }
        }
    }
}
