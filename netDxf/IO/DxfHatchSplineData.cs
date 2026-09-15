// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        private void ValidateHatchSplineData()
        {
            // Registered block definitions are all exported, including unreferenced
            // and paper-space blocks. Validate before preprocessing or stream writes.
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                    if (entity is Hatch hatch)
                        foreach (HatchBoundaryPath path in hatch.BoundaryPaths)
                            foreach (HatchBoundaryPath.Edge edge in path.Edges)
                                if (edge is HatchBoundaryPath.Spline spline)
                                    HatchSplineData.Validate(spline);
        }
    }
}
