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

namespace netDxf
{
    /// <summary>A CLASS definition. Unlike drawing objects, CLASS records have no handle or owner.</summary>
    public sealed class DxfClass : ICloneable
    {
        private string applicationName;
        private int? instanceCount;

        /// <summary>Creates a class definition with stable DXF and C++ identities.</summary>
        /// <param name="name">Unique DXF record name (group 1).</param>
        /// <param name="cppClassName">Unique C++ class name (group 2).</param>
        /// <param name="applicationName">Defining application's name (group 3); may be empty.</param>
        public DxfClass(string name, string cppClassName, string applicationName)
        {
            ValidateString(name, nameof(name), false);
            ValidateString(cppClassName, nameof(cppClassName), false);
            this.Name = name;
            this.CppClassName = cppClassName;
            this.ApplicationName = applicationName;
        }

        /// <summary>Gets the immutable, case-sensitive DXF record name.</summary>
        public string Name { get; }
        /// <summary>Gets the immutable, case-sensitive C++ class name.</summary>
        public string CppClassName { get; }
        /// <summary>Gets or sets the defining application name. No application is loaded or executed.</summary>
        public string ApplicationName
        {
            get { return this.applicationName; }
            set { ValidateString(value, nameof(value), true); this.applicationName = value; }
        }
        /// <summary>Gets or sets the raw 32-bit group 90 proxy-capability bit mask.</summary>
        /// <remarks>Unknown bits are retained; this model does not execute proxy operations.</remarks>
        public int ProxyFlags { get; set; }
        /// <summary>Gets or sets the optional nonnegative declared instance count (group 91).</summary>
        /// <remarks>
        /// This is declaration metadata, not proof that instances were loaded. The writer recomputes
        /// counts for its generated raster classes, preserves other supplied counts in 2004+,
        /// and omits this derived field in the 2000 export profile. Null retains field absence.
        /// </remarks>
        public int? InstanceCount
        {
            get { return this.instanceCount; }
            set
            {
                if (value.HasValue && value.Value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "A class instance count cannot be negative.");
                this.instanceCount = value;
            }
        }
        /// <summary>Gets or sets whether this class was unloaded when the source file was written (group 280).</summary>
        public bool WasProxy { get; set; }
        /// <summary>Gets or sets whether instances are graphical entities rather than OBJECTS records (group 281).</summary>
        public bool IsEntity { get; set; }
        /// <summary>Creates independently editable definition metadata, retaining both identities.</summary>
        /// <returns>A copy without shared mutable data.</returns>
        public object Clone() { return this.MemberwiseClone(); }

        private static void ValidateString(string value, string parameter, bool allowEmpty)
        {
            if (value == null) throw new ArgumentNullException(parameter);
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("CLASS strings must have valid content and cannot contain NUL or line terminators.", parameter);
        }
    }
}
