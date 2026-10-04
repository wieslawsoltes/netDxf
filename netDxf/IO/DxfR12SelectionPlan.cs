// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    /// <summary>A reusable immutable selection snapshot for explicit R12-profile interchange.</summary>
    /// <remarks>
    /// This selects supported entities and their reachable resources, not an entire drawing.
    /// Layouts, unrelated objects, header settings, original handles and lexical formatting are
    /// not copied. Unsupported selected semantics reject during preparation; nothing is flattened.
    /// Each CreateDocument call returns an independent editable graph. Source mutation during
    /// preparation is not supported. Completed plans can be read concurrently.
    /// </remarks>
    public sealed class DxfR12SelectionPlan
    {
        private readonly DxfRawDocument snapshot;
        private readonly DxfRawOptions options;

        private DxfR12SelectionPlan(DxfRawDocument snapshot, DxfRawOptions options)
        {
            this.snapshot = snapshot;
            this.options = options;
            this.DrawingCodePage = DxfR12Codec.SelectionCodePage(snapshot);
            this.ModernDrawingCodePage = DxfR12Codec.ModernSelectionCodePage(
                this.DrawingCodePage, snapshot.EncodingCodePage);
            this.RootEntityCount = DxfR12Codec.ReadEntities(snapshot).Count;
            this.BlockCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "BLOCK");
            this.AttributeDefinitionCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "ATTDEF");
            this.AttributeCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "ATTRIB");
        }

        /// <summary>Gets the immutable normalized AC1009 selection, not the original full drawing.</summary>
        public DxfRawDocument NormalizedSelection { get { return this.snapshot; } }
        /// <summary>Gets the R12 ANSI_/DOS declaration. An absent input declaration becomes ANSI_1252.</summary>
        public string DrawingCodePage { get; }
        /// <summary>Gets the encoding declaration used in fresh modern documents and modern output.</summary>
        /// <remarks>
        /// Known OEM aliases map explicitly to their Windows ANSI counterparts. Other numeric DOS
        /// aliases accepted by the raw codec use ANSI_1252 for modern output. Unicode escapes retain
        /// characters outside that target; the original R12 declaration and encoding do not change.
        /// No platform ANSI code page or optional encoding-provider metadata is consulted.
        /// </remarks>
        public string ModernDrawingCodePage { get; }
        /// <summary>Gets the effective encoding of the normalized R12 snapshot.</summary>
        /// <remarks>Modern output from AutoCAD 2007 onward uses UTF-8 regardless of its legacy hint.</remarks>
        public int EncodingCodePage { get { return this.snapshot.EncodingCodePage; } }
        /// <summary>Gets the number of selected top-level entities; sequence children are not roots.</summary>
        public int RootEntityCount { get; }
        /// <summary>Gets the number of reachable shared block definitions.</summary>
        public int BlockCount { get; }
        /// <summary>Gets the number of selected block attribute definitions.</summary>
        public int AttributeDefinitionCount { get; }
        /// <summary>Gets the number of stored attribute instances, without array expansion.</summary>
        public int AttributeCount { get; }

        /// <summary>Captures a strict supported entity selection using the established Windows-1252 default.</summary>
        public static DxfR12SelectionPlan Prepare(IEnumerable<EntityObject> entities, DxfRawOptions options = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            options = options ?? new DxfRawOptions();
            return new DxfR12SelectionPlan(DxfR12Codec.Create(entities, false, options), options);
        }

        /// <summary>Selects supported ENTITIES and dependencies while retaining an AC1009 drawing's code page.</summary>
        /// <remarks>Unselected raw content remains in the caller's original immutable document only.</remarks>
        public static DxfR12SelectionPlan Prepare(DxfRawDocument document, DxfRawOptions options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return PrepareWithCodePage(DxfR12Codec.ReadEntities(document), DxfR12Codec.SelectionCodePage(document), options);
        }

        /// <summary>Captures a selection with an explicit numeric ANSI_ or DOS code-page declaration.</summary>
        /// <remarks>
        /// A separate method name preserves source compatibility of Prepare(entities, null).
        /// The existing ASCII symbol-name profile is unchanged. R12 serialization rejects characters
        /// not representable in this encoding instead of silently replacing or Unicode-escaping them.
        /// </remarks>
        public static DxfR12SelectionPlan PrepareWithCodePage(IEnumerable<EntityObject> entities,
            string drawingCodePage, DxfRawOptions options = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            options = options ?? new DxfRawOptions();
            return new DxfR12SelectionPlan(DxfR12Codec.CreateWithCodePage(entities, drawingCodePage, false, options), options);
        }

        /// <summary>Explicitly selects and reauthors an R12 drawing using a requested output code page.</summary>
        /// <remarks>The input is decoded before selection. Reencoding changes no logical content or source bytes.</remarks>
        public static DxfR12SelectionPlan PrepareWithCodePage(DxfRawDocument document,
            string drawingCodePage, DxfRawOptions options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            DxfR12Codec.ValidateSelectionCodePage(drawingCodePage);
            return PrepareWithCodePage(DxfR12Codec.ReadEntities(document), drawingCodePage, options);
        }

        /// <summary>Creates a fresh model-space document in a supported modern database family.</summary>
        /// <remarks>
        /// Supports AutoCAD 2000, 2004, 2007, 2010, 2013 and 2018 families. The destination is a new
        /// unitless drawing, not an existing-document merge. R12 output is available through Save.
        /// Text and attribute values remain logical strings in the returned document. Use this
        /// plan's Save method for code-page-aware, safe control-character and literal-escape output;
        /// DxfDocument.Save retains its own framing and transport contracts. The modern declaration
        /// uses ModernDrawingCodePage; R12-only DOS alias spellings stay in the normalized selection.
        /// Cancellation is observed between decoding, resource and graph adoption phases.
        /// </remarks>
        public DxfDocument CreateDocument(DxfVersion version,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireModernVersion(version);
            cancellationToken.ThrowIfCancellationRequested();
            DxfDocument document = DxfR12Codec.CreateSelectionDocument(this.snapshot, version, cancellationToken);
            document.DrawingVariables.KnownValues().Single(v => v.Name == "$DWGCODEPAGE").Value = this.ModernDrawingCodePage;
            return document;
        }

        /// <summary>Stages the complete selected drawing before writing at the destination's current position.</summary>
        /// <remarks>
        /// Supports AC1009 and the six modern families accepted by CreateDocument. Output limits
        /// bound encoded bytes, tag count and decoded strings, not total managed heap. Modern
        /// output transiently uses DXF Unicode escapes for C0 values, literal carets and backslashes.
        /// R12 retains its declared encoding. Modern output retains ANSI_ declarations or maps DOS
        /// encodings using the explicit ModernDrawingCodePage policy; AutoCAD 2007 and newer output is always UTF-8.
        /// This does not change the snapshot or any previously returned editable document. Cancellation
        /// or validation failure before final copying leaves the destination untouched. IO failure
        /// or cancellation during final copying may leave partial output. The stream remains open;
        /// suffix truncation and atomic file replacement remain the caller's responsibility.
        /// </remarks>
        public void Save(Stream stream, DxfVersion version, bool binary = false,
            DxfRawOptions outputLimits = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("A writable stream is required.", nameof(stream));
            if (version != DxfVersion.AutoCad12) RequireModernVersion(version);
            cancellationToken.ThrowIfCancellationRequested();
            DxfRawOptions limits = outputLimits ?? this.options;
            string outputCodePage = version == DxfVersion.AutoCad12 ? this.DrawingCodePage : this.ModernDrawingCodePage;
            using (var staged = new SelectionOutputStream(limits.MaximumBytes, cancellationToken))
            {
                if (version == DxfVersion.AutoCad12)
                {
                    DxfRawDocument output = DxfR12Codec.CreateWithCodePage(DxfR12Codec.ReadEntities(this.snapshot),
                        outputCodePage, binary, limits);
                    output.Save(staged, binary, cancellationToken);
                }
                else
                {
                    DxfDocument document = this.CreateDocument(version, cancellationToken);
                    DxfR12Codec.PrepareSelectionTextOutput(document, limits, cancellationToken);
                    if (!document.Save(staged, binary))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new InvalidDataException("The selected drawing could not be serialized within the output limits.");
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                staged.Position = 0;
                DxfRawDocument validated = DxfRawDocument.Load(staged, limits, cancellationToken);
                // Reauthor only this private output profile, retaining every other tag
                // and enforcing the raw codec's actual encoding. Public raw profile-edit
                // guards remain unchanged; caller source documents are never rewritten.
                if (DxfR12Codec.SelectionCodePage(validated) != outputCodePage)
                {
                    DxfRawDocument recoded = DxfR12Codec.SetAuthoredCodePage(validated, outputCodePage, limits);
                    staged.SetLength(0);
                    staged.Position = 0;
                    recoded.Save(staged, binary, cancellationToken);
                }
                staged.Position = 0;
                var buffer = new byte[65536];
                int count;
                while ((count = staged.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Write(buffer, 0, count);
                }
            }
        }

        private static void RequireModernVersion(DxfVersion version)
        {
            switch (version)
            {
                case DxfVersion.AutoCad2000: case DxfVersion.AutoCad2004: case DxfVersion.AutoCad2007:
                case DxfVersion.AutoCad2010: case DxfVersion.AutoCad2013: case DxfVersion.AutoCad2018: return;
                default: throw new NotSupportedException("This selection plan supports R12 output and the six typed modern DXF families only.");
            }
        }

        // Composition ensures modern Span/async Stream overloads route through the
        // bounded byte-array Write, rather than bypassing MemoryStream overrides.
        private sealed class SelectionOutputStream : Stream
        {
            private readonly MemoryStream buffer = new MemoryStream();
            private readonly long maximum;
            private readonly CancellationToken cancellation;
            internal SelectionOutputStream(int maximum, CancellationToken cancellation)
            { this.maximum = maximum; this.cancellation = cancellation; }
            public override bool CanRead { get { return this.buffer.CanRead; } }
            public override bool CanSeek { get { return this.buffer.CanSeek; } }
            public override bool CanWrite { get { return this.buffer.CanWrite; } }
            public override long Length { get { return this.buffer.Length; } }
            public override long Position
            {
                get { return this.buffer.Position; }
                set { this.Check(value); this.buffer.Position = value; }
            }
            private void Check(long end)
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (end > this.maximum) throw new InvalidDataException("Selected drawing exceeds the encoded output budget.");
            }
            public override void Write(byte[] value, int offset, int count)
            { this.Check(checked(this.Position + count)); this.buffer.Write(value, offset, count); }
            public override void WriteByte(byte value)
            { this.Check(checked(this.Position + 1)); this.buffer.WriteByte(value); }
            public override int Read(byte[] value, int offset, int count) { return this.buffer.Read(value, offset, count); }
            public override int ReadByte() { return this.buffer.ReadByte(); }
            public override void Flush() { this.buffer.Flush(); }
            public override long Seek(long offset, SeekOrigin origin)
            {
                long basis;
                switch (origin)
                {
                    case SeekOrigin.Begin: basis = 0; break;
                    case SeekOrigin.Current: basis = this.Position; break;
                    case SeekOrigin.End: basis = this.Length; break;
                    default: throw new ArgumentOutOfRangeException(nameof(origin));
                }
                long target = checked(basis + offset);
                this.Check(target);
                return this.buffer.Seek(target, SeekOrigin.Begin);
            }
            public override void SetLength(long value)
            { this.Check(value); this.buffer.SetLength(value); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) this.buffer.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
