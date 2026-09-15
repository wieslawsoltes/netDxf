using System;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateHatchSourceRelations()
        {
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Hatch hatch)
                        foreach (HatchBoundaryPath path in hatch.BoundaryPaths)
                            foreach (EntityObject source in path.Entities)
                                if (!hatch.Associative || source == null || ReferenceEquals(source, hatch)
                                    || !ReferenceEquals(source.Owner, block) || !ReferenceEquals(this.doc.GetObjectByHandle(source.Handle), source))
                                    throw new InvalidOperationException("HATCH source boundaries must be registered entities in the same block before output.");
        }
    }
}
