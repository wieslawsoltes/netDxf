using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly Dictionary<DxfLayerIndex, List<Tuple<string, string, int>>> pendingLayerIndexes = new Dictionary<DxfLayerIndex, List<Tuple<string, string, int>>>();

        private DatabaseRecord ReadLayerIndexRecord(List<DxfTag> tags)
        {
            DatabaseRecord record = this.ReadStoredObjectHeader(tags, out List<DxfTag> opaque, out int start, out string handle);
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            bool unknown = opaque.Count != 0;
            double timestamp = 0;
            bool hasTimestamp = false, hasLayerMarker = false, publicScope = true;
            var names = new List<string>();
            var buffers = new List<string>();
            var counts = new List<int>();
            if (start >= end || tags[start].Code != 100) throw new FormatException("LAYER_INDEX requires its public subclass marker.");
            if ((string)tags[start].Value != "AcDbIndex")
            {
                if ((string)tags[start].Value == "AcDbLayerIndex") throw new FormatException("LAYER_INDEX is missing AcDbIndex.");
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("LAYER_INDEX", opaque) { Handle = handle };
                return record;
            }
            for (int i = start + 1; i < end; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 100)
                {
                    string marker = (string)tag.Value;
                    if (marker == "AcDbIndex") throw new FormatException("LAYER_INDEX repeats AcDbIndex.");
                    if (marker == "AcDbLayerIndex")
                    {
                        if (hasLayerMarker || !hasTimestamp) throw new FormatException("LAYER_INDEX requires one timestamp before AcDbLayerIndex.");
                        hasLayerMarker = true; publicScope = true;
                    }
                    else { unknown = true; publicScope = false; }
                    continue;
                }
                if (!publicScope) continue;
                if (tag.Code == 40)
                {
                    if (hasTimestamp || hasLayerMarker) throw new FormatException("LAYER_INDEX repeats or misplaces its timestamp.");
                    timestamp = (double)tag.Value;
                    if (double.IsNaN(timestamp) || double.IsInfinity(timestamp)) throw new FormatException("LAYER_INDEX timestamp must be finite.");
                    hasTimestamp = true;
                }
                else if (!hasLayerMarker && (tag.Code == 8 || tag.Code == 360 || tag.Code == 90))
                    throw new FormatException("LAYER_INDEX entry fields require AcDbLayerIndex.");
                else if (hasLayerMarker && tag.Code == 8)
                    names.Add(this.DecodeEncodedNonAsciiCharacters((string)tag.Value));
                else if (hasLayerMarker && tag.Code == 360)
                {
                    string reference = (string)tag.Value;
                    if (reference == "0") throw new FormatException("LAYER_INDEX requires nonnull IDBUFFER ownership handles.");
                    buffers.Add(reference);
                }
                else if (hasLayerMarker && tag.Code == 90)
                {
                    // LibreDWG emits an extra undocumented leading 90=0. Keep that variant opaque.
                    if (names.Count == 0 && buffers.Count == 0 && counts.Count == 0) unknown = true;
                    int count = (int)tag.Value;
                    if (count < 0) throw new FormatException("LAYER_INDEX IDBUFFER counts cannot be negative.");
                    counts.Add(count);
                }
                else unknown = true;
            }
            if (!unknown && (!hasTimestamp || !hasLayerMarker || names.Count != buffers.Count || names.Count != counts.Count))
                throw new FormatException("LAYER_INDEX has incomplete timestamp, subclass, or entry data.");
            if (unknown)
            {
                opaque.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("LAYER_INDEX", opaque);
            }
            else
            {
                var index = new DxfLayerIndex { Timestamp = timestamp };
                var entries = new List<Tuple<string, string, int>>();
                var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++)
                {
                    if (!distinct.Add(buffers[i])) throw new FormatException("LAYER_INDEX repeats an owned IDBUFFER.");
                    try { new DxfLayerIndexEntry(names[i], new DxfIdBuffer()); }
                    catch (ArgumentException error) { throw new FormatException("Invalid stored layer-index name.", error); }
                    entries.Add(Tuple.Create(names[i], buffers[i], counts[i]));
                }
                record.Object = index;
                if (end < tags.Count) this.ReadDatabaseXData(index, tags, end);
                this.pendingLayerIndexes.Add(index, entries);
            }
            record.Object.Handle = handle;
            return record;
        }

        private void ResolveLayerIndexReferences()
        {
            foreach (KeyValuePair<DxfLayerIndex, List<Tuple<string, string, int>>> pending in this.pendingLayerIndexes)
            {
                if (!(pending.Key.Owner is DxfDictionary)) throw new FormatException("LAYER_INDEX requires a dictionary owner.");
                var entries = new List<DxfLayerIndexEntry>();
                foreach (Tuple<string, string, int> item in pending.Value)
                {
                    DxfIdBuffer buffer = this.GetObjectBySourceHandle(item.Item2) as DxfIdBuffer;
                    if (buffer == null || !ReferenceEquals(buffer.Owner, pending.Key))
                        throw new FormatException("LAYER_INDEX ownership target must be a reciprocally owned IDBUFFER: " + item.Item2);
                    if (buffer.References.Count != item.Item3)
                        throw new FormatException("LAYER_INDEX stored count does not match its IDBUFFER: " + item.Item2);
                    entries.Add(new DxfLayerIndexEntry(item.Item1, buffer));
                }
                try { pending.Key.LoadEntries(entries); }
                catch (ArgumentException error) { throw new FormatException("Invalid LAYER_INDEX ownership graph.", error); }
            }
        }
    }
}
