using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfXRecord
    {
        internal const string TableRoundtripMarker = "ACAD_ROUNDTRIP_2008_TABLE_ENTITY";
        private DxfDatabaseObject tableContent;
        private DxfDatabaseObject tableGeometry;
        private int tableContentSlot = -1;
        private int tableGeometrySlot = -1;

        /// <summary>Gets whether a typed ownership schema controls this record's payload.</summary>
        /// <remarks>Schema-managed payloads expose their stored tags for inspection and reject generic Data edits.</remarks>
        public bool IsSchemaManaged { get { return this.tableContent != null; } }

        internal void CheckPayloadEditable()
        {
            if (this.IsSchemaManaged) throw new InvalidOperationException("This XRECORD payload is controlled by its typed ownership schema.");
        }

        internal bool IsTableRoundtripRecord
        {
            get { return this.Data.Count > 0 && this.Data[0].Code == 102 && (string)this.Data[0].Value == TableRoundtripMarker; }
        }

        // This is deliberately an exact internal schema binding, not a general owner-assignment API.
        // Both slots are validated before either child gains an owner or the payload becomes managed.
        internal void BindTableRoundtripChildren(DxfDatabaseObject content, DxfDatabaseObject geometry)
        {
            if (this.IsErased) throw new InvalidOperationException("An erased record cannot bind owned objects.");
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (this.IsSchemaManaged) throw new InvalidOperationException("The ownership schema is already bound.");
            if (!this.IsTableRoundtripRecord) throw new ArgumentException("The TABLE roundtrip marker is missing.");
            if (content.CodeName != "TABLECONTENT" || geometry.CodeName != "TABLEGEOMETRY")
                throw new ArgumentException("The TABLE roundtrip slots require TABLECONTENT and TABLEGEOMETRY targets.");
            int contentSlot = -1, geometrySlot = -1;
            for (int i = 1; i < this.Data.Count; i++)
            {
                DxfTag tag = this.Data[i];
                if (tag.Code == 360 && contentSlot < 0) contentSlot = i;
                else if (tag.Code == 361 && geometrySlot < 0) geometrySlot = i;
                else if (tag.Code == 102 || tag.HandleKind == DxfHandleKind.HardOwner || tag.HandleKind == DxfHandleKind.SoftOwner)
                    throw new ArgumentException("The TABLE roundtrip ownership envelope contains an unexpected or duplicate slot.");
            }
            if (contentSlot < 0 || geometrySlot < 0) throw new ArgumentException("Both TABLE roundtrip ownership slots are required.");
            this.CheckOwnedCandidate(content, this.Data[contentSlot]);
            this.CheckOwnedCandidate(geometry, this.Data[geometrySlot]);
            this.tableContent = content;
            this.tableGeometry = geometry;
            this.tableContentSlot = contentSlot;
            this.tableGeometrySlot = geometrySlot;
            content.Owner = this;
            geometry.Owner = this;
        }

        private void CheckOwnedCandidate(DxfDatabaseObject child, DxfTag slot)
        {
            if (child.IsErased) throw new InvalidOperationException("An erased object cannot be attached again.");
            if (child.Owner != null && !ReferenceEquals(child.Owner, this)) throw new ArgumentException("A schema child already has another owner.");
            if (DxfObjectDatabase.IsAncestor(child, this)) throw new ArgumentException("Declared ownership cannot form a cycle.");
            if (child.Database != this.Database) throw new ArgumentException("Schema children must share the record's registration state and database.");
            if (this.Database != null)
            {
                this.Database.CheckRegistered(this);
                this.Database.CheckRegistered(child);
                if (!ReferenceEquals(child.Owner, this)) throw new ArgumentException("An imported schema child must already declare this record as its owner.");
                if (!string.Equals((string)slot.Value, child.Handle, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The stored ownership handle does not identify the schema child.");
            }
        }

        internal override IEnumerable<DxfDatabaseObject> DeclaredOwnedObjects
        {
            get { if (this.IsSchemaManaged) { yield return this.tableContent; yield return this.tableGeometry; if (this.tableCellData != null) yield return this.tableCellData; } }
        }

        internal override IEnumerable<DxfObject> DatabaseReferences
        {
            get { foreach (DxfDatabaseObject child in this.DeclaredOwnedObjects) yield return child; }
        }

        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject target, Func<DxfObject, DxfObject> resolve)
        {
            if (this.tableCellData != null)
                ((DxfXRecord)target).BindCompositeTableRoundtripChildren((DxfDatabaseObject)resolve(this.tableContent), (DxfDatabaseObject)resolve(this.tableGeometry), (DxfDataTable)resolve(this.tableCellData));
            else if (this.IsSchemaManaged)
                ((DxfXRecord)target).BindTableRoundtripChildren((DxfDatabaseObject)resolve(this.tableContent), (DxfDatabaseObject)resolve(this.tableGeometry));
        }

        internal override void MaterializeOwnedObjectReferences()
        {
            if (!this.IsSchemaManaged) return;
            this.ReplaceLoadedData(this.tableContentSlot, new DxfTag(360, this.tableContent.Handle));
            this.ReplaceLoadedData(this.tableGeometrySlot, new DxfTag(361, this.tableGeometry.Handle));
            if (this.tableCellData != null) this.ReplaceLoadedData(14, new DxfTag(360, this.tableCellData.Handle));
        }

        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.IsSchemaManaged) return;
            this.ValidateCompositeTableOwnership(errors);
            if (!this.IsTableRoundtripRecord || this.tableContentSlot >= this.Data.Count || this.tableGeometrySlot >= this.Data.Count)
            { errors.Add("Invalid TABLE roundtrip ownership envelope: " + this.Handle); return; }
            if (this.Data[this.tableContentSlot].Code != 360 || this.Data[this.tableGeometrySlot].Code != 361)
                errors.Add("Invalid TABLE roundtrip ownership slot codes: " + this.Handle);
            if (this.tableContent.CodeName != "TABLECONTENT" || this.tableGeometry.CodeName != "TABLEGEOMETRY")
                errors.Add("Invalid TABLE roundtrip ownership target types: " + this.Handle);
            if (this.Database != null && (!string.Equals((string)this.Data[this.tableContentSlot].Value, this.tableContent.Handle, StringComparison.OrdinalIgnoreCase) || !string.Equals((string)this.Data[this.tableGeometrySlot].Value, this.tableGeometry.Handle, StringComparison.OrdinalIgnoreCase)))
                errors.Add("TABLE roundtrip ownership handle mismatch: " + this.Handle);
        }
    }

    public sealed partial class DxfObjectDatabase
    {
        private void ValidateDeclaredOwnership(DxfDatabaseObject parent, IEnumerable<DxfDatabaseObject> candidates, List<string> errors, bool registered)
        {
            List<DxfDatabaseObject> children = parent.DeclaredOwnedObjects.ToList();
            if (children.Count == 0) return;
            HashSet<DxfObject> seen = new HashSet<DxfObject>(ObjectIdentity);
            foreach (DxfDatabaseObject child in children)
            {
                if (child == null) { errors.Add("Null declared ownership slot: " + parent.Handle); continue; }
                if (!seen.Add(child)) errors.Add("Duplicate declared ownership target: " + parent.Handle);
                if (!ReferenceEquals(child.Owner, parent)) errors.Add("Declared ownership is not reciprocal: " + parent.Handle);
                if (IsAncestor(child, parent)) errors.Add("Declared ownership cycle: " + parent.Handle);
                if (registered && !this.IsRegistered(child)) errors.Add("Unregistered declared child: " + parent.Handle);
            }
            foreach (DxfDatabaseObject candidate in candidates)
                if (ReferenceEquals(candidate.Owner, parent) && candidate != parent.ExtensionDictionary && !seen.Contains(candidate))
                    errors.Add("An object is owned outside its parent's declared slots: " + candidate.Handle);
            if (!registered) parent.ValidateDatabaseSchema(this, errors);
        }
    }
}
