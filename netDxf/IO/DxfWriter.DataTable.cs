using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareDataTableClass(DxfClassCollection definitions)
        {
            this.PrepareStoredEnvelopeClass(definitions, "DATATABLE", "AcDbDataTable", "ObjectDBX Classes", 0,
                this.doc.Objects.Items.Any(item => item is DxfDataTable));
        }
        private bool WriteDataTablePayload(DxfDatabaseObject item)
        {
            if (!(item is DxfDataTable table)) return false;
            this.chunk.Write(100, "AcDbDataTable");
            this.chunk.Write(70, table.StoredVersion);
            this.chunk.Write(90, table.Columns.Count);
            this.chunk.Write(91, table.RowCount);
            this.chunk.Write(1, this.EncodeDatabaseString(table.Name));
            foreach (DxfDataColumn column in table.Columns)
            {
                this.chunk.Write(92, (int)column.Type);
                this.chunk.Write(2, this.EncodeDatabaseString(column.Name));
                foreach (object value in column.Values)
                {
                    switch (column.Type)
                    {
                        case DxfDataCellType.Integer: this.chunk.Write(93, (int)value); break;
                        case DxfDataCellType.Double: this.chunk.Write(40, (double)value); break;
                        case DxfDataCellType.String: this.chunk.Write(3, this.EncodeDatabaseString((string)value)); break;
                        case DxfDataCellType.Boolean: this.chunk.Write(71, (short)((bool)value ? 1 : 0)); break;
                        case DxfDataCellType.Point: case DxfDataCellType.Vector:
                            Vector3 point = (Vector3)value; short first = column.Type == DxfDataCellType.Point ? (short)10 : (short)11;
                            this.chunk.Write(first, point.X); this.chunk.Write((short)(first + 10), point.Y); this.chunk.Write((short)(first + 20), point.Z); break;
                        default:
                            short code = column.Type == DxfDataCellType.ObjectId ? (short)331 : column.Type == DxfDataCellType.HardOwner ? (short)360 : column.Type == DxfDataCellType.SoftOwner ? (short)350 : column.Type == DxfDataCellType.HardPointer ? (short)340 : (short)330;
                            this.chunk.Write(code, ((DxfObject)value)?.Handle ?? "0"); break;
                    }
                }
            }
            return true;
        }
    }
}
