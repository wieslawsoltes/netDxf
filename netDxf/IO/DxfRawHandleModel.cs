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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace netDxf.IO
{
    /// <summary>Context-qualified interpretation of an exposed raw handle slot.</summary>
    public enum DxfRawHandleRole
    {
        /// <summary>An object's group 5, or a DIMSTYLE entry's group 105.</summary>
        Identity,
        /// <summary>The common group 330 before subclass data, outside control groups.</summary>
        Owner,
        /// <summary>An ordinary soft pointer.</summary>
        SoftPointer,
        /// <summary>An ordinary hard pointer.</summary>
        HardPointer,
        /// <summary>An ordinary soft ownership link.</summary>
        SoftOwner,
        /// <summary>An ordinary hard ownership link.</summary>
        HardOwner,
        /// <summary>A direct group 330 in a top-level ACAD_REACTORS group.</summary>
        Reactor,
        /// <summary>A direct group 360 in a top-level ACAD_XDICTIONARY group.</summary>
        ExtensionDictionary,
        /// <summary>A group 1005 in an extended-data packet.</summary>
        XData,
        /// <summary>A nontranslated group 320 through 329.</summary>
        Arbitrary,
        /// <summary>The HEADER's next-handle hint, not an object identity.</summary>
        HeaderSeed,
        /// <summary>A header handle other than HANDSEED, not an object identity.</summary>
        HeaderReference,
        /// <summary>Application, XRECORD payload or unknown-section data; semantics not inferred.</summary>
        Opaque
    }

    /// <summary>An immutable handle occurrence with its exact record and absolute tag location.</summary>
    public sealed class DxfRawHandleOccurrence
    {
        internal DxfRawHandleOccurrence(DxfRawRecord record, int index, DxfTag tag,
            DxfRawHandleRole role, string context, string subclass)
        {
            this.Record = record; this.TagIndex = index; this.Code = tag.Code;
            this.Handle = (string)tag.RawValue;
            this.NumericHandle = ulong.Parse(this.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            this.CanonicalHandle = this.NumericHandle.ToString("X", CultureInfo.InvariantCulture);
            this.Role = role; this.Context = context; this.Subclass = subclass;
        }
        /// <summary>Gets the lexical record, or null for section preambles and structural tags.</summary>
        public DxfRawRecord Record { get; }
        /// <summary>Gets the absolute index in the source document's Tags.</summary>
        public int TagIndex { get; }
        /// <summary>Gets the group code.</summary>
        public short Code { get; }
        /// <summary>Gets the exact spelling stored in the source tag.</summary>
        public string Handle { get; }
        /// <summary>Gets the case/leading-zero independent lookup key.</summary>
        public string CanonicalHandle { get; }
        /// <summary>Gets the unsigned handle value; zero is a null reference, not an identity.</summary>
        public ulong NumericHandle { get; }
        /// <summary>Gets the context-qualified role.</summary>
        public DxfRawHandleRole Role { get; }
        /// <summary>Gets the innermost control group or XData application, or null.</summary>
        public string Context { get; }
        /// <summary>Gets the current subclass marker, or null for common/legacy data.</summary>
        public string Subclass { get; }
        /// <summary>Gets whether this is an interpreted inter-object reference.</summary>
        public bool IsReference
        {
            get
            {
                return this.Role != DxfRawHandleRole.Identity && this.Role != DxfRawHandleRole.Arbitrary &&
                       this.Role != DxfRawHandleRole.HeaderSeed && this.Role != DxfRawHandleRole.Opaque;
            }
        }
    }

    /// <summary>A structural issue detected without changing the raw document.</summary>
    public enum DxfRawHandleDiagnosticKind
    {
        /// <summary>More than one identity uses the same numeric handle.</summary>
        DuplicateIdentity,
        /// <summary>One record exposes more than one identity slot.</summary>
        MultipleIdentities,
        /// <summary>Zero was used as an object identity.</summary>
        NullIdentity,
        /// <summary>A nonzero interpreted reference has no definition.</summary>
        UnresolvedReference,
        /// <summary>A reference resolves to more than one definition.</summary>
        AmbiguousReference,
        /// <summary>One record exposes multiple common owner slots.</summary>
        MultipleOwners,
        /// <summary>A malformed, unmatched or unterminated group-102 control block.</summary>
        InvalidControlGroup,
        /// <summary>A cycle among uniquely resolved common owner links.</summary>
        OwnerCycle
    }

    /// <summary>An immutable diagnostic tied to one exact source position.</summary>
    public sealed class DxfRawHandleDiagnostic
    {
        internal DxfRawHandleDiagnostic(DxfRawHandleDiagnosticKind kind, DxfRawRecord record,
            int tagIndex, string handle, string message)
        { this.Kind = kind; this.Record = record; this.TagIndex = tagIndex; this.Handle = handle; this.Message = message; }
        /// <summary>Gets the issue kind.</summary>
        public DxfRawHandleDiagnosticKind Kind { get; }
        /// <summary>Gets the source record, or null outside a record.</summary>
        public DxfRawRecord Record { get; }
        /// <summary>Gets the absolute tag position.</summary>
        public int TagIndex { get; }
        /// <summary>Gets the canonical handle, when applicable.</summary>
        public string Handle { get; }
        /// <summary>Gets a human-readable structural diagnostic.</summary>
        public string Message { get; }
    }

    /// <summary>Limits memory used by explicitly requested handle indexing.</summary>
    public sealed class DxfRawHandleIndexOptions
    {
        /// <summary>Creates an occurrence and diagnostic budget.</summary>
        /// <param name="maximumOccurrences">Positive maximum number of exposed handle slots.</param>
        /// <param name="maximumDiagnostics">Positive maximum number of structural diagnostics.</param>
        public DxfRawHandleIndexOptions(int maximumOccurrences = 1000000, int maximumDiagnostics = 100000)
        {
            if (maximumOccurrences < 1) throw new ArgumentOutOfRangeException(nameof(maximumOccurrences));
            if (maximumDiagnostics < 1) throw new ArgumentOutOfRangeException(nameof(maximumDiagnostics));
            this.MaximumOccurrences = maximumOccurrences; this.MaximumDiagnostics = maximumDiagnostics;
        }
        /// <summary>Gets the maximum indexed handle slots.</summary>
        public int MaximumOccurrences { get; }
        /// <summary>Gets the maximum diagnostic entries; exceeding it throws instead of hiding issues.</summary>
        public int MaximumDiagnostics { get; }
    }
}
