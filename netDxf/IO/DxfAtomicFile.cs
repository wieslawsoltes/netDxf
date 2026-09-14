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

namespace netDxf.IO
{
    // Atomicity is delegated to one same-directory filesystem replacement, never
    // to delete+copy. This protects destination bytes, not the mutable document.
    internal static class DxfAtomicFile
    {
        internal static void Write(string path, Action<FileStream> serialize, CancellationToken cancellationToken)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (serialize == null) throw new ArgumentNullException(nameof(serialize));
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.GetFullPath(path);
            bool existed = ValidateDestination(destination);
            string temporary = Path.Combine(Path.GetDirectoryName(destination),
                ".netdxf-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 65536, FileOptions.None))
                {
                    created = true;
                    serialize(stream);
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Flush(true);
                }
                // Cancellation after this checkpoint cannot undo a committed rename.
                cancellationToken.ThrowIfCancellationRequested();
                bool stillExists = ValidateDestination(destination);
                if (stillExists != existed)
                    throw new IOException("The DXF destination appeared or disappeared while saving; no replacement was attempted.");
                if (existed) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                created = false;
            }
            finally
            {
                if (created)
                {
                    // Do not hide the serialization/commit failure with cleanup failure.
                    // Failure to remove this random staging file is not destination loss.
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static bool ValidateDestination(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return false; }
            // Do not catch DirectoryNotFoundException or access failures: they are
            // path errors, not evidence that an absent destination is safe to create.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new NotSupportedException("Atomic DXF replacement does not follow destination symbolic links or reparse points.");
            if ((attributes & FileAttributes.Directory) != 0)
                throw new IOException("An atomic DXF destination must be a file, not a directory.");
            if ((attributes & FileAttributes.ReadOnly) != 0)
                throw new UnauthorizedAccessException("The atomic DXF destination is read-only.");
            return true;
        }
    }
}
