using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;

namespace netDxf.Objects
{
    /// <summary>The stored AcDbDataCell type numbers used by DATATABLE columns.</summary>
    public enum DxfDataCellType
    {
        /// <summary>A signed 32-bit value in group 93.</summary>
        Integer = 1,
        /// <summary>A finite double in group 40.</summary>
        Double = 2,
        /// <summary>A Unicode string in group 3.</summary>
        String = 3,
        /// <summary>Three raw coordinates in groups 10, 20 and 30.</summary>
        Point = 4,
        /// <summary>An object reference in group 331.</summary>
        ObjectId = 5,
        /// <summary>A hard-owned database object in group 360.</summary>
        HardOwner = 6,
        /// <summary>A soft-owned database object in group 350.</summary>
        SoftOwner = 7,
        /// <summary>A hard reference in group 340.</summary>
        HardPointer = 8,
        /// <summary>A soft reference in group 330.</summary>
        SoftPointer = 9,
        /// <summary>A Boolean in group 71.</summary>
        Boolean = 10,
        /// <summary>Three raw coordinates in groups 11, 21 and 31.</summary>
        Vector = 11
    }

    /// <summary>An immutable homogeneous DATATABLE column snapshot.</summary>
    public sealed class DxfDataColumn
    {
        private readonly IReadOnlyList<object> values;
        /// <summary>Snapshots a column. Values must exactly match the selected stored type; references may be null.</summary>
        public DxfDataColumn(DxfDataCellType type, string name, IEnumerable<object> values)
        {
            if (type < DxfDataCellType.Integer || type > DxfDataCellType.Vector) throw new ArgumentOutOfRangeException(nameof(type));
            CheckText(name);
            if (values == null) throw new ArgumentNullException(nameof(values));
            var snapshot = new List<object>();
            foreach (object value in values)
            {
                if (snapshot.Count == DxfDataTable.MaximumCells) throw new ArgumentException("The DATATABLE cell admission limit was exceeded.", nameof(values));
                CheckValue(type, value); snapshot.Add(value);
            }
            this.Type = type; this.Name = name; this.values = snapshot.AsReadOnly();
        }
        /// <summary>Gets the stored column type.</summary>
        public DxfDataCellType Type { get; }
        /// <summary>Gets the stored column name; empty and duplicate names are allowed.</summary>
        public string Name { get; }
        /// <summary>Gets immutable values, preserving object reference identities.</summary>
        public IReadOnlyList<object> Values { get { return this.values; } }
        internal bool OwnsObjects { get { return this.Type == DxfDataCellType.HardOwner || this.Type == DxfDataCellType.SoftOwner; } }
        internal bool HasReferences { get { return this.Type >= DxfDataCellType.ObjectId && this.Type <= DxfDataCellType.SoftPointer; } }
        internal static void CheckText(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException("DATATABLE text must be a single line without NUL.");
            for (int i = 0; i < value.Length; i++)
                if (char.IsHighSurrogate(value[i]))
                { if (++i == value.Length || !char.IsLowSurrogate(value[i])) throw new ArgumentException("DATATABLE text contains an unpaired surrogate."); }
                else if (char.IsLowSurrogate(value[i])) throw new ArgumentException("DATATABLE text contains an unpaired surrogate.");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void CheckValue(DxfDataCellType type, object value)
        {
            bool valid;
            switch (type)
            {
                case DxfDataCellType.Integer: valid = value is int; break;
                case DxfDataCellType.Double: valid = value is double number && Finite(number); break;
                case DxfDataCellType.String: CheckText(value as string); return;
                case DxfDataCellType.Point: case DxfDataCellType.Vector:
                    valid = value is Vector3 point && Finite(point.X) && Finite(point.Y) && Finite(point.Z); break;
                case DxfDataCellType.Boolean: valid = value is bool; break;
                case DxfDataCellType.HardOwner: case DxfDataCellType.SoftOwner: valid = value == null || value is DxfDatabaseObject; break;
                default: valid = value == null || value is DxfObject; break;
            }
            if (!valid) throw new ArgumentException("The DATATABLE value does not match its stored column type.", nameof(value));
            if (value is DxfDatabaseObject obj && obj.IsErased) throw new InvalidOperationException("An erased object cannot be referenced by a new DATATABLE column.");
        }
    }

    /// <summary>A rectangular, stored-version-2 DATATABLE object for R2004 and later.</summary>
    /// <remarks>No table layout, formula evaluation or coordinate transformation is performed.</remarks>
    public sealed class DxfDataTable : DxfDatabaseObject
    {
        /// <summary>The maximum admitted cell count, used to bound untrusted input allocation.</summary>
        public const int MaximumCells = 1048576;
        private IReadOnlyList<DxfDataColumn> columns = new List<DxfDataColumn>().AsReadOnly();
        private string name = string.Empty;
        /// <summary>Creates an empty detached table.</summary>
        public DxfDataTable() : base("DATATABLE") { }
        /// <summary>Gets the qualified stored version.</summary>
        public short StoredVersion { get { return 2; } }
        /// <summary>Gets or sets the exact stored table name, including an empty name.</summary>
        public string Name { get { return this.name; } set { DxfDataColumn.CheckText(value); this.name = value; } }
        /// <summary>Gets the number of rows, including rows in a table with zero columns.</summary>
        public int RowCount { get; private set; }
        /// <summary>Gets immutable, ordered columns.</summary>
        public IReadOnlyList<DxfDataColumn> Columns { get { return this.columns; } }
        /// <summary>Atomically replaces a rectangular column snapshot and its row count.</summary>
        /// <remarks>Detached tables adopt owner cells. A registered table can edit values and reorder owner slots while retaining the same owned-object set; changing that set requires constructing a detached replacement graph.</remarks>
        public void SetColumns(int rowCount, IEnumerable<DxfDataColumn> values)
        { this.SetColumnsCore(rowCount, values, false); }
        internal void SetLoadedColumns(int rowCount, IEnumerable<DxfDataColumn> values)
        { this.SetColumnsCore(rowCount, values, true); }
        private void SetColumnsCore(int rowCount, IEnumerable<DxfDataColumn> values, bool imported)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var snapshot = new List<DxfDataColumn>();
            foreach (DxfDataColumn column in values)
            {
                if (snapshot.Count == MaximumCells) throw new ArgumentException("The DATATABLE column admission limit was exceeded.");
                if (column == null) throw new ArgumentException("A DATATABLE column cannot be null.");
                snapshot.Add(column);
            }
            // Caller enumerators may mutate this object. Validate current state only after the last callback.
            if (this.IsErased) throw new InvalidOperationException("An erased DATATABLE cannot be edited.");
            if (rowCount < 0 || rowCount > MaximumCells || (long)rowCount * snapshot.Count > MaximumCells) throw new ArgumentOutOfRangeException(nameof(rowCount));
            if (snapshot.Any(column => column.Values.Count != rowCount)) throw new ArgumentException("Every DATATABLE column must contain exactly RowCount values.");
            var owned = new HashSet<DxfDatabaseObject>();
            foreach (DxfDataColumn column in snapshot)
                foreach (object cell in column.Values)
                {
                    if (!(cell is DxfObject reference)) continue;
                    if (reference is DxfDatabaseObject erased && erased.IsErased) throw new InvalidOperationException("An erased object cannot be referenced.");
                    if (column.OwnsObjects)
                    {
                        var child = (DxfDatabaseObject)reference;
                        if (!owned.Add(child)) throw new ArgumentException("A DATATABLE object cannot occupy more than one ownership slot.");
                        if (child.Owner != null && !ReferenceEquals(child.Owner, this)) throw new ArgumentException("A DATATABLE ownership target already has another owner.");
                        if (DxfObjectDatabase.IsAncestor(child, this)) throw new ArgumentException("DATATABLE ownership cannot form a cycle.");
                        if (child.Database != this.Database) throw new ArgumentException("Owned cells must share the table's registration state and database.");
                        if (imported && !ReferenceEquals(child.Owner, this)) throw new ArgumentException("A loaded DATATABLE ownership slot must be reciprocal.");
                    }
                    if (this.Database != null) this.Database.CheckRegistered(reference);
                }
            var previous = new HashSet<DxfDatabaseObject>(this.DeclaredOwnedObjects);
            if (this.Database != null && !imported && !previous.SetEquals(owned)) throw new InvalidOperationException("Changing the owned-object set of a registered DATATABLE requires a detached replacement graph.");
            foreach (DxfDatabaseObject child in previous) if (!owned.Contains(child)) child.Owner = null;
            foreach (DxfDatabaseObject child in owned) child.Owner = this;
            this.columns = snapshot.AsReadOnly(); this.RowCount = rowCount;
        }
        internal override IEnumerable<DxfDatabaseObject> DeclaredOwnedObjects
        { get { return this.columns.Where(column => column.OwnsObjects).SelectMany(column => column.Values).OfType<DxfDatabaseObject>(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences
        { get { return this.columns.Where(column => column.HasReferences).SelectMany(column => column.Values).OfType<DxfObject>(); } }
        internal override DxfDatabaseObject CloneShell() { return new DxfDataTable { Name = this.Name }; }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        {
            ((DxfDataTable)clone).SetColumns(this.RowCount, this.columns.Select(column => new DxfDataColumn(column.Type, column.Name,
                column.Values.Select(value => value is DxfObject reference ? resolve(reference) : value))));
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2004) errors.Add("Typed DATATABLE requires R2004 or later.");
            if (this.RowCount < 0 || this.columns.Any(column => column.Values.Count != this.RowCount)) errors.Add("DATATABLE is not rectangular.");
        }
    }
}
