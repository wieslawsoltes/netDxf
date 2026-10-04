// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using netDxf.Entities;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        /// <summary>Authors an R12 selection with an explicit legacy character-encoding declaration.</summary>
        /// <param name="entities">Supported entities and their reachable resources.</param>
        /// <param name="drawingCodePage">A numeric ANSI_ or DOS alias, for example ANSI_1250 or DOS932.</param>
        /// <param name="binary">Preferred text or binary transport.</param>
        /// <param name="options">Tag, encoded-byte and decoded-string budgets.</param>
        /// <remarks>
        /// The raw codec validates the declared ASCII-compatible encoding before source enumeration.
        /// Saving performs strict encoding: unrepresentable characters are never replaced with question
        /// marks. This does not insert Unicode escapes into R12 values, broaden the existing ASCII symbol
        /// name profile, resolve font files, or convert an arbitrary raw document. Create retains the raw
        /// API's deferred encoded-byte validation; use SaveWithCodePage for complete staged serialization.
        /// Existing Create overloads retain their Windows-1252 default and binary signatures.
        /// </remarks>
        public static DxfRawDocument CreateWithCodePage(IEnumerable<EntityObject> entities, string drawingCodePage,
            bool binary = false, DxfRawOptions options = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            ValidateSelectionCodePage(drawingCodePage);
            options = options ?? new DxfRawOptions();
            DxfRawDocument selected = Create(entities, binary, options);
            return SetAuthoredCodePage(selected, drawingCodePage, options);
        }

        /// <summary>Stages an R12 selection in the explicitly declared legacy encoding, then copies it.</summary>
        /// <param name="stream">Writable caller stream, left open at the end of the copied output.</param>
        /// <param name="entities">Supported selection; enumerated once.</param>
        /// <param name="drawingCodePage">Numeric ANSI_ or DOS code-page declaration.</param>
        /// <param name="binary">Requested transport.</param>
        /// <param name="options">Raw processing limits.</param>
        /// <param name="cancellationToken">Checked during enumeration, serialization and copying.</param>
        /// <remarks>
        /// Profile, encoding, geometry and budget failures before copying leave the stream unchanged.
        /// IO errors or cancellation during copying may leave partial output. No suffix truncation or
        /// atomic file replacement is implied. Text controls retain the existing R12 caret convention.
        /// </remarks>
        public static void SaveWithCodePage(Stream stream, IEnumerable<EntityObject> entities, string drawingCodePage,
            bool binary = false, DxfRawOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(stream));
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            cancellationToken.ThrowIfCancellationRequested();
            CreateWithCodePage(CheckedEntities(entities, cancellationToken), drawingCodePage, binary, options)
                .Save(stream, binary, cancellationToken);
        }

        internal static int ValidateSelectionCodePage(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0 || name.Length > 24)
                throw new ArgumentException("A numeric ANSI_ or DOS code-page declaration is required.", nameof(name));
            // Reuse the raw codec's exception fallbacks and ASCII-compatibility checks.
            // This tiny independent profile cannot enumerate or mutate caller source objects.
            return DxfRawDocument.Create(new DxfTag[] {
                new DxfTag(0, "SECTION"), new DxfTag(2, "HEADER"),
                new DxfTag(9, "$ACADVER"), new DxfTag(1, "AC1009"),
                new DxfTag(9, "$DWGCODEPAGE"), new DxfTag(3, name),
                new DxfTag(0, "ENDSEC"), new DxfTag(0, "EOF")
            }).EncodingCodePage;
        }

        internal static string SelectionCodePage(DxfRawDocument document)
        {
            DxfRawRecord declaration = CodePageRecord(document);
            return declaration == null ? "ANSI_1252" : (string)declaration.Tags.Single(t => t.Code == 3).Value;
        }

        private static DxfRawRecord CodePageRecord(DxfRawDocument document)
        {
            return document.Sections.Single(s => string.Equals(s.Name, "HEADER", StringComparison.OrdinalIgnoreCase))
                .Records.SingleOrDefault(r => r.MarkerCode == 9
                    && string.Equals(r.Name, "$DWGCODEPAGE", StringComparison.OrdinalIgnoreCase));
        }

        // Only used for newly authored private selections, never a general raw WithTags shortcut.
        // Version, record order and all non-profile values are retained. The new raw document has
        // no original byte representation, so its declared encoding controls actual serialization.
        internal static DxfRawDocument SetAuthoredCodePage(DxfRawDocument document, string name, DxfRawOptions options)
        {
            DxfRawRecord declaration = CodePageRecord(document);
            if (declaration == null) throw new InvalidDataException("An authored selection must declare its code page.");
            if (SelectionCodePage(document) == name) return document;
            int position = -1;
            for (int i = declaration.StartTagIndex + 1; i < declaration.EndTagIndex; i++)
                if (document.Tags[i].Code == 3) { position = i; break; }
            if (position < 0) throw new InvalidDataException("The authored code-page declaration is incomplete.");
            return DxfRawDocument.Create(ReplaceCodePage(document.Tags, position, name), document.IsBinary, options);
        }

        private static IEnumerable<DxfTag> ReplaceCodePage(IReadOnlyList<DxfTag> tags, int position, string name)
        {
            for (int i = 0; i < tags.Count; i++)
                yield return i == position ? new DxfTag(3, name) : tags[i];
        }
    }
}
