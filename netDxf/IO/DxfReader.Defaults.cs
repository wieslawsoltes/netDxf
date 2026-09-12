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

using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Idempotent: collections parsed from TABLES retain their handles and metadata.
        private void EnsureTableCollections()
        {
            // check if all table collections has been created
            if (this.doc.ApplicationRegistries == null)
            {
                this.doc.ApplicationRegistries = new ApplicationRegistries(this.doc);
            }
            if (this.doc.Blocks == null)
            {
                this.doc.Blocks = new BlockRecords(this.doc);
            }
            if (this.doc.DimensionStyles == null)
            {
                this.doc.DimensionStyles = new DimensionStyles(this.doc);
            }
            if (this.doc.Layers == null)
            {
                this.doc.Layers = new Layers(this.doc);
            }
            if (this.doc.Linetypes == null)
            {
                this.doc.Linetypes = new Linetypes(this.doc);
            }
            if (this.doc.TextStyles == null)
            {
                this.doc.TextStyles = new TextStyles(this.doc);
            }
            if (this.doc.ShapeStyles == null)
            {
                this.doc.ShapeStyles = new ShapeStyles(this.doc);
            }
            if (this.doc.UCSs == null)
            {
                this.doc.UCSs = new UCSs(this.doc);
            }
            if (this.doc.Views == null)
            {
                this.doc.Views = new Views(this.doc);
            }
            if (this.doc.VPorts == null)
            {
                this.doc.VPorts = new VPorts(this.doc);
            }
        }

        // A real named-object dictionary takes precedence. Synthesize only when absent.
        private void EnsureObjectCollections()
        {
            if (this.doc.Layouts == null)
            {
                this.CreateObjectCollection(new DictionaryObject(null));
            }
            if (this.doc.RasterVariables == null)
            {
                this.doc.RasterVariables = new RasterVariables(this.doc);
            }
        }
    }
}
