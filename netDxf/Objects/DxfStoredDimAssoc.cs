// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>An immutable stored AcDbOsnapPointRef with one directly registered geometry identity.</summary>
    /// <remarks>Values describe the source packet; no snapping or dimension evaluation is performed.</remarks>
    public sealed class DxfStoredDimAssocPoint
    {
        internal DxfStoredDimAssocPoint(int slot, short osnap, string handle, short subentity, int marker, double parameter, Vector3 point)
        { this.PointIndex = slot; this.OsnapType = osnap; this.GeometryHandle = handle; this.SubentityType = subentity; this.MarkerIndex = marker; this.NearParameter = parameter; this.Point = point; }
        /// <summary>Gets the zero-based point index selected by the association mask.</summary>
        public int PointIndex { get; }
        /// <summary>Gets the stored group-72 osnap value (1, 3 or 13 in this qualified variant).</summary>
        public short OsnapType { get; }
        /// <summary>Gets the exact registered geometry entity referenced by group 331.</summary>
        public EntityObject Geometry { get; private set; }
        /// <summary>Gets the stored group-73 subentity type without evaluating it.</summary>
        public short SubentityType { get; }
        /// <summary>Gets the stored group-91 graphics marker index.</summary>
        public int MarkerIndex { get; }
        /// <summary>Gets the finite stored group-40 parameter.</summary>
        public double NearParameter { get; }
        /// <summary>Gets the finite stored WCS point, including any source sentinel coordinates.</summary>
        public Vector3 Point { get; }
        internal string GeometryHandle { get; }
        internal void Bind(EntityObject geometry) { this.Geometry = geometry; }
    }

    /// <summary>A loaded, immutable DIMASSOC with qualified direct geometry references.</summary>
    /// <remarks>
    /// The object remains in its source document and source version. Its native dimension extension ownership
    /// and association reactor backlinks are retained. Editing, cloning, erasure and dimension evaluation require
    /// the complete association lifecycle and are not supported. Unqualified point-reference variants remain opaque.
    /// </remarks>
    public sealed class DxfStoredDimAssoc : DxfDatabaseObject
    {
        private readonly DxfDocument source;
        private readonly string dimensionHandle;
        private DxfDictionary sourceOwner;
        private DxfObject[] sourceReactors;
        private readonly Dictionary<EntityObject, bool> backlinks = new Dictionary<EntityObject, bool>();
        private readonly List<DxfObject> references = new List<DxfObject>();
        private bool resolved;

        internal DxfStoredDimAssoc(DxfDocument source, IList<DxfTag> tags, string dimensionHandle,
            int mask, short transSpace, short rotatedType, IList<DxfStoredDimAssocPoint> points) : base("DIMASSOC")
        {
            this.source = source; this.SourceVersion = source.DrawingVariables.AcadVer;
            this.dimensionHandle = dimensionHandle; this.AssociativityMask = mask;
            this.IsTransSpace = transSpace != 0; this.RotatedDimensionType = rotatedType;
            this.Tags = new List<DxfTag>(tags).AsReadOnly(); this.PointReferences = new List<DxfStoredDimAssocPoint>(points).AsReadOnly();
        }
        /// <summary>Gets the source profile; conversion to another version is not qualified.</summary>
        public DxfVersion SourceVersion { get; }
        /// <summary>Gets the exact immutable subclass packet, excluding common metadata and XData.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
        /// <summary>Gets the dimension referenced by the subclass group 330.</summary>
        public Dimension Dimension { get; private set; }
        /// <summary>Gets the stored low-four-bit group-90 association mask.</summary>
        public int AssociativityMask { get; }
        /// <summary>Gets the stored group-70 trans-space flag.</summary>
        public bool IsTransSpace { get; }
        /// <summary>Gets the stored group-71 rotated-dimension type.</summary>
        public short RotatedDimensionType { get; }
        /// <summary>Gets the ordered point references corresponding to the set association bits.</summary>
        public IReadOnlyList<DxfStoredDimAssocPoint> PointReferences { get; }
        /// <summary>Gets exact registered dimension and geometry dependencies in packet order.</summary>
        public IReadOnlyList<DxfObject> References { get { return this.references.AsReadOnly(); } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.references; } }
        internal override IEnumerable<DxfTag> AllocationReservations { get { return this.Tags; } }
        internal override DxfDatabaseObject CloneShell() { throw new NotSupportedException("Stored DIMASSOC cloning requires the complete dimension association lifecycle."); }

        internal void Resolve(Func<string, DxfObject> resolve)
        {
            this.Dimension = resolve(this.dimensionHandle) as Dimension;
            if (this.Dimension == null) throw new FormatException("DIMASSOC requires an exact source DIMENSION identity: " + this.dimensionHandle);
            this.references.Add(this.Dimension);
            foreach (DxfStoredDimAssocPoint point in this.PointReferences)
            {
                var geometry = resolve(point.GeometryHandle) as EntityObject;
                if (geometry == null) throw new FormatException("DIMASSOC requires an exact source geometry entity: " + point.GeometryHandle);
                point.Bind(geometry); this.references.Add(geometry);
            }
            this.sourceOwner = this.Owner as DxfDictionary;
            if (this.sourceOwner == null || !ReferenceEquals(this.sourceOwner.Owner, this.Dimension)
                || !ReferenceEquals(this.Dimension.ExtensionDictionary, this.sourceOwner)
                || !this.sourceOwner.Entries.Any(entry => entry.Name == "ACAD_DIMASSOC" && entry.IsHardOwner && ReferenceEquals(entry.Target, this)))
                throw new FormatException("DIMASSOC requires its reciprocal DIMENSION extension dictionary and ACAD_DIMASSOC hard-owner entry.");
            this.sourceReactors = this.PersistentReactors.ToArray();
            foreach (EntityObject entity in this.references.OfType<EntityObject>()) this.backlinks[entity] = entity.PersistentReactors.Contains(this);
            this.resolved = true;
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!this.resolved || !ReferenceEquals(database.Document, this.source))
            { errors.Add("Stored DIMASSOC must remain in its source document."); return; }
            if (this.source.DrawingVariables.AcadVer != this.SourceVersion) errors.Add("Stored DIMASSOC version conversion requires complete schema regeneration.");
            foreach (DxfObject target in this.references)
                if (!ReferenceEquals(this.source.GetObjectByHandle(target.Handle), target)) errors.Add("A stored DIMASSOC dependency is no longer registered.");
            if (!ReferenceEquals(this.Owner, this.sourceOwner) || !ReferenceEquals(this.sourceOwner.Owner, this.Dimension)
                || !ReferenceEquals(this.Dimension.ExtensionDictionary, this.sourceOwner)
                || !this.sourceOwner.Entries.Any(entry => entry.Name == "ACAD_DIMASSOC" && entry.IsHardOwner && ReferenceEquals(entry.Target, this)))
                errors.Add("Stored DIMASSOC dimension ownership changed.");
            if (!this.PersistentReactors.SequenceEqual(this.sourceReactors)) errors.Add("Stored DIMASSOC persistent reactors changed.");
            foreach (var backlink in this.backlinks)
                if (backlink.Key.PersistentReactors.Contains(this) != backlink.Value) errors.Add("A stored DIMASSOC source reactor backlink changed.");
        }
    }
}
