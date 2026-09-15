using System;
using netDxf.Blocks;
using netDxf.Entities;

namespace netDxf.IO
{
    internal partial class DxfWriter
    {
        private void ValidatePolyfaceMeshOutput()
        {
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    PolyfaceMesh mesh = entity as PolyfaceMesh;
                    if (mesh == null) continue;
                    try { mesh.ValidateFaceIndexes(); }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidOperationException("Invalid POLYFACE face indices in block '" + block.Name + "'.", exception);
                    }
                }
        }
    }
}
