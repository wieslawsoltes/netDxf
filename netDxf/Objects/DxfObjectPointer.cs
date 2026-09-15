using System.Collections.Generic;
namespace netDxf.Objects
{
    /// <summary>The published OBJECT_PTR envelope, exposing common metadata and XData only.</summary>
    /// <remarks>No pointer field, subclass, ASE behavior, DC015 registration, or placement convention is inferred from this record's name.</remarks>
    public sealed class DxfObjectPointer : DxfDatabaseObject
    {
        /// <summary>Creates a detached object with no application data.</summary>
        public DxfObjectPointer() : base("OBJECT_PTR") { }
        internal override DxfDatabaseObject CloneShell() { return new DxfObjectPointer(); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!(this.Owner is DxfDictionary)) errors.Add("OBJECT_PTR requires a dictionary owner.");
        }
    }
}
