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
    /// <summary>Specifies which helix parameter is constrained by an authoring application.</summary>
    public enum HelixConstraint : short
    {
        /// <summary>Constrain the height of one turn.</summary>
        TurnHeight = 0,
        /// <summary>Constrain the number of turns.</summary>
        Turns = 1,
        /// <summary>Constrain the total height.</summary>
        Height = 2
    }

    /// <summary>A HELIX entity with independent stored spline geometry and helix parameters.</summary>
    /// <remarks>
    /// Changing parameters does not refit the inherited control polygon. Imported
    /// files may contain independently authored representations; neither replaces
    /// the other. Use ToSpline for explicit conversion to an ordinary spline.
    /// </remarks>
    public sealed partial class Helix : Spline
    {
        private int majorReleaseNumber = 29;
        private int maintenanceReleaseNumber = 63;
        private Vector3 axisBasePoint;
        private Vector3 startPoint = Vector3.UnitX;
        private Vector3 axisVector = Vector3.UnitZ;
        private double radius = 1.0;
        private double turns = 1.0;
        private double turnHeight = 1.0;
        private HelixConstraint constraint = HelixConstraint.TurnHeight;

        /// <summary>Creates a helix by deep-copying an existing spline payload.</summary>
        /// <remarks>Default parameters are independent of the supplied curve; this does not infer a helix from a spline.</remarks>
        public Helix(Spline spline) : base(spline, EntityType.Helix, DxfObjectCode.Helix)
        {
            this.IsRightHanded = true;
        }

        private Helix(Helix source) : base(source, EntityType.Helix, DxfObjectCode.Helix)
        {
            this.majorReleaseNumber = source.majorReleaseNumber;
            this.maintenanceReleaseNumber = source.maintenanceReleaseNumber;
            this.axisBasePoint = source.axisBasePoint;
            this.startPoint = source.startPoint;
            this.axisVector = source.axisVector;
            this.radius = source.radius;
            this.turns = source.turns;
            this.turnHeight = source.turnHeight;
            this.constraint = source.constraint;
            this.IsRightHanded = source.IsRightHanded;
        }

        /// <summary>Gets or sets the nonnegative class major release number (group 90).</summary>
        public int MajorReleaseNumber
        {
            get { return this.majorReleaseNumber; }
            set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.majorReleaseNumber = value; }
        }

        /// <summary>Gets or sets the nonnegative maintenance release number (group 91).</summary>
        public int MaintenanceReleaseNumber
        {
            get { return this.maintenanceReleaseNumber; }
            set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.maintenanceReleaseNumber = value; }
        }

        /// <summary>Gets or sets the finite world-coordinate axis base point.</summary>
        public Vector3 AxisBasePoint
        {
            get { return this.axisBasePoint; }
            set { ValidateFinite(value, nameof(value)); this.axisBasePoint = value; }
        }

        /// <summary>Gets or sets the finite world-coordinate start point of the parameterized helix.</summary>
        public Vector3 StartPoint
        {
            get { return this.startPoint; }
            set { ValidateFinite(value, nameof(value)); this.startPoint = value; }
        }

        /// <summary>Gets or sets the finite, nonzero world-coordinate axis vector; its magnitude is retained.</summary>
        public Vector3 AxisVector
        {
            get { return this.axisVector; }
            set
            {
                ValidateFinite(value, nameof(value));
                if (value.X == 0.0 && value.Y == 0.0 && value.Z == 0.0) throw new ArgumentOutOfRangeException(nameof(value));
                this.axisVector = value;
            }
        }

        /// <summary>Gets or sets the nonnegative terminal radius (group 40).</summary>
        /// <remarks>The initial radius is defined separately by StartPoint and AxisBasePoint.</remarks>
        public double Radius
        {
            get { return this.radius; }
            set { ValidateFinite(value, nameof(value)); if (value < 0.0) throw new ArgumentOutOfRangeException(nameof(value)); this.radius = value; }
        }

        /// <summary>Gets or sets a finite, positive number of turns.</summary>
        public double Turns
        {
            get { return this.turns; }
            set { ValidateFinite(value, nameof(value)); if (value <= 0.0) throw new ArgumentOutOfRangeException(nameof(value)); this.turns = value; }
        }

        /// <summary>Gets or sets finite signed height per turn. Zero retains a planar spiral definition.</summary>
        public double TurnHeight
        {
            get { return this.turnHeight; }
            set { ValidateFinite(value, nameof(value)); this.turnHeight = value; }
        }

        /// <summary>Gets or sets right (true) versus left (false) handedness.</summary>
        public bool IsRightHanded { get; set; }

        /// <summary>Gets or sets the authoring constraint selector; changing it does not solve or refit the curve.</summary>
        public HelixConstraint Constraint
        {
            get { return this.constraint; }
            set
            {
                if (value < HelixConstraint.TurnHeight || value > HelixConstraint.Height)
                    throw new ArgumentOutOfRangeException(nameof(value));
                this.constraint = value;
            }
        }

        /// <summary>Creates an independent ordinary SPLINE with exactly the stored curve and common metadata.</summary>
        public Spline ToSpline() { return (Spline) base.Clone(); }

        /// <inheritdoc />
        public override object Clone() { return new Helix(this); }

        /// <summary>Transforms both representations by a nonsingular similarity, including reflections.</summary>
        /// <remarks>Nonuniform scaling and shear are rejected before mutation. Convert to a spline for a general affine transform.</remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            double scale;
            if (!TryGetSimilarityScale(transformation, out scale))
                throw new NotSupportedException("HELIX parameter transforms require a nonsingular similarity. Use ToSpline for nonuniform scaling or shear.");
            ValidateFinite(translation, nameof(translation));
            Vector3 axisBase = transformation * this.axisBasePoint + translation;
            Vector3 start = transformation * this.startPoint + translation;
            Vector3 axis = transformation * this.axisVector;
            Vector3 normal = transformation * this.Normal;
            double normalLength = StableLength(normal);
            double newRadius = this.radius * scale, newHeight = this.turnHeight * scale;
            ValidateFinite(axisBase, nameof(transformation)); ValidateFinite(start, nameof(transformation));
            ValidateFinite(axis, nameof(transformation)); ValidateFinite(newRadius, nameof(transformation));
            ValidateFinite(newHeight, nameof(transformation));
            if ((axis.X == 0.0 && axis.Y == 0.0 && axis.Z == 0.0) || !IsFinite(normalLength) || normalLength == 0.0)
                throw new ArgumentOutOfRangeException(nameof(transformation));
            // Validate every transformed value before the inherited mutable arrays change.
            foreach (Vector3 point in this.ControlPoints) ValidateFinite(transformation * point + translation, nameof(transformation));
            foreach (Vector3 point in this.FitPoints) ValidateFinite(transformation * point + translation, nameof(transformation));
            if (this.StartTangent.HasValue) ValidateFinite(transformation * this.StartTangent.Value, nameof(transformation));
            if (this.EndTangent.HasValue) ValidateFinite(transformation * this.EndTangent.Value, nameof(transformation));
            Vector3 x = (transformation * Vector3.UnitX) / scale;
            Vector3 y = (transformation * Vector3.UnitY) / scale;
            Vector3 z = (transformation * Vector3.UnitZ) / scale;
            bool reflected = Vector3.DotProduct(Vector3.CrossProduct(x, y), z) < 0.0;
            base.TransformBy(transformation, translation);
            this.Normal = normal / normalLength;
            this.axisBasePoint = axisBase; this.startPoint = start; this.axisVector = axis;
            this.radius = newRadius; this.turnHeight = newHeight;
            if (reflected) this.IsRightHanded = !this.IsRightHanded;
        }

        internal static bool TryGetSimilarityScale(Matrix3 matrix, out double scale)
        {
            Vector3 x = matrix * Vector3.UnitX, y = matrix * Vector3.UnitY, z = matrix * Vector3.UnitZ;
            scale = StableLength(x);
            double sy = StableLength(y), sz = StableLength(z);
            if (!IsFinite(scale) || scale == 0.0 || !IsFinite(sy) || !IsFinite(sz)) return false;
            x /= scale; y /= scale; z /= scale;
            return Math.Abs(sy / scale - 1.0) <= 1.0e-10 && Math.Abs(sz / scale - 1.0) <= 1.0e-10 &&
                Math.Abs(Vector3.DotProduct(x, y)) <= 1.0e-10 && Math.Abs(Vector3.DotProduct(x, z)) <= 1.0e-10 &&
                Math.Abs(Vector3.DotProduct(y, z)) <= 1.0e-10;
        }

        internal static double StableLength(Vector3 vector)
        {
            double max = Math.Max(Math.Abs(vector.X), Math.Max(Math.Abs(vector.Y), Math.Abs(vector.Z)));
            if (max == 0.0) return 0.0;
            // Divide components directly: reciprocal multiplication overflows
            // for subnormal scales and loses the dominant 1 for very large ones.
            Vector3 v = new Vector3(vector.X / max, vector.Y / max, vector.Z / max);
            return max * Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        }

        internal static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        internal static void ValidateFinite(double value, string parameter)
        {
            if (!IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "HELIX values must be finite.");
        }
        internal static void ValidateFinite(Vector3 value, string parameter)
        {
            ValidateFinite(value.X, parameter); ValidateFinite(value.Y, parameter); ValidateFinite(value.Z, parameter);
        }
    }
}
