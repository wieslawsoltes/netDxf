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
using System.IO;
using System.Text;
using System.Threading;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>An immutable, ordered DXF tag document that retains unfamiliar records and sections.</summary>
    /// <remarks>
    /// This is a preservation API, separate from DxfDocument's typed geometry model. It does not
    /// evaluate entities, repair ownership, remap handles, execute application data or downgrade schemas.
    /// Unchanged same-transport saves reproduce the original bytes. Edited or cross-transport saves
    /// normalize lexical formatting while retaining tag order and values. R11/R12 and later profiles only.
    /// </remarks>
    public sealed class DxfRawDocument
    {
        private readonly byte[] originalBytes;
        private readonly Encoding encoding;
        private readonly string codePageName;
        private readonly DxfRawOptions options;

        private DxfRawDocument(List<DxfTag> tags, bool binary, byte[] originalBytes, DxfRawOptions options)
        {
            this.options = options;
            this.Tags = new ReadOnlyCollection<DxfTag>(tags);
            this.Sections = new ReadOnlyCollection<DxfRawSection>(IndexSections(this.Tags));
            DxfRawSection header = null;
            foreach (DxfRawSection section in this.Sections)
            {
                if (!string.Equals(section.Name, "HEADER", StringComparison.OrdinalIgnoreCase)) continue;
                if (header != null) throw new FormatException("Raw DXF requires an unambiguous single HEADER section.");
                header = section;
            }
            if (header == null) throw new FormatException("Raw DXF requires a HEADER section declaring its version.");
            string versionName;
            ReadProfile(header.Content, out versionName, out this.codePageName);
            this.Version = ParseVersion(versionName);
            this.encoding = ResolveEncoding(this.Version, this.codePageName);
            this.IsBinary = binary;
            this.originalBytes = originalBytes;
        }

        /// <summary>Gets the declared, supported database-format family.</summary>
        public DxfVersion Version { get; }
        /// <summary>Gets the input transport or the transport selected when creating this document.</summary>
        public bool IsBinary { get; }
        /// <summary>Gets whether an original byte representation is available for exact same-transport saving.</summary>
        public bool HasOriginalBytes { get { return this.originalBytes != null; } }
        /// <summary>Gets every tag in its original order, including structure, comments and repeated codes.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Gets ordered section indexes; unknown names are retained, not dispatched away.</summary>
        public IReadOnlyList<DxfRawSection> Sections { get; }
        /// <summary>Gets the effective character-encoding code page; modern UTF-8 is 65001.</summary>
        public int EncodingCodePage { get { return this.encoding.CodePage; } }

        /// <summary>Loads a raw document from the current position through physical stream EOF.</summary>
        /// <param name="stream">Readable stream; nonseekable streams are supported and never closed.</param>
        /// <param name="options">Optional resource budgets.</param>
        /// <param name="cancellationToken">Cancellation for copying and tag parsing.</param>
        /// <returns>An immutable document with its original bytes.</returns>
        /// <remarks>
        /// Input is buffered within the byte budget. String limits are checked after decoding, within
        /// that input bound. Invalid encodings, missing EOF, ambiguous profiles and trailing non-whitespace
        /// are rejected. Exceptions are consistent in Debug and Release. No external resources are loaded.
        /// </remarks>
        public static DxfRawDocument Load(Stream stream, DxfRawOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("A readable stream is required.", nameof(stream));
            options = options ?? new DxfRawOptions();
            byte[] bytes = ReadBounded(stream, options.MaximumBytes, cancellationToken);
            bool binary = HasBinaryPrefix(bytes);
            // Every supported binary file begins with a group-0 structural string. Modern
            // framing places a second zero code byte before that string; pre-R13 does not.
            // Detect framing, not an arbitrary occurrence of $ACADVER in payload data.
            bool legacyGroupCodes = binary && bytes.Length > 23 && bytes[22] == 0 && bytes[23] != 0;
            if (!binary && HasUnsupportedBom(bytes))
                throw new NotSupportedException("This raw DXF profile supports ASCII-compatible code pages and UTF-8, not UTF-16/UTF-32 transports.");

            // Bootstrap with a byte-preserving single-byte encoding. Only the ASCII profile fields
            // are used here; the real pass uses the declared encoding with exception fallbacks.
            DxfRawOptions bootstrap = new DxfRawOptions(options.MaximumBytes, options.MaximumTags, options.MaximumBytes);
            List<DxfTag> header = FindHeader(ReadTags(bytes, binary, Encoding.GetEncoding(28591), bootstrap, cancellationToken, legacyGroupCodes));
            string versionName, codePage;
            ReadProfile(header, out versionName, out codePage);
            DxfVersion version = ParseVersion(versionName);
            if (binary && legacyGroupCodes != (version < DxfVersion.AutoCad13))
                throw new FormatException("Binary DXF group-code framing conflicts with its declared $ACADVER profile.");
            Encoding encoding = ResolveEncoding(version, codePage);
            if (!binary && HasUtf8Bom(bytes) && encoding.CodePage != 65001)
                throw new NotSupportedException("A UTF-8 byte-order mark conflicts with the legacy raw DXF encoding profile.");
            List<DxfTag> tags = Snapshot(ReadTags(bytes, binary, encoding, options, cancellationToken, legacyGroupCodes), options, cancellationToken);
            return new DxfRawDocument(tags, binary, bytes, options);
        }

        /// <summary>Creates a raw document from complete, explicitly authored tags.</summary>
        /// <param name="tags">Complete section grammar with a declared version and terminating EOF.</param>
        /// <param name="binary">Preferred output transport.</param>
        /// <param name="options">Optional resource budgets.</param>
        /// <returns>A document that will normalize formatting on save.</returns>
        /// <remarks>Strings are raw tag values: DXF Unicode escapes are not automatically interpreted or inserted.</remarks>
        public static DxfRawDocument Create(IEnumerable<DxfTag> tags, bool binary = false, DxfRawOptions options = null)
        {
            options = options ?? new DxfRawOptions();
            return new DxfRawDocument(Snapshot(tags, options, default(CancellationToken)), binary, null, options);
        }

        /// <summary>Creates an edited snapshot without changing the source or silently converting its profile.</summary>
        /// <param name="tags">The replacement complete ordered tag sequence.</param>
        /// <returns>An edited document without an original-byte representation.</returns>
        /// <remarks>
        /// Version and code-page declarations must remain unchanged. Removing or changing handles and
        /// references is the caller's responsibility; no dependency repair is implied by a tag edit.
        /// </remarks>
        public DxfRawDocument WithTags(IEnumerable<DxfTag> tags)
        {
            DxfRawDocument edited = Create(tags, this.IsBinary, this.options);
            if (edited.Version != this.Version || !string.Equals(edited.codePageName, this.codePageName, StringComparison.Ordinal))
                throw new NotSupportedException("Raw tag edits cannot implicitly change the DXF version or code-page declaration.");
            return edited;
        }

        /// <summary>Replaces one indexed record in a new document snapshot.</summary>
        /// <param name="record">A record from this exact document snapshot.</param>
        /// <param name="replacement">One complete record, including its opening marker.</param>
        /// <returns>A new document retaining every tag outside the selected range.</returns>
        /// <remarks>
        /// Foreign or stale record indexes are rejected before replacement enumeration. The marker
        /// code must remain the same, and the replacement cannot contain another record boundary.
        /// Version/code-page edits retain WithTags restrictions. References are not repaired or remapped;
        /// modified output is normalized rather than byte-identical. Use WithoutRecord for removal.
        /// </remarks>
        public DxfRawDocument WithRecord(DxfRawRecord record, IEnumerable<DxfTag> replacement)
        {
            this.ValidateRecordSnapshot(record);
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            return this.WithTags(this.RewriteRecord(record, replacement, false));
        }

        /// <summary>Removes one indexed record in a new document snapshot.</summary>
        /// <param name="record">A record from this exact document snapshot.</param>
        /// <returns>A new document without the selected tag range.</returns>
        /// <remarks>
        /// This removes only the lexical record, including comments within its range. Children,
        /// sequence terminators and incoming references are not removed. The caller must preserve
        /// semantic validity; mandatory profile declarations still cannot be removed implicitly.
        /// </remarks>
        public DxfRawDocument WithoutRecord(DxfRawRecord record)
        {
            this.ValidateRecordSnapshot(record);
            return this.WithTags(this.RewriteRecord(record, new DxfTag[0], true));
        }

        private void ValidateRecordSnapshot(DxfRawRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!ReferenceEquals(record.SourceTags, this.Tags))
                throw new ArgumentException("The raw record belongs to a different document snapshot. Retrieve its current index before editing.", nameof(record));
        }

        private IEnumerable<DxfTag> RewriteRecord(DxfRawRecord record, IEnumerable<DxfTag> replacement, bool remove)
        {
            for (int i = 0; i < record.StartTagIndex; i++) yield return this.Tags[i];
            bool first = true;
            foreach (DxfTag tag in replacement)
            {
                if (tag == null) throw new ArgumentException("A replacement record cannot contain null tags.", nameof(replacement));
                if (Is(tag, 0, "EOF") || Is(tag, 0, "ENDSEC"))
                    throw new ArgumentException("A replacement record cannot terminate a section or document.", nameof(replacement));
                if (first)
                {
                    if (tag.Code != record.MarkerCode || string.IsNullOrEmpty(tag.RawValue as string))
                        throw new ArgumentException("A replacement must begin with the same record marker code and a nonempty name.", nameof(replacement));
                    first = false;
                }
                else if (tag.Code == record.MarkerCode || tag.Code == 0)
                    throw new ArgumentException("A replacement must contain exactly one lexical record.", nameof(replacement));
                yield return tag;
            }
            if (first && !remove)
                throw new ArgumentException("An empty replacement is not a record. Use WithoutRecord to remove it explicitly.", nameof(replacement));
            for (int i = record.EndTagIndex; i < this.Tags.Count; i++) yield return this.Tags[i];
        }

        /// <summary>Saves using the document's input or selected transport.</summary>
        /// <param name="stream">Writable destination, left open.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public void Save(Stream stream, CancellationToken cancellationToken = default(CancellationToken))
        {
            this.Save(stream, this.IsBinary, cancellationToken);
        }

        /// <summary>Saves without dropping unknown tags or silently translating their schemas.</summary>
        /// <param name="stream">Writable destination, left open and written at its current position.</param>
        /// <param name="binary">Requested transport; the database version is not changed.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <remarks>
        /// Same-transport unedited output is byte-identical. Other output is fully staged within the byte
        /// budget before touching the destination. Binary comments and unrepresentable chunk lengths or
        /// character data are rejected. IO failure/cancellation during final copying can leave partial
        /// destination output. Existing destination suffixes are not truncated; file replacement is caller policy.
        /// </remarks>
        public void Save(Stream stream, bool binary, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(stream));
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = this.originalBytes != null && binary == this.IsBinary
                ? this.originalBytes : this.Serialize(binary, cancellationToken);
            for (int offset = 0; offset < bytes.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(65536, bytes.Length - offset);
                stream.Write(bytes, offset, count);
                offset += count;
            }
        }

        private byte[] Serialize(bool binary, CancellationToken cancellationToken)
        {
            // Preflight every immutable value before creating the transport writer.
            foreach (DxfTag tag in this.Tags)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (binary && tag.Code == 999)
                    throw new NotSupportedException("Binary DXF does not carry comment tags. Remove them explicitly before converting transports.");
                if (binary && tag.RawValue is byte[] data && data.Length > 255)
                    throw new NotSupportedException("The binary chunk length cannot fit in one byte. Raw output never splits unknown records automatically.");
                if (tag.RawValue is string text)
                {
                    if (!binary && (text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0))
                        throw new NotSupportedException("A raw binary string containing physical line breaks cannot be emitted as one text DXF value. Convert its record semantics explicitly.");
                    this.encoding.GetByteCount(text);
                }
            }
            using (LimitedMemoryStream output = new LimitedMemoryStream(this.options.MaximumBytes))
            {
                if (binary)
                {
                    using (BinaryWriter writer = new BinaryWriter(output, this.encoding, true))
                    {
                        WriteTags(new BinaryCodeValueWriter(writer, this.Version < DxfVersion.AutoCad13), this.Tags, cancellationToken);
                    }
                }
                else
                {
                    using (StreamWriter writer = new StreamWriter(output, this.encoding, 4096, true))
                    {
                        writer.NewLine = "\r\n";
                        WriteTags(new TextCodeValueWriter(writer), this.Tags, cancellationToken);
                    }
                }
                return output.ToArray();
            }
        }

        private static void WriteTags(ICodeValueWriter writer, IReadOnlyList<DxfTag> tags, CancellationToken token)
        {
            foreach (DxfTag tag in tags)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(tag.Code, tag.RawValue);
            }
            writer.Flush();
        }

        private static List<DxfTag> Snapshot(IEnumerable<DxfTag> tags, DxfRawOptions options, CancellationToken token)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            List<DxfTag> result = new List<DxfTag>();
            DxfRawTagContext context = new DxfRawTagContext();
            foreach (DxfTag tag in tags)
            {
                token.ThrowIfCancellationRequested();
                if (tag == null) throw new ArgumentException("A raw DXF tag sequence cannot contain null.", nameof(tags));
                if (result.Count >= options.MaximumTags) throw new InvalidDataException("Raw DXF exceeds the tag-count budget.");
                CheckValueBudget(tag, options);
                if (tag.Code == 5 && (tag.ValueType == DxfTagValueType.String) != context.Code5IsString)
                    throw new ArgumentException("Group 5 requires a DIMBLK name only inside a DIMSTYLE table entry, and a handle elsewhere.", nameof(tags));
                context.Advance(tag);
                result.Add(tag);
            }
            return result;
        }

        private static void CheckValueBudget(DxfTag tag, DxfRawOptions options)
        {
            if (tag.RawValue is string text && text.Length > options.MaximumStringLength)
                throw new InvalidDataException("Raw DXF exceeds the decoded string-length budget.");
            if (tag.RawValue is byte[] bytes && bytes.Length > options.MaximumBytes)
                throw new InvalidDataException("Raw DXF binary data exceeds the byte budget.");
        }

        private static byte[] ReadBounded(Stream stream, int limit, CancellationToken token)
        {
            using (MemoryStream copy = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int wanted = (int)Math.Min(buffer.Length, (long)limit - copy.Length + 1);
                    int count = stream.Read(buffer, 0, wanted);
                    if (count == 0) return copy.ToArray();
                    if (copy.Length + count > limit) throw new InvalidDataException("Raw DXF exceeds the input byte budget.");
                    copy.Write(buffer, 0, count);
                }
            }
        }

        private static IEnumerable<DxfTag> ReadTags(byte[] bytes, bool binary, Encoding encoding,
            DxfRawOptions options, CancellationToken token, bool legacyGroupCodes)
        {
            using (MemoryStream input = new MemoryStream(bytes, false))
            {
                if (binary)
                {
                    using (BinaryReader reader = new BinaryReader(input, encoding, true))
                    {
                        foreach (DxfTag tag in ReadTagCore(new BinaryCodeValueReader(reader, encoding, legacyGroupCodes), options, token)) yield return tag;
                        if (input.Position != input.Length) throw new FormatException("Unexpected bytes after binary DXF EOF.");
                    }
                }
                else
                {
                    if (HasUtf8Bom(bytes)) input.Position = 3;
                    using (StreamReader reader = new StreamReader(input, encoding, false, 4096, true))
                    {
                        foreach (DxfTag tag in ReadTagCore(new TextCodeValueReader(reader), options, token)) yield return tag;
                        int c;
                        while ((c = reader.Read()) >= 0)
                        {
                            token.ThrowIfCancellationRequested();
                            if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                                throw new FormatException("Unexpected data after text DXF EOF.");
                        }
                    }
                }
            }
        }

        private static IEnumerable<DxfTag> ReadTagCore(ICodeValueReader reader, DxfRawOptions options, CancellationToken token)
        {
            int count = 0;
            DxfRawTagContext context = new DxfRawTagContext();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (count++ >= options.MaximumTags) throw new InvalidDataException("Raw DXF exceeds the tag-count budget.");
                reader.Code5IsString = context.Code5IsString;
                reader.Next();
                DxfTag tag = reader.Code == 5 && reader.Code5IsString
                    ? DxfTag.CreateDimensionStyleArrowName(reader.ReadString())
                    : new DxfTag(reader.Code, reader.Value);
                CheckValueBudget(tag, options);
                context.Advance(tag);
                yield return tag;
                if (Is(tag, 0, "EOF")) yield break;
            }
        }

        private static bool Is(DxfTag tag, short code, string value)
        {
            return tag.Code == code && string.Equals(tag.RawValue as string, value, StringComparison.OrdinalIgnoreCase);
        }

        private static List<DxfRawSection> IndexSections(IReadOnlyList<DxfTag> tags)
        {
            List<DxfRawSection> sections = new List<DxfRawSection>();
            int start = -1, content = -1;
            string name = null;
            for (int i = 0; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (Is(tag, 0, "EOF"))
                {
                    if (start >= 0) throw new FormatException("DXF EOF occurred before the section's ENDSEC.");
                    if (i != tags.Count - 1) throw new FormatException("DXF tags cannot follow EOF.");
                    return sections;
                }
                if (tag.Code == 999) continue;
                if (start < 0)
                {
                    if (!Is(tag, 0, "SECTION")) throw new FormatException("Expected a DXF SECTION or EOF outside a section.");
                    start = i;
                }
                else if (name == null)
                {
                    if (tag.Code != 2 || string.IsNullOrEmpty((string)tag.RawValue))
                        throw new FormatException("A DXF SECTION requires a nonempty group-2 name.");
                    name = (string)tag.RawValue;
                    content = i + 1;
                }
                else if (Is(tag, 0, "ENDSEC"))
                {
                    sections.Add(new DxfRawSection(name, tags, start, content, i + 1));
                    start = -1; content = -1; name = null;
                }
                // A SECTION entity inside ENTITIES is data, not a nested file-section marker.
            }
            throw new EndOfStreamException("The raw DXF tag sequence is missing its EOF record.");
        }

        private static List<DxfTag> FindHeader(IEnumerable<DxfTag> tags)
        {
            int state = 0; // outside section, expecting name, inside body
            List<DxfTag> header = null;
            foreach (DxfTag tag in tags)
            {
                if (tag.Code == 999) { if (header != null) header.Add(tag); continue; }
                if (Is(tag, 0, "EOF")) break;
                if (state == 0)
                {
                    if (!Is(tag, 0, "SECTION")) throw new FormatException("Expected SECTION while determining the raw DXF profile.");
                    state = 1;
                }
                else if (state == 1)
                {
                    if (tag.Code != 2) throw new FormatException("A DXF section name requires group 2.");
                    if (Is(tag, 2, "HEADER")) header = new List<DxfTag>();
                    state = 2;
                }
                else if (Is(tag, 0, "ENDSEC"))
                {
                    if (header != null) return header;
                    state = 0;
                }
                else if (header != null) header.Add(tag);
            }
            throw new FormatException("No complete HEADER section was found for the raw DXF profile.");
        }

        private static void ReadProfile(IEnumerable<DxfTag> tags, out string version, out string codePage)
        {
            version = null; codePage = null;
            string pending = null;
            bool completedProfileValue = false;
            foreach (DxfTag tag in tags)
            {
                if (tag.Code == 999) continue;
                if (pending != null)
                {
                    short expected = pending == "$ACADVER" ? (short)1 : (short)3;
                    if (tag.Code != expected) throw new FormatException("Incorrect value group for " + pending + ".");
                    if (expected == 1) version = (string)tag.RawValue;
                    else codePage = (string)tag.RawValue;
                    pending = null;
                    completedProfileValue = true;
                    continue;
                }
                if (tag.Code != 9)
                {
                    if (completedProfileValue) throw new FormatException("A raw DXF profile variable requires exactly one value tag.");
                    continue;
                }
                completedProfileValue = false;
                string name = (string)tag.RawValue;
                if (string.Equals(name, "$ACADVER", StringComparison.OrdinalIgnoreCase))
                {
                    if (version != null) throw new FormatException("Duplicate $ACADVER makes the raw DXF profile ambiguous.");
                    pending = name.ToUpperInvariant();
                }
                else if (string.Equals(name, "$DWGCODEPAGE", StringComparison.OrdinalIgnoreCase))
                {
                    if (codePage != null) throw new FormatException("Duplicate $DWGCODEPAGE makes the raw DXF profile ambiguous.");
                    pending = name.ToUpperInvariant();
                }
            }
            if (pending != null) throw new FormatException("Missing value for " + pending + ".");
            if (version == null) throw new FormatException("Raw DXF requires a declared $ACADVER.");
        }

        private static DxfVersion ParseVersion(string name)
        {
            DxfVersion version = StringEnum<DxfVersion>.Parse(name, StringComparison.OrdinalIgnoreCase);
            switch (version)
            {
                case DxfVersion.AutoCad12: case DxfVersion.AutoCad13: case DxfVersion.AutoCad14:
                case DxfVersion.AutoCad2000: case DxfVersion.AutoCad2004: case DxfVersion.AutoCad2007:
                case DxfVersion.AutoCad2010: case DxfVersion.AutoCad2013: case DxfVersion.AutoCad2018: return version;
                default: throw new DxfVersionNotSupportedException("This raw DXF profile requires a recognized R11/R12–2018 database family.", version);
            }
        }

        private static Encoding ResolveEncoding(DxfVersion version, string name)
        {
            if (version >= DxfVersion.AutoCad2007) return new UTF8Encoding(false, true);
            int codePage = 1252;
            if (name != null)
            {
                // R13 producers also use DOS names such as dos932. Resolve only numeric
                // ANSI_/DOS aliases, never arbitrary runtime encoding names or silent fallbacks.
                int prefixLength = name.StartsWith("ANSI_", StringComparison.OrdinalIgnoreCase) ? 5 :
                    name.StartsWith("DOS", StringComparison.OrdinalIgnoreCase) ? 3 : -1;
                if (prefixLength < 0 || !int.TryParse(name.Substring(prefixLength), NumberStyles.None,
                    CultureInfo.InvariantCulture, out codePage))
                    throw new NotSupportedException("The legacy raw DXF profile requires ANSI_<codepage>, DOS<codepage>, or absent/default 1252.");
            }
#if !NET4X && !NETSTANDARD
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            Encoding encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            byte[] ascii = encoding.GetBytes("0\r\nA\0");
            byte[] expected = { 48, 13, 10, 65, 0 };
            if (ascii.Length != expected.Length) throw new NotSupportedException("The raw DXF encoding must be ASCII-compatible.");
            for (int i = 0; i < ascii.Length; i++)
                if (ascii[i] != expected[i]) throw new NotSupportedException("The raw DXF encoding must be ASCII-compatible.");
            return encoding;
        }

        private static bool HasBinaryPrefix(byte[] data)
        {
            const string prefix = "AutoCAD Binary DXF";
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (data[i] != (byte)prefix[i]) return false;
            return true;
        }
        private static bool HasUtf8Bom(byte[] data)
        {
            return data.Length >= 3 && data[0] == 239 && data[1] == 187 && data[2] == 191;
        }
        private static bool HasUnsupportedBom(byte[] data)
        {
            return (data.Length >= 2 && ((data[0] == 255 && data[1] == 254) || (data[0] == 254 && data[1] == 255))) ||
                (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 254 && data[3] == 255);
        }

        // Composition is intentional: MemoryStream's modern Span overloads could otherwise
        // bypass an override of Write(byte[], int, int) and escape the byte budget.
        private sealed class LimitedMemoryStream : Stream
        {
            private readonly MemoryStream buffer = new MemoryStream();
            private readonly int limit;
            internal LimitedMemoryStream(int limit) { this.limit = limit; }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return this.buffer.Length; } }
            public override long Position
            {
                get { return this.buffer.Position; }
                set { throw new NotSupportedException(); }
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (this.buffer.Position + count > this.limit) throw new InvalidDataException("Raw DXF exceeds the normalized-output byte budget.");
                this.buffer.Write(buffer, offset, count);
            }
            public override void WriteByte(byte value)
            {
                if (this.buffer.Position >= this.limit) throw new InvalidDataException("Raw DXF exceeds the normalized-output byte budget.");
                this.buffer.WriteByte(value);
            }
            public override void Flush() { this.buffer.Flush(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            internal byte[] ToArray() { return this.buffer.ToArray(); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) this.buffer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
