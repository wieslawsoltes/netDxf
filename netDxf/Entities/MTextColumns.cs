using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.Entities
{
    /// <summary>MTEXT column layout mode. This records layout instructions, not font layout results.</summary>
    public enum MTextColumnType : short
    {
        /// <summary>No columns; a defined text height can still be recorded.</summary>
        None = 0,
        /// <summary>A fixed number of equal-height columns.</summary>
        Static = 1,
        /// <summary>Dynamic columns with automatic or individual heights.</summary>
        Dynamic = 2
    }

    /// <summary>The on-disk representation of an MTEXT column definition.</summary>
    public enum MTextColumnStorage
    {
        /// <summary>The column tags documented in the Autodesk DXF reference.</summary>
        Direct,
        /// <summary>ACAD XDATA and independently owned linked MTEXT entities (R2000 through R2013).</summary>
        LegacyLinked,
        /// <summary>A single MTEXT with a 2018 embedded column object.</summary>
        Embedded
    }

    /// <summary>Editable MTEXT column metadata and explicit legacy entity links.</summary>
    /// <remarks>Heights and linked entities are validated by Validate and before serialization.
    /// The final manual column height may be zero, meaning it consumes the remaining text.
    /// Linked entities remain in their owning block and must be added to a document along with the first column.</remarks>
    public sealed class MTextColumns : ICloneable
    {
        private MTextColumnType type = MTextColumnType.Static;
        private MTextColumnStorage storage = MTextColumnStorage.Embedded;
        private int count = 1;
        private double width = 1.0, gutter, definedHeight, totalHeight;
        private readonly List<double> heights = new List<double>();
        private readonly List<MText> linkedColumns = new List<MText>();
        internal readonly List<string> PendingHandles = new List<string>();

        /// <summary>Gets or sets the column type.</summary>
        public MTextColumnType Type
        {
            get { return this.type; }
            set { if (value < MTextColumnType.None || value > MTextColumnType.Dynamic) throw new ArgumentOutOfRangeException(nameof(value)); this.type = value; }
        }
        /// <summary>Gets or sets the required wire representation. Saving never implicitly changes it.</summary>
        public MTextColumnStorage Storage
        {
            get { return this.storage; }
            set { if (value < MTextColumnStorage.Direct || value > MTextColumnStorage.Embedded) throw new ArgumentOutOfRangeException(nameof(value)); this.storage = value; }
        }
        /// <summary>Gets or sets the number of columns, including the first MTEXT.</summary>
        public int Count
        {
            get { return this.count; }
            set { if (value < 1 || value > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value)); this.count = value; this.StoredTotalWidth = null; }
        }
        /// <summary>Gets or sets automatic equal-height balancing for dynamic columns.</summary>
        public bool AutoHeight { get; set; }
        /// <summary>Gets or sets reversed column flow.</summary>
        public bool FlowReversed { get; set; }
        /// <summary>Gets or sets the width shared by all columns.</summary>
        public double Width { get { return this.width; } set { Positive(value, nameof(value)); this.width = value; this.StoredTotalWidth = null; } }
        /// <summary>Gets or sets the space between columns.</summary>
        public double Gutter { get { return this.gutter; } set { NonNegative(value, nameof(value)); this.gutter = value; this.StoredTotalWidth = null; } }
        /// <summary>Gets or sets the common defined height; zero is permitted for content-defined height.</summary>
        public double DefinedHeight { get { return this.definedHeight; } set { NonNegative(value, nameof(value)); this.definedHeight = value; } }
        /// <summary>Gets the calculated total width for the current count, width and gutter.</summary>
        public double TotalWidth { get { return this.count * this.width + (this.count - 1) * this.gutter; } }
        /// <summary>Gets or sets the saved total height. This is supplied layout metadata, never a font measurement.</summary>
        public double TotalHeight { get { return this.totalHeight; } set { NonNegative(value, nameof(value)); this.totalHeight = value; } }
        /// <summary>Gets or sets the original embedded total width. Editing Count, Width or Gutter clears this cache.</summary>
        public double? StoredTotalWidth { get; set; }
        /// <summary>Gets or sets an embedded duplicate text direction. Null writes the current entity direction.</summary>
        public Vector3? EmbeddedTextDirection { get; set; }
        /// <summary>Gets or sets an embedded duplicate insertion point. Null writes the current entity position.</summary>
        public Vector3? EmbeddedInsertionPoint { get; set; }
        /// <summary>Gets or sets the embedded duplicate reference width. Null writes the current entity reference width.</summary>
        public double? EmbeddedReferenceWidth { get; set; }
        /// <summary>Gets the per-column heights for dynamic columns without automatic height.</summary>
        public IList<double> Heights { get { return this.heights; } }
        /// <summary>Gets the legacy second and subsequent MTEXT entities, in reading order.</summary>
        public IList<MText> LinkedColumns { get { return this.linkedColumns; } }

        /// <summary>Validates metadata and rejects invalid counts, nonfinite dimensions, or nested column graphs.</summary>
        public void Validate()
        {
            Positive(this.TotalWidth, nameof(TotalWidth));
            if (this.StoredTotalWidth.HasValue)
            {
                NonNegative(this.StoredTotalWidth.Value, nameof(StoredTotalWidth));
                if (this.Type == MTextColumnType.Dynamic && this.AutoHeight &&
                    Math.Abs((this.StoredTotalWidth.Value + this.Gutter) / (this.Width + this.Gutter) - this.Count) > 1e-5)
                    throw new InvalidOperationException("The stored total width must encode Count for automatic embedded columns.");
            }
            if (this.EmbeddedReferenceWidth.HasValue) NonNegative(this.EmbeddedReferenceWidth.Value, nameof(EmbeddedReferenceWidth));
            if (this.EmbeddedTextDirection.HasValue)
            {
                Vector3 direction = this.EmbeddedTextDirection.Value;
                ValidateVector(direction);
                if (direction.X == 0 && direction.Y == 0 && direction.Z == 0)
                    throw new ArgumentOutOfRangeException(nameof(EmbeddedTextDirection), "An embedded text direction must be nonzero.");
            }
            if (this.EmbeddedInsertionPoint.HasValue) ValidateVector(this.EmbeddedInsertionPoint.Value);
            if (this.Type == MTextColumnType.None && (this.Count != 1 || this.AutoHeight || this.heights.Count != 0 || this.linkedColumns.Count != 0))
                throw new InvalidOperationException("No-column MTEXT must have one column and no height list or links.");
            if (this.Type != MTextColumnType.Dynamic && this.AutoHeight)
                throw new InvalidOperationException("Automatic height is only valid for dynamic columns.");
            bool manual = this.Type == MTextColumnType.Dynamic && !this.AutoHeight;
            if (manual && this.heights.Count != this.Count)
                throw new InvalidOperationException("Manual dynamic MTEXT requires one height for every column.");
            if (!manual && this.heights.Count != 0)
                throw new InvalidOperationException("Only manual dynamic MTEXT stores individual column heights.");
            for (int i = 0; i < this.heights.Count; i++)
            {
                NonNegative(this.heights[i], "Heights");
                if (i < this.heights.Count - 1) Positive(this.heights[i], "Heights");
            }
            if (manual && this.DefinedHeight != 0)
                throw new InvalidOperationException("Manual dynamic columns use individual heights, not DefinedHeight.");
            if (this.Storage != MTextColumnStorage.LegacyLinked && this.linkedColumns.Count != 0)
                throw new InvalidOperationException("Only the legacy column representation supports linked MTEXT entities.");
            if (this.Storage == MTextColumnStorage.LegacyLinked && this.linkedColumns.Count != this.Count - 1)
                throw new InvalidOperationException("Legacy MTEXT must have exactly Count minus one linked entities.");
            var unique = new HashSet<MText>();
            foreach (MText linked in this.linkedColumns)
            {
                if (linked == null || !unique.Add(linked) || linked.Columns != null)
                    throw new InvalidOperationException("Linked MTEXT columns must be distinct non-null entities without column definitions.");
            }
        }

        private static void ValidateVector(Vector3 vector)
        {
            if (double.IsNaN(vector.X) || double.IsInfinity(vector.X) || double.IsNaN(vector.Y) || double.IsInfinity(vector.Y) || double.IsNaN(vector.Z) || double.IsInfinity(vector.Z))
                throw new ArgumentOutOfRangeException(nameof(vector), "Embedded coordinates must be finite.");
        }
        /// <summary>Uses the current MTEXT placement and reference width when writing the embedded duplicate fields.</summary>
        public void ResetEmbeddedPlacement()
        {
            this.EmbeddedTextDirection = null; this.EmbeddedInsertionPoint = null; this.EmbeddedReferenceWidth = null;
        }

        internal static void NonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name, "A finite nonnegative dimension is required.");
        }
        internal static void Positive(double value, string name)
        {
            NonNegative(value, name);
            if (value == 0) throw new ArgumentOutOfRangeException(name, "A positive dimension is required.");
        }
        internal void Scale(double scale)
        {
            this.Width *= scale; this.Gutter *= scale; this.DefinedHeight *= scale; this.TotalHeight *= scale;
            for (int i = 0; i < this.heights.Count; i++) this.heights[i] *= scale;
        }
        internal MTextColumns CloneMetadata()
        {
            var result = new MTextColumns { Type = this.Type, Storage = this.Storage, Count = this.Count,
                AutoHeight = this.AutoHeight, FlowReversed = this.FlowReversed, Width = this.Width,
                Gutter = this.Gutter, DefinedHeight = this.DefinedHeight, TotalHeight = this.TotalHeight };
            result.StoredTotalWidth = this.StoredTotalWidth;
            result.EmbeddedTextDirection = this.EmbeddedTextDirection;
            result.EmbeddedInsertionPoint = this.EmbeddedInsertionPoint;
            result.EmbeddedReferenceWidth = this.EmbeddedReferenceWidth;
            result.heights.AddRange(this.heights);
            return result;
        }
        /// <summary>Creates independent metadata and detached copies of all linked columns.</summary>
        public object Clone()
        {
            this.Validate();
            if (this.PendingHandles.Count != 0) throw new InvalidOperationException("Unresolved MTEXT column handles cannot be cloned.");
            MTextColumns result = this.CloneMetadata();
            foreach (MText linked in this.linkedColumns) result.linkedColumns.Add((MText)linked.Clone());
            return result;
        }
    }

    public partial class MText
    {
        /// <summary>Gets or sets the optional typed column definition.</summary>
        public MTextColumns Columns { get; set; }
        /// <summary>Gets or sets the defined height on a standalone or linked MTEXT. Null means absent.</summary>
        public double? DefinedHeight
        {
            get { return this.definedColumnHeight; }
            set { if (value.HasValue) MTextColumns.NonNegative(value.Value, nameof(value)); this.definedColumnHeight = value; }
        }
        private double? definedColumnHeight;

        /// <summary>Creates a detached 2018 column entity, combining the exact linked text strings in reading order.</summary>
        /// <remarks>No column break or paragraph characters are invented. This changes the storage model,
        /// not the text layout. Remove the old linked entities explicitly when replacing them in a document.</remarks>
        public MText ConvertToEmbeddedColumns()
        {
            if (this.Columns == null) throw new InvalidOperationException("The MTEXT has no column definition.");
            this.Columns.Validate();
            if (this.Columns.Storage == MTextColumnStorage.LegacyLinked && this.Columns.LinkedColumns.Count != this.Columns.Count - 1)
                throw new InvalidOperationException("All legacy columns must be resolved before conversion.");
            foreach (MText linked in this.Columns.LinkedColumns)
                if (!this.HasSameColumnFormatting(linked))
                    throw new NotSupportedException("Embedded MTEXT cannot retain distinct entity formatting on linked columns. Normalize the linked formatting explicitly before conversion.");
            MText result = (MText)this.Clone();
            string combined = this.Value;
            foreach (MText linked in this.Columns.LinkedColumns) combined += linked.Value;
            result.Value = combined;
            result.Columns.LinkedColumns.Clear();
            result.Columns.Storage = MTextColumnStorage.Embedded;
            if (this.Columns.Storage != MTextColumnStorage.Embedded) result.Columns.ResetEmbeddedPlacement();
            return result;
        }

        /// <summary>Creates detached legacy columns from explicit text partitions, first column first.</summary>
        /// <param name="columnTexts">Exactly Count text strings whose concatenation equals the entire source text.</param>
        /// <returns>The main MTEXT and all linked columns. Add every returned entity to the same block.</returns>
        /// <remarks>Text partitioning requires a renderer or caller knowledge. This method does not guess line wrapping.</remarks>
        public IReadOnlyList<MText> ConvertToLinkedColumns(IEnumerable<string> columnTexts)
        {
            if (columnTexts == null) throw new ArgumentNullException(nameof(columnTexts));
            if (this.Columns == null) throw new InvalidOperationException("The MTEXT has no column definition.");
            this.Columns.Validate();
            var parts = new List<string>(columnTexts);
            if (parts.Count != this.Columns.Count || parts.Exists(value => value == null))
                throw new ArgumentException("Supply one non-null text partition for each column.", nameof(columnTexts));
            string source = this.Value;
            foreach (MText linked in this.Columns.LinkedColumns) source += linked.Value;
            if (string.Concat(parts) != source) throw new ArgumentException("Column partitions must preserve the exact source text.", nameof(columnTexts));
            MText main = (MText)this.Clone();
            main.Columns.LinkedColumns.Clear(); main.Columns.Storage = MTextColumnStorage.LegacyLinked;
            main.Value = parts[0]; main.RectangleWidth = main.Columns.Width;
            main.DefinedHeight = main.Columns.Type == MTextColumnType.Dynamic && !main.Columns.AutoHeight ? (double?)null : main.Columns.DefinedHeight;
            main.Columns.ResetEmbeddedPlacement();
            var result = new List<MText> { main };
            Vector3 direction = MathHelper.Transform(new Vector3(Math.Cos(this.Rotation * MathHelper.DegToRad),
                Math.Sin(this.Rotation * MathHelper.DegToRad), 0), this.Normal, CoordinateSystem.Object, CoordinateSystem.World);
            double advance = main.Columns.Width + main.Columns.Gutter;
            if (main.Columns.FlowReversed) advance = -advance;
            for (int i = 1; i < parts.Count; i++)
            {
                MText column = (MText)this.Clone(); column.Columns = null; column.Value = parts[i];
                column.Position = this.Position + direction * (advance * i); column.RectangleWidth = main.Columns.Width;
                column.DefinedHeight = main.DefinedHeight;
                main.Columns.LinkedColumns.Add(column); result.Add(column);
            }
            return new ReadOnlyCollection<MText>(result);
        }

        private bool HasSameColumnFormatting(MText other)
        {
            MTextBackgroundFill a = this.BackgroundFill, b = other.BackgroundFill;
            bool sameBackground = a == null && b == null || a != null && b != null && a.Flags == b.Flags &&
                a.ScaleFactor == b.ScaleFactor && a.ColorIndex == b.ColorIndex && a.TrueColor == b.TrueColor &&
                a.ColorName == b.ColorName && a.Transparency == b.Transparency;
            return sameBackground && this.Style.Name == other.Style.Name && this.Height == other.Height &&
                this.Rotation == other.Rotation && this.Normal == other.Normal && this.AttachmentPoint == other.AttachmentPoint &&
                this.LineSpacingStyle == other.LineSpacingStyle && this.LineSpacingFactor == other.LineSpacingFactor &&
                this.DrawingDirection == other.DrawingDirection && this.Layer.Name == other.Layer.Name &&
                this.Color.Index == other.Color.Index && this.Color.UseTrueColor == other.Color.UseTrueColor &&
                AciColor.ToTrueColor(this.Color) == AciColor.ToTrueColor(other.Color) &&
                this.Linetype.Name == other.Linetype.Name && this.Lineweight == other.Lineweight &&
                this.LinetypeScale == other.LinetypeScale && this.IsVisible == other.IsVisible &&
                this.Transparency.Value == other.Transparency.Value;
        }

        internal static void RelinkClonedColumns(IDictionary<EntityObject, EntityObject> copies)
        {
            foreach (KeyValuePair<EntityObject, EntityObject> pair in copies)
            {
                MText source = pair.Key as MText;
                if (source == null || source.Columns == null || source.Columns.LinkedColumns.Count == 0) continue;
                MText target = (MText)pair.Value;
                target.Columns.LinkedColumns.Clear();
                foreach (MText linked in source.Columns.LinkedColumns)
                {
                    EntityObject copied;
                    if (!copies.TryGetValue(linked, out copied))
                        throw new InvalidOperationException("Cloning a block requires all linked MTEXT columns to belong to that block.");
                    target.Columns.LinkedColumns.Add((MText)copied);
                }
            }
        }

        private void ValidateColumnTransform(Matrix3 transformation)
        {
            if (this.Columns == null) return;
            this.Columns.Validate();
            if (this.Columns.Storage == MTextColumnStorage.LegacyLinked && this.Columns.LinkedColumns.Count != 0)
                throw new NotSupportedException("Transforming one legacy MTEXT would leave linked columns behind. Convert explicitly to an embedded entity first, or transform detached columns individually.");
            Vector3 x = transformation * Vector3.UnitX, y = transformation * Vector3.UnitY, z = transformation * Vector3.UnitZ;
            double scale = x.Modulus();
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= MathHelper.Epsilon ||
                !MathHelper.IsEqual(scale, y.Modulus()) || !MathHelper.IsEqual(scale, z.Modulus()) ||
                !MathHelper.IsZero(Vector3.DotProduct(x, y)) || !MathHelper.IsZero(Vector3.DotProduct(x, z)) || !MathHelper.IsZero(Vector3.DotProduct(y, z)) ||
                Vector3.DotProduct(Vector3.CrossProduct(x, y), z) < 0)
                throw new NotSupportedException("Column MTEXT supports translation, rotation and positive uniform scaling. Shear, reflection, singular and nonuniform transforms require explicit layout conversion.");
            MTextColumns trial = this.Columns.CloneMetadata(); trial.Scale(scale); trial.Validate();
        }
    }
}
