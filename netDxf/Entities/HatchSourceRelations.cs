using System;
using netDxf.Blocks;

namespace netDxf.Entities
{
    internal static class HatchSourceRelations
    {
        internal static void ValidateOwner(Hatch hatch, Block block, DxfDocument document = null)
        {
            foreach (HatchBoundaryPath path in hatch.BoundaryPaths) ValidatePathOwner(hatch, path, block, document);
        }
        internal static void ValidatePathOwner(Hatch hatch, HatchBoundaryPath path, Block block, DxfDocument document = null)
        {
            if (path.ContainingHatch != null && !ReferenceEquals(path.ContainingHatch, hatch))
                throw new ArgumentException("A HATCH boundary path instance cannot belong to multiple hatches; clone the path before reuse.");
            document = document ?? block?.Record.Owner?.Owner;
            foreach (EntityObject entity in path.Entities)
            {
                if (entity == null || ReferenceEquals(entity, hatch) || block != null && entity.Owner != null && !ReferenceEquals(entity.Owner, block))
                    throw new ArgumentException("HATCH source boundary entities must belong to the same block as their hatch.");
                if (document != null && entity.Owner == null) document.ValidateStoredTableEntityAdoption(entity);
            }
        }
    }
}
