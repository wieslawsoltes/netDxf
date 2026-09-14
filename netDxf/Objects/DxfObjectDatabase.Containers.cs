using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Copies a registered owner's complete extension dictionary to another registered owner.</summary>
        /// <remarks>The destination must have no extension dictionary. The owner mapping is supplied automatically; other external references need explicit mappings across documents.</remarks>
        public DxfDictionary CloneExtensionDictionary(DxfObject sourceOwner, DxfObject destinationOwner,
            IReadOnlyDictionary<DxfObject, DxfObject> externalReferences = null)
        {
            if (sourceOwner == null) throw new ArgumentNullException(nameof(sourceOwner));
            if (destinationOwner == null) throw new ArgumentNullException(nameof(destinationOwner));
            DxfDictionary source = sourceOwner.ExtensionDictionary;
            if (source == null || source.Database == null) throw new ArgumentException("The source has no registered extension dictionary.", nameof(sourceOwner));
            this.CheckRegistered(destinationOwner);
            if (destinationOwner == this.Document.Layers || destinationOwner.ExtensionDictionary != null) throw new InvalidOperationException("The destination extension-dictionary slot is occupied or reserved.");
            Dictionary<DxfObject, DxfObject> mappings = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            if (externalReferences != null) foreach (KeyValuePair<DxfObject, DxfObject> pair in externalReferences) mappings.Add(pair.Key, pair.Value);
            if (mappings.TryGetValue(sourceOwner, out DxfObject supplied) && supplied != destinationOwner) throw new ArgumentException("The source owner mapping conflicts with the destination owner.", nameof(externalReferences));
            mappings[sourceOwner] = destinationOwner;
            return this.CloneDictionaryGraph(source, destinationOwner, null, true, mappings);
        }
        /// <summary>Creates an ACAD_SORTENTS table for a block record, optionally enabling the HEADER regeneration sorting bit.</summary>
        /// <remarks>An existing table is not replaced. Use its Entries collection to edit it. A failed input sequence leaves the document unchanged.</remarks>
        public DxfSortentsTable CreateSortentsTable(BlockRecord blockRecord, IEnumerable<DxfSortOrderEntry> entries, bool enableRegeneration = true)
        {
            if (blockRecord == null) throw new ArgumentNullException(nameof(blockRecord));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            this.CheckRegistered(blockRecord);
            if (this.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2004) throw new NotSupportedException("SORTENTSTABLE authoring requires AutoCAD 2004 or later.");
            DxfSortentsTable table = new DxfSortentsTable(blockRecord);
            foreach (DxfSortOrderEntry entry in entries) { if (entry != null) this.CheckRegistered(entry.Entity); table.Entries.Add(entry); }
            DxfDictionary extension = blockRecord.ExtensionDictionary;
            if (extension != null && extension.Contains("ACAD_SORTENTS")) throw new InvalidOperationException("The block already has an ACAD_SORTENTS entry.");
            HeaderVariable sorting = null;
            short flags = 0;
            if (enableRegeneration && this.Document.DrawingVariables.TryGetCustomVariable("$SORTENTS", out sorting))
            {
                if (sorting.GroupCode != 280 || !(sorting.Value is short value) || value < 0 || value > 255) throw new InvalidOperationException("The existing SORTENTS header variable is malformed.");
                flags = value;
            }
            if (extension == null)
            {
                extension = new DxfDictionary(); extension.Add("ACAD_SORTENTS", table);
                this.SetExtensionDictionary(blockRecord, extension);
            }
            else extension.Add("ACAD_SORTENTS", table);
            if (enableRegeneration)
            {
                if (sorting == null) this.Document.DrawingVariables.AddCustomVariable(new HeaderVariable("$SORTENTS", 280, (short)(flags | 16)));
                else sorting.Value = (short)(flags | 16);
            }
            return table;
        }
        /// <summary>Attaches a public-schema clipping filter under INSERT/ACAD_FILTER/SPATIAL.</summary>
        /// <remarks>An existing SPATIAL entry is not replaced. Geometry is not clipped by this operation.</remarks>
        public void SetSpatialFilter(Insert insert, DxfSpatialFilter filter)
        {
            if (insert == null) throw new ArgumentNullException(nameof(insert));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            this.CheckRegistered(insert);
            if (filter.Database != null || filter.Owner != null) throw new ArgumentException("The filter must be detached and unowned.", nameof(filter));
            DxfDictionary extension = insert.ExtensionDictionary;
            DxfDictionary filters = null;
            if (extension != null && extension.Contains("ACAD_FILTER"))
            {
                filters = extension["ACAD_FILTER"] as DxfDictionary ?? throw new InvalidOperationException("ACAD_FILTER is not a dictionary.");
                if (filters.Contains("SPATIAL")) throw new InvalidOperationException("The INSERT already has a SPATIAL filter.");
            }
            bool createdFilters = filters == null;
            if (createdFilters) filters = new DxfDictionary();
            bool addedOwnerReactor = !filter.PersistentReactors.Contains(filters);
            try
            {
                if (addedOwnerReactor) filter.PersistentReactors.Add(filters);
                filters.Add("SPATIAL", filter);
                if (createdFilters)
                {
                    if (extension == null)
                    {
                        extension = new DxfDictionary(); extension.Add("ACAD_FILTER", filters);
                        this.SetExtensionDictionary(insert, extension);
                    }
                    else extension.Add("ACAD_FILTER", filters);
                }
            }
            catch
            {
                // Failed preflight must leave the supplied filter reusable after correction.
                if (filter.Database == null)
                {
                    filter.Owner = null;
                    if (addedOwnerReactor) filter.PersistentReactors.Remove(filters);
                    if (createdFilters && filters.Database == null) filters.Remove("SPATIAL");
                }
                throw;
            }
        }
    }
}
