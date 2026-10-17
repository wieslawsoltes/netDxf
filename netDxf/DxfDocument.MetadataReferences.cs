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
            VisitObjectMetadataMembers(owner, result, (item, items) => { items.Add(item); return false; });
            return result;
        }

        // Walk the actual owned records in the same order as metadata enumeration.
        // A visitor may stop at a matching member without materializing the graph.
        private static DxfObject VisitObjectMetadataMembers<TState>(DxfObject owner, TState state, Func<DxfObject, TState, bool> visit)
        {
            if (owner == null) return null;
            if (visit(owner, state)) return owner;
            if (owner is Insert insert)
            {
                foreach (netDxf.Entities.Attribute attribute in insert.Attributes)
                    if (visit(attribute, state)) return attribute;
                if (insert.SequenceEnd != null && visit(insert.SequenceEnd, state)) return insert.SequenceEnd;
            }
            if (owner is Block block && visit(block.End, state)) return block.End;
            if (owner is Polyline3D polyline)
                foreach (Polyline3DRecord record in polyline.StoredRecords)
                    if (visit(record, state)) return record;
            if (owner is PolygonMesh mesh)
                foreach (PolygonMeshRecord record in mesh.StoredRecords)
                    if (visit(record, state)) return record;
            if (owner is PolyfaceMesh polyface)
                foreach (PolyfaceMeshRecord record in polyface.StoredRecords)
                    if (visit(record, state)) return record;
            if (owner is Polyline2D legacy)
                foreach (Polyline2DRecord record in legacy.StoredRecords)
                    if (visit(record, state)) return record;
            if (owner is Layout layout && layout.Viewport != null && visit(layout.Viewport, state)) return layout.Viewport;
            return null;
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
            var collector = new MetadataCollector();
            foreach (DxfObject item in this.AddedObjects.Values)
                VisitObjectMetadataMembers(item, collector, (member, state) => { state.Add(member); return false; });
            return collector.Items;
        }
        private sealed class MetadataCollector
        {
            private readonly HashSet<DxfObject> found = new HashSet<DxfObject>(new MetadataIdentityComparer());
            internal readonly List<DxfObject> Items = new List<DxfObject>();
            internal void Add(DxfObject item)
            { if (item != null && this.found.Add(item)) this.Items.Add(item); }
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
