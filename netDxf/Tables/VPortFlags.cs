// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Tables
{
    /// <summary>Stored VPORT symbol-table flags (DXF group 70).</summary>
    [Flags]
    public enum VPortFlags : short
    {
        /// <summary>No flags.</summary>
        None = 0,
        /// <summary>The configuration is externally dependent.</summary>
        ExternallyDependent = 16,
        /// <summary>The dependent external reference has been resolved.</summary>
        ExternalReferenceResolved = 32,
        /// <summary>The record was referenced at the last drawing edit.</summary>
        Referenced = 64
    }
}
