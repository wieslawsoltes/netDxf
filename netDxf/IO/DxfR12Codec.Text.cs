// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private static double TextWidthFactor(double value)
        {
            Finite(value);
            if (value < .01 || value > 100) throw new NotSupportedException("The text width factor is outside the typed model's range.");
            return value;
        }

        private static double TextOblique(double value)
        {
            Finite(value);
            if (value < -85 || value > 85) throw new NotSupportedException("The text oblique angle is outside the typed model's range.");
            return value;
        }

        private static string FontReference(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            foreach (char c in value)
                if (c < ' ' || c == '\x7f') throw new NotSupportedException("A font reference cannot contain control characters.");
            // References are stored, never resolved, loaded, downloaded or executed.
            return value;
        }

        private sealed class StylePacket
        {
            internal string Name, Font, BigFont;
            internal short Flags, Generation;
            internal double Height, Width, Oblique;
            internal double? LastHeight;
            internal static StylePacket Capture(TextStyle style)
            {
                if (style == null || style.GetType() != typeof(TextStyle)) throw new NotSupportedException("A plain TextStyle is required.");
                Metadata(style);
                if (((int)style.Flags & ~68) != 0 || (style.TextGenerationFlags & ~6) != 0)
                    throw new NotSupportedException("Shape, external or unknown text-style flags require another codec.");
                if (Finite(style.Height) < 0) throw new ArgumentOutOfRangeException(nameof(style));
                if (style.LastHeight.HasValue) Finite(style.LastHeight.Value);
                return new StylePacket { Name = ResourceName(style.Name), Font = FontReference(style.FontFile),
                    BigFont = FontReference(style.BigFont), Flags = (short)style.Flags, Generation = style.TextGenerationFlags,
                    Height = style.Height, Width = TextWidthFactor(style.WidthFactor), Oblique = TextOblique(style.ObliqueAngle),
                    LastHeight = style.LastHeight };
            }
            internal bool SameSettings(StylePacket other)
            {
                return this.Font == other.Font && this.BigFont == other.BigFont && this.Flags == other.Flags
                    && this.Generation == other.Generation && SameBits(this.Height, other.Height)
                    && SameBits(this.Width, other.Width) && SameBits(this.Oblique, other.Oblique)
                    && this.LastHeight.HasValue == other.LastHeight.HasValue
                    && (!this.LastHeight.HasValue || SameBits(this.LastHeight.Value, other.LastHeight.Value));
            }
            private static bool SameBits(double a, double b) { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
        }

        private static TextStyle ReadTextStyle(DxfRawRecord record)
        {
            var fields = new Fields(record);
            string name = ResourceName(fields.Text(2, null, true)); fields.Identity();
            short flags = fields.Integer(70, 0), generation = fields.Integer(71, 0);
            if ((flags & ~68) != 0 || (generation & ~6) != 0)
                throw new NotSupportedException("Shape, external or unknown text-style flags require another codec.");
            double height = fields.Number(40, 0), width = TextWidthFactor(fields.Number(41, 1)), oblique = TextOblique(fields.Number(50, 0));
            if (height < 0) throw new FormatException("Fixed STYLE height cannot be negative.");
            double? last = fields.OptionalNumber(42);
            string font = FontReference(fields.Text(3, "")), big = FontReference(fields.Text(4, ""));
            fields.Finish();
            // Use the stored-file path to avoid filesystem-dependent font discovery or
            // extension validation: legacy font references may be empty or extensionless.
            var style = new TextStyle(name, TextStyle.DefaultFont)
            { Height = height, WidthFactor = width, ObliqueAngle = oblique,
                Flags = (TextStyleFlags)flags, TextGenerationFlags = generation, LastHeight = last };
            style.SetStoredFontFiles(font, big);
            return style;
        }

        private static void TextAlignmentCodes(TextAlignment alignment, out short horizontal, out short vertical)
        {
            int value = (int)alignment;
            if (value < 0 || value > (int)TextAlignment.Fit) throw new ArgumentOutOfRangeException(nameof(alignment));
            if (value < 12) { horizontal = (short)(value % 3); vertical = (short)(3 - value / 3); }
            else { horizontal = (short)(value - 9); vertical = 0; }
        }

        private static TextAlignment TextAlignmentValue(short horizontal, short vertical)
        {
            if (horizontal < 0 || horizontal > 5 || vertical < 0 || vertical > 3 || (horizontal > 2 && vertical != 0))
                throw new FormatException("Invalid TEXT horizontal/vertical justification combination.");
            return (TextAlignment)(horizontal > 2 ? horizontal + 9 : (3 - vertical) * 3 + horizontal);
        }

        private static string EncodeTextControls(string value, int limit)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            long length = value.Length;
            foreach (char c in value) if (c < ' ' || c == '^') length++;
            if (length > limit) throw new InvalidDataException("Escaped TEXT exceeds the raw string budget.");
            if (length == value.Length) return value;
            var result = new StringBuilder((int)length);
            foreach (char c in value)
            {
                if (c < ' ') { result.Append('^'); result.Append((char)(c + 64)); }
                else if (c == '^') result.Append("^ ");
                else result.Append(c);
            }
            return result.ToString();
        }

        private static string DecodeTextControls(string value)
        {
            if (value.IndexOf('^') < 0) return value;
            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '^') { result.Append(c); continue; }
                if (++i == value.Length) throw new FormatException("Unterminated TEXT caret escape.");
                c = value[i];
                if (c == ' ') result.Append('^');
                else if (c >= '@' && c <= '_') result.Append((char)(c - 64));
                else throw new NotSupportedException("Unrecognized TEXT caret escape; use raw preservation for this value.");
            }
            return result.ToString();
        }

        private sealed partial class PrimitiveWriter
        {
            private readonly Dictionary<string, StylePacket> textStyles = new Dictionary<string, StylePacket>(StringComparer.OrdinalIgnoreCase);
            private readonly List<StylePacket> orderedTextStyles = new List<StylePacket>();

            private void TextEntity(Text text, Vector3 normal, short verticalCode = 73)
            {
                StylePacket style = StylePacket.Capture(text.Style);
                if (this.textStyles.TryGetValue(style.Name, out StylePacket previous))
                {
                    if (!previous.SameSettings(style)) throw new InvalidOperationException("Conflicting same-named R12 text styles.");
                    style = previous;
                }
                else
                {
                    if (this.textStyles.Count == short.MaxValue) throw new NotSupportedException("R12 STYLE table count limit exceeded.");
                    this.textStyles.Add(style.Name, style); this.orderedTextStyles.Add(style);
                }
                TextAlignmentCodes(text.Alignment, out short horizontal, out short vertical);
                Vector3 first = ToObject(text.Position, normal), second = first;
                double rotation = Finite(text.Rotation), height = Finite(text.Height);
                if (height <= 0) throw new ArgumentOutOfRangeException(nameof(text));
                if (horizontal == 3 || horizontal == 5)
                {
                    double width = Finite(text.Width);
                    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(text));
                    double radians = rotation * MathHelper.DegToRad;
                    second = FinitePoint(first + new Vector3(width * Math.Cos(radians), width * Math.Sin(radians), 0));
                    if (second.X == first.X && second.Y == first.Y)
                        throw new NotSupportedException("Aligned/Fit endpoints collapse at this coordinate magnitude.");
                }
                this.Tag(1, EncodeTextControls(text.Value, this.options.MaximumStringLength));
                this.Point(10, first); this.Tag(40, height); this.Tag(41, TextWidthFactor(text.WidthFactor));
                this.Tag(50, rotation); this.Tag(51, TextOblique(text.ObliqueAngle)); this.Tag(7, style.Name);
                if (horizontal != 0 || vertical != 0) this.Point(11, second);
                this.Point(210, normal);
                this.Tag(71, (short)((text.IsBackward ? 2 : 0) | (text.IsUpsideDown ? 4 : 0)));
                this.Tag(72, horizontal); this.Tag(verticalCode, vertical);
            }

            private void WriteTextStyles(List<DxfTag> destination)
            {
                if (this.textStyles.Count == 0) return; // Preserve primitive-only output exactly.
                if (!this.textStyles.ContainsKey(TextStyle.DefaultName))
                {
                    if (this.textStyles.Count == short.MaxValue) throw new NotSupportedException("R12 STYLE table count limit exceeded.");
                    this.orderedTextStyles.Insert(0, StylePacket.Capture(TextStyle.Default));
                }
                this.Add(destination, 0, "TABLE"); this.Add(destination, 2, "STYLE");
                this.Add(destination, 70, (short)this.orderedTextStyles.Count);
                foreach (StylePacket style in this.orderedTextStyles)
                {
                    this.Add(destination, 0, "STYLE"); this.Add(destination, 2, style.Name); this.Add(destination, 70, style.Flags);
                    this.Add(destination, 40, style.Height); this.Add(destination, 41, style.Width); this.Add(destination, 50, style.Oblique);
                    this.Add(destination, 71, style.Generation);
                    if (style.LastHeight.HasValue) this.Add(destination, 42, style.LastHeight.Value);
                    this.Add(destination, 3, style.Font); this.Add(destination, 4, style.BigFont);
                }
                this.Add(destination, 0, "ENDTAB");
            }
        }

        private static Text ReadTextEntity(Fields fields, Vector3 normal, double thickness, Dictionary<string, TextStyle> styles, short verticalCode = 73)
        {
            if (thickness != 0) throw new NotSupportedException("Text does not expose nonzero extrusion thickness; use raw preservation.");
            string value = DecodeTextControls(fields.Text(1, null, true));
            string name = ResourceName(fields.Text(7, TextStyle.DefaultName));
            if (!styles.TryGetValue(name, out TextStyle style))
            {
                if (!string.Equals(name, TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("TEXT references an undefined STYLE: " + name);
                style = TextStyle.Default; styles.Add(style.Name, style);
            }
            Vector3 first = fields.Vector(10, Vector3.Zero, true);
            double height = fields.Number(40, 0, true), widthFactor = TextWidthFactor(fields.Number(41, 1));
            double rotation = fields.Number(50, 0), oblique = TextOblique(fields.Number(51, 0));
            if (height <= 0) throw new FormatException("TEXT height must be positive.");
            short generation = fields.Integer(71, 0), horizontal = fields.Integer(72, 0), vertical = fields.Integer(verticalCode, 0);
            if ((generation & ~6) != 0) throw new NotSupportedException("Unknown TEXT generation flags.");
            TextAlignment alignment = TextAlignmentValue(horizontal, vertical);
            bool justified = horizontal != 0 || vertical != 0;
            // An unused second point may be omitted, but a partial X/Y pair is not a valid point.
            bool hasSecond = fields.Has(11) || fields.Has(21) || fields.Has(31);
            Vector3 second = fields.Vector(11, Vector3.Zero, justified || hasSecond);
            Vector3 position = justified ? second : first;
            double width = 1;
            if (alignment == TextAlignment.Aligned || alignment == TextAlignment.Fit)
            {
                if (first.Z != second.Z) throw new NotSupportedException("Aligned/Fit TEXT requires a single OCS elevation.");
                double dx = Finite(second.X - first.X), dy = Finite(second.Y - first.Y);
                double scale = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (scale == 0) throw new FormatException("Aligned/Fit TEXT endpoints must differ.");
                width = Finite(scale * Math.Sqrt((dx / scale) * (dx / scale) + (dy / scale) * (dy / scale)));
                rotation = Math.Atan2(dy, dx) * MathHelper.RadToDeg;
                position = first; // Two-point justification, not font-dependent measurement.
            }
            return new Text(value, ToWorld(position, normal), height, style)
            { Normal = normal, Alignment = alignment, Rotation = rotation, Width = width, WidthFactor = widthFactor,
                ObliqueAngle = oblique, IsBackward = (generation & 2) != 0, IsUpsideDown = (generation & 4) != 0 };
        }
    }
}
