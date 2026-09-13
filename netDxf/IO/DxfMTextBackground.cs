#region netDxf library licensed under the MIT License
//
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
#endregion

using System;
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal partial class DxfReader
    {
        // Called only inside AcDbMText: foreground 420/430/440 belong to AcDbEntity.
        private bool TryReadMTextBackground(ref MTextBackgroundFill background)
        {
            short code = this.chunk.Code;
            if (code != 90 && code != 45 && code != 63 && code != 421 && code != 431 && code != 441)
                return false;

            if (background == null)
                background = new MTextBackgroundFill { Flags = MTextBackgroundFillFlags.None, ScaleFactor = null, ColorIndex = null };
            try
            {
                switch (code)
                {
                    case 90: background.Flags = (MTextBackgroundFillFlags) this.chunk.ReadInt(); break;
                    case 45: background.ScaleFactor = this.chunk.ReadDouble(); break;
                    case 63: background.ColorIndex = this.chunk.ReadShort(); break;
                    case 421: background.TrueColor = this.chunk.ReadInt(); break;
                    case 431: background.ColorName = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                    case 441: background.Transparency = this.chunk.ReadInt(); break;
                }
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(string.Format("Invalid MTEXT background value for group code {0} at position {1}.", code, this.chunk.CurrentPosition), exception);
            }
            return true;
        }
    }

    internal partial class DxfWriter
    {
        private static void ValidateMTextBackgroundVersion(MTextBackgroundFill background, DxfVersion version)
        {
            if (background == null) return;
            // Deliberately conservative DXF profile; do not infer DXF legality from native DWG fields.
            if (version < DxfVersion.AutoCad2007)
                throw new NotSupportedException("MTEXT background data requires the AutoCAD 2007 or later DXF writer profile. Remove BackgroundFill explicitly before down-saving.");
            if ((background.Flags & MTextBackgroundFillFlags.TextFrame) != 0 && version < DxfVersion.AutoCad2018)
                throw new NotSupportedException("MTEXT text frames require the AutoCAD 2018 DXF writer profile. Remove the frame flag explicitly before down-saving.");
        }

        private void ValidateMTextBackgroundVersions()
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            if (version >= DxfVersion.AutoCad2018) return;
            // Model/paper space and referenced definitions all live in the document's block registry.
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    MText text = entity as MText;
                    if (text != null) ValidateMTextBackgroundVersion(text.BackgroundFill, version);
                }
        }

        private void WriteMTextBackground(MTextBackgroundFill background)
        {
            if (background == null) return;
            ValidateMTextBackgroundVersion(background, this.doc.DrawingVariables.AcadVer);
            this.chunk.Write(90, (int) background.Flags);
            // Active fills need the companion tags in interoperable output. Preserve missing
            // fields in the in-memory source; materialize defaults only in serialized output.
            bool active = ((int) background.Flags & 3) != 0;
            if (active || background.ScaleFactor.HasValue) this.chunk.Write(45, background.ScaleFactor ?? 1.5);
            if (active || background.ColorIndex.HasValue) this.chunk.Write(63, background.ColorIndex ?? (short) 7);
            if (background.TrueColor.HasValue) this.chunk.Write(421, background.TrueColor.Value);
            if (background.ColorName != null) this.chunk.Write(431, this.EncodeNonAsciiCharacters(background.ColorName));
            if (background.Transparency.HasValue) this.chunk.Write(441, background.Transparency.Value);
        }
    }
}
