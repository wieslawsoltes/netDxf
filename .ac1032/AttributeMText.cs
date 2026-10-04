// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Immutable, independently placed MTEXT content owned by an AC1032 attribute.</summary>
    /// <remarks>
    /// This is not a standalone database entity. The enclosing attribute's fallback TEXT fields
    /// remain independent. Tags contain logical strings and the embedded MTEXT body only, without
    /// handles, subclasses, XData or the group-101 marker. Known optional fields retain presence.
    /// A shared TextStyle is mutable like other document resources. Font measurement, automatic
    /// wrapping and column layout are not performed. Unknown or pointer-bearing body fields reject.
    /// </remarks>
    public sealed class AttributeMText
    {
        private readonly ReadOnlyCollection<DxfTag> tags;
        private readonly TextStyle style;

        /// <summary>Creates ordinary multiline content with an explicit WCS position and reference width.</summary>
        public AttributeMText(string value, Vector3 position, double height, double rectangleWidth, TextStyle style = null)
            : this(new[] { new DxfTag(10, position.X), new DxfTag(20, position.Y), new DxfTag(30, position.Z),
                new DxfTag(40, height), new DxfTag(41, rectangleWidth), new DxfTag(71, (short)1),
                new DxfTag(72, (short)1), new DxfTag(1, value ?? throw new ArgumentNullException(nameof(value))) },
                style ?? TextStyle.Default) { }

        /// <summary>Creates a validated body; text chunks must already be joined into one logical group 1.</summary>
        /// <remarks>All input tags are copied. Use WithTags to replace the complete immutable definition.</remarks>
        public AttributeMText(IEnumerable<DxfTag> tags, TextStyle style)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            this.style = style ?? throw new ArgumentNullException(nameof(style));
            var copy = tags.ToArray();
            var seen = new HashSet<short>();
            foreach (DxfTag tag in copy)
            {
                if (!IsSupportedCode(tag.Code)) throw new NotSupportedException("Unsupported embedded attribute MTEXT group " + tag.Code);
                if (!seen.Add(tag.Code)) throw new FormatException("Duplicate embedded attribute MTEXT group " + tag.Code);
                if (tag.Value is double d && (double.IsNaN(d) || double.IsInfinity(d)))
                    throw new ArgumentException("Embedded text dimensions and coordinates must be finite.", nameof(tags));
                if (tag.Code == 7 && !string.Equals((string)tag.Value, style.Name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Embedded style tag and resource disagree.", nameof(style));
            }
            foreach (short required in new short[] { 10, 20, 30, 40, 41, 1 })
                if (!seen.Contains(required)) throw new FormatException("Missing embedded attribute MTEXT group " + required);
            this.tags = new ReadOnlyCollection<DxfTag>(copy);
            ValidateVector(seen, 11, false); ValidateVector(seen, 210, true);
            if (this.Height <= 0 || this.RectangleWidth < 0) throw new ArgumentOutOfRangeException(nameof(tags));
            foreach (short code in new short[] { 42, 43, 46 })
                if (this.Number(code, 0) < 0) throw new ArgumentOutOfRangeException(nameof(tags));
            int attachment = this.Integer(71, 1), flow = this.Integer(72, 1), spacing = this.Integer(73, 1);
            if (attachment < 1 || attachment > 9 || (flow != 1 && flow != 3 && flow != 5)
                || spacing < 1 || spacing > 2 || this.Number(44, 1) < .25 || this.Number(44, 1) > 4)
                throw new ArgumentOutOfRangeException(nameof(tags), "Invalid embedded MTEXT layout setting.");
            if (seen.Contains(90) && (Convert.ToInt32(this.Item(90)) & ~19) != 0)
                throw new NotSupportedException("Unknown embedded MTEXT background flags.");
            if (seen.Contains(45) && (this.Number(45, 1.5) < 1 || this.Number(45, 1.5) > 5))
                throw new ArgumentOutOfRangeException(nameof(tags));
            if (seen.Contains(63) && (this.Integer(63, 0) < 0 || this.Integer(63, 0) > 256))
                throw new ArgumentOutOfRangeException(nameof(tags));
            if (seen.Contains(421) && (Convert.ToInt32(this.Item(421)) < 0 || Convert.ToInt32(this.Item(421)) > 0xffffff))
                throw new ArgumentOutOfRangeException(nameof(tags));
        }

        /// <summary>Gets the immutable embedded body, with logical rather than DXF-escaped strings.</summary>
        public IReadOnlyList<DxfTag> Tags { get { return this.tags; } }
        /// <summary>Gets the embedded resource, independently of the fallback TEXT style.</summary>
        public TextStyle Style { get { return this.style; } }
        /// <summary>Gets the complete MTEXT content, including formatting controls.</summary>
        public string Value { get { return (string)this.Item(1); } }
        /// <summary>Gets the WCS insertion point.</summary>
        public Vector3 Position { get { return this.Vector(10, Vector3.Zero); } }
        /// <summary>Gets the character height.</summary>
        public double Height { get { return this.Number(40, 1); } }
        /// <summary>Gets the reference rectangle width, not a measured text extent.</summary>
        public double RectangleWidth { get { return this.Number(41, 0); } }
        /// <summary>Gets the extrusion direction, defaulting to the world Z axis.</summary>
        public Vector3 Normal { get { return this.Vector(210, Vector3.UnitZ); } }
        /// <summary>Gets optional stored text direction.</summary>
        public Vector3? TextDirection { get { return this.Has(11) ? (Vector3?)this.Vector(11, Vector3.UnitX) : null; } }
        /// <summary>Gets optional stored common rotation in degrees.</summary>
        public double? Rotation { get { return this.Has(50) ? (double?)this.Number(50, 0) : null; } }
        /// <summary>Gets optional defined height.</summary>
        public double? DefinedHeight { get { return this.Optional(46); } }
        /// <summary>Gets optional saved horizontal extent; this class does not measure it.</summary>
        public double? ActualWidth { get { return this.Optional(42); } }
        /// <summary>Gets optional saved vertical extent; this class does not measure it.</summary>
        public double? ActualHeight { get { return this.Optional(43); } }
        /// <summary>Gets attachment in the MTEXT 3-by-3 placement grid.</summary>
        public MTextAttachmentPoint AttachmentPoint { get { return (MTextAttachmentPoint)this.Integer(71, 1); } }
        /// <summary>Gets the stored drawing-direction setting.</summary>
        public MTextDrawingDirection DrawingDirection { get { return (MTextDrawingDirection)this.Integer(72, 1); } }
        /// <summary>Gets the line-spacing style.</summary>
        public MTextLineSpacingStyle LineSpacingStyle { get { return (MTextLineSpacingStyle)this.Integer(73, 1); } }
        /// <summary>Gets the line-spacing factor.</summary>
        public double LineSpacingFactor { get { return this.Number(44, 1); } }

        /// <summary>Returns edited text with measured extents removed because they are now stale.</summary>
        public AttributeMText WithValue(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value == this.Value) return this;
            return new AttributeMText(this.tags.Where(t => t.Code != 42 && t.Code != 43)
                .Select(t => t.Code == 1 ? new DxfTag(1, value) : t), this.style);
        }
        /// <summary>Returns a complete validated body replacement.</summary>
        public AttributeMText WithTags(IEnumerable<DxfTag> value) { return new AttributeMText(value, this.style); }
        /// <summary>Returns the same body bound to a different resource; any explicit style tag follows it.</summary>
        public AttributeMText WithStyle(TextStyle value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (ReferenceEquals(value, this.style)) return this;
            return new AttributeMText(this.tags.Select(t => t.Code == 7 ? new DxfTag(7, value.Name) : t), value);
        }
        /// <summary>Clones the body and its text-style resource independently.</summary>
        public AttributeMText Clone() { return this.WithStyle((TextStyle)this.style.Clone()); }

        /// <summary>Returns a detached editable MTEXT entity with the supported embedded layout settings.</summary>
        public MText ToMText()
        {
            var result = new MText(this.Value) { Position = this.Position, Height = this.Height,
                RectangleWidth = this.RectangleWidth, Normal = this.Normal, Style = this.style,
                AttachmentPoint = this.AttachmentPoint, DrawingDirection = this.DrawingDirection,
                LineSpacingStyle = this.LineSpacingStyle, LineSpacingFactor = this.LineSpacingFactor };
            if (this.DefinedHeight.HasValue) result.DefinedHeight = this.DefinedHeight;
            // Respect the last physical direction/rotation declaration, like ordinary MTEXT.
            foreach (DxfTag tag in this.tags)
            {
                if (tag.Code == 50) result.Rotation = (double)tag.Value;
                else if (tag.Code == 11)
                {
                    Vector3 direction = MathHelper.Transform(this.TextDirection.Value, this.Normal,
                        CoordinateSystem.World, CoordinateSystem.Object);
                    result.Rotation = Math.Atan2(direction.Y, direction.X) * MathHelper.RadToDeg;
                }
            }
            if (this.Has(90) || this.Has(45) || this.Has(63) || this.Has(421) || this.Has(431) || this.Has(441))
            {
                result.BackgroundFill = new MTextBackgroundFill {
                    Flags = (MTextBackgroundFillFlags)Convert.ToInt32(this.Item(90) ?? 0),
                    FillBoxScale = this.Optional(45), ColorIndex = this.Has(63) ? (short?)this.Integer(63, 0) : null,
                    TrueColor = this.Has(421) ? (int?)Convert.ToInt32(this.Item(421)) : null,
                    ColorName = (string)this.Item(431), Transparency = this.Has(441) ? (int?)Convert.ToInt32(this.Item(441)) : null };
            }
            return result;
        }

        /// <summary>Returns a transformed copy for positive uniform similarities; other transforms reject before mutation.</summary>
        public AttributeMText TransformBy(Matrix3 matrix, Vector3 translation)
        {
            Vector3 x = matrix * Vector3.UnitX, y = matrix * Vector3.UnitY, z = matrix * Vector3.UnitZ;
            double scale = x.Modulus();
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0
                || Math.Abs(y.Modulus() / scale - 1) > 1e-10 || Math.Abs(z.Modulus() / scale - 1) > 1e-10
                || Math.Abs(Vector3.DotProduct(x / scale, y / scale)) > 1e-10
                || Math.Abs(Vector3.DotProduct(x / scale, z / scale)) > 1e-10
                || Math.Abs(Vector3.DotProduct(y / scale, z / scale)) > 1e-10
                || Vector3.DotProduct(Vector3.CrossProduct(x / scale, y / scale), z / scale) <= 0)
                throw new NotSupportedException("Embedded attribute text requires a positive uniform similarity transform.");
            MText text = this.ToMText(); text.TransformBy(matrix, translation);
            var output = this.tags.Where(t => !VectorCode(t.Code, 10) && !VectorCode(t.Code, 11)
                && !VectorCode(t.Code, 210) && t.Code != 50).ToList();
            for (int i = 0; i < output.Count; i++)
                if (new short[] { 40, 41, 42, 43, 46 }.Contains(output[i].Code))
                    output[i] = new DxfTag(output[i].Code, (double)output[i].Value * scale);
            AddVector(output, 10, text.Position); AddVector(output, 210, text.Normal);
            double radians = text.Rotation * MathHelper.DegToRad;
            AddVector(output, 11, MathHelper.Transform(new Vector3(Math.Cos(radians), Math.Sin(radians), 0),
                text.Normal, CoordinateSystem.Object, CoordinateSystem.World));
            return new AttributeMText(output, this.style);
        }
        internal static bool IsSupportedCode(short code)
        {
            return VectorCode(code, 10) || VectorCode(code, 11) || VectorCode(code, 210)
                || new short[] { 1, 7, 40, 41, 42, 43, 44, 45, 46, 50, 63, 71, 72, 73, 90, 421, 431, 441 }.Contains(code);
        }
        private static bool VectorCode(short code, short first) { return code == first || code == first + 10 || code == first + 20; }
        private static void AddVector(List<DxfTag> target, short first, Vector3 value)
        { target.Add(new DxfTag(first, value.X)); target.Add(new DxfTag((short)(first + 10), value.Y)); target.Add(new DxfTag((short)(first + 20), value.Z)); }
        private void ValidateVector(HashSet<short> seen, short first, bool normal)
        {
            if (!seen.Contains(first) && !seen.Contains((short)(first + 10)) && !seen.Contains((short)(first + 20))) return;
            if (!seen.Contains(first) || !seen.Contains((short)(first + 10)) || !seen.Contains((short)(first + 20)))
                throw new FormatException("Incomplete embedded MTEXT vector.");
            Vector3 v = this.Vector(first, Vector3.Zero);
            if (v.X == 0 && v.Y == 0 && v.Z == 0) throw new FormatException(normal ? "Zero embedded normal." : "Zero embedded text direction.");
        }
        private object Item(short code) { foreach (DxfTag t in this.tags) if (t.Code == code) return t.Value; return null; }
        private bool Has(short code) { return this.Item(code) != null; }
        private double Number(short code, double fallback) { object value = this.Item(code); return value == null ? fallback : (double)value; }
        private short Integer(short code, short fallback) { object value = this.Item(code); return value == null ? fallback : (short)value; }
        private double? Optional(short code) { return this.Has(code) ? (double?)this.Number(code, 0) : null; }
        private Vector3 Vector(short first, Vector3 fallback)
        { return new Vector3(this.Number(first, fallback.X), this.Number((short)(first + 10), fallback.Y), this.Number((short)(first + 20), fallback.Z)); }
    }
}
