using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Insert
    {
        private short columnCount = 1;
        private short rowCount = 1;
        private double columnSpacing;
        private double rowSpacing;

        /// <summary>Gets or sets the positive column count (DXF group 70), default 1.</summary>
        public short ColumnCount
        {
            get { return this.columnCount; }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "An INSERT requires at least one column.");
                this.columnCount = value;
            }
        }

        /// <summary>Gets or sets the positive row count (DXF group 71), default 1.</summary>
        public short RowCount
        {
            get { return this.rowCount; }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "An INSERT requires at least one row.");
                this.rowCount = value;
            }
        }

        /// <summary>Gets or sets the finite, signed column spacing (DXF group 44), default 0.</summary>
        /// <remarks>Spacing is in the containing drawing/block coordinate units, independent of the inserted block's scale and units.</remarks>
        public double ColumnSpacing
        {
            get { return this.columnSpacing; }
            set { ValidateArraySpacing(value); this.columnSpacing = value; }
        }

        /// <summary>Gets or sets the finite, signed row spacing (DXF group 45), default 0.</summary>
        /// <remarks>Zero spacing retains the logical multiplicity of coincident cells.</remarks>
        public double RowSpacing
        {
            get { return this.rowSpacing; }
            set { ValidateArraySpacing(value); this.rowSpacing = value; }
        }

        /// <summary>Gets whether this INSERT represents a rectangular array (MINSERT).</summary>
        public bool IsMultiple { get { return this.columnCount > 1 || this.rowCount > 1; } }

        /// <summary>Gets the number of logical cells, including coincident zero-spacing cells.</summary>
        public int InstanceCount { get { return (int) this.rowCount * this.columnCount; } }

        /// <summary>Gets a cell's insertion point in the same coordinate system as Position.</summary>
        /// <param name="row">Zero-based row index.</param>
        /// <param name="column">Zero-based column index.</param>
        /// <returns>The insertion point, without allocating or expanding block contents.</returns>
        /// <remarks>The grid follows Normal and Rotation, but is not multiplied by Scale or block unit conversion.</remarks>
        public Vector3 GetGridPosition(int row, int column)
        {
            return CheckedArrayPoint(this.Position + this.GetArrayOffset(row, column));
        }

        /// <summary>Explodes only the specified cell using the existing block/attribute explosion rules.</summary>
        /// <param name="row">Zero-based row index.</param>
        /// <param name="column">Zero-based column index.</param>
        /// <returns>Independent entities for this cell. Nested INSERTs remain nested INSERTs.</returns>
        public List<EntityObject> ExplodeCell(int row, int column)
        {
            Vector3 offset = this.GetArrayOffset(row, column);
            Matrix3 transformation = this.GetTransformation();
            Vector3 translation = CheckedArrayPoint(this.Position + offset - transformation * this.block.Origin);
            return this.ExplodeCellCore(transformation, translation, offset);
        }

        /// <summary>Enumerates exploded entities cell by cell in row-major order.</summary>
        /// <returns>A lazy sequence; only the current cell's exploded entity list is materialized.</returns>
        /// <remarks>
        /// Use this method or ExplodeCell instead of materializing a large array with Explode.
        /// Coincident cells are not deduplicated. Nested INSERT arrays are not recursively expanded.
        /// Do not mutate the source or its block while enumerating. Attribute positions are translated
        /// per cell; their shared array values and other formatting follow the existing explosion rules.
        /// </remarks>
        public IEnumerable<EntityObject> ExplodeEnumerable()
        {
            if (this.block.Entities.Count == 0 && this.attributes.Count == 0) yield break;
            Matrix3 transformation = this.GetTransformation();
            Matrix3 grid = MathHelper.ArbitraryAxis(this.Normal) * Matrix3.RotationZ(this.Rotation * MathHelper.DegToRad);
            Vector3 translation = this.Position - transformation * this.block.Origin;
            int rows = this.rowCount, columns = this.columnCount;
            double dy = this.rowSpacing, dx = this.columnSpacing;
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    Vector3 offset = CheckedArrayPoint(grid * new Vector3(column * dx, row * dy, 0));
                    foreach (EntityObject entity in this.ExplodeCellCore(transformation, CheckedArrayPoint(translation + offset), offset))
                        yield return entity;
                }
        }

        private Vector3 GetArrayOffset(int row, int column)
        {
            if (row < 0 || row >= this.rowCount) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 0 || column >= this.columnCount) throw new ArgumentOutOfRangeException(nameof(column));
            Matrix3 grid = MathHelper.ArbitraryAxis(this.Normal) * Matrix3.RotationZ(this.Rotation * MathHelper.DegToRad);
            return CheckedArrayPoint(grid * new Vector3(column * this.columnSpacing, row * this.rowSpacing, 0));
        }

        private static void ValidateArraySpacing(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Array spacing must be finite.");
        }

        private static Vector3 CheckedArrayPoint(Vector3 value)
        {
            if (double.IsNaN(value.X) || double.IsInfinity(value.X) ||
                double.IsNaN(value.Y) || double.IsInfinity(value.Y) ||
                double.IsNaN(value.Z) || double.IsInfinity(value.Z))
                throw new InvalidOperationException("The INSERT array calculation produced a non-finite coordinate.");
            return value;
        }

        private void TransformArray(Matrix3 transformation, Vector3 translation)
        {
            Matrix3 frame = MathHelper.ArbitraryAxis(this.Normal) * Matrix3.RotationZ(this.Rotation * MathHelper.DegToRad);
            Vector3 x = CheckedArrayPoint(transformation * (frame * Vector3.UnitX));
            Vector3 y = CheckedArrayPoint(transformation * (frame * Vector3.UnitY));
            Vector3 z = CheckedArrayPoint(transformation * (frame * Vector3.UnitZ));
            double lx = ArrayAxisLength(x), ly = ArrayAxisLength(y), lz = ArrayAxisLength(z);
            Vector3 ux = x / lx, uy = y / ly, uz = z / lz;
            // A rectangular INSERT cannot encode shear. Validate before changing entity state.
            const double orthogonalityTolerance = 1e-10;
            if (Math.Abs(Vector3.DotProduct(ux, uy)) > orthogonalityTolerance ||
                Math.Abs(Vector3.DotProduct(ux, uz)) > orthogonalityTolerance ||
                Math.Abs(Vector3.DotProduct(uy, uz)) > orthogonalityTolerance)
                throw new NotSupportedException("This transformation produces a non-orthogonal INSERT array. Explode the array before applying shear.");

            Matrix3 target = MathHelper.ArbitraryAxis(uz);
            Vector3 localX = target.Transpose() * ux;
            double rotation = Math.Atan2(localX.Y, localX.X);
            Vector3 targetY = target * (Matrix3.RotationZ(rotation) * Vector3.UnitY);
            double signedY = Vector3.DotProduct(uy, targetY) < 0 ? -ly : ly;
            Vector3 scale = CheckedArrayPoint(new Vector3(this.Scale.X * lx, this.Scale.Y * signedY, this.Scale.Z * lz));
            if (MathHelper.IsZero(scale.X) || MathHelper.IsZero(scale.Y) || MathHelper.IsZero(scale.Z))
                throw new NotSupportedException("The transformation collapses an INSERT scale component.");
            Vector3 position = CheckedArrayPoint(transformation * this.Position + translation);
            double dx = this.columnSpacing * lx, dy = this.rowSpacing * signedY;
            ValidateArraySpacing(dx); ValidateArraySpacing(dy);

            this.Normal = uz;
            this.Position = position;
            this.Scale = scale;
            this.Rotation = rotation * MathHelper.RadToDeg;
            this.columnSpacing = dx;
            this.rowSpacing = dy;
            foreach (Attribute attribute in this.attributes) attribute.TransformBy(transformation, translation);
        }

        private static double ArrayAxisLength(Vector3 value)
        {
            double largest = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
            if (largest == 0) throw new NotSupportedException("The transformation collapses an INSERT array axis.");
            Vector3 scaled = value / largest;
            double length = largest * Math.Sqrt(Vector3.DotProduct(scaled, scaled));
            if (double.IsInfinity(length) || MathHelper.IsZero(length))
                throw new NotSupportedException("The transformed INSERT array axis has an unsupported length.");
            return length;
        }
    }
}
