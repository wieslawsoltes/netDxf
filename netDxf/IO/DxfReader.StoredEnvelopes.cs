using System;
using System.Collections.Generic;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private bool ReadStoredEnvelopePayload(DatabaseRecord record, string type, List<DxfTag> tags, int start)
        {
            if (type != "SPATIAL_INDEX" && type != "VBA_PROJECT") return false;
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            string first = type == "SPATIAL_INDEX" ? "AcDbIndex" : "AcDbVbaProject";
            if (start >= end || tags[start].Code != 100) throw new FormatException(type + " requires its public subclass marker.");
            // An unrecognized first subclass belongs wholly to the opaque path.
            if ((string)tags[start].Value != first)
            {
                if (type == "SPATIAL_INDEX" && (string)tags[start].Value == "AcDbSpatialIndex")
                    throw new FormatException("SPATIAL_INDEX is missing its AcDbIndex base subclass.");
                return false;
            }
            bool unknown = false, publicScope = true, spatialMarker = false, hasValue = false;
            double timestamp = 0; int count = 0, length = 0;
            var chunks = new List<byte[]>();
            for (int i = start + 1; i < end; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 100)
                {
                    string marker = (string)tag.Value;
                    if (marker == first) throw new FormatException(type + " repeats its public subclass marker.");
                    if (type == "SPATIAL_INDEX" && marker == "AcDbSpatialIndex")
                    {
                        if (spatialMarker || !hasValue) throw new FormatException("SPATIAL_INDEX requires one timestamp before its unique spatial subclass.");
                        spatialMarker = true; publicScope = true;
                    }
                    else { unknown = true; publicScope = false; }
                    continue;
                }
                if (!publicScope) continue;
                if (type == "SPATIAL_INDEX" && tag.Code == 40)
                {
                    if (hasValue || spatialMarker) throw new FormatException("SPATIAL_INDEX repeats or misplaces its timestamp.");
                    timestamp = (double)tag.Value;
                    if (double.IsNaN(timestamp) || double.IsInfinity(timestamp)) throw new FormatException("SPATIAL_INDEX timestamp must be finite.");
                    hasValue = true;
                }
                else if (type == "VBA_PROJECT" && tag.Code == 90)
                {
                    if (hasValue) throw new FormatException("VBA_PROJECT repeats its byte count.");
                    count = (int)tag.Value;
                    if (count < 0 || count > DxfVbaProject.MaximumDataLength) throw new FormatException("VBA_PROJECT byte count exceeds the admitted range.");
                    hasValue = true;
                }
                else if (type == "VBA_PROJECT" && tag.Code == 310)
                {
                    byte[] chunk = (byte[])tag.Value;
                    if (!hasValue || chunk.Length > DxfVbaProject.MaximumChunkLength || chunks.Count == DxfVbaProject.MaximumChunkCount || chunk.Length > count - length)
                        throw new FormatException("VBA_PROJECT chunks violate their count, order, or admission limits.");
                    chunks.Add(chunk); length += chunk.Length;
                }
                else unknown = true;
            }
            if (!hasValue || type == "SPATIAL_INDEX" && !spatialMarker || type == "VBA_PROJECT" && length != count)
                throw new FormatException(type + " has an incomplete public envelope.");
            if (unknown) return false;
            if (type == "SPATIAL_INDEX") record.Object = new DxfSpatialIndex { Timestamp = timestamp };
            else { var project = new DxfVbaProject(); project.SetChunks(chunks); record.Object = project; }
            if (end < tags.Count) this.ReadDatabaseXData(record.Object, tags, end);
            return true;
        }
    }
}
