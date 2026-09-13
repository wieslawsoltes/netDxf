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
    /// <summary>
    /// Represents the hatch gradient pattern style.
    /// </summary>
    /// <remarks>
    /// Gradient patterns are only supported by AutoCad2004 and higher DXF versions. It will default to a solid pattern if saved as AutoCad2000.
    /// </remarks>
    public class HatchGradientPattern :
        HatchPattern
    {
        #region private fields

        private HatchGradientPatternType gradientType;
        private AciColor color1;
        private AciColor color2;
        private bool singleColor;
        private double tint;
        private double shift;

        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a default linear gradient. 
        /// </summary>
        public HatchGradientPattern()
            : this(string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a default linear gradient. 
        /// </summary>
        /// <param name="description">Description of the pattern (optional, this information is not saved in the DXF file). By default it will use the supplied name.</param>
        public HatchGradientPattern(string description)
            : base("SOLID", description)
        {
            this.color1 = AciColor.Blue;
            this.color2 = AciColor.Yellow;
            this.singleColor = false;
            this.gradientType = HatchGradientPatternType.Linear;
            this.tint = 1.0;
            this.shift = 0.0;
        }

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a single color gradient. 
        /// </summary>
        /// <param name="color">Gradient <see cref="AciColor">color</see>.</param>
        /// <param name="tint">Gradient tint.</param>
        /// <param name="type">Gradient <see cref="HatchGradientPatternType">type</see>.</param>
        public HatchGradientPattern(AciColor color, double tint, HatchGradientPatternType type)
            : this(color, tint, type, string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a single color gradient. 
        /// </summary>
        /// <param name="color">Gradient <see cref="AciColor">color</see>.</param>
        /// <param name="tint">Gradient tint.</param>
        /// <param name="type">Gradient <see cref="HatchGradientPatternType">type</see>.</param>
        /// <param name="description">Description of the pattern (optional, this information is not saved in the DXF file). By default it will use the supplied name.</param>
        public HatchGradientPattern(AciColor color, double tint, HatchGradientPatternType type, string description)
            : base("SOLID", description)
        {
            this.color1 = color ?? throw new ArgumentNullException(nameof(color));
            this.color2 = this.Color2FromTint(tint);
            this.singleColor = true;
            this.gradientType = type;
            this.tint = tint;
            this.shift = 0.0;
        }

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a two color gradient. 
        /// </summary>
        /// <param name="color1">Gradient <see cref="AciColor">color</see> 1.</param>
        /// <param name="color2">Gradient <see cref="AciColor">color</see> 2.</param>
        /// <param name="type">Gradient <see cref="HatchGradientPatternType">type</see>.</param>
        public HatchGradientPattern(AciColor color1, AciColor color2, HatchGradientPatternType type)
            : this(color1, color2, type, string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>HatchGradientPattern</c> class as a two color gradient. 
        /// </summary>
        /// <param name="color1">Gradient <see cref="AciColor">color</see> 1.</param>
        /// <param name="color2">Gradient <see cref="AciColor">color</see> 2.</param>
        /// <param name="type">Gradient <see cref="HatchGradientPatternType">type</see>.</param>
        /// <param name="description">Description of the pattern (optional, this information is not saved in the DXF file). By default it will use the supplied name.</param>
        public HatchGradientPattern(AciColor color1, AciColor color2, HatchGradientPatternType type, string description)
            : base("SOLID", description)
        {
            this.color1 = color1 ?? throw new ArgumentNullException(nameof(color1));
            this.color2 = color2 ?? throw new ArgumentNullException(nameof(color2));
            this.singleColor = false;
            this.gradientType = type;
            this.tint = 1.0;
            this.shift = 0.0;
        }

        #endregion

        #region public properties

        /// <summary>
        /// Gets or set the gradient pattern <see cref="HatchGradientPatternType">type</see>.
        /// </summary>
        public HatchGradientPatternType GradientType
        {
            get { return this.gradientType; }
            set { this.gradientType = value; }
        }

        /// <summary>
        /// Gets or sets the gradient <see cref="AciColor">color</see> 1.
        /// </summary>
        public AciColor Color1
        {
            get { return this.color1; }
            set
            {
                this.color1 = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>
        /// Gets or sets the gradient <see cref="AciColor">color</see> 2.
        /// </summary>
        /// <remarks>
        /// If color 2 is defined, automatically the single color property will be set to false.  
        /// </remarks>
        public AciColor Color2
        {
            get { return this.color2; }
            set
            {
                this.singleColor = false;
                this.color2 = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        /// <summary>
        /// Gets or sets the gradient pattern color type.
        /// </summary>
        public bool SingleColor
        {
            get { return this.singleColor; }
            set
            {
                if (value)
                    this.Color2 = this.Color2FromTint(this.tint);
                this.singleColor = value;
            }
        }

        /// <summary>
        /// Gets or sets the gradient pattern tint.
        /// </summary>
        /// <remarks>It only applies to single color gradient patterns.</remarks>
        public double Tint
        {
            get { return this.tint; }
            set
            {
                if (this.singleColor)
                    this.Color2 = this.Color2FromTint(value);
                this.tint = value;
            }
        }

        /// <summary>
        /// Gets or sets the blend between non-shifted (0.0) and shifted (1.0)
        /// gradient definitions, stored in DXF group 461.
        /// </summary>
        /// <remarks>
        /// Intermediate values retain the authored blend. The value must be finite
        /// and in the inclusive range [0, 1]. The default is 0.0.
        /// </remarks>
        public double Shift
        {
            get { return this.shift; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 || value > 1.0)
                    throw new ArgumentOutOfRangeException(nameof(value), value,
                        "The gradient shift must be finite and between zero and one.");
                this.shift = value;
            }
        }

        /// <summary>
        /// Gets or sets whether the gradient uses only its non-shifted definition.
        /// </summary>
        /// <remarks>
        /// This compatibility property is true exactly when <see cref="Shift"/> is zero.
        /// Setting true assigns Shift = 0.0; setting false assigns Shift = 1.0,
        /// preserving the existing writer's endpoint convention. Use Shift directly
        /// to retain or edit an intermediate blend without quantizing it.
        /// </remarks>
        public bool Centered
        {
            get { return this.shift == 0.0; }
            set { this.shift = value ? 0.0 : 1.0; }
        }

        #endregion

        #region private methods

        private AciColor Color2FromTint(double value)
        {
            AciColor.ToHsl(this.color1, out double h, out double s, out double _);
            return AciColor.FromHsl(h, s, value);
        }

        #endregion

        #region ICloneable

        public override object Clone()
        {
            HatchGradientPattern copy = new HatchGradientPattern
            {
                // Pattern
                Fill = this.Fill,
                Type = this.Type,
                IsDouble = this.IsDouble,
                Origin = this.Origin,
                Angle = this.Angle,
                Scale = this.Scale,
                Style = this.Style,
                // GraientPattern
                GradientType = this.gradientType,
                Color1 = (AciColor) this.color1.Clone(),
                Color2 = (AciColor) this.color2.Clone(),
                SingleColor = this.singleColor,
                Tint = this.tint,
                Shift = this.shift
            };

            return copy;
        }

        #endregion
    }
}