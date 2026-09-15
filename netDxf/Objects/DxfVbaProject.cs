using System;
using System.Collections.Generic;

namespace netDxf.Objects
{
    /// <summary>An inert VBA_PROJECT byte envelope. Its contents are never executed or interpreted.</summary>
    /// <remarks>Typed export is qualified for AutoCAD 2000 and later. Physical group-310 chunk boundaries, including empty chunks, are retained.</remarks>
    public sealed class DxfVbaProject : DxfDatabaseObject
    {
        /// <summary>The maximum admitted payload size, in bytes (16 MiB).</summary>
        public const int MaximumDataLength = 16 * 1024 * 1024;
        /// <summary>The maximum admitted number of physical group-310 chunks.</summary>
        public const int MaximumChunkCount = 262144;
        /// <summary>The maximum size of one qualified group-310 chunk, in bytes.</summary>
        public const int MaximumChunkLength = 127;
        private List<byte[]> chunks = new List<byte[]>();
        private int dataLength;
        /// <summary>Creates a detached project with zero bytes and no physical chunks.</summary>
        public DxfVbaProject() : base("VBA_PROJECT") { }
        /// <summary>Gets the byte count written in group 90.</summary>
        public int DataLength { get { return this.dataLength; } }
        /// <summary>Gets a defensive byte snapshot, or replaces the payload using canonical chunks of up to 127 bytes.</summary>
        /// <remarks>Null or an oversized array is rejected before changing the payload.</remarks>
        public byte[] Data
        {
            get
            {
                byte[] result = new byte[this.dataLength]; int offset = 0;
                foreach (byte[] chunk in this.chunks) { Array.Copy(chunk, 0, result, offset, chunk.Length); offset += chunk.Length; }
                return result;
            }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (value.Length > MaximumDataLength) throw new ArgumentOutOfRangeException(nameof(value));
                var replacement = new List<byte[]>();
                for (int offset = 0; offset < value.Length; offset += MaximumChunkLength)
                {
                    byte[] chunk = new byte[Math.Min(MaximumChunkLength, value.Length - offset)];
                    Array.Copy(value, offset, chunk, 0, chunk.Length); replacement.Add(chunk);
                }
                this.chunks = replacement; this.dataLength = value.Length;
            }
        }
        /// <summary>Gets a defensive snapshot of the exact physical chunks, including zero-length chunks.</summary>
        public IReadOnlyList<byte[]> Chunks
        {
            get
            {
                var result = new List<byte[]>(this.chunks.Count);
                foreach (byte[] chunk in this.chunks) result.Add((byte[])chunk.Clone());
                return result.AsReadOnly();
            }
        }
        /// <summary>Replaces the physical chunks transactionally after validating and copying the complete sequence.</summary>
        /// <remarks>Each chunk must be nonnull and no longer than 127 bytes. Payload and chunk-count admission limits also apply.</remarks>
        public void SetChunks(IEnumerable<byte[]> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var replacement = new List<byte[]>(); int length = 0;
            foreach (byte[] chunk in value)
            {
                if (chunk == null) throw new ArgumentException("A chunk cannot be null.", nameof(value));
                if (chunk.Length > MaximumChunkLength || replacement.Count == MaximumChunkCount || chunk.Length > MaximumDataLength - length)
                    throw new ArgumentOutOfRangeException(nameof(value), "The project exceeds a payload or physical chunk admission limit.");
                replacement.Add((byte[])chunk.Clone()); length += chunk.Length;
            }
            this.chunks = replacement; this.dataLength = length;
        }
        internal IReadOnlyList<byte[]> StoredChunks { get { return this.chunks; } }
        internal override DxfDatabaseObject CloneShell()
        {
            var result = new DxfVbaProject(); result.SetChunks(this.chunks); return result;
        }
    }
}
