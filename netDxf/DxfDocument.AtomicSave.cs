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
using System.Threading;
using netDxf.IO;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        /// <summary>Saves to a sibling staging file and commits with a filesystem replacement.</summary>
        /// <param name="file">Destination file. Parent directories must already exist.</param>
        /// <param name="isBinary">True for binary output, false for text output.</param>
        /// <param name="cancellationToken">Checked before staging, after serialization and before commit.</param>
        /// <remarks>
        /// This opt-in API always propagates exceptions in Debug and Release. Existing Save overloads
        /// retain their original behavior. Serialization failures leave an existing destination intact,
        /// or leave an absent destination absent. Unsupported replacement filesystems are not emulated
        /// with a destructive delete/copy fallback. Destination symlinks, directories and read-only files
        /// are rejected. File metadata, links to the old inode, power-loss durability, concurrent-writer
        /// isolation and hostile changes to parent directories are not guaranteed. Temporary-file removal
        /// is best effort on failure. Name and working folder are restored on failure, but writer changes
        /// to handles, registrations and other document state are not rolled back. Cancellation does not
        /// interrupt typed serialization itself and is not observed after successful commit.
        /// </remarks>
        public void SaveAtomic(string file, bool isBinary = false,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(file);
            string previousName = this.name;
            string previousFolder = this.supportFolders.WorkingFolder;
            bool committed = false;
            try
            {
                DxfAtomicFile.Write(destination, stream =>
                {
                    this.name = Path.GetFileNameWithoutExtension(destination);
                    this.supportFolders.WorkingFolder = Path.GetDirectoryName(destination);
                    new DxfWriter().Write(stream, this, isBinary);
                }, cancellationToken);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    this.name = previousName;
                    this.supportFolders.WorkingFolder = previousFolder;
                }
            }
        }
    }
}
