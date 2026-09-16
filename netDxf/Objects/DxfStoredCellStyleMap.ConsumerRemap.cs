// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredCellStyleMap
    {
        /// <summary>Atomically replaces this TABLESTYLE-owned map and remaps qualified public TABLECONTENT style-ID consumers.</summary>
        /// <returns>The number of changed column, row and cell identifier occurrences.</returns>
        /// <remarks>
        /// Mappings are simultaneous. Missing source keys retain their identifiers; removed referenced
        /// identifiers require an explicit surviving target or zero to select the default/unselected slot.
        /// Nonzero old/new map identifiers must be positive and unique. All definitions and mappings are
        /// enumerated before validation. Unqualified or opaque TABLE consumers reject before any publication.
        /// This synchronizes public identifiers, not private schemas, formatting overrides, values or geometry.
        /// No handles are allocated. Independent callback changes and concurrent-thread mutations are not rolled back.
        /// The existing ReplaceStructure method remains the explicit stored-only operation.
        /// </remarks>
        public int ReplaceStructureAndRemapConsumers(IEnumerable<DxfCellStyleMapEntryDefinition> entries,
            IEnumerable<KeyValuePair<int, int>> identifierRemapping)
        {
            if (this.editing) { this.reentered = true; throw new InvalidOperationException("CELLSTYLEMAP replacement cannot be reentered."); }
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (identifierRemapping == null) throw new ArgumentNullException(nameof(identifierRemapping));
            this.editing = true; this.reentered = false;
            try
            {
                var definitions = MaterializeDefinitions(entries);
                var mapping = new Dictionary<int, int>();
                foreach (var pair in identifierRemapping)
                {
                    if (mapping.Count >= this.Entries.Count || pair.Key <= 0 || pair.Value < 0 || mapping.ContainsKey(pair.Key))
                        throw new ArgumentException("Remapping requires distinct positive source IDs and nonnegative targets within the source inventory.", nameof(identifierRemapping));
                    mapping.Add(pair.Key, pair.Value);
                }
                if (this.reentered) throw new InvalidOperationException("Reentry invalidated the coordinated map transaction.");
                this.ValidateNameReplacementSource();
                var owner = this.Owner as DxfDictionary;
                var style = owner?.Owner as DxfTableStyle;
                const string slot = "ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP";
                if (style == null || !ReferenceEquals(style.ExtensionDictionary, owner) || !ReferenceEquals(style.StoredCellStyleMap, this) ||
                    !owner.Contains(slot) || !ReferenceEquals(owner[slot], this))
                    throw new NotSupportedException("Consumer remapping requires the actual TABLESTYLE-owned CELLSTYLEMAP slot.");
                if (this.Entries.Any(entry => entry.Format == null))
                    throw new NotSupportedException("Unqualified map formatting cannot be discarded during consumer remapping.");
                var oldIds = new HashSet<int>(); var newIds = new HashSet<int>();
                foreach (var entry in this.Entries)
                    if (entry.Id <= 0 || !oldIds.Add(entry.Id)) throw new NotSupportedException("Source map identifiers must be positive and unique for consumer remapping.");
                foreach (var entry in definitions)
                    if (entry.Id <= 0 || !newIds.Add(entry.Id)) throw new ArgumentException("Destination map identifiers must be positive and unique.", nameof(entries));
                foreach (var pair in mapping)
                    if (!oldIds.Contains(pair.Key) || pair.Value != 0 && !newIds.Contains(pair.Value))
                        throw new ArgumentException("Every mapping must identify a source entry and an existing destination entry or zero.", nameof(identifierRemapping));
                var objects = this.Database.Items;
                if (objects.Any(item => item.CodeName == "TABLECONTENT" && !(item is DxfStoredTableContent)))
                    throw new NotSupportedException("Opaque TABLECONTENT prevents complete public consumer discovery.");
                var graphical = this.source.Blocks.SelectMany(block => block.Entities).ToArray();
                if (graphical.Any(entity => (entity.CodeName == "ACAD_TABLE" || entity.CodeName == "TABLE") && !(entity is StoredTable)))
                    throw new NotSupportedException("Opaque TABLE entities prevent complete public consumer discovery.");
                foreach (var table in graphical.OfType<StoredTable>().Where(table => table.References.Contains(style)))
                {
                    table.Validate(this.source);
                    var subclasses = table.Payload.Where(tag => tag.Code == 100).Select(tag => (string)tag.Value).ToArray();
                    if (!subclasses.SequenceEqual(new[] { "AcDbBlockReference", "AcDbTable" }))
                        throw new NotSupportedException("Inline or private TABLE schemas require their own qualified style-ID remapper.");
                }
                var candidate = BuildDefinition(this.source, this.Owner, definitions);
                this.ValidateStructureBudget(candidate.Payload.Count);
                var contents = objects.OfType<DxfStoredTableContent>().Where(content => ReferenceEquals(content.TableStyle, style)).ToArray();
                var staged = new List<Tuple<DxfStoredTableContent, DxfStoredTableContent>>();
                int changed = 0;
                foreach (var content in contents)
                {
                    var next = content.PrepareStyleReferenceRemap(oldIds, newIds, mapping, out int count);
                    changed = checked(changed + count);
                    if (next != null) staged.Add(Tuple.Create(content, next));
                }
                bool mapChanged = !SameDefinitionPacket(this.Payload, candidate.Payload);
                // All callbacks, graph checks, allocation, parsing and count checks have completed.
                // These nonthrowing state assignments publish one single-threaded transaction.
                if (mapChanged)
                {
                    this.Payload = candidate.Payload; this.Entries = candidate.Entries;
                    this.references = candidate.references; this.handles = candidate.handles;
                }
                foreach (var pair in staged) pair.Item1.PublishStyleReferenceRemap(pair.Item2);
                return changed;
            }
            finally { this.editing = false; this.reentered = false; }
        }
    }
}
