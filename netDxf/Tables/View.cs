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
using System.Collections.Generic;
using netDxf.Collections;

namespace netDxf.Tables
{
    /// <summary>
    /// Represents a named view in the DXF VIEW symbol table.
    /// </summary>
    public partial class View :
        TableObject
    {
        private Vector3 target;
        private Vector3 camera;
        private Vector2 center;
        private double height;
        private double width;
        private double rotation;
        private ViewModeFlags viewmode;
        private double fov;
        private double frontClippingPlane;
        private double backClippingPlane;
        private ViewRenderMode renderMode;
        private ViewFlags flags;
        private bool cameraPlottable;

        /// <summary>
        /// Initializes a new instance of the <c>View</c> class.
        /// </summary>
        /// <param name="name">Name of the view.</param>
        public View(string name)
            : this(name ?? throw new ArgumentNullException(nameof(name)), true)
        {
        }

        internal View(string name, bool checkName)
            : base(name, DxfObjectCode.View, checkName)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name), "The view name should be at least one character long.");
            }

            this.target = Vector3.Zero;
            this.camera = Vector3.UnitZ;
            this.center = Vector2.Zero;
            this.height = 1.0;
            this.width = 1.0;
            this.fov = 40.0;
        }

        /// <summary>
        /// Gets or sets the target point in world coordinates (DXF 12/22/32).
        /// </summary>
        public Vector3 Target
        {
            get { return this.target; }
            set
            {
                RequireFinite(value.X, nameof(Target));
                RequireFinite(value.Y, nameof(Target));
                RequireFinite(value.Z, nameof(Target));
                this.target = value;
            }
        }

        /// <summary>
        /// Gets or sets the view direction from the target, in world coordinates (DXF 11/21/31).
        /// </summary>
        /// <remarks>This nonzero vector is not an absolute camera position and is not normalized on assignment.</remarks>
        public Vector3 ViewDirection
        {
            get { return this.camera; }
            set
            {
                RequireFinite(value.X, nameof(ViewDirection));
                RequireFinite(value.Y, nameof(ViewDirection));
                RequireFinite(value.Z, nameof(ViewDirection));
                if (value.X == 0.0 && value.Y == 0.0 && value.Z == 0.0)
                {
                    throw new ArgumentException("The view direction cannot be zero.", nameof(value));
                }
                this.camera = value;
            }
        }

        /// <summary>
        /// Gets or sets the view direction; compatibility alias for <see cref="ViewDirection"/>.
        /// </summary>
        public Vector3 Camera
        {
            get { return this.ViewDirection; }
            set { this.ViewDirection = value; }
        }

        /// <summary>
        /// Gets or sets the view center in display coordinates (DXF 10/20).
        /// </summary>
        public Vector2 ViewCenter
        {
            get { return this.center; }
            set
            {
                RequireFinite(value.X, nameof(ViewCenter));
                RequireFinite(value.Y, nameof(ViewCenter));
                this.center = value;
            }
        }

        /// <summary>
        /// Gets or sets the positive view height in display coordinates (DXF 40).
        /// </summary>
        public double Height
        {
            get { return this.height; }
            set { this.height = RequirePositive(value, nameof(Height)); }
        }

        /// <summary>
        /// Gets or sets the positive view width in display coordinates (DXF 41).
        /// </summary>
        public double Width
        {
            get { return this.width; }
            set { this.width = RequirePositive(value, nameof(Width)); }
        }

        /// <summary>
        /// Gets or sets the view twist angle in degrees (DXF 50).
        /// </summary>
        public double Rotation
        {
            get { return this.rotation; }
            set { this.rotation = RequireFinite(value, nameof(Rotation)); }
        }

        /// <summary>
        /// Gets or sets the perspective, clipping and UCS-follow flags (DXF 71).
        /// </summary>
        public ViewModeFlags ViewMode
        {
            get { return this.viewmode; }
            set
            {
                if ((int)value < short.MinValue || (int)value > short.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "The view mode must fit a DXF 16-bit integer.");
                }
                this.viewmode = value;
            }
        }

        /// <summary>
        /// Gets or sets the view mode; compatibility alias for <see cref="ViewMode"/>.
        /// </summary>
        public ViewModeFlags Viewmode
        {
            get { return this.ViewMode; }
            set { this.ViewMode = value; }
        }

        /// <summary>
        /// Gets or sets the positive camera lens length (DXF 42).
        /// </summary>
        /// <remarks>This is a lens length, not an angular field of view.</remarks>
        public double LensLength
        {
            get { return this.fov; }
            set { this.fov = RequirePositive(value, nameof(LensLength)); }
        }

        /// <summary>
        /// Gets or sets the lens length; compatibility alias for <see cref="LensLength"/>.
        /// </summary>
        /// <remarks>Despite the legacy member name, the value is not an angle.</remarks>
        public double Fov
        {
            get { return this.LensLength; }
            set { this.LensLength = value; }
        }

        /// <summary>
        /// Gets or sets the front clipping plane offset from the target (DXF 43).
        /// </summary>
        public double FrontClippingPlane
        {
            get { return this.frontClippingPlane; }
            set { this.frontClippingPlane = RequireFinite(value, nameof(FrontClippingPlane)); }
        }

        /// <summary>
        /// Gets or sets the back clipping plane offset from the target (DXF 44).
        /// </summary>
        public double BackClippingPlane
        {
            get { return this.backClippingPlane; }
            set { this.backClippingPlane = RequireFinite(value, nameof(BackClippingPlane)); }
        }

        /// <summary>
        /// Gets or sets the VIEW table entry flags (DXF 70).
        /// </summary>
        /// <remarks>External-reference flags are metadata, not an instruction to load an external drawing.</remarks>
        public ViewFlags Flags
        {
            get { return this.flags; }
            set { this.flags = value; }
        }

        /// <summary>
        /// Gets or sets whether this is a paper-space view (bit 1 of DXF 70).
        /// </summary>
        public bool IsPaperSpace
        {
            get { return (this.flags & ViewFlags.PaperSpace) != 0; }
            set
            {
                if (value) this.flags |= ViewFlags.PaperSpace;
                else this.flags &= ~ViewFlags.PaperSpace;
            }
        }

        /// <summary>
        /// Gets or sets the display rendering mode (DXF 281).
        /// </summary>
        public ViewRenderMode RenderMode
        {
            get { return this.renderMode; }
            set
            {
                if (value < ViewRenderMode.TwoDimensionalOptimized || value > ViewRenderMode.GouraudShadedWithWireframe)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown view render mode.");
                }
                this.renderMode = value;
            }
        }

        /// <summary>
        /// Gets or sets whether the camera is plottable (DXF 73, AutoCAD 2007 and later).
        /// </summary>
        /// <remarks>Saving a true value to an earlier version is rejected rather than silently discarding it.</remarks>
        public bool IsCameraPlottable
        {
            get { return this.cameraPlottable; }
            set { this.cameraPlottable = value; }
        }

        /// <summary>
        /// Gets the owner of the view.
        /// </summary>
        public new Views Owner
        {
            get { return (Views)base.Owner; }
            internal set { base.Owner = value; }
        }

        /// <summary>
        /// Checks whether this view is referenced by another DXF object.
        /// </summary>
        /// <returns>True when a document object references this view.</returns>
        public override bool HasReferences()
        {
            return this.Owner != null && this.Owner.HasReferences(this.Name);
        }

        /// <summary>
        /// Gets the DXF objects referencing this view.
        /// </summary>
        /// <returns>References, or null when the view does not belong to a document.</returns>
        public override List<DxfObjectReference> GetReferences()
        {
            return this.Owner?.GetReferences(this.Name);
        }

        /// <summary>
        /// Creates an independent copy of this view under a new name.
        /// </summary>
        /// <param name="newName">Name of the copy.</param>
        /// <returns>The copied view.</returns>
        public override TableObject Clone(string newName)
        {
            View copy = new View(newName)
            {
                Target = this.target,
                ViewDirection = this.camera,
                ViewCenter = this.center,
                Height = this.height,
                Width = this.width,
                Rotation = this.rotation,
                ViewMode = this.viewmode,
                LensLength = this.fov,
                FrontClippingPlane = this.frontClippingPlane,
                BackClippingPlane = this.backClippingPlane,
                Flags = this.flags,
                RenderMode = this.renderMode,
                IsCameraPlottable = this.cameraPlottable,
                Ucs = this.Ucs == null ? null : (ViewUcs)this.Ucs.Clone()
            };
            foreach (XData data in this.XData.Values)
            {
                copy.XData.Add((XData)data.Clone());
            }
            return copy;
        }

        /// <summary>
        /// Creates an independent copy retaining the view name.
        /// </summary>
        /// <returns>The copied view.</returns>
        public override object Clone()
        {
            return this.Clone(this.Name);
        }

        private static double RequireFinite(double value, string property)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(property, value, "The view value must be finite.");
            }
            return value;
        }

        private static double RequirePositive(double value, string property)
        {
            RequireFinite(value, property);
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(property, value, "The view value must be greater than zero.");
            }
            return value;
        }
    }
}
