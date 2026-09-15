// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Objects
{
    public sealed partial class DxfXRecord
    {
        private const string Pre2007TableMarker = "ACAD_ROUNDTRIP_PRE2007_TABLE";
        private const string Pre2007TableCellMarker = "ACAD_ROUNDTRIP_PRE2007_TABLECELL";
        private static readonly short[] CompositeTableCodes = { 102, 360, 70, 90, 10, 20, 30, 90, 90, 361, 102, 90, 91, 102, 360 };
        private DxfDataTable tableCellData;

        // The native R2004 envelope has three complete, ordered sections. Their
        // scalar values remain stored data; only the three owner slots are bound.
        internal bool IsCompositeTableRoundtripRecord
        {
            get
            {
                if (this.Data.Count != CompositeTableCodes.Length || !this.IsTableRoundtripRecord) return false;
                for (int i = 0; i < CompositeTableCodes.Length; i++)
                    if (this.Data[i].Code != CompositeTableCodes[i]) return false;
                return (string)this.Data[10].Value == Pre2007TableMarker && (string)this.Data[13].Value == Pre2007TableCellMarker;
            }
        }

        internal void BindCompositeTableRoundtripChildren(DxfDatabaseObject content, DxfDatabaseObject geometry, DxfDataTable cellData)
        {
            if (this.IsErased) throw new InvalidOperationException("An erased record cannot bind owned objects.");
            if (this.IsSchemaManaged) throw new InvalidOperationException("The ownership schema is already bound.");
            if (!this.IsCompositeTableRoundtripRecord) throw new ArgumentException("The composite TABLE ownership envelope is not recognized.");
            if (content == null || content.CodeName != "TABLECONTENT" || geometry == null || geometry.CodeName != "TABLEGEOMETRY" || cellData == null)
                throw new ArgumentException("The composite TABLE slots require TABLECONTENT, TABLEGEOMETRY and DATATABLE targets.");
            // Validate every identity, owner and registration state before mutation.
            this.CheckOwnedCandidate(content, this.Data[1]);
            this.CheckOwnedCandidate(geometry, this.Data[9]);
            this.CheckOwnedCandidate(cellData, this.Data[14]);
            if (this.Database != null)
                foreach (DxfDatabaseObject candidate in this.Database.Items)
                    if (ReferenceEquals(candidate.Owner, this) && candidate != content && candidate != geometry && candidate != cellData && candidate != this.ExtensionDictionary)
                        throw new ArgumentException("The composite TABLE envelope has an undeclared owned child.");
            this.tableContent = content;
            this.tableGeometry = geometry;
            this.tableCellData = cellData;
            this.tableContentSlot = 1;
            this.tableGeometrySlot = 9;
            content.Owner = geometry.Owner = cellData.Owner = this;
        }

        private void ValidateCompositeTableOwnership(List<string> errors)
        {
            if (this.tableCellData == null) return;
            if (!this.IsCompositeTableRoundtripRecord)
            { errors.Add("Invalid composite TABLE ownership envelope: " + this.Handle); return; }
            if (this.Database != null && !string.Equals((string)this.Data[14].Value, this.tableCellData.Handle, StringComparison.OrdinalIgnoreCase))
                errors.Add("Composite TABLE DATATABLE handle mismatch: " + this.Handle);
        }
    }
}
