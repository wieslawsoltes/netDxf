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
using System.Runtime.CompilerServices;
using netDxf.Collections;

namespace netDxf.Tables
{
    /// <summary>A model-space viewport record belonging to a named viewport configuration.</summary>
    /// <remarks>Multiple records may share a configuration name. Record equality is object identity.</remarks>
    public class VPort : TableObject
    {
        /// <summary>The current viewport configuration name.</summary>
        public const string DefaultName = "*Active";
        /// <summary>Creates a detached record for the current viewport configuration.</summary>
        public static VPort Active { get { return new VPort(DefaultName, false); } }

        /// <summary>Creates a viewport record for a named configuration.</summary>
        public VPort(string name) : this(name, true) { }

        internal VPort(string name, bool checkName)
            : base(ValidateName(name), DxfObjectCode.VPort, checkName && !IsActiveName(name))
        {
            this.IsReserved = IsActiveName(this.Name);
        }

        internal static bool IsActiveName(string name)
        {
            return string.Equals(name == null ? null : name.Trim(), DefaultName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Validates renaming and updates the configuration index only after all observers accept it.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A viewport configuration name cannot be blank.", nameof(newName));
            VPorts table = this.Owner;
            if (table != null) table.ValidateRecordRename(this);
            base.OnNameChangedEvent(oldName, newName);
            // Collection indexes must not change if a later user event handler rejects the rename.
            // Observers may attach, detach, or move a record while handling the event.
            table = this.Owner;
            if (table != null)
            {
                table.ValidateRecordRename(this);
                table.CommitRecordRename(this, newName);
            }
        }

        private static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            return name;
        }

        private Vector2 viewCenter = Vector2.Zero;
        /// <summary>Gets or sets the view center in display coordinates.</summary>
        public Vector2 ViewCenter
        {
            get { return this.viewCenter; }
            set { this.viewCenter = Finite(value, nameof(value)); }
        }

        private Vector2 snapBasePoint = Vector2.Zero;
        /// <summary>Gets or sets the snap origin in display coordinates.</summary>
        public Vector2 SnapBasePoint
        {
            get { return this.snapBasePoint; }
            set { this.snapBasePoint = Finite(value, nameof(value)); }
        }

        private Vector2 snapSpacing = new Vector2(0.5);
        /// <summary>Gets or sets the X and Y snap spacing.</summary>
        public Vector2 SnapSpacing
        {
            get { return this.snapSpacing; }
            set { this.snapSpacing = Finite(value, nameof(value)); }
        }

        private Vector2 gridSpacing = new Vector2(10.0);
        /// <summary>Gets or sets the X and Y grid spacing.</summary>
        public Vector2 GridSpacing
        {
            get { return this.gridSpacing; }
            set { this.gridSpacing = Finite(value, nameof(value)); }
        }

        private Vector3 viewDirection = Vector3.UnitZ;
        /// <summary>Gets or sets the nonzero WCS direction from the target; its authored magnitude is retained.</summary>
        public Vector3 ViewDirection
        {
            get { return this.viewDirection; }
            set { this.viewDirection = Nonzero(value, nameof(value)); }
        }

        private Vector3 viewTarget = Vector3.Zero;
        /// <summary>Gets or sets the view target in world coordinates.</summary>
        public Vector3 ViewTarget
        {
            get { return this.viewTarget; }
            set { this.viewTarget = Finite(value, nameof(value)); }
        }

        private double viewHeight = 10.0;
        /// <summary>Gets or sets the view height (DXF group 40).</summary>
        public double ViewHeight
        {
            get { return this.viewHeight; }
            set { this.viewHeight = Positive(value, nameof(value)); }
        }

        private double viewAspectRatio = 1.0;
        /// <summary>Gets or sets the view width divided by the view height.</summary>
        public double ViewAspectRatio
        {
            get { return this.viewAspectRatio; }
            set { this.viewAspectRatio = Positive(value, nameof(value)); }
        }

        private bool showGrid = true;
        /// <summary>Gets or sets whether the grid is shown.</summary>
        public bool ShowGrid
        {
            get { return this.showGrid; }
            set { this.showGrid = value; }
        }

        private bool snapMode = false;
        /// <summary>Gets or sets whether snapping is enabled.</summary>
        public bool SnapMode
        {
            get { return this.snapMode; }
            set { this.snapMode = value; }
        }

        private Vector2 lowerLeftCorner = Vector2.Zero;
        /// <summary>Gets or sets the lower-left viewport corner in normalized display coordinates.</summary>
        public Vector2 LowerLeftCorner
        {
            get { return this.lowerLeftCorner; }
            set { this.lowerLeftCorner = Finite(value, nameof(value)); }
        }

        private Vector2 upperRightCorner = new Vector2(1.0);
        /// <summary>Gets or sets the upper-right viewport corner in normalized display coordinates.</summary>
        public Vector2 UpperRightCorner
        {
            get { return this.upperRightCorner; }
            set { this.upperRightCorner = Finite(value, nameof(value)); }
        }

        private VPortFlags flags = VPortFlags.None;
        /// <summary>Gets or sets the stored symbol-table flags (group 70).</summary>
        public VPortFlags Flags
        {
            get { return this.flags; }
            set { this.flags = value; }
        }

        private double lensLength = 50.0;
        /// <summary>Gets or sets the camera lens focal length in millimeters.</summary>
        public double LensLength
        {
            get { return this.lensLength; }
            set { this.lensLength = Positive(value, nameof(value)); }
        }

        private double frontClippingPlane = 0.0;
        /// <summary>Gets or sets the front clipping plane offset from the target.</summary>
        public double FrontClippingPlane
        {
            get { return this.frontClippingPlane; }
            set { this.frontClippingPlane = Finite(value, nameof(value)); }
        }

        private double backClippingPlane = 0.0;
        /// <summary>Gets or sets the back clipping plane offset from the target.</summary>
        public double BackClippingPlane
        {
            get { return this.backClippingPlane; }
            set { this.backClippingPlane = Finite(value, nameof(value)); }
        }

        private double snapRotation = 0.0;
        /// <summary>Gets or sets the snap rotation in degrees.</summary>
        public double SnapRotation
        {
            get { return this.snapRotation; }
            set { this.snapRotation = Finite(value, nameof(value)); }
        }

        private double viewTwist = 0.0;
        /// <summary>Gets or sets the view twist in degrees.</summary>
        public double ViewTwist
        {
            get { return this.viewTwist; }
            set { this.viewTwist = Finite(value, nameof(value)); }
        }

        private ViewModeFlags viewMode = ViewModeFlags.Off;
        /// <summary>Gets or sets the VIEWMODE flags (group 71).</summary>
        public ViewModeFlags ViewMode
        {
            get { return this.viewMode; }
            set { this.viewMode = CheckedMode(value); }
        }

        private short circleSides = 1000;
        /// <summary>Gets or sets the circle resolution (group 72), from 1 through 32767.</summary>
        public short CircleSides
        {
            get { return this.circleSides; }
            set { this.circleSides = (short)Range(value, 1, short.MaxValue, nameof(value)); }
        }

        private bool fastZoom = true;
        /// <summary>Gets or sets the stored fast-zoom setting (group 73).</summary>
        public bool FastZoom
        {
            get { return this.fastZoom; }
            set { this.fastZoom = value; }
        }

        private short ucsIcon = 3;
        /// <summary>Gets or sets the UCS icon bits: 1 shows the icon and 2 places it at the origin.</summary>
        public short UcsIcon
        {
            get { return this.ucsIcon; }
            set { this.ucsIcon = (short)Range(value, 0, 3, nameof(value)); }
        }

        private short snapStyle = 0;
        /// <summary>Gets or sets the snap style: 0 standard or 1 isometric.</summary>
        public short SnapStyle
        {
            get { return this.snapStyle; }
            set { this.snapStyle = (short)Range(value, 0, 1, nameof(value)); }
        }

        private short snapIsopair = 0;
        /// <summary>Gets or sets the isometric snap plane: 0 left, 1 top, or 2 right.</summary>
        public short SnapIsopair
        {
            get { return this.snapIsopair; }
            set { this.snapIsopair = (short)Range(value, 0, 2, nameof(value)); }
        }

        private ViewRenderMode renderMode = ViewRenderMode.TwoDimensionalOptimized;
        /// <summary>Gets or sets the stored viewport rendering mode.</summary>
        public ViewRenderMode RenderMode
        {
            get { return this.renderMode; }
            set { this.renderMode = (ViewRenderMode)Range((int)value, 0, 6, nameof(value)); }
        }

        private bool ucsPerViewport = false;
        /// <summary>Gets or sets whether activation restores this viewport's stored UCS.</summary>
        public bool UcsPerViewport
        {
            get { return this.ucsPerViewport; }
            set { this.ucsPerViewport = value; }
        }

        private Vector3 ucsOrigin = Vector3.Zero;
        /// <summary>Gets or sets the stored unnamed UCS origin in world coordinates.</summary>
        public Vector3 UcsOrigin
        {
            get { return this.ucsOrigin; }
            set { this.ucsOrigin = Finite(value, nameof(value)); }
        }

        private Vector3 ucsXAxis = Vector3.UnitX;
        /// <summary>Gets or sets the stored nonzero UCS X-axis, without normalization.</summary>
        public Vector3 UcsXAxis
        {
            get { return this.ucsXAxis; }
            set { this.ucsXAxis = Nonzero(value, nameof(value)); }
        }

        private Vector3 ucsYAxis = Vector3.UnitY;
        /// <summary>Gets or sets the stored nonzero UCS Y-axis, without normalization.</summary>
        public Vector3 UcsYAxis
        {
            get { return this.ucsYAxis; }
            set { this.ucsYAxis = Nonzero(value, nameof(value)); }
        }

        private short ucsOrthographicType = 0;
        /// <summary>Gets or sets the orthographic UCS type: 0 none, 1 top, 2 bottom, 3 front, 4 back, 5 left, 6 right.</summary>
        public short UcsOrthographicType
        {
            get { return this.ucsOrthographicType; }
            set { this.ucsOrthographicType = (short)Range(value, 0, 6, nameof(value)); }
        }

        private double ucsElevation = 0.0;
        /// <summary>Gets or sets the stored UCS elevation.</summary>
        public double UcsElevation
        {
            get { return this.ucsElevation; }
            set { this.ucsElevation = Finite(value, nameof(value)); }
        }

        /// <summary>The owning viewport configuration table, or null when detached.</summary>
        public new VPorts Owner
        {
            get { return (VPorts)base.Owner; }
            internal set { base.Owner = value; }
        }

        /// <summary>Checks references to this record's configuration.</summary>
        public override bool HasReferences() { return this.Owner != null && this.Owner.HasReferences(this.Name); }
        /// <summary>Returns references to this record's configuration, or null when detached.</summary>
        public override List<DxfObjectReference> GetReferences() { return this.Owner?.GetReferences(this.Name); }

        /// <summary>Viewport records compare by identity, including records with the same configuration name.</summary>
        protected override bool UsesReferenceIdentity { get { return true; } }
        /// <summary>Returns a stable identity hash that is unaffected by configuration renaming.</summary>
        public override int GetHashCode() { return RuntimeHelpers.GetHashCode(this); }

        /// <summary>Creates an independent detached record with the supplied configuration name.</summary>
        public override TableObject Clone(string newName)
        {
            VPort copy = new VPort(newName)
            {
                ViewCenter = this.ViewCenter,
                SnapBasePoint = this.SnapBasePoint,
                SnapSpacing = this.SnapSpacing,
                GridSpacing = this.GridSpacing,
                ViewDirection = this.ViewDirection,
                ViewTarget = this.ViewTarget,
                ViewHeight = this.ViewHeight,
                ViewAspectRatio = this.ViewAspectRatio,
                ShowGrid = this.ShowGrid,
                SnapMode = this.SnapMode,
                LowerLeftCorner = this.LowerLeftCorner,
                UpperRightCorner = this.UpperRightCorner,
                Flags = this.Flags,
                LensLength = this.LensLength,
                FrontClippingPlane = this.FrontClippingPlane,
                BackClippingPlane = this.BackClippingPlane,
                SnapRotation = this.SnapRotation,
                ViewTwist = this.ViewTwist,
                ViewMode = this.ViewMode,
                CircleSides = this.CircleSides,
                FastZoom = this.FastZoom,
                UcsIcon = this.UcsIcon,
                SnapStyle = this.SnapStyle,
                SnapIsopair = this.SnapIsopair,
                RenderMode = this.RenderMode,
                UcsPerViewport = this.UcsPerViewport,
                UcsOrigin = this.UcsOrigin,
                UcsXAxis = this.UcsXAxis,
                UcsYAxis = this.UcsYAxis,
                UcsOrthographicType = this.UcsOrthographicType,
                UcsElevation = this.UcsElevation
            };
            foreach (XData data in this.XData.Values) copy.XData.Add((XData)data.Clone());
            return copy;
        }

        /// <summary>Creates an independent detached record in the same named configuration.</summary>
        public override object Clone() { return this.Clone(this.Name); }

        private static double Finite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "The value must be finite.");
            return value;
        }
        private static Vector2 Finite(Vector2 value, string name)
        {
            Finite(value.X, name); Finite(value.Y, name); return value;
        }
        private static Vector3 Finite(Vector3 value, string name)
        {
            Finite(value.X, name); Finite(value.Y, name); Finite(value.Z, name); return value;
        }
        private static Vector3 Nonzero(Vector3 value, string name)
        {
            Finite(value, name);
            if (value.X == 0 && value.Y == 0 && value.Z == 0) throw new ArgumentException("The vector must not be zero.", name);
            return value;
        }
        private static double Positive(double value, string name)
        {
            Finite(value, name);
            if (value <= 0) throw new ArgumentOutOfRangeException(name, "The value must be positive.");
            return value;
        }
        private static int Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name);
            return value;
        }
        private static ViewModeFlags CheckedMode(ViewModeFlags value)
        {
            Range((int)value, 0, short.MaxValue, nameof(value)); return value;
        }
    }
}
