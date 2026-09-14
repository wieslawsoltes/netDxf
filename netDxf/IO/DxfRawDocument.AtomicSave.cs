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

using System.Threading;

namespace netDxf.IO
{
    public sealed partial class DxfRawDocument
    {
        /// <summary>Atomically saves a file using this document's current transport.</summary>
        /// <param name="file">Destination file in an existing directory.</param>
        /// <param name="cancellationToken">Cancellation before commit.</param>
        /// <remarks>See the explicit-transport overload for filesystem and failure semantics.</remarks>
        public void SaveAtomic(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            this.SaveAtomic(file, this.IsBinary, cancellationToken);
        }

        /// <summary>Stages complete raw output before committing a same-directory file replacement.</summary>
        /// <param name="file">Destination file in an existing directory.</param>
        /// <param name="binary">Requested transport; version and encoding are not changed.</param>
        /// <param name="cancellationToken">Cancellation during serialization/copy and before commit.</param>
        /// <remarks>
        /// Unchanged same-transport saves preserve exact input bytes. Conversion, encoding, budget,
        /// cancellation and pre-commit IO errors do not truncate the destination. Exceptions propagate
        /// in both configurations. Destination symlinks, read-only files and directories are rejected.
        /// No delete/copy fallback is used. Atomic replacement relies on filesystem support; file metadata,
        /// old hard links, concurrent-writer isolation and power-loss durability are not guaranteed.
        /// Parent-directory races are outside this API's contract. Staging-file cleanup is best effort.
        /// </remarks>
        public void SaveAtomic(string file, bool binary,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            DxfAtomicFile.Write(file, stream => this.Save(stream, binary, cancellationToken), cancellationToken);
        }
    }
}
