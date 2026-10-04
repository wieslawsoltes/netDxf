// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable, independent MTEXT content embedded in a DXF 2018 attribute.</summary>
    /// <remarks>Use GetMText/SetMText on the host to edit snapshots. This is not a database entity.
    /// Optional fields and both ordered orientation representations are retained. No font measurement,
    /// wrapping, column layout or automatic single-line fallback regeneration is performed.</remarks>
    public sealed class AttributeMText : ICloneable
    {
        private readonly Dictionary<short, object> fields = new Dictionary<short, object>();
        private readonly List<short> order = new List<short>();
        private string value = string.Empty;
        private TextStyle style = TextStyle.Default;
        /// <summary>Creates an empty embedded MTEXT with explicit basic layout fields.</summary>
        public AttributeMText()
        {
            this.Position = Vector3.Zero; this.Height = 2.5; this.RectangleWidth = 0;
            this.AttachmentPoint = MTextAttachmentPoint.TopLeft;
            this.DrawingDirection = MTextDrawingDirection.ByStyle;
        }
        private AttributeMText(bool empty) { }
        private T Get<T>(short code, T fallback) { return this.fields.TryGetValue(code, out object v) ? (T)v : fallback; }
        private double? Number(short code) { return this.fields.TryGetValue(code, out object v) ? (double?)v : null; }
        private Vector3 Vector(short code, Vector3 fallback)
        { return new Vector3(this.Get(code, fallback.X), this.Get((short)(code + 10), fallback.Y), this.Get((short)(code + 20), fallback.Z)); }
        private Vector3? OptionalVector(short code) { return this.fields.ContainsKey(code) ? (Vector3?)this.Vector(code, Vector3.Zero) : null; }
        private void Set(short code, object v)
        {
            Check(code, v);
            if (!this.fields.ContainsKey(code)) this.order.Add(code);
            this.fields[code] = v;
        }
        private void SetNumber(short code, double? v)
        { if (v.HasValue) this.Set(code, v.Value); else { this.fields.Remove(code); this.order.Remove(code); } }
        private void SetVector(short code, Vector3? v)
        {
            if (v.HasValue) { Insert.FiniteInsert(v.Value); if ((code == 11 || code == 210) && v.Value == Vector3.Zero) throw new ArgumentException("Direction must be nonzero."); }
            for (short i = 0; i < 3; i++) this.SetNumber((short)(code + 10 * i), v.HasValue ? (double?)v.Value[i] : null);
        }
        /// <summary>Gets or sets logical MTEXT content, including inline formatting commands.</summary>
        public string Value { get { return this.value; } set { this.value = value ?? throw new ArgumentNullException(nameof(value)); } }
        /// <summary>Gets or sets the embedded text style independently of the host's fallback style.</summary>
        public TextStyle Style { get { return this.style; } set { this.style = value ?? throw new ArgumentNullException(nameof(value)); } }
        /// <summary>Gets or sets the WCS insertion point.</summary>
        public Vector3 Position { get { return this.Vector(10, Vector3.Zero); } set { this.SetVector(10, value); } }
        /// <summary>Gets or sets positive character height.</summary>
        public double Height { get { return this.Get(40, 2.5); } set { this.Set(40, value); } }
        /// <summary>Gets or sets the nonnegative reference rectangle width.</summary>
        public double RectangleWidth { get { return this.Get(41, 0.0); } set { this.Set(41, value); } }
        /// <summary>Gets or sets optional defined height (group 46).</summary>
        public double? DefinedHeight { get { return this.Number(46); } set { this.SetNumber(46, value); } }
        /// <summary>Gets or sets optional measured width; no measurement is performed.</summary>
        public double? ActualWidth { get { return this.Number(42); } set { this.SetNumber(42, value); } }
        /// <summary>Gets or sets optional measured height; no measurement is performed.</summary>
        public double? ActualHeight { get { return this.Number(43); } set { this.SetNumber(43, value); } }
        /// <summary>Gets or sets optional extrusion direction. Absent means world Z.</summary>
        public Vector3? Normal { get { return this.OptionalVector(210); } set { this.SetVector(210, value); } }
        /// <summary>Gets or sets optional WCS text direction. Its stored magnitude is retained.</summary>
        public Vector3? TextDirection { get { return this.OptionalVector(11); } set { this.SetVector(11, value); } }
        /// <summary>Gets or sets optional rotation in degrees. When both orientations exist, last stored representation wins.</summary>
        public double? Rotation { get { return this.Number(50); } set { this.SetNumber(50, value); } }
        /// <summary>Gets or sets the attachment point.</summary>
        public MTextAttachmentPoint AttachmentPoint { get { return (MTextAttachmentPoint)this.Get<short>(71, 1); } set { this.Set(71, (short)value); } }
        /// <summary>Gets or sets the flow direction.</summary>
        public MTextDrawingDirection DrawingDirection { get { return (MTextDrawingDirection)this.Get<short>(72, 5); } set { this.Set(72, (short)value); } }
        /// <summary>Gets or sets optional line-spacing style.</summary>
        public MTextLineSpacingStyle? LineSpacingStyle
        { get { return this.fields.ContainsKey(73) ? (MTextLineSpacingStyle?)(short)this.fields[73] : null; } set { if (value.HasValue) this.Set(73, (short)value.Value); else { this.fields.Remove(73); this.order.Remove(73); } } }
        /// <summary>Gets or sets optional line-spacing factor in [0.25, 4].</summary>
        public double? LineSpacingFactor { get { return this.Number(44); } set { this.SetNumber(44, value); } }
        /// <summary>Gets an independent background snapshot or replaces it. Null removes all background fields.</summary>
        public MTextBackgroundFill BackgroundFill
        {
            get
            {
                if (!this.fields.ContainsKey(90)) return null;
                return new MTextBackgroundFill
                {
                    Flags = (MTextBackgroundFillFlags)this.Get(90, 0),
                    ScaleFactor = this.Number(45),
                    ColorIndex = this.fields.ContainsKey(63) ? (short?)this.fields[63] : null,
                    TrueColor = this.fields.ContainsKey(421) ? (int?)this.fields[421] : null,
                    ColorName = this.Get<string>(431, null),
                    Transparency = this.fields.ContainsKey(441) ? (int?)this.fields[441] : null
                };
            }
            set
            {
                foreach (short code in new short[] { 90, 45, 63, 421, 431, 441 }) { this.fields.Remove(code); this.order.Remove(code); }
                if (value == null) return;
                this.Set(90, (int)value.Flags);
                if (value.ScaleFactor.HasValue) this.Set(45, value.ScaleFactor.Value);
                if (value.ColorIndex.HasValue) this.Set(63, value.ColorIndex.Value);
                if (value.TrueColor.HasValue) this.Set(421, value.TrueColor.Value);
                if (value.ColorName != null) this.Set(431, value.ColorName);
                if (value.Transparency.HasValue) this.Set(441, value.Transparency.Value);
            }
        }
        internal static void Check(short code, object v)
        {
            if (v is double n) { Insert.FiniteInsert(n); if ((code >= 40 && code <= 46 && code != 44) && n < 0) throw new ArgumentOutOfRangeException(nameof(v)); }
            switch (code)
            {
                case 10:
                case 20:
                case 30:
                case 11:
                case 21:
                case 31:
                case 210:
                case 220:
                case 230:
                case 41:
                case 42:
                case 43:
                case 46:
                case 50: if (!(v is double)) throw new ArgumentException("Expected double."); break;
                case 40: if (!(v is double h) || h <= 0) throw new ArgumentOutOfRangeException(nameof(v)); break;
                case 44: if (!(v is double f) || f < .25 || f > 4) throw new ArgumentOutOfRangeException(nameof(v)); break;
                case 71: if (!(v is short a) || a < 1 || a > 9) throw new ArgumentOutOfRangeException(nameof(v)); break;
                case 72: if (!(v is short d) || (d != 1 && d != 3 && d != 5)) throw new ArgumentOutOfRangeException(nameof(v)); break;
                case 73: if (!(v is short s) || (s != 1 && s != 2)) throw new ArgumentOutOfRangeException(nameof(v)); break;
                case 90: var b = new MTextBackgroundFill { Flags = (MTextBackgroundFillFlags)(int)v }; break;
                case 45: var bs = new MTextBackgroundFill { ScaleFactor = (double)v }; break;
                case 63: var bc = new MTextBackgroundFill { ColorIndex = (short)v }; break;
                case 421: var bt = new MTextBackgroundFill { TrueColor = (int)v }; break;
                case 431: if (!(v is string)) throw new ArgumentException("Expected string."); break;
                case 441: if (!(v is int)) throw new ArgumentException("Expected integer."); break;
                default: throw new NotSupportedException("Unsupported embedded attribute MTEXT group " + code + ".");
            }
        }
        internal static AttributeMText Read(IEnumerable<DxfTag> tags, Func<string, TextStyle> resolve)
        {
            var result = new AttributeMText(true); var text = new System.Text.StringBuilder(); bool terminal = false, styleSeen = false;
            foreach (DxfTag tag in tags)
            {
                if (tag.Code == 999) continue;
                if (tag.Code == 1 || tag.Code == 3)
                {
                    if (terminal) throw new InvalidDataException("MTEXT content follows its terminal group 1.");
                    text.Append((string)tag.Value); terminal = tag.Code == 1;
                    if (text.Length > 16777216) throw new InvalidDataException("Embedded content exceeds the 16 Mi-character budget.");
                }
                else if (tag.Code == 7)
                { if (styleSeen) throw new InvalidDataException("Duplicate embedded text style."); result.Style = resolve((string)tag.Value); styleSeen = true; }
                else
                { if (result.fields.ContainsKey(tag.Code)) throw new InvalidDataException("Duplicate embedded MTEXT scalar."); result.Set(tag.Code, tag.Value); }
            }
            if (!terminal) throw new InvalidDataException("Embedded MTEXT lacks its terminal content field.");
            result.Value = text.ToString(); result.Validate(); return result;
        }
        internal void Validate()
        {
            foreach (var pair in this.fields) Check(pair.Key, pair.Value);
            foreach (short code in new short[] { 10, 11, 210 })
            {
                int count = Enumerable.Range(0, 3).Count(i => this.fields.ContainsKey((short)(code + i * 10)));
                if (count != 0 && count != 3) throw new InvalidDataException("Incomplete embedded MTEXT vector.");
                if (count == 3 && code != 10 && this.Vector(code, Vector3.Zero) == Vector3.Zero) throw new InvalidDataException("Zero embedded direction.");
            }
            if (this.value.Length > 16777216) throw new InvalidDataException("Embedded content exceeds the 16 Mi-character budget.");
            if (this.style.ExtensionDictionary != null || this.style.PersistentReactors.Count != 0)
                throw new NotSupportedException("Embedded style graph metadata requires explicit graph mapping.");
        }
        internal IEnumerable<DxfTag> Fields { get { foreach (short code in this.order) yield return new DxfTag(code, this.fields[code]); } }
        internal AttributeMText Copy(bool cloneStyle)
        {
            var result = new AttributeMText(true) { value = this.value, style = cloneStyle ? (TextStyle)this.style.Clone() : this.style };
            foreach (short code in this.order) result.Set(code, this.fields[code]); return result;
        }
        /// <summary>Returns an independent content and style snapshot.</summary>
        public object Clone() { return this.Copy(true); }
        /// <summary>Creates a regular detached MTEXT using effective layout values, without calculating glyph metrics.</summary>
        public MText ToMText()
        {
            Vector3 n = this.Normal ?? Vector3.UnitZ;
            double angle = this.Rotation ?? 0;
            if (this.TextDirection.HasValue && (!this.Rotation.HasValue || this.order.IndexOf(11) > this.order.IndexOf(50)))
            { Vector3 d = MathHelper.Transform(this.TextDirection.Value, n, CoordinateSystem.World, CoordinateSystem.Object); angle = Math.Atan2(d.Y, d.X) * MathHelper.RadToDeg; }
            return new MText(this.Value, this.Position, this.Height, this.RectangleWidth, (TextStyle)this.Style.Clone())
            {
                Normal = n,
                Rotation = angle,
                DefinedHeight = this.DefinedHeight,
                AttachmentPoint = this.AttachmentPoint,
                DrawingDirection = this.DrawingDirection,
                LineSpacingStyle = this.LineSpacingStyle ?? MTextLineSpacingStyle.AtLeast,
                LineSpacingFactor = this.LineSpacingFactor ?? 1,
                BackgroundFill = this.BackgroundFill
            };
        }
        internal void CopyLayoutFrom(AttributeMText source)
        {
            // Restore definition geometry without replacing an instance's text, style or background.
            short[] geometry = { 10, 20, 30, 11, 21, 31, 210, 220, 230, 40, 41, 42, 43, 46, 50 };
            foreach (short code in geometry) { this.fields.Remove(code); this.order.Remove(code); }
            foreach (short code in source.order) if (Array.IndexOf(geometry, code) >= 0) this.Set(code, source.fields[code]);
        }
        internal AttributeMText Transformed(Matrix3 matrix, Vector3 translation)
        {
            this.Validate(); Insert.FiniteInsert(translation);
            bool identity = true; for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) { Insert.FiniteInsert(matrix[i, j]); identity &= matrix[i, j] == (i == j ? 1 : 0); }
            if (identity && translation == Vector3.Zero) return this;
            var result = this.Copy(false); result.Position = InfiniteLineTransform.TransformPoint(matrix, this.Position, translation);
            if (identity) return result;
            Vector3 x = matrix * Vector3.UnitX, y = matrix * Vector3.UnitY, z = matrix * Vector3.UnitZ;
            double scale = x.Modulus(); Insert.FiniteInsert(scale);
            if (scale <= 0 || Math.Abs(y.Modulus() / scale - 1) > 1e-10 || Math.Abs(z.Modulus() / scale - 1) > 1e-10)
                throw new NotSupportedException("Embedded MTEXT requires positive uniform scaling.");
            x /= scale; y /= scale; z /= scale;
            if (Math.Abs(Vector3.DotProduct(x, y)) > 1e-10 || Math.Abs(Vector3.DotProduct(x, z)) > 1e-10 || Math.Abs(Vector3.DotProduct(y, z)) > 1e-10 || Vector3.DotProduct(Vector3.CrossProduct(x, y), z) < 0)
                throw new NotSupportedException("Embedded MTEXT cannot represent shear or reflection without a layout policy.");
            result.Normal = Vector3.NormalizeFiniteDirection(matrix * (this.Normal ?? Vector3.UnitZ), nameof(matrix));
            if (this.TextDirection.HasValue) result.TextDirection = matrix * this.TextDirection.Value;
            if (this.Rotation.HasValue)
            {
                Vector3 d = MathHelper.ArbitraryAxis(this.Normal ?? Vector3.UnitZ) * (Matrix3.RotationZ(this.Rotation.Value * MathHelper.DegToRad) * Vector3.UnitX);
                Vector3 local = MathHelper.ArbitraryAxis(result.Normal.Value).Transpose() * (matrix * d);
                result.Rotation = MathHelper.NormalizeAngle(Math.Atan2(local.Y, local.X) * MathHelper.RadToDeg);
            }
            else if (!this.TextDirection.HasValue) result.TextDirection = matrix * (MathHelper.ArbitraryAxis(this.Normal ?? Vector3.UnitZ) * Vector3.UnitX);
            foreach (short c in new short[] { 40, 41, 42, 43, 46 }) if (this.fields.TryGetValue(c, out object v)) result.Set(c, (double)v * scale);
            result.Validate(); return result;
        }
    }
    internal sealed class AttributeTextState
    {
        internal short? Version, Kind, SecondaryFlag, FieldLength;
        internal bool? Locked;
        internal Vector3? SecondaryAlignment;
        internal AttributeMText Content;
        internal readonly List<DxfTag> Auxiliary = new List<DxfTag>();
        internal AttributeTextState Copy(bool styles)
        { var s = new AttributeTextState { Version = Version, Kind = Kind, SecondaryFlag = SecondaryFlag, FieldLength = FieldLength, Locked = Locked, SecondaryAlignment = SecondaryAlignment, Content = Content?.Copy(styles) }; s.Auxiliary.AddRange(Auxiliary); return s; }
    }
}
