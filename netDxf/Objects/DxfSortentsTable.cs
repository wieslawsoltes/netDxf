using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>An immutable redraw association between a graphical object and an opaque sort key.</summary>
    public sealed class DxfSortOrderEntry
    {
        /// <summary>Creates a redraw association. Keys need not be unique; zero is a valid key drawn last by CAD applications.</summary>
        public DxfSortOrderEntry(EntityObject entity, string sortHandle)
            : this((DxfObject)entity, sortHandle)
        {
        }
        /// <summary>Creates an association for a graphical entity or an attribute definition.</summary>
        /// <remarks>Attribute definitions are graphical DXF records but are modeled separately from EntityObject.</remarks>
        public DxfSortOrderEntry(DxfObject target, string sortHandle)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (!(target is EntityObject) && !(target is AttributeDefinition))
                throw new ArgumentException("The redraw target must be a graphical entity or attribute definition.", nameof(target));
            this.Target = target;
            new DxfTag(5, sortHandle);
            this.SortHandle = sortHandle;
        }
        /// <summary>Gets the graphical object being ordered, including attribute definitions.</summary>
        public DxfObject Target { get; }
        /// <summary>Gets the graphical entity, or null when Target is an attribute definition.</summary>
        public EntityObject Entity { get { return this.Target as EntityObject; } }
        /// <summary>Gets the literal hexadecimal sort key. It is not an object identity or reference.</summary>
        public string SortHandle { get; }
        internal BlockRecord BlockRecord
        {
            get { return this.Target is AttributeDefinition definition ? definition.Owner?.Record : this.Entity.Owner?.Record; }
        }
    }
    /// <summary>A SORTENTSTABLE attached as ACAD_SORTENTS to a block record's extension dictionary.</summary>
    /// <remarks>Authoring and typed export use the conservative AutoCAD 2004-or-later profile.</remarks>
    public sealed class DxfSortentsTable : DxfDatabaseObject
    {
        private readonly OrderCollection entries;
        /// <summary>Creates an empty table for a registered block record.</summary>
        public DxfSortentsTable(BlockRecord blockRecord) : this()
        { this.BlockRecord = blockRecord ?? throw new ArgumentNullException(nameof(blockRecord)); }
        internal DxfSortentsTable() : base("SORTENTSTABLE") { this.entries = new OrderCollection(this); }
        /// <summary>Gets the block record whose entities are ordered.</summary>
        public BlockRecord BlockRecord { get; internal set; }
        /// <summary>Gets editable associations in their exact stored order; entities are unique but sort keys may repeat.</summary>
        public Collection<DxfSortOrderEntry> Entries { get { return this.entries; } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        {
            get { yield return this.BlockRecord; foreach (DxfSortOrderEntry entry in this.entries) yield return entry.Target; }
        }
        internal override DxfDatabaseObject CloneShell() { return new DxfSortentsTable(); }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        {
            DxfSortentsTable table = (DxfSortentsTable)clone;
            table.BlockRecord = resolve(this.BlockRecord) as BlockRecord ?? throw new InvalidOperationException("A redraw block must map to a block record.");
            foreach (DxfSortOrderEntry entry in this.entries)
            {
                DxfObject target = resolve(entry.Target);
                if (!(target is EntityObject) && !(target is AttributeDefinition))
                    throw new InvalidOperationException("A redraw target must map to a graphical entity or attribute definition.");
                table.Entries.Add(new DxfSortOrderEntry(target, entry.SortHandle));
            }
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2004) errors.Add("SORTENTSTABLE export requires AutoCAD 2004 or later.");
            if (this.BlockRecord == null) errors.Add("SORTENTSTABLE has no block record.");
            if (!(this.Owner is DxfDictionary dictionary) || dictionary.Owner != this.BlockRecord || dictionary.Database != null && this.BlockRecord?.ExtensionDictionary != dictionary || !dictionary.Entries.Any(e => e.Name.Equals("ACAD_SORTENTS", StringComparison.OrdinalIgnoreCase) && e.Target == this))
                errors.Add("SORTENTSTABLE must occupy ACAD_SORTENTS in its block record's extension dictionary.");
            HashSet<DxfObject> seen = new HashSet<DxfObject>();
            foreach (DxfSortOrderEntry entry in this.entries)
            {
                if (!seen.Add(entry.Target)) errors.Add("Duplicate SORTENTSTABLE entity.");
                if (entry.BlockRecord != this.BlockRecord) errors.Add("A SORTENTSTABLE entity belongs to another block.");
            }
        }
        private sealed class OrderCollection : Collection<DxfSortOrderEntry>
        {
            private readonly DxfSortentsTable owner;
            private readonly HashSet<DxfObject> entities = new HashSet<DxfObject>();
            internal OrderCollection(DxfSortentsTable owner) { this.owner = owner; }
            private void Check(int index, DxfSortOrderEntry item, bool replacement)
            {
                if (item == null) throw new ArgumentNullException(nameof(item));
                if (this.entities.Contains(item.Target) && (!replacement || this[index].Target != item.Target)) throw new ArgumentException("Each redraw entity occurs at most once.", nameof(item));
                if (this.owner.BlockRecord != null && item.BlockRecord != this.owner.BlockRecord) throw new ArgumentException("The entity must belong to the table's block.", nameof(item));
                if (this.owner.Database != null) this.owner.Database.CheckRegistered(item.Target);
            }
            protected override void InsertItem(int index, DxfSortOrderEntry item)
            {
                if (index < 0 || index > this.Count) throw new ArgumentOutOfRangeException(nameof(index));
                this.Check(index, item, false);
                base.InsertItem(index, item);
                this.entities.Add(item.Target);
            }
            protected override void SetItem(int index, DxfSortOrderEntry item)
            {
                if (index < 0 || index >= this.Count) throw new ArgumentOutOfRangeException(nameof(index));
                this.Check(index, item, true);
                DxfObject previous = this[index].Target;
                base.SetItem(index, item);
                this.entities.Remove(previous); this.entities.Add(item.Target);
            }
            protected override void RemoveItem(int index)
            {
                DxfObject previous = this[index].Target;
                base.RemoveItem(index); this.entities.Remove(previous);
            }
            protected override void ClearItems() { base.ClearItems(); this.entities.Clear(); }
        }
    }
}
