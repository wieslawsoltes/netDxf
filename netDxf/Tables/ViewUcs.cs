using System;
using netDxf.Collections;

namespace netDxf.Tables
{
    /// <summary>The stored UCS associated with a named VIEW, available in DXF 2000 and later.</summary>
    /// <remarks>These values are stored independently of a named UCS. They do not activate a coordinate system.
    /// Clone copies the value bundle and retains referenced UCS identities; explicitly map them before adding the view to another document.</remarks>
    public sealed class ViewUcs : ICloneable
    {
        private Vector3 origin, xAxis = Vector3.UnitX, yAxis = Vector3.UnitY;
        private double elevation;
        private short orthographicType;
        private UCS namedUcs, baseUcs;
        internal View View { get; set; }

        /// <summary>Gets or sets the finite UCS origin in WCS (110/120/130).</summary>
        public Vector3 Origin { get { return this.origin; } set { ValidateVector(value, false); this.origin = value; } }
        /// <summary>Gets or sets the nonzero UCS X direction (111/121/131), without normalization.</summary>
        public Vector3 XAxis { get { return this.xAxis; } set { ValidateVector(value, true); this.xAxis = value; } }
        /// <summary>Gets or sets the nonzero UCS Y direction (112/122/132), without normalization.</summary>
        public Vector3 YAxis { get { return this.yAxis; } set { ValidateVector(value, true); this.yAxis = value; } }
        /// <summary>Gets or sets the orthographic type: zero for nonorthographic, one through six for Top through Right (79).</summary>
        public short OrthographicType { get { return this.orthographicType; } set { ValidateOrthographicType(value); this.orthographicType = value; } }
        /// <summary>Gets or sets the finite stored UCS elevation (146).</summary>
        public double Elevation { get { return this.elevation; } set { ValidateFinite(value); this.elevation = value; } }
        /// <summary>Gets or sets the named UCS reference (345). Null denotes an unnamed UCS.</summary>
        public UCS NamedUcs { get { return this.namedUcs; } set { UcsReferences.Replace(this.View, this.namedUcs, value); this.namedUcs = value; } }
        /// <summary>Gets or sets the orthographic base UCS reference (346). Null uses WORLD for a nonzero orthographic type.</summary>
        public UCS BaseUcs { get { return this.baseUcs; } set { UcsReferences.Replace(this.View, this.baseUcs, value); this.baseUcs = value; } }

        internal void Validate()
        {
            if (this.BaseUcs != null && this.OrthographicType == 0)
                throw new InvalidOperationException("A VIEW base UCS requires a nonzero orthographic type.");
        }
        internal static void ValidateFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "A finite UCS value is required.");
        }
        internal static void ValidateVector(Vector3 value, bool direction)
        {
            ValidateFinite(value.X); ValidateFinite(value.Y); ValidateFinite(value.Z);
            if (direction && value.X == 0 && value.Y == 0 && value.Z == 0) throw new ArgumentException("A UCS axis cannot be zero.", nameof(value));
        }
        internal static void ValidateOrthographicType(short value)
        {
            if (value < 0 || value > 6) throw new ArgumentOutOfRangeException(nameof(value), "The orthographic type must be between zero and six.");
        }
        /// <summary>Copies all values and retains the named/base UCS references without acquiring a view owner.</summary>
        public object Clone()
        {
            return new ViewUcs { Origin = this.Origin, XAxis = this.XAxis, YAxis = this.YAxis,
                OrthographicType = this.OrthographicType, Elevation = this.Elevation, NamedUcs = this.NamedUcs, BaseUcs = this.BaseUcs };
        }
    }

    public partial class View
    {
        private ViewUcs ucs;
        /// <summary>Commits collection indexes only after all rename observers accept the name.</summary>
        protected override void OnNameChangedEvent(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("A view name cannot be blank.", nameof(newName));
            if (this.Owner != null) this.Owner.ValidateRecordRename(this, newName);
            base.OnNameChangedEvent(oldName, newName);
            if (this.Owner != null)
            {
                this.Owner.ValidateRecordRename(this, newName);
                this.Owner.CommitRecordRename(this, newName);
            }
        }
        /// <summary>Gets or sets the UCS restored with this named view. Null writes group 72 as zero and no UCS bundle.</summary>
        /// <remarks>A bundle belongs to one view; clone it to attach an independent copy to another view.
        /// Referenced UCS records must already be registered in the view's document.</remarks>
        public ViewUcs Ucs
        {
            get { return this.ucs; }
            set
            {
                if (ReferenceEquals(this.ucs, value)) return;
                if (value != null)
                {
                    if (value.View != null && !ReferenceEquals(value.View, this)) throw new ArgumentException("The UCS bundle already belongs to another view.", nameof(value));
                    UcsReferences.Check(this, value.NamedUcs); UcsReferences.Check(this, value.BaseUcs);
                }
                UcsReferences.Unregister(this);
                if (this.ucs != null) this.ucs.View = null;
                this.ucs = value;
                if (value != null) value.View = this;
                UcsReferences.Register(this);
            }
        }
    }
}
