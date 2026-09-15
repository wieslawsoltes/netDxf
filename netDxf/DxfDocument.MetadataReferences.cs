using System;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private sealed class MetadataIdentityComparer : IEqualityComparer<DxfObject>
        {
            public bool Equals(DxfObject first, DxfObject second) { return ReferenceEquals(first, second); }
            public int GetHashCode(DxfObject value) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value); }
        }
        private readonly HashSet<DxfObject> xdataMetadataBindings = new HashSet<DxfObject>(new MetadataIdentityComparer());

        private static List<DxfObject> ObjectMetadataMembers(DxfObject owner)
        {
            var result = new List<DxfObject>();
            if (owner == null) return result;
            result.Add(owner);
            if (owner is Insert insert) foreach (netDxf.Entities.Attribute attribute in insert.Attributes) result.Add(attribute);
            if (owner is Block block) result.Add(block.End);
            if (owner is Polyline3D polyline) foreach (Polyline3DRecord record in polyline.StoredRecords) result.Add(record);
            if (owner is PolygonMesh mesh) foreach (PolygonMeshRecord record in mesh.StoredRecords) result.Add(record);
            if (owner is PolyfaceMesh polyface) foreach (PolyfaceMeshRecord record in polyface.StoredRecords) result.Add(record);
            if (owner is Polyline2D legacy) foreach (Polyline2DRecord record in legacy.StoredRecords) result.Add(record);
            if (owner is Layout layout && layout.Viewport != null) result.Add(layout.Viewport);
            return result;
        }
        internal void BindMetadataObject(DxfObject item)
        {
            if (item == null || !this.xdataMetadataBindings.Add(item)) return;
            foreach (XData data in new List<XData>(item.XData.Values))
            {
                ApplicationRegistry registry = this.CanonicalXDataRegistry(data.ApplicationRegistry);
                item.XData.CanonicalizeApplicationRegistry(data.ApplicationRegistry.Name, registry);
                this.appRegistries.References[registry.Name].Add(item);
            }
            item.XDataAddAppReg += this.DxfObject_XDataAddAppReg;
            item.XDataRemoveAppReg += this.DxfObject_XDataRemoveAppReg;
        }
        internal void UnbindMetadataObject(DxfObject item)
        {
            if (item == null || !this.xdataMetadataBindings.Remove(item)) return;
            foreach (XData data in item.XData.Values)
                this.appRegistries.References[data.ApplicationRegistry.Name].Remove(item);
            item.XDataAddAppReg -= this.DxfObject_XDataAddAppReg;
            item.XDataRemoveAppReg -= this.DxfObject_XDataRemoveAppReg;
        }
        internal void ReplaceLayoutViewportMetadata(Layout layout, Viewport previous, Viewport current)
        {
            if (!this.xdataMetadataBindings.Contains(layout)) return;
            this.UnbindMetadataObject(previous);
            this.BindMetadataObject(current);
        }

        internal List<DxfObject> RetainedMetadataObjects()
        {
            var found = new HashSet<DxfObject>(new MetadataIdentityComparer());
            var result = new List<DxfObject>();
            Action<DxfObject> add = item => { if (item != null && found.Add(item)) result.Add(item); };
            foreach (DxfObject item in this.AddedObjects.Values)
            {
                foreach (DxfObject member in ObjectMetadataMembers(item)) add(member);
            }
            return result;
        }
        internal List<DxfObjectReference> ApplicationRegistryReferences(ApplicationRegistry registry)
        {
            var result = new List<DxfObjectReference>();
            if (registry == null || !this.ApplicationRegistries.Contains(registry)) return result;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                int uses = 0;
                foreach (XData data in item.XData.Values) if (ReferenceEquals(data.ApplicationRegistry, registry)) uses++;
                if (uses != 0) result.Add(new DxfObjectReference(item, uses));
            }
            foreach (DxfObjectReference reference in this.MLeaderReferences(registry))
            {
                int index = result.FindIndex(r => ReferenceEquals(r.Reference, reference.Reference));
                if (index < 0) result.Add(reference);
                else result[index] = new DxfObjectReference(reference.Reference, result[index].Uses + reference.Uses);
            }
            return result;
        }
    }
}
