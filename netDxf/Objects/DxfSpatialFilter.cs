using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Entities;

namespace netDxf.Objects
{
    /// <summary>The public SPATIAL_FILTER schema for a block reference's clipping boundary.</summary>
    /// <remarks>This models the stored filter definition; it does not clip or regenerate block geometry or interpret private inverted-XCLIP records.</remarks>
    public sealed class DxfSpatialFilter : DxfDatabaseObject
    {
        private Vector2[] boundary = { new Vector2(-1, -1), new Vector2(1, 1) };
        private Vector3 normal = Vector3.UnitZ;
        private Vector3 origin;
        private double? front;
        private double? back;
        private Matrix4 inverse = Matrix4.Identity;
        private Matrix4 transform = Matrix4.Identity;
        /// <summary>Creates a filter with an enabled two-corner rectangle and identity transforms.</summary>
        public DxfSpatialFilter() : base("SPATIAL_FILTER") { }
        /// <summary>Gets an immutable snapshot of the ordered OCS boundary vertices. Two vertices describe opposite rectangle corners.</summary>
        public IReadOnlyList<Vector2> Boundary { get { return new ReadOnlyCollection<Vector2>((Vector2[])this.boundary.Clone()); } }
        /// <summary>Replaces the boundary after validating and copying the entire input sequence.</summary>
        public void SetBoundary(IEnumerable<Vector2> vertices)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            List<Vector2> copy = new List<Vector2>();
            foreach (Vector2 vertex in vertices)
            {
                Finite(vertex.X); Finite(vertex.Y);
                if (copy.Count == short.MaxValue) throw new ArgumentException("A spatial boundary cannot exceed 32767 vertices.", nameof(vertices));
                copy.Add(vertex);
            }
            if (copy.Count < 2) throw new ArgumentException("A spatial boundary needs at least two vertices.", nameof(vertices));
            this.boundary = copy.ToArray();
        }
        /// <summary>Gets or sets the nonzero OCS normal; its magnitude is retained.</summary>
        public Vector3 Normal
        {
            get { return this.normal; }
            set { CheckVector(value); if (value.X == 0 && value.Y == 0 && value.Z == 0) throw new ArgumentException("The spatial normal cannot be zero.", nameof(value)); this.normal = value; }
        }
        /// <summary>Gets or sets the OCS origin.</summary>
        public Vector3 Origin { get { return this.origin; } set { CheckVector(value); this.origin = value; } }
        /// <summary>Gets or sets the clipping-enabled flag.</summary>
        public bool IsClippingEnabled { get; set; } = true;
        /// <summary>Gets or sets the front plane distance, or null when no front plane is enabled.</summary>
        public double? FrontClippingDistance { get { return this.front; } set { if (value.HasValue) Finite(value.Value); this.front = value; } }
        /// <summary>Gets or sets the back plane distance, or null when no back plane is enabled.</summary>
        public double? BackClippingDistance { get { return this.back; } set { if (value.HasValue) Finite(value.Value); this.back = value; } }
        /// <summary>Gets or sets the stored inverse original INSERT transformation, using netDxf's column-vector matrix convention.</summary>
        public Matrix4 InverseInsertTransform { get { return this.inverse; } set { CheckMatrix(value); this.inverse = value; } }
        /// <summary>Gets or sets the transformation into boundary coordinates, using netDxf's column-vector convention.</summary>
        public Matrix4 ClipBoundaryTransform { get { return this.transform; } set { CheckMatrix(value); this.transform = value; } }
        internal static void Finite(double value) { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "Spatial filter values must be finite."); }
        private static void CheckVector(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static void CheckMatrix(Matrix4 value)
        {
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) Finite(value[row, col]);
            if (value.M41 != 0 || value.M42 != 0 || value.M43 != 0 || value.M44 != 1) throw new ArgumentException("Spatial filter matrices must be affine (last row 0,0,0,1).", nameof(value));
        }
        internal override DxfDatabaseObject CloneShell()
        {
            DxfSpatialFilter copy = new DxfSpatialFilter { Normal = this.Normal, Origin = this.Origin, IsClippingEnabled = this.IsClippingEnabled, FrontClippingDistance = this.FrontClippingDistance, BackClippingDistance = this.BackClippingDistance, InverseInsertTransform = this.InverseInsertTransform, ClipBoundaryTransform = this.ClipBoundaryTransform };
            copy.SetBoundary(this.boundary); return copy;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            DxfDictionary filters = this.Owner as DxfDictionary;
            DxfDictionary extension = filters?.Owner as DxfDictionary;
            Insert insert = extension?.Owner as Insert;
            if (insert == null || extension.Database != null && insert.ExtensionDictionary != extension || !extension.Entries.Any(e => e.Name.Equals("ACAD_FILTER", StringComparison.OrdinalIgnoreCase) && e.Target == filters) || !filters.Entries.Any(e => e.Name.Equals("SPATIAL", StringComparison.OrdinalIgnoreCase) && e.Target == this))
                errors.Add("SPATIAL_FILTER must occupy ACAD_FILTER/SPATIAL in an INSERT extension dictionary.");
        }
    }
}
