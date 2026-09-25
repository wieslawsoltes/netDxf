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
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>
    /// Represents an underlay <see cref="EntityObject">entity</see>.
    /// </summary>
    public partial class Underlay :
        EntityObject
    {
        #region delegates and events

        public delegate void UnderlayDefinitionChangedEventHandler(Underlay sender, TableObjectChangedEventArgs<UnderlayDefinition> e);
        public event UnderlayDefinitionChangedEventHandler UnderlayDefinitionChanged;
        protected virtual UnderlayDefinition OnUnderlayDefinitionChangedEvent(UnderlayDefinition oldUnderlayDefinition, UnderlayDefinition newUnderlayDefinition)
        {
            UnderlayDefinitionChangedEventHandler ae = this.UnderlayDefinitionChanged;
            if (ae != null)
            {
                TableObjectChangedEventArgs<UnderlayDefinition> eventArgs = new TableObjectChangedEventArgs<UnderlayDefinition>(oldUnderlayDefinition, newUnderlayDefinition);
                ae(this, eventArgs);
                return eventArgs.NewValue;
            }
            return newUnderlayDefinition;
        }

        #endregion

        #region private fields

        private UnderlayDefinition definition;
        private Vector3 position;
        private Vector2 scale;
        private double rotation;
        private short contrast;
        private short fade;
        private UnderlayDisplayFlags displayOptions;
        private ClippingBoundary clippingBoundary;

        #endregion

        #region constructor

        internal Underlay()
            : base(EntityType.Underlay, DxfObjectCode.Underlay)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Underlay</c> class.
        /// </summary>
        /// <param name="definition"><see cref="UnderlayDefinition">Underlay definition</see>.</param>
        public Underlay(UnderlayDefinition definition)
            : this(definition, Vector3.Zero, 1.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Underlay</c> class.
        /// </summary>
        /// <param name="definition"><see cref="UnderlayDefinition">Underlay definition</see>.</param>
        /// <param name="position">Underlay <see cref="Vector3">position</see> in world coordinates.</param>
        public Underlay(UnderlayDefinition definition, Vector3 position)
            : this(definition, position, 1.0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <c>Underlay</c> class.
        /// </summary>
        /// <param name="definition"><see cref="UnderlayDefinition">Underlay definition</see>.</param>
        /// <param name="position">Underlay <see cref="Vector3">position</see> in world coordinates.</param>
        /// <param name="scale">Underlay scale.</param>
        public Underlay(UnderlayDefinition definition, Vector3 position, double scale)
            : base(EntityType.Underlay, DxfObjectCode.Underlay)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.position = position;
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), scale, "The Underlay scale must be greater than zero.");
            }
            this.scale = new Vector2(scale);
            this.rotation = 0.0;
            this.contrast = 100;
            this.fade = 0;
            this.displayOptions = UnderlayDisplayFlags.ShowUnderlay;
            this.clippingBoundary = null;
            switch (this.definition.Type)
            {
                case UnderlayType.DGN:
                    this.CodeName = DxfObjectCode.UnderlayDgn;
                    break;
                case UnderlayType.DWF:
                    this.CodeName = DxfObjectCode.UnderlayDwf;
                    break;
                case UnderlayType.PDF:
                    this.CodeName = DxfObjectCode.UnderlayPdf;
                    break;
            }
        }
        #endregion

        #region public properties

        /// <summary>
        /// Gets the underlay definition.
        /// </summary>
        public UnderlayDefinition Definition
        {
            get { return this.definition; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                this.definition = this.OnUnderlayDefinitionChangedEvent(this.definition, value);

                switch (value.Type)
                {
                    case UnderlayType.DGN:
                        this.CodeName = DxfObjectCode.UnderlayDgn;
                        break;
                    case UnderlayType.DWF:
                        this.CodeName = DxfObjectCode.UnderlayDwf;
                        break;
                    case UnderlayType.PDF:
                        this.CodeName = DxfObjectCode.UnderlayPdf;
                        break;
                }
            }
        }

        /// <summary>
        /// Gets or sets the underlay position in world coordinates.
        /// </summary>
        public Vector3 Position
        {
            get { return this.position; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.position, value); }
        }

        /// <summary>
        /// Gets or sets the underlay scale.
        /// </summary>
        /// <remarks>
        /// Assignments require finite, nonzero components; negative and subnormal values are allowed.<br />
        /// The signed X and Y components scale the two local page axes. The independent
        /// DXF Z scalar is exposed by <see cref="ScaleZ"/> and does not change this page geometry.<br />
        /// Loaded finite zero components are retained for file fidelity. A nonidentity linear
        /// transformation of a collapsed page is not supported.
        /// </remarks>
        public Vector2 Scale
        {
            get { return this.scale; }
            set
            {
                FiniteUnderlay(value.X);
                FiniteUnderlay(value.Y);
                if (value.X == 0.0 || value.Y == 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Any of the vector scale components cannot be zero.");
                }
                bool changed = !SameUnderlay(this.scale.X, value.X) || !SameUnderlay(this.scale.Y, value.Y);
                this.scale = value;
                if (changed) this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the underlay rotation around its normal.
        /// </summary>
        public double Rotation
        {
            get { return this.rotation; }
            set { PrimitiveGeometryMutation.Assign(this, ref this.rotation, MathHelper.NormalizeAngle(value)); }
        }

        /// <summary>
        /// Gets or sets the underlay contrast.
        /// </summary>
        /// <remarks>Valid values range from 20 to 100.</remarks>
        public short Contrast
        {
            get { return this.contrast; }
            set
            {
                if (value < 20 || value > 100)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Accepted contrast values range from 20 to 100.");
                }
                bool changed = this.contrast != value;
                this.contrast = value;
                if (changed) this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the underlay fade.
        /// </summary>
        /// <remarks>Valid values range from 0 to 80.</remarks>
        public short Fade
        {
            get { return this.fade; }
            set
            {
                if (value < 0 || value > 80)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Accepted fade values range from 0 to 80.");
                }
                bool changed = this.fade != value;
                this.fade = value;
                if (changed) this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the underlay display options.
        /// </summary>
        public UnderlayDisplayFlags DisplayOptions
        {
            get { return this.displayOptions; }
            set
            {
                bool changed = this.displayOptions != value;
                this.displayOptions = value;
                if (changed) this.ClearProxyGraphics();
            }
        }

        /// <summary>
        /// Gets or sets the underlay clipping boundary.
        /// </summary>
        /// <remarks>
        /// Set as null to restore the default clipping boundary, show the full underlay without clipping.
        /// </remarks>
        public ClippingBoundary ClippingBoundary
        {
            get { return this.clippingBoundary; }
            set
            {
                bool changed = !ReferenceEquals(this.clippingBoundary, value);
                this.clippingBoundary = value;
                if (changed) this.ClearProxyGraphics();
            }
        }

        #endregion

        #region overrides

        /// <summary>
        /// Moves, scales, and/or rotates the current entity given a 3x3 transformation matrix and a translation vector.
        /// </summary>
        /// <param name="transformation">Transformation matrix.</param>
        /// <param name="translation">Translation vector.</param>
        /// <remarks>
        /// Transformed local axes must remain orthogonal: unlike IMAGE, an underlay cannot store shear.<br />
        /// Invalid, collapsed, or unrepresentable geometry rejects before changing this entity.
        /// Nonidentity linear transforms use equivalent positive scales and an oriented normal.<br />
        /// Matrix3 adopts the convention of using column vectors to represent a transformation matrix.
        /// </remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            this.ApplyUnderlayTransform(transformation, translation);
        }

        /// <summary>
        /// Creates a new Underlay that is a copy of the current instance.
        /// </summary>
        /// <returns>A new Underlay that is a copy of this instance.</returns>
        public override object Clone()
        {
            Underlay entity = new Underlay
            {
                //EntityObject properties
                Layer = (Layer) this.Layer.Clone(),
                Linetype = (Linetype) this.Linetype.Clone(),
                Color = (AciColor) this.Color.Clone(),
                Lineweight = this.Lineweight,
                Transparency = (Transparency) this.Transparency.Clone(),
                LinetypeScale = this.LinetypeScale,
                Normal = this.Normal,
                IsVisible = this.IsVisible,
                //Underlay properties
                Definition = (UnderlayDefinition) this.definition.Clone(),
                Position = this.position,
                // Copy stored scalars directly: loaded finite zero scales are valid
                // retained data, but are not accepted as new public assignments.
                scale = this.scale,
                scaleZ = this.scaleZ,
                Rotation = this.rotation,
                Contrast = this.contrast,
                Fade = this.fade,
                DisplayOptions = this.displayOptions,
                ClippingBoundary = this.clippingBoundary != null ? (ClippingBoundary) this.clippingBoundary.Clone() : null
            };

            foreach (XData data in this.XData.Values)
                entity.XData.Add((XData) data.Clone());

            this.CopyCommonDataTo(entity);
            return entity;
        }

        #endregion
    }
}