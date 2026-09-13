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
using System.Globalization;

namespace netDxf.IO
{
    /// <summary>An immutable, validated DXF group-code/value pair.</summary>
    /// <remarks>
    /// Values retain their exact CLR type. No integer narrowing, culture-dependent coercion,
    /// handle remapping, Unicode escape decoding, or geometry interpretation is performed.
    /// Byte arrays are copied on input and output. This is a typed tag, not its lexical spelling.
    /// CR/LF are retained for binary strings; a text transport must reject them before writing.
    /// Record-specific constraints and historical version eligibility require a higher-level schema.
    /// </remarks>
    public sealed class DxfTag
    {
        private readonly object value;

        /// <summary>Creates a typed tag with independent storage for binary data.</summary>
        /// <param name="code">A group code supported by the modern codecs.</param>
        /// <param name="value">A value with the exact CLR type required by the code.</param>
        public DxfTag(short code, object value)
        {
            this.ValueType = DxfGroupCode.GetValueType(code);
            if (value == null) throw new ArgumentNullException(nameof(value));
            Type expected;
            switch (this.ValueType)
            {
                case DxfTagValueType.String:
                case DxfTagValueType.Handle: expected = typeof(string); break;
                case DxfTagValueType.Double: expected = typeof(double); break;
                case DxfTagValueType.Int16: expected = typeof(short); break;
                case DxfTagValueType.Int32: expected = typeof(int); break;
                case DxfTagValueType.Int64: expected = typeof(long); break;
                case DxfTagValueType.Boolean: expected = typeof(bool); break;
                default: expected = typeof(byte[]); break;
            }
            if (value.GetType() != expected)
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                    "DXF group code {0} requires a value of type {1}.", code, expected.Name), nameof(value));
            if (value is double real && (double.IsNaN(real) || double.IsInfinity(real)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "DXF numeric values must be finite in this library.");
            if (value is string text)
            {
                if (text.IndexOf('\0') >= 0)
                    throw new ArgumentException("A DXF tag string cannot contain NUL.", nameof(value));
                if (this.ValueType == DxfTagValueType.Handle &&
                    (text.Length == 0 || text.Length > 16 || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out _)))
                    throw new ArgumentException("A DXF handle requires one through sixteen ASCII hexadecimal digits.", nameof(value));
            }
            this.Code = code;
            this.value = value is byte[] bytes ? bytes.Clone() : value;
        }

        /// <summary>Gets the group code.</summary>
        public short Code { get; }

        /// <summary>Gets the primitive encoding type.</summary>
        public DxfTagValueType ValueType { get; }

        /// <summary>Gets the immutable value, or a new array for a binary tag.</summary>
        public object Value { get { return this.value is byte[] bytes ? bytes.Clone() : this.value; } }

        /// <summary>Gets the handle category; it does not resolve the reference or its context.</summary>
        public DxfHandleKind HandleKind { get { return DxfGroupCode.GetHandleKind(this.Code); } }

        // Only trusted codecs access the backing array. They must never mutate it.
        internal object RawValue { get { return this.value; } }
    }
}
