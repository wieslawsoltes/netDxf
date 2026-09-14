using System;

namespace netDxf.Tables
{
    /// <summary>Stored STYLE group 70 flags. Unknown bits are retained.</summary>
    [Flags]
    public enum TextStyleFlags : short
    {
        /// <summary>No flags.</summary>
        None = 0,
        /// <summary>The STYLE record describes a shape file.</summary>
        Shape = 1,
        /// <summary>Vertical text.</summary>
        Vertical = 4,
        /// <summary>The entry depends on an external reference.</summary>
        ExternallyDependent = 16,
        /// <summary>The external reference was resolved.</summary>
        ExternalReferenceResolved = 32,
        /// <summary>The entry was referenced when the drawing was last edited.</summary>
        Referenced = 64
    }
}
