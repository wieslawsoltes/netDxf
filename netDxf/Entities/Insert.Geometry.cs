// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Insert
    {
        // Reader-only restoration retains finite source zeros without admitting
        // new collapsed geometry through the public authoring setter.
        internal void RestoreScale(Vector3 value) { FiniteInsert(value); this.scale = value; }

        internal static void FiniteInsert(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "INSERT geometry and transformations must be finite.");
        }
        internal static void FiniteInsert(Vector3 value)
        { FiniteInsert(value.X); FiniteInsert(value.Y); FiniteInsert(value.Z); }
        internal static bool SameInsert(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }
        internal static bool SameInsert(Vector3 a, Vector3 b)
        { return SameInsert(a.X, b.X) && SameInsert(a.Y, b.Y) && SameInsert(a.Z, b.Z); }

        private static Vector3 InsertAxis(Vector3 axis, out double largest, out double factor)
        {
            FiniteInsert(axis);
            largest = Math.Max(Math.Abs(axis.X), Math.Max(Math.Abs(axis.Y), Math.Abs(axis.Z)));
            if (largest == 0) throw new NotSupportedException("The transformation collapses an INSERT axis.");
            // Component division, not reciprocal multiplication: a subnormal
            // divisor can have an infinite reciprocal despite a finite quotient.
            var scaled = new Vector3(axis.X / largest, axis.Y / largest, axis.Z / largest);
            factor = Math.Sqrt(Vector3.DotProduct(scaled, scaled));
            return new Vector3(scaled.X / factor, scaled.Y / factor, scaled.Z / factor);
        }
        private static double InsertProduct(double value, double largest, double factor)
        {
            double result = (value * largest) * factor;
            FiniteInsert(result);
            if (value != 0 && result == 0)
                throw new NotSupportedException("A nonzero transformed INSERT scale or spacing underflows to zero.");
            return result;
        }

        private void ApplyInsertTransform(Matrix3 matrix, Vector3 translation)
        {
            bool identity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            {
                FiniteInsert(matrix[r, c]);
                identity &= matrix[r, c] == (r == c ? 1.0 : 0.0);
            }
            FiniteInsert(translation); FiniteInsert(this.position); FiniteInsert(base.Normal);
            FiniteInsert(this.scale); FiniteInsert(this.rotation);
            FiniteInsert(this.columnSpacing); FiniteInsert(this.rowSpacing);
            if (identity && translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;

            Vector3 nextPosition = InfiniteLineTransform.TransformPoint(matrix, this.position, translation);
            Vector3 nextNormal = base.Normal, nextScale = this.scale;
            double nextRotation = this.rotation, nextColumns = this.columnSpacing, nextRows = this.rowSpacing;
            if (!identity)
            {
                if (this.scale.X == 0 || this.scale.Y == 0 || this.scale.Z == 0)
                    throw new NotSupportedException("A nonidentity linear transform requires three nonzero INSERT scales.");
                Matrix3 frame = MathHelper.ArbitraryAxis(base.Normal) * Matrix3.RotationZ(this.rotation * MathHelper.DegToRad);
                double ax, ay, az, fx, fy, fz;
                Vector3 x = InsertAxis(InfiniteLineTransform.TransformPoint(matrix, frame * Vector3.UnitX, Vector3.Zero), out ax, out fx);
                Vector3 y = InsertAxis(InfiniteLineTransform.TransformPoint(matrix, frame * Vector3.UnitY, Vector3.Zero), out ay, out fy);
                Vector3 z = InsertAxis(InfiniteLineTransform.TransformPoint(matrix, frame * Vector3.UnitZ, Vector3.Zero), out az, out fz);
                const double tolerance = 1e-10; // Existing rectangular-array admission threshold.
                if (Math.Abs(Vector3.DotProduct(x, y)) > tolerance || Math.Abs(Vector3.DotProduct(x, z)) > tolerance
                    || Math.Abs(Vector3.DotProduct(y, z)) > tolerance)
                    throw new NotSupportedException("A nonorthogonal INSERT cannot be represented. Explode the block before applying shear.");

                nextNormal = Vector3.NormalizeFiniteDirection(z, nameof(matrix));
                Matrix3 target = MathHelper.ArbitraryAxis(nextNormal);
                Vector3 localX = target.Transpose() * x;
                nextRotation = MathHelper.NormalizeAngle(Math.Atan2(localX.Y, localX.X) * MathHelper.RadToDeg);
                Matrix3 nextFrame = target * Matrix3.RotationZ(nextRotation * MathHelper.DegToRad);
                double signY = Vector3.DotProduct(y, nextFrame * Vector3.UnitY) < 0 ? -1 : 1;
                if ((nextFrame * Vector3.UnitX - x).Modulus() > tolerance
                    || (signY * (nextFrame * Vector3.UnitY) - y).Modulus() > tolerance)
                    throw new NotSupportedException("The transformed INSERT frame cannot be reconstructed within tolerance.");
                nextScale = new Vector3(InsertProduct(this.scale.X, ax, fx),
                    InsertProduct(this.scale.Y, ay, fy) * signY, InsertProduct(this.scale.Z, az, fz));
                // Dormant singleton spacings describe the same frame as active
                // MINSERT spacings and must transform before later activation.
                nextColumns = InsertProduct(this.columnSpacing, ax, fx);
                nextRows = InsertProduct(this.rowSpacing, ay, fy) * signY;
            }

            // Prepare every attribute with the original owner/MIRRTEXT context.
            // Preparation has no mutation path into live metadata or callbacks.
            var candidates = new Attribute[this.attributes.Count];
            for (int i = 0; i < candidates.Length; i++)
                candidates[i] = this.attributes[i].PrepareInsertTransform(matrix, translation, identity);

            bool changed = !SameInsert(this.position, nextPosition) || !SameInsert(base.Normal, nextNormal)
                || !SameInsert(this.scale, nextScale) || !SameInsert(this.rotation, nextRotation)
                || !SameInsert(this.columnSpacing, nextColumns) || !SameInsert(this.rowSpacing, nextRows);
            if (changed) this.ClearProxyGraphics();
            if (!identity) base.Normal = nextNormal; // Bypass virtual Normal callbacks.
            this.position = nextPosition; this.scale = nextScale; this.rotation = nextRotation;
            this.columnSpacing = nextColumns; this.rowSpacing = nextRows;
            for (int i = 0; i < candidates.Length; i++)
                if (this.attributes[i].CommitInsertTransform(candidates[i])) this.ClearProxyGraphics();
        }

        /// <summary>Transforms this block reference by a finite affine matrix.</summary>
        /// <param name="transformation">Affine matrix using column vectors.</param>
        /// <remarks>Projective input rejects before the virtual Matrix3 entry point is called.</remarks>
        public override void TransformBy(Matrix4 transformation)
        {
            PlanarEntityTransform.CheckAffine(transformation);
            base.TransformBy(transformation);
        }
    }
}
