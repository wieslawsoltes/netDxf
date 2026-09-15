// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.IO;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal void ValidateStoredTableEntityAdoption(EntityObject entity)
        {
            if (entity is DxfOpaqueEntity opaque) opaque.ValidateIncoming(this);
            if (entity is Polyline3D polyline) polyline.ValidateStoredRecords(this, false);
            if (entity is PolygonMesh mesh) mesh.ValidateStoredRecords(this, false);
            if (entity is PolyfaceMesh polyface) polyface.ValidateStoredRecords(this, false);
            if (entity is StoredTable table) table.ValidateIncoming(this);
            else if (entity is Insert insert) this.ValidateStoredTableBlockAdoption(insert.Block);
            else if (entity is Dimension dimension && dimension.Block != null) this.ValidateStoredTableBlockAdoption(dimension.Block);
        }
        internal void ValidateStoredTableBlockAdoption(Block root)
        {
            var visited = new HashSet<DxfObject>(new MetadataIdentityComparer());
            Action<Block> visit = null;
            visit = block =>
            {
                if (block == null || !visited.Add(block) || this.Blocks.Contains(block.Name)) return;
                foreach (EntityObject entity in block.Entities)
                {
                    if (entity is DxfOpaqueEntity opaque) opaque.ValidateIncoming(this, block);
                    if (entity is Hatch hatch) HatchSourceRelations.ValidateOwner(hatch, block, this);
                    if (entity is Polyline3D polyline) polyline.ValidateStoredRecords(this, false);
                    if (entity is PolygonMesh mesh) mesh.ValidateStoredRecords(this, false);
                    if (entity is PolyfaceMesh polyface) polyface.ValidateStoredRecords(this, false);
                    if (entity is Section section) section.Validate(this);
                    else if (entity is StoredTable table) table.ValidateIncoming(this);
                    else if (entity is Insert insert) visit(insert.Block);
                    else if (entity is Dimension dimension) visit(dimension.Block);
                }
            };
            visit(root);
        }
        internal DxfObject StoredTableHandleTarget(string handle)
        {
            DxfObject target = this.GetObjectByHandle(handle);
            if (target != null) return target;
            if (!ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value) || value == 0) return null;
            foreach (DxfObject item in this.RetainedMetadataObjects())
                if (ulong.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong candidate) && candidate == value) return item;
            return null;
        }
        // One predicate serves entity, containing-block, attribute synchronization and viewport replacement.
        // It expands actual retained members; referenced block definitions are not owned descendants.
        internal bool StoredTableReferencesRemoval(DxfObject root)
        {
            var removed = new HashSet<DxfObject>(new MetadataIdentityComparer());
            Action<DxfObject> add = item => { foreach (var member in ObjectMetadataMembers(item)) removed.Add(member); };
            add(root);
            Block block = root as Block ?? (root as Layout)?.AssociatedBlock;
            if (block != null)
            {
                add(block);
                removed.Add(block.Record);
                foreach (var entity in block.Entities) add(entity);
                foreach (var definition in block.AttributeDefinitions.Values) add(definition);
            }
            if (this.OpaqueEntityReferencesRemoval(removed)) return true;
            if (this.StoredPolylineReferencesRemoval(removed)) return true;
            if (this.SectionReferencesRemoval(removed)) return true;
            foreach (DxfObject item in removed) if (SunReferences.Get(item) != null) return true;
            foreach (DxfDatabaseObject content in this.AddedObjects.Values.OfType<DxfDatabaseObject>().Where(item => item.CodeName == "TABLECONTENT" || item.CodeName == "TABLEGEOMETRY" || item.CodeName == "CELLSTYLEMAP"))
            {
                if (content is DxfStoredTableContent stored && stored.References.Any(removed.Contains)) return true;
                if (content is DxfStoredTableGeometry geometry && geometry.References.Any(removed.Contains)) return true;
                if (content is DxfStoredCellStyleMap map && map.References.Any(removed.Contains)) return true;
                if (content is DxfOpaqueObject opaque)
                    foreach (DxfTag tag in opaque.Tags)
                        if (DxfObjectDatabase.IsReference(tag) && removed.Contains(this.StoredTableHandleTarget((string)tag.Value))) return true;
                var owners = new HashSet<DxfObject>(new MetadataIdentityComparer());
                for (DxfObject owner = content.Owner; owner != null && owners.Add(owner); owner = owner.Owner)
                    if (removed.Contains(owner)) return true;
            }
            foreach (DxfDatabaseObject study in this.AddedObjects.Values.OfType<DxfDatabaseObject>().Where(item => item.CodeName == "SUNSTUDY"))
            {
                if (study is DxfStoredSunStudy stored && stored.References.Any(removed.Contains)) return true;
                if (study is DxfOpaqueObject opaque && opaque.Tags.Where(DxfObjectDatabase.IsReference)
                    .Select(tag => this.StoredTableHandleTarget((string)tag.Value)).Any(target => target != null && removed.Contains(target))) return true;
                var owners = new HashSet<DxfObject>(new MetadataIdentityComparer());
                for (DxfObject owner = study.Owner; owner != null && owners.Add(owner); owner = owner.Owner)
                    if (removed.Contains(owner)) return true;
            }
            foreach (DxfStoredField field in this.AddedObjects.Values.OfType<DxfStoredField>())
            {
                if (field.References.Any(removed.Contains)) return true;
                var ancestors = new HashSet<DxfObject>(new MetadataIdentityComparer());
                for (DxfObject owner = field.Owner; owner != null; owner = owner.Owner)
                    if (!ancestors.Add(owner) || removed.Contains(owner)) return true;
            }
            foreach (DxfDatabaseObject association in this.AddedObjects.Values.OfType<DxfDatabaseObject>().Where(item => item.CodeName == "DIMASSOC"))
            {
                if (association is DxfStoredDimAssoc typed && typed.References.Any(removed.Contains)) return true;
                if (association is DxfOpaqueObject opaque && opaque.Tags.Where(DxfObjectDatabase.IsReference)
                    .Select(tag => this.StoredTableHandleTarget((string)tag.Value)).Any(target => target != null && removed.Contains(target))) return true;
                var owners = new HashSet<DxfObject>(new MetadataIdentityComparer());
                for (DxfObject owner = association.Owner; owner != null && owners.Add(owner); owner = owner.Owner)
                    if (removed.Contains(owner)) return true;
            }
            foreach (StoredTable table in this.AddedObjects.Values.OfType<StoredTable>())
                if (!removed.Contains(table) && table.References.Any(removed.Contains)) return true;
            foreach (DxfTableStyle style in this.AddedObjects.Values.OfType<DxfTableStyle>())
                if (!removed.Contains(style) && style.References.Any(removed.Contains)) return true;
            return false;
        }
    }
}
