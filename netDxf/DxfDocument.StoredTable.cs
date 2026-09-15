// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal void ValidateStoredTableEntityAdoption(EntityObject entity)
        {
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
            if (this.SectionReferencesRemoval(removed)) return true;
            foreach (DxfObject item in removed) if (SunReferences.Get(item) != null) return true;
            foreach (StoredTable table in this.AddedObjects.Values.OfType<StoredTable>())
                if (!removed.Contains(table) && table.References.Any(removed.Contains)) return true;
            foreach (DxfTableStyle style in this.AddedObjects.Values.OfType<DxfTableStyle>())
                if (!removed.Contains(style) && style.References.Any(removed.Contains)) return true;
            return false;
        }
    }
}
