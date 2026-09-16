// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections.Generic;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>Immutable stored TABLESTYLE cell data-type and unit-type codes.</summary>
    /// <remarks>Codes are signed 32-bit stored values. This API does not evaluate formats, convert cell contents or infer enum meanings. Unknown values remain representable.</remarks>
    public sealed class DxfTableStyleRowDataTypes
    {
        /// <summary>Creates a stored data/unit pair without converting values or assigning application-specific meanings.</summary>
        /// <param name="storedDataType">The stored group-90 code.</param>
        /// <param name="storedUnitType">The stored group-91 code.</param>
        public DxfTableStyleRowDataTypes(int storedDataType, int storedUnitType)
        { this.StoredDataType = storedDataType; this.StoredUnitType = storedUnitType; }
        /// <summary>Gets the unmodified group-90 data-type code.</summary>
        public int StoredDataType { get; }
        /// <summary>Gets the unmodified group-91 unit-type code.</summary>
        public int StoredUnitType { get; }
        internal static DxfTableStyleRowDataTypes TryRead(List<DxfTag> tags)
        {
            DxfTag data = null, unit = null;
            foreach (DxfTag tag in tags)
            {
                if (tag.Code == 90) { if (data != null) return null; data = tag; }
                else if (tag.Code == 91) { if (unit != null) return null; unit = tag; }
            }
            return data == null || unit == null ? null : new DxfTableStyleRowDataTypes((int)data.Value, (int)unit.Value);
        }
    }
}
