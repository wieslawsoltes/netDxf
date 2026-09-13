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

namespace netDxf.IO
{
    /// <summary>The primitive value representation of a modern DXF group code.</summary>
    public enum DxfTagValueType
    {
        /// <summary>A string value, including subclass, control and comment strings.</summary>
        String,
        /// <summary>A finite IEEE 754 double-precision value.</summary>
        Double,
        /// <summary>A signed 16-bit integer.</summary>
        Int16,
        /// <summary>A signed 32-bit integer.</summary>
        Int32,
        /// <summary>A signed 64-bit integer.</summary>
        Int64,
        /// <summary>A Boolean value.</summary>
        Boolean,
        /// <summary>An opaque byte sequence.</summary>
        BinaryData,
        /// <summary>A hexadecimal handle represented by a string.</summary>
        Handle
    }

    /// <summary>Reference semantics associated with a DXF handle group code.</summary>
    /// <remarks>
    /// This classification does not determine record ownership from tag position. In particular,
    /// a group 330 inside an ACAD_REACTORS control group is not the containing object's owner.
    /// Context is still required; this API does not resolve or remap any reference.
    /// </remarks>
    public enum DxfHandleKind
    {
        /// <summary>The group code does not contain a handle.</summary>
        None,
        /// <summary>An object's own identity, groups 5 and 105.</summary>
        ObjectIdentity,
        /// <summary>An arbitrary handle, not translated by INSERT or XREF, groups 320 through 329.</summary>
        Arbitrary,
        /// <summary>A soft pointer, groups 330 through 339.</summary>
        SoftPointer,
        /// <summary>A hard pointer, groups 340 through 349, 390 through 399 and 480 through 481.</summary>
        HardPointer,
        /// <summary>A soft ownership reference, groups 350 through 359.</summary>
        SoftOwner,
        /// <summary>A hard ownership reference, groups 360 through 369.</summary>
        HardOwner,
        /// <summary>An extended-data handle, group 1005; XData translation has its own rules.</summary>
        XData
    }

    /// <summary>Primitive encoding metadata for the group codes recognized by the modern codecs.</summary>
    /// <remarks>
    /// Encoding recognition is not permission to use a field in every record or DXF version.
    /// Group 5 is classified as a handle without context. The obsolete DIMBLK name in a
    /// DIMSTYLE table entry is an exception; use DxfTag.CreateDimensionStyleArrowName for that field.
    /// Negative AutoLISP-only codes and gaps without a known encoding are not supported.
    /// The broad legacy codec ranges within 1000 through 1071 are retained for compatibility;
    /// this is not a declaration that every code in those ranges is a defined XData field.
    /// </remarks>
    public static class DxfGroupCode
    {
        /// <summary>Returns the primitive type, or throws for an unrecognized group code.</summary>
        /// <param name="code">DXF group code.</param>
        /// <returns>The encoded value type.</returns>
        public static DxfTagValueType GetValueType(short code)
        {
            if (TryGetValueType(code, out DxfTagValueType type)) return type;
            throw new ArgumentOutOfRangeException(nameof(code), code, "No supported DXF value encoding exists for this group code.");
        }

        /// <summary>Tries to classify a modern codec group code without throwing.</summary>
        /// <param name="code">DXF group code.</param>
        /// <param name="type">The value type when true is returned; unspecified otherwise.</param>
        /// <returns>True when the group's encoding is known.</returns>
        public static bool TryGetValueType(short code, out DxfTagValueType type)
        {
            if (GetHandleKind(code) != DxfHandleKind.None) type = DxfTagValueType.Handle;
            else if ((code >= 0 && code <= 9) || (code >= 100 && code <= 102) ||
                     (code >= 300 && code <= 309) || (code >= 410 && code <= 419) ||
                     (code >= 430 && code <= 439) || (code >= 470 && code <= 479) ||
                     code == 999 || (code >= 1000 && code <= 1003) || (code >= 1006 && code <= 1009))
                type = DxfTagValueType.String;
            else if ((code >= 10 && code <= 59) || (code >= 110 && code <= 149) ||
                     (code >= 210 && code <= 239) || (code >= 460 && code <= 469) ||
                     (code >= 1010 && code <= 1059)) type = DxfTagValueType.Double;
            else if ((code >= 60 && code <= 79) || (code >= 170 && code <= 179) ||
                     (code >= 270 && code <= 289) || (code >= 370 && code <= 389) ||
                     (code >= 400 && code <= 409) || (code >= 1060 && code <= 1070))
                type = DxfTagValueType.Int16;
            else if ((code >= 90 && code <= 99) || (code >= 420 && code <= 429) ||
                     (code >= 440 && code <= 459) || code == 1071) type = DxfTagValueType.Int32;
            else if (code >= 160 && code <= 169) type = DxfTagValueType.Int64;
            else if (code >= 290 && code <= 299) type = DxfTagValueType.Boolean;
            else if ((code >= 310 && code <= 319) || code == 1004) type = DxfTagValueType.BinaryData;
            else { type = default(DxfTagValueType); return false; }
            return true;
        }

        /// <summary>Classifies a handle code; ordinary or unknown codes return None.</summary>
        /// <param name="code">DXF group code.</param>
        /// <returns>Handle semantics, requiring additional record/control-group context.</returns>
        public static DxfHandleKind GetHandleKind(short code)
        {
            if (code == 5 || code == 105) return DxfHandleKind.ObjectIdentity;
            if (code >= 320 && code <= 329) return DxfHandleKind.Arbitrary;
            if (code >= 330 && code <= 339) return DxfHandleKind.SoftPointer;
            if ((code >= 340 && code <= 349) || (code >= 390 && code <= 399) ||
                code == 480 || code == 481) return DxfHandleKind.HardPointer;
            if (code >= 350 && code <= 359) return DxfHandleKind.SoftOwner;
            if (code >= 360 && code <= 369) return DxfHandleKind.HardOwner;
            return code == 1005 ? DxfHandleKind.XData : DxfHandleKind.None;
        }
    }
}
