using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A loaded OBJECTS record whose application-specific subclass is not modeled by this library.</summary>
    /// <remarks>Subclass tags are preserved without interpreting or evaluating them. Graph cloning rejects opaque records because their application reference rules are unknown.</remarks>
    public sealed class DxfOpaqueObject : DxfDatabaseObject
    {
        internal DxfOpaqueObject(string code, IList<DxfTag> tags) : base(code)
        { this.Tags = new ReadOnlyCollection<DxfTag>(new List<DxfTag>(tags)); }
        /// <summary>Gets the immutable ordered subclass and payload tags, excluding object identity and common metadata.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Opaque object cloning requires an application-specific schema: " + this.CodeName); }
    }
}
