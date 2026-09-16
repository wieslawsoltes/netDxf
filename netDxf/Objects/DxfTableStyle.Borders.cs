// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>Immutable stored lineweight, visibility and color for one TABLESTYLE border slot.</summary>
    /// <remarks>Signed lineweight and color codes are retained without normalization or layout evaluation.</remarks>
    public sealed class DxfTableStyleBorderValues
    {
        /// <summary>Creates one immutable border value triple.</summary>
        /// <param name="storedLineweight">The signed value of the slot's group 274 through 279.</param>
        /// <param name="isVisible">The value of the slot's group 284 through 289.</param>
        /// <param name="storedColor">The signed value of the slot's group 64 through 69.</param>
        public DxfTableStyleBorderValues(short storedLineweight, bool isVisible, short storedColor)
        { this.StoredLineweight = storedLineweight; this.IsVisible = isVisible; this.StoredColor = storedColor; }
        /// <summary>Gets the stored lineweight code, including special values.</summary>
        public short StoredLineweight { get; }
        /// <summary>Gets whether this stored border is visible.</summary>
        public bool IsVisible { get; }
        /// <summary>Gets the stored color code, including special values.</summary>
        public short StoredColor { get; }
    }

    /// <summary>Immutable values for all six classic TABLESTYLE border slots.</summary>
    /// <remarks>
    /// Slot i contains groups 274+i, 284+i and 64+i. This preserves the source slot order;
    /// it does not resolve shared cell edges, infer row roles, synchronize maps or render borders.
    /// All eighteen public fields must be present exactly once to project or edit a stored set.
    /// </remarks>
    public sealed class DxfTableStyleRowBorders
    {
        /// <summary>The number of border slots in the classic stored row format.</summary>
        public const int BorderCount = 6;
        /// <summary>Creates a stored border set from exactly six non-null immutable value triples.</summary>
        /// <remarks>Input is copied with bounded enumeration. Enumeration/disposal failures do not publish a border set.</remarks>
        public DxfTableStyleRowBorders(IEnumerable<DxfTableStyleBorderValues> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var snapshot = new List<DxfTableStyleBorderValues>(BorderCount);
            foreach (DxfTableStyleBorderValues value in values)
            {
                if (snapshot.Count == BorderCount) throw new ArgumentException("Exactly six border values are required.", nameof(values));
                if (value == null) throw new ArgumentException("A border value cannot be null.", nameof(values));
                snapshot.Add(value);
            }
            if (snapshot.Count != BorderCount) throw new ArgumentException("Exactly six border values are required.", nameof(values));
            this.Values = snapshot.AsReadOnly();
        }
        /// <summary>Gets six immutable triples in stored slot order.</summary>
        public IReadOnlyList<DxfTableStyleBorderValues> Values { get; }
        /// <summary>Returns a new border set replacing one zero-based stored slot.</summary>
        public DxfTableStyleRowBorders WithBorder(int index, DxfTableStyleBorderValues value)
        {
            if (index < 0 || index >= BorderCount) throw new ArgumentOutOfRangeException(nameof(index));
            if (value == null) throw new ArgumentNullException(nameof(value));
            var snapshot = this.Values.ToArray(); snapshot[index] = value;
            return new DxfTableStyleRowBorders(snapshot);
        }
        internal static DxfTableStyleRowBorders TryRead(List<DxfTag> tags)
        {
            var fields = new DxfTag[BorderCount * 3];
            foreach (DxfTag tag in tags)
            {
                int slot = tag.Code >= 274 && tag.Code <= 279 ? tag.Code - 274
                    : tag.Code >= 284 && tag.Code <= 289 ? tag.Code - 284 + BorderCount
                    : tag.Code >= 64 && tag.Code <= 69 ? tag.Code - 64 + 2 * BorderCount : -1;
                if (slot < 0) continue;
                if (fields[slot] != null) return null;
                fields[slot] = tag;
            }
            var values = new DxfTableStyleBorderValues[BorderCount];
            for (int i = 0; i < BorderCount; i++)
            {
                if (fields[i] == null || fields[i + BorderCount] == null || fields[i + 2 * BorderCount] == null) return null;
                short flag = (short)fields[i + BorderCount].Value;
                if (flag != 0 && flag != 1) return null;
                values[i] = new DxfTableStyleBorderValues((short)fields[i].Value, flag != 0, (short)fields[i + 2 * BorderCount].Value);
            }
            return new DxfTableStyleRowBorders(values);
        }
    }
}
