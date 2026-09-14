using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.Objects
{
    /// <summary>An IDBUFFER containing an ordered sequence of soft object references.</summary>
    /// <remarks>Duplicates and null (group-330 handle zero) entries are retained.</remarks>
    public sealed class DxfIdBuffer : DxfDatabaseObject
    {
        private readonly ReferenceCollection references;
        /// <summary>Creates an empty detached IDBUFFER.</summary>
        public DxfIdBuffer() : base("IDBUFFER") { this.references = new ReferenceCollection(this); }
        /// <summary>Gets the editable reference sequence. Null represents the DXF null handle.</summary>
        public Collection<DxfObject> References { get { return this.references; } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.references; } }
        internal override DxfDatabaseObject CloneShell() { return new DxfIdBuffer(); }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        { foreach (DxfObject target in this.references) ((DxfIdBuffer)clone).References.Add(resolve(target)); }
        private sealed class ReferenceCollection : Collection<DxfObject>
        {
            private readonly DxfIdBuffer owner;
            internal ReferenceCollection(DxfIdBuffer owner) { this.owner = owner; }
            private void Check(DxfObject target)
            {
                if (target is DxfDocument) throw new ArgumentException("Use null for an IDBUFFER null handle; a document is not an object reference.", nameof(target));
                if (target != null && this.owner.Database != null) this.owner.Database.CheckRegistered(target);
            }
            protected override void InsertItem(int index, DxfObject item) { this.Check(item); base.InsertItem(index, item); }
            protected override void SetItem(int index, DxfObject item) { this.Check(item); base.SetItem(index, item); }
        }
    }
}
