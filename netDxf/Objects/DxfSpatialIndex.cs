using System;

namespace netDxf.Objects
{
    /// <summary>A stored SPATIAL_INDEX envelope with its public AcDbIndex timestamp.</summary>
    /// <remarks>The AcDbSpatialIndex subclass is empty in DXF. This object does not build or evaluate a spatial index.</remarks>
    public sealed class DxfSpatialIndex : DxfDatabaseObject
    {
        private double timestamp;
        /// <summary>Creates a detached index with a zero timestamp.</summary>
        public DxfSpatialIndex() : base("SPATIAL_INDEX") { }
        /// <summary>Gets or sets the finite group-40 Julian-date value exactly as stored, without calendar conversion.</summary>
        public double Timestamp
        {
            get { return this.timestamp; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "The stored timestamp must be finite.");
                this.timestamp = value;
            }
        }
        internal override DxfDatabaseObject CloneShell() { return new DxfSpatialIndex { Timestamp = this.Timestamp }; }
    }
}
