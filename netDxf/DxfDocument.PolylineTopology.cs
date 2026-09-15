using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal Polyline3DRecord PreparePolylineVertexInsertion(Polyline3D parent, Vector3 position)
        {
            if (parent.Layer == null || !ReferenceEquals(this.GetObjectByHandle(parent.Layer.Handle), parent.Layer))
                throw new InvalidOperationException("The inserted VERTEX requires the parent's registered layer.");
            long retainedTags = 10;
            foreach (Polyline3DRecord record in this.AddedObjects.Values.OfType<Polyline3DRecord>())
            {
                int count = record.TopologyTagCount();
                if (count > 4096) throw new NotSupportedException("An existing retained polyline record exceeds its tag admission budget.");
                retainedTags += count;
            }
            foreach (PolygonMeshRecord record in this.AddedObjects.Values.OfType<PolygonMeshRecord>())
            {
                int count = record.TopologyTagCount();
                if (count > 4096) throw new NotSupportedException("An existing retained polygon mesh record exceeds its tag admission budget.");
                retainedTags += count;
            }
            foreach (PolyfaceMeshRecord record in this.AddedObjects.Values.OfType<PolyfaceMeshRecord>())
            {
                int count = record.TopologyTagCount();
                if (count > 4096) throw new NotSupportedException("An existing retained polyface record exceeds its tag admission budget.");
                retainedTags += count;
            }
            foreach (Polyline2DRecord record in this.AddedObjects.Values.OfType<Polyline2DRecord>())
            {
                int count = record.TopologyTagCount();
                if (count > 4096) throw new NotSupportedException("An existing retained polyface record exceeds its tag admission budget.");
                retainedTags += count;
            }
            if (retainedTags > 1048576) throw new NotSupportedException("The inserted VERTEX exceeds the document's retained polyline tag admission budget.");
            var occupied = new HashSet<ulong>();
            foreach (DxfObject item in this.RetainedMetadataObjects())
                if (ulong.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)) occupied.Add(value);
            long number = this.NumHandles;
            while (number > 0 && number < long.MaxValue && occupied.Contains((ulong)number)) number++;
            if (number <= 0 || number == long.MaxValue) throw new InvalidOperationException("No VERTEX identity can be allocated from the current handle seed.");
            string handle = number.ToString("X", CultureInfo.InvariantCulture);
            var tags = new List<DxfTag>
            {
                new DxfTag(5, handle), new DxfTag(330, parent.Handle),
                new DxfTag(100, "AcDbEntity"), new DxfTag(8, parent.Layer.Name),
                new DxfTag(100, "AcDbVertex"), new DxfTag(100, "AcDb3dPolylineVertex"),
                new DxfTag(10, position.X), new DxfTag(20, position.Y), new DxfTag(30, position.Z),
                new DxfTag(70, (short)32)
            };
            var result = new Polyline3DRecord(DxfObjectCode.Vertex, tags)
            {
                Owner = parent, Handle = handle, SourceOwner = parent.Handle, SourceDocument = this,
                SourceVersion = parent.EndSequenceRecord.SourceVersion, CommonEnd = 2,
                IdentityIndex = 0, OwnerIndex = 1, Position = position, IsAuthored = true
            };
            result.Resources.Add(3, parent.Layer);
            result.OriginalResourceNames.Add(3, parent.Layer.Name);
            result.Coordinates.Add(10, 6); result.Coordinates.Add(20, 7); result.Coordinates.Add(30, 8);
            // This is an authored record. It is never entered in the reader's accepted
            // physical source-identity map, even when an input advertised a lower seed.
            return result;
        }

        internal void RegisterPolylineVertexInsertion(Polyline3DRecord record)
        {
            this.AddedObjects.Add(record.Handle, record);
            this.NumHandles = long.Parse(record.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + 1;
        }

        internal void ValidatePolylineVertexRemoval(Polyline3DRecord record)
        {
            if (record.IsRemoved || !ReferenceEquals(this.GetObjectByHandle(record.Handle), record))
                throw new InvalidOperationException("The removed VERTEX must be registered in its source document.");
            if (this.StoredTableReferencesRemoval(record))
                throw new NotSupportedException("Removing this VERTEX requires resolving its incoming references or private/owned payload first.");
            foreach (XData data in record.XData.Values)
                if (!this.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name))
                    throw new InvalidOperationException("The removed VERTEX has inconsistent APPID reference bookkeeping.");
        }

        internal void UnregisterPolylineVertexRemoval(Polyline3DRecord record)
        {
            this.AddedObjects.Remove(record.Handle);
        }
    }
}
