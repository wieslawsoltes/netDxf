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
            this.RootEntityCount = DxfR12Codec.ReadEntities(snapshot).Count;
            this.BlockCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "BLOCK");
            this.AttributeDefinitionCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "ATTDEF");
            this.AttributeCount = snapshot.Sections.SelectMany(s => s.Records).Count(r => r.Name == "ATTRIB");
        }

        /// <summary>Gets the immutable normalized AC1009 selection, not the original full drawing.</summary>
        public DxfRawDocument NormalizedSelection { get { return this.snapshot; } }
        /// <summary>Gets the number of selected top-level entities; sequence children are not roots.</summary>
        public int RootEntityCount { get; }
        /// <summary>Gets the number of reachable shared block definitions.</summary>
        public int BlockCount { get; }
        /// <summary>Gets the number of selected block attribute definitions.</summary>
        public int AttributeDefinitionCount { get; }
        /// <summary>Gets the number of stored attribute instances, without array expansion.</summary>
        public int AttributeCount { get; }

        /// <summary>Captures a strict supported entity selection without adopting or changing its source.</summary>
        public static DxfR12SelectionPlan Prepare(IEnumerable<EntityObject> entities, DxfRawOptions options = null)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            options = options ?? new DxfRawOptions();
            return new DxfR12SelectionPlan(DxfR12Codec.Create(entities, false, options), options);
        }

        /// <summary>Selects supported ENTITIES and their reachable dependencies from an AC1009 raw drawing.</summary>
        /// <remarks>Unselected raw content remains in the caller's original immutable document only.</remarks>
        public static DxfR12SelectionPlan Prepare(DxfRawDocument document, DxfRawOptions options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return Prepare(DxfR12Codec.ReadEntities(document), options);
        }

        /// <summary>Creates a fresh model-space document in a supported modern database family.</summary>
        /// <remarks>
        /// Supports AutoCAD 2000, 2004, 2007, 2010, 2013 and 2018 families. The destination is a new
        /// unitless drawing, not an existing-document merge. R12 output is available through Save.
        /// Cancellation is observed between decoding, resource and graph adoption phases.
        /// </remarks>
        public DxfDocument CreateDocument(DxfVersion version,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            RequireModernVersion(version);
            cancellationToken.ThrowIfCancellationRequested();
            return DxfR12Codec.CreateSelectionDocument(this.snapshot, version, cancellationToken);
        }

        /// <summary>Stages the complete selected drawing before writing at the destination's current position.</summary>
        /// <remarks>
        /// Supports AC1009 and the six modern families accepted by CreateDocument. Output limits
        /// bound encoded bytes, tag count and decoded strings, not total managed heap. Cancellation
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
            using (var staged = new SelectionOutputStream(limits.MaximumBytes, cancellationToken))
            {
                if (version == DxfVersion.AutoCad12)
                {
                    // Author directly in the requested transport; never push text comment tags
                    // into the strict raw binary serializer.
                    DxfRawDocument output = DxfR12Codec.Create(DxfR12Codec.ReadEntities(this.snapshot), binary, limits);
                    output.Save(staged, binary, cancellationToken);
                }
                else
                {
                    DxfDocument document = this.CreateDocument(version, cancellationToken);
                    if (!document.Save(staged, binary))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new InvalidDataException("The selected drawing could not be serialized within the output limits.");
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                staged.Position = 0;
                DxfRawDocument.Load(staged, limits, cancellationToken); // Enforce tag and string budgets too.
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

        private sealed class SelectionOutputStream : MemoryStream
        {
            private readonly long maximum;
            private readonly CancellationToken cancellation;
            internal SelectionOutputStream(int maximum, CancellationToken cancellation)
            { this.maximum = maximum; this.cancellation = cancellation; }
            private void Check(long end)
            {
                this.cancellation.ThrowIfCancellationRequested();
                if (end > this.maximum) throw new InvalidDataException("Selected drawing exceeds the encoded output budget.");
            }
            public override void Write(byte[] buffer, int offset, int count)
            { this.Check(this.Position + count); base.Write(buffer, offset, count); }
            public override void WriteByte(byte value)
            { this.Check(this.Position + 1); base.WriteByte(value); }
            public override void SetLength(long value)
            { this.Check(value); base.SetLength(value); }
        }
    }
}
