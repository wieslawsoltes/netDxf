using System;
using netDxf.Collections;

namespace netDxf.Tables
{
    /// <summary>Published UCS symbol-table flags (70); unknown bits remain representable.</summary>
    [Flags]
    public enum UcsFlags : short
    {
        /// <summary>No flags.</summary>
        None = 0,
        /// <summary>Externally dependent on an xref.</summary>
        ExternallyDependent = 16,
        /// <summary>The dependent xref was resolved.</summary>
        XrefResolved = 32,
        /// <summary>The entry was referenced when last edited.</summary>
        Referenced = 64
    }

    public partial class UCS
    {
        /// <summary>Commits collection indexes only after all rename observers accept the name.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A UCS name cannot be blank.", nameof(newName));
            if (this.Owner != null) this.Owner.ValidateRecordRename(this, newName);
            base.OnNameChangedEvent(oldName, newName);
            if (this.Owner != null)
            {
                this.Owner.ValidateRecordRename(this, newName);
                this.Owner.CommitRecordRename(this, newName);
            }
        }
        /// <summary>Gets or sets the UCS symbol-table flags (70).</summary>
        public UcsFlags Flags { get; set; }
    }

    public partial class VPort
    {
        private UCS namedUcs, baseUcs;
        /// <summary>Gets or sets the named UCS reference (345). Null denotes an unnamed UCS.</summary>
        public UCS NamedUcs { get { return this.namedUcs; } set { UcsReferences.Replace(this, this.namedUcs, value); this.namedUcs = value; } }
        /// <summary>Gets or sets the orthographic base UCS reference (346). Null uses WORLD for a nonzero orthographic type.</summary>
        public UCS BaseUcs { get { return this.baseUcs; } set { UcsReferences.Replace(this, this.baseUcs, value); this.baseUcs = value; } }
    }
}
