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
using System.Globalization;

namespace netDxf
{
    /// <summary>
    /// Represents the transparency of a layer or an entity.
    /// </summary>
    /// <remarks>
    /// When the transparency of an entity is ByLayer the code 440 will not appear in the dxf,
    /// but for comparison purposes the ByLayer transparency is assigned a value of -1.
    /// </remarks>
    public class Transparency :
        ICloneable,
        IEquatable<Transparency>
    {
        #region private fields

        private short transparency;
        private int? storedAlphaValue;
        internal bool HasValueEdit { get; private set; }

        #endregion

        #region constants

        /// <summary>
        /// Gets the ByLayer transparency.
        /// </summary>
        public static Transparency ByLayer
        {
            get { return new Transparency {transparency = -1}; }
        }

        /// <summary>
        /// Gets the ByBlock transparency.
        /// </summary>
        public static Transparency ByBlock
        {
            get { return new Transparency {transparency = 100}; }
        }

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>Transparency</c> class.
        /// </summary>
        public Transparency()
        {
            this.transparency = -1;
        }

        /// <summary>
        /// Initializes a new instance of the <c>Transparency</c> class.
        /// </summary>
        /// <param name="value">Alpha value range from 0 to 90.</param>
        /// <remarks>
        /// Accepted transparency values range from 0 (opaque) to 90 (almost transparent), the reserved values -1 and 100 represents ByLayer and ByBlock transparency.
        /// </remarks>
        public Transparency(short value)
        {
            if (value < 0 || value > 90)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Accepted transparency values range from 0 to 90.");
            }
            this.transparency = value;
        }

        // Layer-state zero has an opaque effective value while its exact stored bits remain zero.
        internal Transparency(short effectiveValue, int storedValue) : this(effectiveValue)
        {
            this.storedAlphaValue = storedValue;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Defines if the transparency is defined by layer.
        /// </summary>
        public bool IsByLayer
        {
            get { return this.transparency == -1; }
        }

        /// <summary>
        /// Defines if the transparency is defined by block.
        /// </summary>
        public bool IsByBlock
        {
            get { return this.transparency == 100; }
        }

        /// <summary>
        /// Gets the exact packed alpha value supplied to FromAlphaValue, or null for percentage-authored values.
        /// </summary>
        /// <remarks>Stored bits remain independent of the legacy percentage/index interpretation and equality. Unknown flag bits are retained without interpretation.</remarks>
        public int? StoredAlphaValue
        {
            get { return this.storedAlphaValue; }
        }

        /// <summary>Gets or sets the effective percentage retained by the legacy transparency API.</summary>
        /// <remarks>A successful edit discards the imported packed value. Failed edits leave it unchanged.</remarks>
        public short Value
        {
            get { return this.transparency; }
            set
            {
                if (value < 0 || value > 90)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Accepted transparency values range from 0 to 90.");
                }
                this.transparency = value;
                this.storedAlphaValue = null;
                this.HasValueEdit = true;
            }
        }

        #endregion

        #region public methods

        /// <summary>
        /// Gets the transparency value from a <see cref="Transparency">transparency</see> object.
        /// </summary>
        /// <param name="transparency">A <see cref="Transparency">transparency</see>.</param>
        /// <returns>A transparency value.</returns>
        public static int ToAlphaValue(Transparency transparency)
        {
            if (transparency == null)
            {
                throw new ArgumentNullException(nameof(transparency));
            }

            if (transparency.storedAlphaValue.HasValue) return transparency.storedAlphaValue.Value;

            byte alpha = (byte) (255 * (100 - transparency.Value) / 100.0);
            byte[] bytes = transparency.IsByBlock ? new byte[] {0, 0, 0, 1} : new byte[] {alpha, 0, 0, 2};
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>
        /// Gets the <see cref="Transparency">transparency</see> object from a transparency value.
        /// </summary>
        /// <param name="value">A transparency value.</param>
        /// <returns>A <see cref="Transparency">transparency</see></returns>
        public static Transparency FromAlphaValue(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            short alpha = (short) (100 - (bytes[0] / 255.0) * 100);
            Transparency result = FromCadIndex(alpha);
            result.storedAlphaValue = value;
            return result;
        }

        public static Transparency FromCadIndex(short alpha)
        {
            if (alpha == -1)
            {
                return ByLayer;
            }
            if (alpha == 100)
            {
                return ByBlock;
            }
            if (alpha < 0)
            {
                return new Transparency(0);
            }
            if (alpha > 90)
            {
                return new Transparency(90);
            }

            return new Transparency(alpha);
        }

        #endregion

        #region implements ICloneable

        /// <summary>
        /// Creates a new transparency that is a copy of the current instance.
        /// </summary>
        /// <returns>A new transparency that is a copy of this instance.</returns>
        public object Clone()
        {
            return new Transparency { transparency = this.transparency, storedAlphaValue = this.storedAlphaValue, HasValueEdit = this.HasValueEdit };
        }

        #endregion

        #region implements IEquatable

        /// <summary>
        /// Check if the components of two transparencies are equal.
        /// </summary>
        /// <param name="other">Another transparency to compare to.</param>
        /// <returns>True if their indexes are equal or false in any other case.</returns>
        public bool Equals(Transparency other)
        {
            if (other == null)
            {
                return false;
            }

            return other.transparency == this.transparency;
        }

        #endregion

        #region overrides

        /// <summary>
        /// Converts the value of this instance to its equivalent string representation.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            if (this.transparency == -1)
            {
                return "ByLayer";
            }

            if (this.transparency == 100)
            {
                return "ByBlock";
            }

            return this.transparency.ToString(CultureInfo.CurrentCulture);
        }

        #endregion
    }
}