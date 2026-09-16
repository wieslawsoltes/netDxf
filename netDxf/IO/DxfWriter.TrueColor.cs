// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        // Group 420 is RGB, not the AcCmColor tagged 0xC2RRGGBB integer used by
        // other schemas. Keep the public packed-color API and opaque tags intact.
        private void WriteTrueColor(AciColor color)
        {
            if (color.UseTrueColor && this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2004)
                this.chunk.Write(420, (color.R << 16) | (color.G << 8) | color.B);
        }
    }
}
