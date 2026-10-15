using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal static class DxfDimensionPictureExport
    {
        private const string Marker = "NETDXF_DIMENSION_PICTURE";
        private const string Schema = "1";

        private sealed class Picture
        {
            internal Dimension Source;
            internal Dimension Prepared;
            internal Block Generated;
            internal Block Existing;
        }

        internal static void Write(DxfDocument document, Stream destination, bool binary,
            DxfDimensionPictureOptions options, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var pictures = Prepare(document, options, token);
            // Zero eligible dimensions retains the ordinary typed writer path, still staged/bounded.
            using (var staging = new BoundedBuffer(options.RawOptions.MaximumBytes))
            {
                IReadOnlyDictionary<string, IReadOnlyList<DxfTag>> replacements;
                using (var registrations = new Registrations(document))
                {
                    foreach (Picture picture in pictures)
                    {
                        token.ThrowIfCancellationRequested();
                        registrations.Attach(picture);
                    }
                    new DxfWriter().Write(staging, document, binary);
                    token.ThrowIfCancellationRequested();
                    var byHandle = new Dictionary<string, IReadOnlyList<DxfTag>>(StringComparer.OrdinalIgnoreCase);
                    foreach (Picture picture in pictures)
                        byHandle.Add(picture.Source.Handle, DxfWriter.DimensionPictureTags(document, picture.Prepared));
                    replacements = byHandle;
                }
                // Source caches have already been restored. A raw snapshot preserves every other tag
                // rather than introducing a lossy typed save/load/clone round trip.
                staging.Position = 0;
                var raw = DxfRawDocument.Load(staging, options.RawOptions, token);
                if (pictures.Count == 0) { raw.Save(destination, binary, token); return; }
                var edits = new Dictionary<int, Tuple<int, IReadOnlyList<DxfTag>>>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DxfRawSection section in raw.Sections)
                {
                    if (section.Name != "ENTITIES" && section.Name != "BLOCKS") continue;
                    foreach (DxfRawRecord record in section.Records)
                    {
                        if (record.Name != "DIMENSION" && record.Name != "ARC_DIMENSION") continue;
                        string handle = record.Tags.FirstOrDefault(t => t.Code == 5)?.Value as string;
                        if (handle == null || !replacements.TryGetValue(handle, out IReadOnlyList<DxfTag> replacement)) continue;
                        if (!seen.Add(handle)) throw new InvalidOperationException("A dimension handle occurs more than once in the export.");
                        int first = record.StartTagIndex;
                        while (first < record.EndTagIndex && !(raw.Tags[first].Code == 100 && (string)raw.Tags[first].Value == SubclassMarker.Dimension)) first++;
                        if (first == record.EndTagIndex) throw new InvalidOperationException("The native dimension subclass was not written.");
                        edits.Add(first, Tuple.Create(record.EndTagIndex, replacement));
                    }
                }
                if (seen.Count != replacements.Count) throw new InvalidOperationException("Not every prepared dimension was serialized.");
                var tags = new List<DxfTag>(raw.Tags.Count);
                for (int i = 0; i < raw.Tags.Count;)
                {
                    if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                    if (edits.TryGetValue(i, out Tuple<int, IReadOnlyList<DxfTag>> edit))
                    { tags.AddRange(edit.Item2); i = edit.Item1; }
                    else tags.Add(raw.Tags[i++]);
                    if (tags.Count > options.RawOptions.MaximumTags) throw new InvalidOperationException("Dimension export exceeded the tag budget.");
                }
                raw.WithTags(tags).Save(destination, binary, token);
            }
        }

        private static List<Picture> Prepare(DxfDocument document, DxfDimensionPictureOptions options, CancellationToken token)
        {
            var result = new List<Picture>();
            var names = new HashSet<string>(document.Blocks.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            int index = 0, entities = 0;
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                foreach (Dimension source in document.Blocks.SelectMany(b => b.Entities).OfType<Dimension>())
                {
                    token.ThrowIfCancellationRequested();
                    Block existing = source.Block;
                    if (existing != null && !GeneratedByUs(existing)) continue;
                    if (result.Count >= options.MaximumPictures) throw new InvalidOperationException("Dimension picture count exceeds the export budget.");
                    if (existing != null && document.Blocks.GetReferences(existing).Any(r => !ReferenceEquals(r.Reference, source) || r.Uses != 1))
                        throw new NotSupportedException("A generated dimension picture is shared; it cannot be regenerated without an explicit ownership decision.");
                    string name = existing?.Name;
                    if (name == null)
                    {
                        do { name = "*D" + (++index).ToString(CultureInfo.InvariantCulture); } while (!names.Add(name));
                    }
                    var prepared = (Dimension)source.Clone(); prepared.Block = null;
                    // The detached geometry may use existing immutable resource identities. This
                    // keeps override handles valid; cloning a style would erase its registered handle.
                    prepared.Style = source.Style; prepared.StyleOverrides.Clear();
                    foreach (DimensionStyleOverride item in source.StyleOverrides.Values)
                        prepared.StyleOverrides.Add(new DimensionStyleOverride(item.Type, item.Value));
                    prepared.Update();
                    Block generated = DimensionBlock.BuildForOwner(prepared, name, source.Owner)
                        ?? throw new NotSupportedException("No native dimension picture builder is available.");
                    entities = checked(entities + generated.Entities.Count);
                    if (entities > options.MaximumPictureEntities) throw new InvalidOperationException("Dimension picture entities exceed the export budget.");
                    var marker = new XData(new ApplicationRegistry(Marker));
                    marker.XDataRecord.Add(new XDataRecord(XDataCode.String, Schema)); generated.XData.Add(marker);
                    result.Add(new Picture { Source = source, Prepared = prepared, Generated = generated, Existing = existing });
                }
                return result;
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private static bool GeneratedByUs(Block block)
        {
            return block.XData.TryGetValue(Marker, out XData data) && data.XDataRecord.Count == 1 &&
                data.XDataRecord[0].Code == XDataCode.String && (data.XDataRecord[0].Value as string) == Schema;
        }

        private sealed class Registrations : IDisposable
        {
            private readonly DxfDocument document;
            private readonly List<Action> restore = new List<Action>();
            internal Registrations(DxfDocument document) { this.document = document; }
            internal void Attach(Picture picture)
            {
                if (picture.Existing == null)
                {
                    Block block = picture.Generated;
                    // Register cleanup first, so partial table attachment is not left behind.
                    this.restore.Add(() =>
                    {
                        if (ReferenceEquals(this.document.Blocks[block.Name], block) && !this.document.Blocks.Remove(block))
                            throw new InvalidOperationException("Could not remove a temporary dimension picture.");
                    });
                    Block registered = this.document.Blocks.Add(block);
                    if (!ReferenceEquals(registered, block)) throw new InvalidOperationException("A dimension picture name was acquired concurrently.");
                    picture.Prepared.Block = registered;
                }
                else
                {
                    Block block = picture.Existing;
                    EntityObject[] original = block.Entities.ToArray();
                    EntityObject[] generated = picture.Generated.Entities.ToArray();
                    picture.Generated.Entities.Clear();
                    this.restore.Add(() => { block.Entities.Clear(); block.Entities.AddRange(original); });
                    block.Entities.Clear(); block.Entities.AddRange(generated);
                    picture.Prepared.Block = block;
                }
            }
            public void Dispose()
            {
                var failures = new List<Exception>();
                for (int i = this.restore.Count - 1; i >= 0; i--)
                { try { this.restore[i](); } catch (Exception error) { failures.Add(error); } }
                this.restore.Clear();
                if (failures.Count != 0) throw new AggregateException("Dimension export could not restore temporary source registrations.", failures);
            }
        }

        // Wrap rather than inherit MemoryStream: modern span writes then dispatch through
        // Stream's byte[] fallback and cannot bypass the configured budget.
        private sealed class BoundedBuffer : Stream
        {
            private readonly int maximum;
            private readonly MemoryStream buffer = new MemoryStream();
            internal BoundedBuffer(int maximum) { this.maximum = maximum; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { return this.buffer.Length; } }
            public override long Position { get { return this.buffer.Position; } set { this.buffer.Position = value; } }
            public override void Flush() { this.buffer.Flush(); }
            public override int Read(byte[] bytes, int offset, int count) { return this.buffer.Read(bytes, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) { return this.buffer.Seek(offset, origin); }
            public override void Write(byte[] bytes, int offset, int count)
            {
                if (count > this.maximum - this.Position) throw new InvalidOperationException("Dimension export exceeded the byte budget.");
                this.buffer.Write(bytes, offset, count);
            }
            public override void WriteByte(byte value)
            {
                if (this.Position >= this.maximum) throw new InvalidOperationException("Dimension export exceeded the byte budget.");
                this.buffer.WriteByte(value);
            }
            public override void SetLength(long value)
            {
                if (value > this.maximum) throw new InvalidOperationException("Dimension export exceeded the byte budget.");
                this.buffer.SetLength(value);
            }
            protected override void Dispose(bool disposing)
            { if (disposing) this.buffer.Dispose(); base.Dispose(disposing); }
        }
    }
}
