// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>An immutable, bounded binary FIELD value. Bytes are inert and are not interpreted as code or references.</summary>
    public sealed class DxfFieldBinaryValue : IEquatable<DxfFieldBinaryValue>
    {
        /// <summary>Maximum bytes in one projected or submitted binary value.</summary>
        public const int MaximumLength = 1048576;
        private readonly byte[] bytes;

        /// <summary>Creates a binary value by copying the complete source buffer.</summary>
        public DxfFieldBinaryValue(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length > MaximumLength) throw new ArgumentOutOfRangeException(nameof(bytes));
            this.bytes = (byte[])bytes.Clone();
        }

        // Only already validated immutable tag buffers enter this constructor. The
        // public length is verified from actual chunks before allocating that length.
        internal DxfFieldBinaryValue(IList<byte[]> chunks, int length)
        {
            this.bytes = new byte[length];
            int offset = 0;
            foreach (byte[] chunk in chunks)
            { Buffer.BlockCopy(chunk, 0, this.bytes, offset, chunk.Length); offset += chunk.Length; }
        }

        /// <summary>Gets the number of bytes, including zero for an explicitly empty binary value.</summary>
        public int Length { get { return this.bytes.Length; } }
        /// <summary>Gets one byte; the index must be within the value.</summary>
        public byte this[int index] { get { return this.bytes[index]; } }
        /// <summary>Returns a new independent copy of the complete buffer.</summary>
        public byte[] ToArray() { return (byte[])this.bytes.Clone(); }
        /// <summary>Compares binary contents, not buffer identities.</summary>
        public bool Equals(DxfFieldBinaryValue other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || other.Length != this.Length) return false;
            for (int i = 0; i < this.bytes.Length; i++) if (this.bytes[i] != other.bytes[i]) return false;
            return true;
        }
        /// <summary>Compares another immutable FIELD binary value by its contents.</summary>
        public override bool Equals(object obj) { return this.Equals(obj as DxfFieldBinaryValue); }
        /// <summary>Gets a content-based hash without exposing the backing buffer.</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (byte item in this.bytes) hash = (hash ^ item) * 16777619;
                return hash;
            }
        }

        internal void WriteScalarTags(List<DxfTag> tags)
        {
            tags.Add(new DxfTag(92, this.Length));
            for (int offset = 0; offset < this.Length; offset += 127)
            {
                int count = Math.Min(127, this.Length - offset);
                var chunk = new byte[count];
                Buffer.BlockCopy(this.bytes, offset, chunk, 0, count);
                tags.Add(new DxfTag(310, chunk));
            }
        }
    }
}
