using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteContainerPayload(DxfDatabaseObject item)
        {
            if (item is DxfIdBuffer buffer)
            {
                this.chunk.Write(100, "AcDbIdBuffer");
                foreach (DxfObject target in buffer.References) this.chunk.Write(330, target?.Handle ?? "0");
            }
            else if (item is DxfSortentsTable table)
            {
                this.chunk.Write(100, "AcDbSortentsTable"); this.chunk.Write(330, table.BlockRecord.Handle);
                foreach (DxfSortOrderEntry entry in table.Entries)
                { this.chunk.Write(331, entry.Entity.Handle); this.chunk.Write(5, entry.SortHandle); }
            }
            else if (item is DxfSpatialFilter filter)
            {
                this.chunk.Write(100, "AcDbFilter"); this.chunk.Write(100, "AcDbSpatialFilter");
                this.chunk.Write(70, (short)filter.Boundary.Count);
                foreach (Vector2 point in filter.Boundary) { this.chunk.Write(10, point.X); this.chunk.Write(20, point.Y); }
                this.WriteContainerVector(210, filter.Normal); this.WriteContainerVector(11, filter.Origin);
                this.chunk.Write(71, filter.IsClippingEnabled ? (short)1 : (short)0);
                this.chunk.Write(72, filter.FrontClippingDistance.HasValue ? (short)1 : (short)0);
                if (filter.FrontClippingDistance.HasValue) this.chunk.Write(40, filter.FrontClippingDistance.Value);
                this.chunk.Write(73, filter.BackClippingDistance.HasValue ? (short)1 : (short)0);
                if (filter.BackClippingDistance.HasValue) this.chunk.Write(41, filter.BackClippingDistance.Value);
                this.WriteContainerMatrix(filter.InverseInsertTransform); this.WriteContainerMatrix(filter.ClipBoundaryTransform);
            }
            else return false;
            return true;
        }
        private void WriteContainerVector(short code, Vector3 vector)
        { this.chunk.Write(code, vector.X); this.chunk.Write((short)(code + 10), vector.Y); this.chunk.Write((short)(code + 20), vector.Z); }
        private void WriteContainerMatrix(Matrix4 matrix)
        { for (int row = 0; row < 3; row++) for (int col = 0; col < 4; col++) this.chunk.Write(40, matrix[row, col]); }
    }
}
