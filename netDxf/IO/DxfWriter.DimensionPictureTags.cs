using System;
using System.Collections.Generic;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        // Reuse the production dimension schema, including family-specific derived coordinates,
        // style overrides and preserved XData. Do not maintain a second DXF dimension serializer.
        internal static IReadOnlyList<DxfTag> DimensionPictureTags(DxfDocument document, Dimension dimension)
        {
            var sink = new DimensionTagWriter();
            var writer = new DxfWriter
            {
                doc = document,
                encodedStrings = new Dictionary<string, string>(),
                chunk = sink
            };
            writer.WriteDimension(dimension);
            return sink.Tags;
        }

        private sealed class DimensionTagWriter : ICodeValueWriter
        {
            internal readonly List<DxfTag> Tags = new List<DxfTag>();
            public short Code { get; private set; }
            public object Value { get; private set; }
            public long CurrentPosition { get { return this.Tags.Count; } }
            public void Write(short code, object value)
            {
                this.Tags.Add(new DxfTag(code, value)); this.Code = code; this.Value = value;
            }
            public void Flush() { }
            // Only complete typed tags are meaningful in a detached schema collector.
            public void WriteByte(byte value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteBytes(byte[] value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteShort(short value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteInt(int value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteLong(long value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteBool(bool value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteDouble(double value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
            public void WriteString(string value) { throw new InvalidOperationException("Expected a complete dimension tag."); }
        }
    }
}
