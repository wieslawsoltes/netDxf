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
        private short? color1AciIndex;
        private short? color2AciIndex;
        private bool color1AciIndexAutomatic = true;
        private bool color2AciIndexAutomatic = true;

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
            ValidateTint(tint, nameof(tint));
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

        // Restore authored DXF state without interpreting the dialog mode as a
        // request to recompute the second color. Both RGB stops remain authoritative.
        internal HatchGradientPattern(AciColor color1, AciColor color2, bool singleColor,
            double tint, HatchGradientPatternType type)
            : this(color1, color2, type)
        {
            ValidateTint(tint, nameof(tint));
            this.singleColor = singleColor;
            this.tint = tint;
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
                this.color2 = value ?? throw new ArgumentNullException(nameof(value));
                this.singleColor = false;
            }
        }

        /// <summary>
        /// Gets or sets the optional group-63 ACI metadata for the first RGB stop.
        /// </summary>
        /// <remarks>
        /// New patterns derive this value from Color1.Index until this property is
        /// assigned. Assigning a value retains that exact Int16 metadata; assigning
        /// null omits the tag. Loaded patterns retain the value or absence from the
        /// file. Explicit metadata is independent of RGB edits and is not palette
        /// validation. Call ResetColor1AciIndex to resume automatic derivation.
        /// </remarks>
        public short? Color1AciIndex
        {
            get { return this.color1AciIndexAutomatic ? this.color1.Index : this.color1AciIndex; }
            set
            {
                this.color1AciIndex = value;
                this.color1AciIndexAutomatic = false;
            }
        }

        /// <summary>
        /// Gets or sets the optional group-63 ACI metadata for the second RGB stop.
        /// </summary>
        /// <remarks>
        /// New patterns derive this value from Color2.Index until this property is
        /// assigned. Null explicitly omits the tag; an assigned Int16 is retained
        /// independently of RGB or tint edits. Loaded patterns retain the authored
        /// value or absence. Call ResetColor2AciIndex to resume automatic derivation.
        /// This property does not select a color mode or validate palette semantics.
        /// </remarks>
        public short? Color2AciIndex
        {
            get { return this.color2AciIndexAutomatic ? this.color2.Index : this.color2AciIndex; }
            set
            {
                this.color2AciIndex = value;
                this.color2AciIndexAutomatic = false;
            }
        }

        /// <summary>
        /// Gets whether the first optional ACI value follows Color1.Index.
        /// </summary>
        public bool IsColor1AciIndexAutomatic
        {
            get { return this.color1AciIndexAutomatic; }
        }

        /// <summary>
        /// Gets whether the second optional ACI value follows Color2.Index.
        /// </summary>
        public bool IsColor2AciIndexAutomatic
        {
            get { return this.color2AciIndexAutomatic; }
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
                    this.color2 = this.Color2FromTint(this.tint);
                this.singleColor = value;
            }
        }

        /// <summary>
        /// Gets or sets the gradient pattern tint.
        /// </summary>
        /// <remarks>
        /// The value must be finite and in [0, 1]. Setting the tint derives Color2
        /// only in single-color mode and does not change that mode. In two-color
        /// mode the tint remains stored dialog metadata; neither RGB stop changes.
        /// </remarks>
        public double Tint
        {
            get { return this.tint; }
            set
            {
                ValidateTint(value, nameof(value));
                if (this.singleColor)
                    this.color2 = this.Color2FromTint(value);
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

        #region public methods

        /// <summary>
        /// Restores automatic first-stop ACI output from the current Color1.Index.
        /// </summary>
        public void ResetColor1AciIndex()
        {
            this.color1AciIndex = null;
            this.color1AciIndexAutomatic = true;
        }

        /// <summary>
        /// Restores automatic second-stop ACI output from the current Color2.Index.
        /// </summary>
        public void ResetColor2AciIndex()
        {
            this.color2AciIndex = null;
            this.color2AciIndexAutomatic = true;
        }

        #endregion

        #region private methods

        private static void ValidateTint(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 || value > 1.0)
                throw new ArgumentOutOfRangeException(parameterName, value,
                    "The gradient tint must be finite and between zero and one.");
        }

        private AciColor Color2FromTint(double value)
        {
            AciColor.ToHsl(this.color1, out double h, out double s, out double _);
            return AciColor.FromHsl(h, s, value);
        }

        #endregion

        #region ICloneable

        public override object Clone()
        {
            HatchGradientPattern copy = new HatchGradientPattern(
                (AciColor) this.color1.Clone(), (AciColor) this.color2.Clone(),
                this.singleColor, this.tint, this.gradientType)
            {
                // Pattern
                Description = this.Description,
                Fill = this.Fill,
                Type = this.Type,
                IsDouble = this.IsDouble,
                Origin = this.Origin,
                Angle = this.Angle,
                Scale = this.Scale,
                Style = this.Style,
                // Gradient metadata; colors and dialog state were copied without
                // running editing setters or regenerating the authored second stop.
                Shift = this.shift,
                // Copy all three ACI states: automatic, explicit value and absent.
                // Copying through the public getters would freeze automatic values.
                color1AciIndex = this.color1AciIndex,
                color2AciIndex = this.color2AciIndex,
                color1AciIndexAutomatic = this.color1AciIndexAutomatic,
                color2AciIndexAutomatic = this.color2AciIndexAutomatic
            };

            foreach (HatchPatternLineDefinition line in this.LineDefinitions)
                copy.LineDefinitions.Add((HatchPatternLineDefinition) line.Clone());

            return copy;
        }

        #endregion
    }
}