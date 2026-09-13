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
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace netDxf.Collections
{
    /// <summary>
    /// Represents a list of support folders for the document.
    /// </summary>
    public class SupportFolders :
        IList<string>
    {
        #region private fields

        private readonly List<string> folders;
        private string workingFolder;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of <c>SupportFolders</c> class.
        /// </summary>
        public SupportFolders()
        {
            this.folders = new List<string>();
            this.workingFolder = Environment.CurrentDirectory;
        }

        /// <summary>
        /// Initializes a new instance of <c>SupportFolders</c> class.
        /// </summary>
        /// <param name="folders">The collection whose elements should be added to the list. The items in the collection cannot be null.</param>
        public SupportFolders(IEnumerable<string> folders)
        {
            if (folders == null)
            {
                throw new ArgumentNullException(nameof(folders));
            }
            this.folders = new List<string>(folders);
            this.workingFolder = Environment.CurrentDirectory;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or sets the base folder used to resolve relative paths of external references.
        /// </summary>
        /// <remarks>
        /// Defaults to the process current directory captured when this collection was created.
        /// An absolute folder is recommended for independent concurrent document lookups.
        /// A relative folder is resolved once per lookup without changing the process directory.
        /// The folder need not exist to search an absolute resource or absolute support folder.
        /// </remarks>
        public string WorkingFolder
        {
            get { return this.workingFolder; }
            set { this.workingFolder = value; }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Finds a file relative to the working folder, then by name in the ordered support folders.
        /// </summary>
        /// <param name="file">An absolute or relative file path; null or empty returns an empty string.</param>
        /// <returns>The full path of the first existing file, or an empty string if none is found.</returns>
        /// <remarks>
        /// An existing supplied path takes precedence over every support folder. Otherwise its file
        /// name is searched in collection order. Relative support folders use WorkingFolder as their
        /// base. Empty entries admitted by the collection constructor or AddRange are ignored.
        /// Lookup never changes Environment.CurrentDirectory and does not open the resource contents.
        /// Configure separate collections, or synchronize mutations, when using them concurrently.
        /// This is path resolution, not a sandbox: absolute paths, parent traversal and links are allowed.
        /// </remarks>
        public string FindFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return string.Empty;

            string basePath = Path.GetFullPath(this.workingFolder);
            string candidate = ResolvePath(file, basePath);
            if (File.Exists(candidate)) return candidate;

            string name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name)) return string.Empty;
            foreach (string folder in this.folders)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                candidate = ResolvePath(Path.Combine(folder, name), basePath);
                if (File.Exists(candidate)) return candidate;
            }
            return string.Empty;
        }

        // Explicit-base resolution also supports netstandard2.0 and .NET Framework, where
        // Path.GetFullPath(path, basePath) is unavailable. Never emulate it by changing CWD.
        private static string ResolvePath(string path, string basePath)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (path.IndexOf('\0') >= 0) throw new ArgumentException("A path cannot contain NUL.", nameof(path));
            if (path.Length == 0) return basePath;
            if (Path.DirectorySeparatorChar != '\\')
                return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

            bool rooted = IsWindowsSeparator(path[0]);
            bool drive = path.Length >= 2 && path[1] == ':' && IsDriveLetter(path[0]);
            if ((rooted && path.Length >= 2 && IsWindowsSeparator(path[1])) ||
                (drive && path.Length >= 3 && IsWindowsSeparator(path[2])))
                return Path.GetFullPath(path);

            string root = Path.GetPathRoot(basePath);
            string combined;
            if (rooted)
            {
                combined = Path.Combine(root, path.Substring(1));
            }
            else if (drive)
            {
                int driveOffset = IsDevicePath(basePath) ? 4 : 0;
                bool sameDrive = root.Length > driveOffset + 1 && root[driveOffset + 1] == ':' &&
                    char.ToUpperInvariant(root[driveOffset]) == char.ToUpperInvariant(path[0]);
                if (sameDrive) combined = Path.Combine(basePath, path.Substring(2));
                else
                {
                    string device = IsDevicePath(basePath) ? basePath.Substring(0, 4) : string.Empty;
                    combined = device + path.Substring(0, 2) + "\\" + path.Substring(2);
                }
            }
            else
            {
                combined = Path.Combine(basePath, path);
            }
            // Fully qualified device paths are normally verbatim; a RELATIVE path joined to
            // a device base still needs its dot segments resolved within that base's root.
            return IsDevicePath(combined) ? NormalizeDeviceRelativePath(combined) : Path.GetFullPath(combined);
        }

        private static bool IsWindowsSeparator(char value)
        {
            return value == '\\' || value == '/';
        }

        private static bool IsDriveLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsDevicePath(string path)
        {
            return path.Length >= 4 && IsWindowsSeparator(path[0]) && IsWindowsSeparator(path[1]) &&
                (path[2] == '?' || path[2] == '.') && IsWindowsSeparator(path[3]);
        }

        private static string NormalizeDeviceRelativePath(string path)
        {
            string root = Path.GetPathRoot(path);
            List<string> segments = new List<string>();
            string rest = path.Substring(root.Length);
            foreach (string segment in rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                }
                else segments.Add(segment);
            }
            string result = root + (segments.Count == 0 ? string.Empty :
                (IsWindowsSeparator(root[root.Length - 1]) ? string.Empty : "\\") + string.Join("\\", segments));
            if (IsWindowsSeparator(path[path.Length - 1]) && !IsWindowsSeparator(result[result.Length - 1])) result += "\\";
            return result;
        }

        #endregion

        #region implements IList<string>

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <returns>
        /// The element at the specified index.
        /// </returns>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        public string this[int index]
        {
            get { return this.folders[index]; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(value));
                }
                this.folders[index] = value;
            }
        }

        /// <summary>
        /// Gets the number of elements contained in the list.
        /// </summary>
        /// <returns>The number of elements contained in the list.</returns>
        public int Count
        {
            get { return this.folders.Count; }
        }

        /// <summary>
        /// Returns if the list is read only.
        /// </summary>
        /// <returns>Return always true.</returns>
        public bool IsReadOnly
        {
            get {return false;}
        }

        /// <summary>
        /// Returns an enumerator that iterates through the list.
        /// </summary>
        /// <returns>The enumerator for the list.</returns>
        public IEnumerator<string> GetEnumerator()
        {
            return this.folders.GetEnumerator();
        }

        /// <summary>
        /// Returns an enumerator that iterates through the list.
        /// </summary>
        /// <returns>The enumerator for the list.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.folders.GetEnumerator();
        }

        /// <summary>
        /// Adds an item to the list.
        /// </summary>
        /// <param name="item">Folder path to add to the list. The item cannot be null.</param>
        public void Add(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentNullException(nameof(item));
            }
            this.folders.Add(item);
        }

        /// <summary>
        /// Adds the elements of the collection to the list.
        /// </summary>
        /// <param name="collection">The collection whose elements should be added to the end of the list. The items in the collection cannot be null.</param>
        public void AddRange(IEnumerable<string> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            foreach (string s in collection)
            {
                this.folders.Add(s);
            }
        }

        /// <summary>
        /// Removes all elements from the list.
        /// </summary>
        public void Clear()
        {
            this.folders.Clear();
        }

        /// <summary>
        /// Determines whether an element is in the list.
        /// </summary>
        /// <param name="item">The object to locate in the list. The value cannot be null.</param>
        /// <returns>True if the item is found in the list; otherwise, false.</returns>
        public bool Contains(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentNullException(nameof(item));
            }
            return this.folders.Contains(item);
        }

        /// <summary>
        /// Copies the entire list to a compatible one-dimensional array, starting at the specified index of the target array.
        /// </summary>
        /// <param name="array">The one-dimensional array that is the destination of the elements copied from list. The array must have zero-based indexing.</param>
        /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
        public void CopyTo(string[] array, int arrayIndex)
        {
            this.folders.CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the list.
        /// </summary>
        /// <param name="item">The object to remove from the list. The value cannot be null.</param>
        /// <returns>True if the item is successfully removed; otherwise, false. This method also returns false the item was not found in the list.</returns>
        public bool Remove(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentNullException(nameof(item));
            }
            return this.folders.Remove(item);
        }

        /// <summary>
        /// Determines the index of a specific item in the list.
        /// </summary>
        /// <param name="item">The object to locate in the list.</param>
        /// <returns>The index of <paramref name="item"/> if found in the list; otherwise, -1.</returns>
        public int IndexOf(string item)
        {
            return this.folders.IndexOf(item);
        }

        /// <summary>
        /// Inserts an item to the list at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which the item should be inserted.</param>
        /// <param name="item">The object to insert into the list.</param>
        public void Insert(int index, string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                throw new ArgumentNullException(nameof(item));
            }
            this.folders.Insert(index, item);
        }

        /// <summary>
        /// Removes the item at the specified index from the list.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        public void RemoveAt(int index)
        {
            this.folders.RemoveAt(index);
        }

        #endregion
    }
}