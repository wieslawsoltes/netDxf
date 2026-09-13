#region netDxf library licensed under the MIT License
//
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
#endregion

using System;

namespace netDxf.Entities
{
    /// <summary>MTEXT background settings stored in DXF group 90.</summary>
    [Flags]
    public enum MTextBackgroundFillFlags
    {
        /// <summary>No background fill.</summary>
        None = 0,
        /// <summary>Enable the explicit background color.</summary>
        UseColor = 1,
        /// <summary>Use the drawing-window color. Common writers combine this with UseColor.</summary>
        UseDrawingWindowColor = 2,
        /// <summary>Draw a text frame. The current writer profile requires AutoCAD 2018.</summary>
        TextFrame = 16
    }

    /// <summary>Editable MTEXT background data, independent of the entity's foreground color.</summary>
    /// <remarks>
    /// Nullable fields retain optional tag presence. The default is an enabled indexed-color
    /// background with scale 1.5 and color index 7. Reading an absent field does not invent a value.
    /// This class stores data; it does not render a mask, evaluate a color book or apply transparency.
    /// </remarks>
    public sealed class MTextBackgroundFill : ICloneable
    {
        private MTextBackgroundFillFlags flags = MTextBackgroundFillFlags.UseColor;
        private double? scaleFactor = 1.5;
        private short? colorIndex = 7;
        private int? trueColor;
        private string colorName;

        /// <summary>Gets or sets the group 90 flags. Only bits 1, 2 and 16 are defined here.</summary>
        public MTextBackgroundFillFlags Flags
        {
            get { return this.flags; }
            set
            {
                if (((int) value & ~19) != 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Undefined MTEXT background flag bits.");
                this.flags = value;
            }
        }

        /// <summary>Gets or sets the optional positive finite fill-box scale, group 45.</summary>
        /// <remarks>Null retains absence. The usual effective default is 1.5; 1 through 5 is the recommended authoring range.</remarks>
        public double? ScaleFactor
        {
            get { return this.scaleFactor; }
            set
            {
                if (value.HasValue && (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0))
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The fill-box scale must be positive and finite.");
                this.scaleFactor = value;
            }
        }

        /// <summary>Gets or sets the optional group 63 indexed color, from 0 through 256.</summary>
        /// <remarks>The fallback index is retained independently when a true color or color-book name is present.</remarks>
        public short? ColorIndex
        {
            get { return this.colorIndex; }
            set
            {
                if (value.HasValue && (value.Value < 0 || value.Value > 256))
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The color index must be in the range 0 through 256.");
                this.colorIndex = value;
            }
        }

        /// <summary>Gets or sets the optional raw group 421 packed RGB color.</summary>
        /// <remarks>
        /// RGB components occupy the low 24 bits (0xRRGGBB). All 32 input bits are retained,
        /// including producer-specific high-byte color-method values. FromColor emits a zero high byte.
        /// The fallback index is stored independently of this field.
        /// </remarks>
        public int? TrueColor
        {
            get { return this.trueColor; }
            set
            {
                this.trueColor = value;
            }
        }

        /// <summary>Gets or sets the optional group 431 color-book name. Null and empty are distinct.</summary>
        /// <remarks>Names are stored, not resolved against an external color book.</remarks>
        public string ColorName
        {
            get { return this.colorName; }
            set
            {
                if (value != null && value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                    throw new ArgumentException("A background color name cannot contain NUL or line terminators.", nameof(value));
                this.colorName = value;
            }
        }

        /// <summary>Gets or sets the optional raw 32-bit group 441 transparency value.</summary>
        /// <remarks>
        /// Autodesk documents this field as not implemented. All bits are preserved without
        /// interpreting them as a percentage, applying the entity's transparency, or rendering them.
        /// </remarks>
        public int? Transparency { get; set; }

        /// <summary>Creates a background from an indexed or RGB color with an independent fallback index.</summary>
        /// <param name="color">Source color.</param>
        /// <param name="scaleFactor">Positive finite fill-box scale.</param>
        /// <returns>A background that does not retain the mutable source color.</returns>
        public static MTextBackgroundFill FromColor(AciColor color, double scaleFactor = 1.5)
        {
            if (color == null) throw new ArgumentNullException(nameof(color));
            return new MTextBackgroundFill
            {
                ScaleFactor = scaleFactor,
                ColorIndex = color.Index,
                TrueColor = color.UseTrueColor ? (int?) ((color.R << 16) | (color.G << 8) | color.B) : null
            };
        }

        /// <summary>Creates drawing-window background data using the conventional combined flags 3.</summary>
        /// <param name="scaleFactor">Positive finite fill-box scale.</param>
        /// <returns>A drawing-window background.</returns>
        public static MTextBackgroundFill FromDrawingWindow(double scaleFactor = 1.5)
        {
            return new MTextBackgroundFill
            {
                Flags = MTextBackgroundFillFlags.UseColor | MTextBackgroundFillFlags.UseDrawingWindowColor,
                ScaleFactor = scaleFactor,
                ColorIndex = 0
            };
        }

        /// <summary>Creates frame-only data without inventing fill scale or color tags.</summary>
        /// <returns>A text frame with no fill payload.</returns>
        public static MTextBackgroundFill CreateTextFrame()
        {
            return new MTextBackgroundFill { Flags = MTextBackgroundFillFlags.TextFrame, ScaleFactor = null, ColorIndex = null };
        }

        /// <summary>Creates independently editable background data, including optional-field presence.</summary>
        /// <returns>A copy of this background.</returns>
        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
