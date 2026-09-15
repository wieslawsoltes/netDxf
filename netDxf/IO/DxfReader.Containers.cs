using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private bool ReadContainerPayload(DatabaseRecord record, string type, List<DxfTag> tags, int start)
        {
            if (type != "IDBUFFER" && type != "SORTENTSTABLE" && type != "SPATIAL_FILTER") return false;
            int end = tags.FindIndex(start, t => t.Code == 1001);
            if (end < 0) end = tags.Count;
            List<DxfTag> body = tags.GetRange(start, end - start);
            if (type == "IDBUFFER")
            {
                RequireContainerMarker(body, 0, "AcDbIdBuffer");
                record.Object = new DxfIdBuffer();
                for (int i = 1; i < body.Count; i++)
                {
                    if (body[i].Code != 330) throw new FormatException("IDBUFFER contains an unsupported field.");
                    record.ContainerReferences.Add((string)body[i].Value);
                }
            }
            else if (type == "SORTENTSTABLE")
            {
                RequireContainerMarker(body, 0, "AcDbSortentsTable");
                if (body.Count < 2 || body[1].Code != 330 || (string)body[1].Value == "0") throw new FormatException("SORTENTSTABLE requires its block record pointer.");
                record.Object = new DxfSortentsTable();
                record.ContainerReferences.Add((string)body[1].Value);
                for (int i = 2; i < body.Count; i += 2)
                {
                    if (i + 1 >= body.Count || body[i].Code != 331 || body[i + 1].Code != 5 || (string)body[i].Value == "0") throw new FormatException("SORTENTSTABLE requires complete nonnull entity/key pairs.");
                    record.ContainerReferences.Add((string)body[i].Value);
                    record.SortKeys.Add((string)body[i + 1].Value);
                }
            }
            else record.Object = ReadSpatialFilterPayload(body);
            if (end < tags.Count) this.ReadDatabaseXData(record.Object, tags, end);
            return true;
        }
        private static void RequireContainerMarker(List<DxfTag> tags, int index, string marker)
        {
            if (index >= tags.Count || tags[index].Code != 100 || (string)tags[index].Value != marker) throw new FormatException("Missing " + marker + " subclass.");
        }
        private static DxfSpatialFilter ReadSpatialFilterPayload(List<DxfTag> tags)
        {
            RequireContainerMarker(tags, 0, "AcDbFilter"); RequireContainerMarker(tags, 1, "AcDbSpatialFilter");
            Dictionary<short, DxfTag> fields = new Dictionary<short, DxfTag>();
            List<Vector2> boundary = new List<Vector2>(); List<double> matrix = new List<double>();
            double? pointX = null; bool pointComplete = false;
            foreach (DxfTag tag in tags.Skip(2))
            {
                switch (tag.Code)
                {
                    case 10:
                        if (pointX.HasValue) throw new FormatException("SPATIAL_FILTER boundary point is missing group 20.");
                        pointX = (double)tag.Value; pointComplete = false; break;
                    case 20:
                        if (!pointX.HasValue) throw new FormatException("SPATIAL_FILTER boundary point is missing group 10.");
                        boundary.Add(new Vector2(pointX.Value, (double)tag.Value)); pointX = null; pointComplete = true; break;
                    case 30:
                        if (!pointComplete || (double)tag.Value != 0) throw new FormatException("SPATIAL_FILTER boundary vertices must be 2D.");
                        pointComplete = false; break;
                    case 40: matrix.Add((double)tag.Value); break;
                    case 70: case 71: case 72: case 73: case 210: case 220: case 230: case 11: case 21: case 31: case 41:
                        if (fields.ContainsKey(tag.Code)) throw new FormatException("Duplicate SPATIAL_FILTER scalar field: " + tag.Code);
                        fields.Add(tag.Code, tag); break;
                    default: throw new FormatException("Unsupported SPATIAL_FILTER field: " + tag.Code);
                }
            }
            if (pointX.HasValue || !fields.TryGetValue(70, out DxfTag count) || (short)count.Value != boundary.Count) throw new FormatException("SPATIAL_FILTER boundary count or coordinates are incomplete.");
            bool enabled = ContainerFlag(fields, 71, true), front = ContainerFlag(fields, 72, false), back = ContainerFlag(fields, 73, false);
            if (matrix.Count != (front ? 25 : 24)) throw new FormatException("SPATIAL_FILTER requires two complete affine matrices and its enabled front-plane distance.");
            if (fields.ContainsKey(41) != back) throw new FormatException("SPATIAL_FILTER back-plane distance does not match its enabled flag.");
            DxfSpatialFilter result = new DxfSpatialFilter
            {
                IsClippingEnabled = enabled,
                Normal = ContainerVector(fields, 210, Vector3.UnitZ),
                Origin = ContainerVector(fields, 11, Vector3.Zero),
                FrontClippingDistance = front ? (double?)matrix[0] : null,
                BackClippingDistance = back ? (double?)fields[41].Value : null
            };
            result.SetBoundary(boundary);
            int offset = front ? 1 : 0;
            result.InverseInsertTransform = ContainerMatrix(matrix, offset);
            result.ClipBoundaryTransform = ContainerMatrix(matrix, offset + 12);
            return result;
        }
        private static bool ContainerFlag(Dictionary<short, DxfTag> fields, short code, bool fallback)
        {
            if (!fields.TryGetValue(code, out DxfTag tag)) return fallback;
            short value = (short)tag.Value;
            if (value != 0 && value != 1) throw new FormatException("SPATIAL_FILTER flags must be zero or one.");
            return value == 1;
        }
        private static Vector3 ContainerVector(Dictionary<short, DxfTag> fields, short code, Vector3 fallback)
        {
            bool x = fields.ContainsKey(code), y = fields.ContainsKey((short)(code + 10)), z = fields.ContainsKey((short)(code + 20));
            if (!x && !y && !z) return fallback;
            if (!x || !y || !z) throw new FormatException("SPATIAL_FILTER vector is missing an axis.");
            return new Vector3((double)fields[code].Value, (double)fields[(short)(code + 10)].Value, (double)fields[(short)(code + 20)].Value);
        }
        private static Matrix4 ContainerMatrix(List<double> values, int offset)
        {
            return new Matrix4(values[offset], values[offset + 1], values[offset + 2], values[offset + 3],
                values[offset + 4], values[offset + 5], values[offset + 6], values[offset + 7],
                values[offset + 8], values[offset + 9], values[offset + 10], values[offset + 11], 0, 0, 0, 1);
        }
        private void ResolveContainerReferences(DatabaseRecord record)
        {
            if (record.Object is DxfIdBuffer buffer)
            {
                foreach (string handle in record.ContainerReferences)
                {
                    DxfObject target = handle == "0" ? null : this.GetObjectBySourceHandle(handle);
                    if (target == null && handle != "0") throw new FormatException("Unresolved IDBUFFER reference: " + handle);
                    buffer.References.Add(target);
                }
            }
            else if (record.Object is DxfSortentsTable table)
            {
                table.BlockRecord = this.GetObjectBySourceHandle(record.ContainerReferences[0]) as BlockRecord ?? throw new FormatException("SORTENTSTABLE block pointer does not identify a block record.");
                for (int i = 1; i < record.ContainerReferences.Count; i++)
                {
                    EntityObject entity = this.GetObjectBySourceHandle(record.ContainerReferences[i]) as EntityObject ?? throw new FormatException("SORTENTSTABLE reference does not identify a graphical entity.");
                    table.Entries.Add(new DxfSortOrderEntry(entity, record.SortKeys[i - 1]));
                }
            }
        }
    }
}
