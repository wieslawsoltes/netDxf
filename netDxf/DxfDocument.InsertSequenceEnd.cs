// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal void RegisterInsertSequenceEnd(Insert insert)
        {
            EndSequence end = insert?.SequenceEnd;
            if (end == null) return;
            if (!ReferenceEquals(end.Owner, insert)) throw new InvalidOperationException("Invalid INSERT terminator owner.");
            if (end.Handle == null) this.NumHandles = end.AssignHandle(this.NumHandles);
            DxfObject current = this.GetObjectByHandle(end.Handle);
            if (ReferenceEquals(current, end)) return;
            if (current != null) throw new InvalidOperationException("Duplicate INSERT terminator identity: " + end.Handle);
            if (end.StoredLayer != null)
            {
                end.StoredLayer = this.Layers.Add(end.StoredLayer);
                this.Layers.References[end.StoredLayer.Name].Add(end);
            }
            this.AddedObjects.Add(end.Handle, end);
        }
        private void UnregisterInsertSequenceEnd(Insert insert)
        {
            EndSequence end = insert?.SequenceEnd;
            if (end == null) return;
            this.AddedObjects.Remove(end.Handle);
            if (end.StoredLayer != null) this.Layers.References[end.StoredLayer.Name].Remove(end);
            end.Handle = null;
        }
        private bool InsertSequenceReferencesRemoval(HashSet<DxfObject> removed)
        {
            var ends = new HashSet<DxfObject>(removed.OfType<EndSequence>().Where(end => end.Owner is Insert));
            if (ends.Count == 0) return false;
            // A registered extension or opaque raw handle cannot be orphaned by an entity move/remove.
            foreach (DxfObject end in ends)
                if (end.ExtensionDictionary != null || end.XData.Values.SelectMany(data => data.XDataRecord)
                    .Any(tag => tag.Code == XDataCode.DatabaseHandle && this.RemovedPolylineHandle((string)tag.Value, removed))) return true;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (removed.Contains(item)) continue;
                if (ends.Contains(item.Owner) || ends.Contains(item.ExtensionDictionary) || item.PersistentReactors.Any(ends.Contains)) return true;
                if (item is EntityObject entity && entity.Reactors.Any(ends.Contains)) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(ends.Contains)) return true;
                if (item is DxfDictionary dictionary && dictionary.Entries.Any(entry => ends.Contains(entry.Target))) return true;
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && this.RemovedPolylineHandle((string)tag.Value, ends)) return true;
                IEnumerable<DxfTag> tags = item is DxfXRecord record ? record.Data : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                if (tags.Any(tag => DxfObjectDatabase.IsReference(tag) && this.RemovedPolylineHandle((string)tag.Value, ends))) return true;
            }
            foreach (netDxf.Header.HeaderVariable variable in this.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary && variable.Value is string handle
                    && this.RemovedPolylineHandle(handle, ends)) return true;
            }
            return false;
        }
    }
}
