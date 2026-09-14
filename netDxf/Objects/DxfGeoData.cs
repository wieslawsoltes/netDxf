using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.Units;

namespace netDxf.Objects
{
    /// <summary>The coordinate type declared by a GEODATA object.</summary>
    public enum DxfGeoCoordinateType
    {
        /// <summary>Unspecified coordinates.</summary>
        Unknown = 0,
        /// <summary>Local grid coordinates.</summary>
        LocalGrid = 1,
        /// <summary>Projected grid coordinates.</summary>
        ProjectedGrid = 2,
        /// <summary>Geographic latitude/longitude coordinates.</summary>
        Geographic = 3
    }
    /// <summary>The scale estimation policy stored in GEODATA; this library does not evaluate it.</summary>
    public enum DxfGeoScaleEstimation
    {
        /// <summary>No scale estimation.</summary>
        None = 1,
        /// <summary>Use the declared user scale.</summary>
        UserScale = 2,
        /// <summary>Grid scale at the reference point.</summary>
        GridScale = 3,
        /// <summary>Prismoidal estimation.</summary>
        Prismoidal = 4
    }
    /// <summary>An immutable pair of source and target mesh coordinates.</summary>
    public sealed class DxfGeoMeshPoint
    {
        /// <summary>Creates a finite pair of 2D coordinates.</summary>
        public DxfGeoMeshPoint(Vector2 source, Vector2 target)
        { DxfGeoData.CheckFinite(source, nameof(source)); DxfGeoData.CheckFinite(target, nameof(target)); this.Source = source; this.Target = target; }
        /// <summary>Gets the source mesh coordinate.</summary>
        public Vector2 Source { get; }
        /// <summary>Gets the target mesh coordinate.</summary>
        public Vector2 Target { get; }
    }
    /// <summary>An immutable triangle with zero-based mesh point indices.</summary>
    public sealed class DxfGeoMeshFace
    {
        /// <summary>Creates a face. Upper bounds are checked against the complete mesh during validation.</summary>
        public DxfGeoMeshFace(int first, int second, int third)
        {
            if (first < 0 || second < 0 || third < 0) throw new ArgumentOutOfRangeException(nameof(first), "Mesh indices must be nonnegative.");
            this.First = first; this.Second = second; this.Third = third;
        }
        /// <summary>Gets the first vertex index.</summary>
        public int First { get; }
        /// <summary>Gets the second vertex index.</summary>
        public int Second { get; }
        /// <summary>Gets the third vertex index.</summary>
        public int Third { get; }
    }
    /// <summary>Editable public version-2 GEODATA metadata for DXF 2010 and later.</summary>
    /// <remarks>Coordinate definitions, mesh coordinates and scale policies are stored without interpreting XML, resolving EPSG codes, or applying coordinate transformations.</remarks>
    public sealed class DxfGeoData : DxfDatabaseObject
    {
        private BlockRecord hostBlock;
        private Vector3 designPoint, referencePoint, upDirection = Vector3.UnitZ;
        private Vector2 northDirection = Vector2.UnitY;
        private double horizontalScale = 1, verticalScale = 1, userScale = 1, elevation, radius;
        private DxfGeoCoordinateType coordinateType = DxfGeoCoordinateType.Geographic;
        private DxfGeoScaleEstimation scaleEstimation = DxfGeoScaleEstimation.None;
        private DrawingUnits horizontalUnits = DrawingUnits.Meters, verticalUnits = DrawingUnits.Meters;
        private string definition = string.Empty, rss = string.Empty, from = string.Empty, to = string.Empty, coverage = string.Empty;
        private readonly Collection<DxfGeoMeshPoint> points = new NonNullCollection<DxfGeoMeshPoint>();
        private readonly Collection<DxfGeoMeshFace> faces = new NonNullCollection<DxfGeoMeshFace>();
        /// <summary>Creates detached metadata referring to a host block record.</summary>
        public DxfGeoData(BlockRecord hostBlock) : this() { this.hostBlock = hostBlock ?? throw new ArgumentNullException(nameof(hostBlock)); }
        internal DxfGeoData() : base("GEODATA") { }
        /// <summary>Gets the supported AcDbGeoData implementation version, always 2.</summary>
        public int Version { get { return 2; } }
        /// <summary>Gets the host block record. Graph cloning remaps this reference to its destination host.</summary>
        public BlockRecord HostBlock { get { return this.hostBlock; } }
        internal void SetLoadedHost(BlockRecord host) { this.hostBlock = host ?? throw new FormatException("GEODATA host must resolve to a BLOCK_RECORD."); }
        /// <summary>Gets or sets the declared coordinate type.</summary>
        public DxfGeoCoordinateType CoordinateType { get { return this.coordinateType; } set { if ((int)value < 0 || (int)value > 3) throw new ArgumentOutOfRangeException(nameof(value)); this.coordinateType = value; } }
        /// <summary>Gets or sets the design reference point in WCS.</summary>
        public Vector3 DesignPoint { get { return this.designPoint; } set { CheckFinite(value, nameof(value)); this.designPoint = value; } }
        /// <summary>Gets or sets the reference point in the declared coordinate system.</summary>
        public Vector3 ReferencePoint { get { return this.referencePoint; } set { CheckFinite(value, nameof(value)); this.referencePoint = value; } }
        /// <summary>Gets or sets the finite nonzero up vector; its magnitude is preserved.</summary>
        public Vector3 UpDirection { get { return this.upDirection; } set { CheckFinite(value, nameof(value)); if (value.X == 0 && value.Y == 0 && value.Z == 0) throw new ArgumentOutOfRangeException(nameof(value)); this.upDirection = value; } }
        /// <summary>Gets or sets the finite nonzero 2D north vector; its magnitude is preserved.</summary>
        public Vector2 NorthDirection { get { return this.northDirection; } set { CheckFinite(value, nameof(value)); if (value.X == 0 && value.Y == 0) throw new ArgumentOutOfRangeException(nameof(value)); this.northDirection = value; } }
        /// <summary>Gets or sets the positive factor converting horizontal design units to meters.</summary>
        public double HorizontalUnitScale { get { return this.horizontalScale; } set { CheckPositive(value); this.horizontalScale = value; } }
        /// <summary>Gets or sets the positive factor converting vertical design units to meters.</summary>
        public double VerticalUnitScale { get { return this.verticalScale; } set { CheckPositive(value); this.verticalScale = value; } }
        /// <summary>Gets or sets the horizontal units declaration.</summary>
        public DrawingUnits HorizontalUnits { get { return this.horizontalUnits; } set { CheckUnits(value); this.horizontalUnits = value; } }
        /// <summary>Gets or sets the vertical units declaration.</summary>
        public DrawingUnits VerticalUnits { get { return this.verticalUnits; } set { CheckUnits(value); this.verticalUnits = value; } }
        /// <summary>Gets or sets the declared scale estimation policy.</summary>
        public DxfGeoScaleEstimation ScaleEstimation { get { return this.scaleEstimation; } set { if ((int)value < 1 || (int)value > 4) throw new ArgumentOutOfRangeException(nameof(value)); this.scaleEstimation = value; } }
        /// <summary>Gets or sets the finite positive user scale factor.</summary>
        public double UserScaleFactor { get { return this.userScale; } set { CheckPositive(value); this.userScale = value; } }
        /// <summary>Gets or sets the sea-level correction declaration.</summary>
        public bool SeaLevelCorrection { get; set; }
        /// <summary>Gets or sets the finite sea-level elevation.</summary>
        public double SeaLevelElevation { get { return this.elevation; } set { CheckFinite(value, nameof(value)); this.elevation = value; } }
        /// <summary>Gets or sets a finite nonnegative projection radius.</summary>
        public double CoordinateProjectionRadius { get { return this.radius; } set { CheckFinite(value, nameof(value)); if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.radius = value; } }
        /// <summary>Gets or sets uninterpreted coordinate system text. LF is encoded as ^J; literal ^J and CR are rejected to prevent ambiguous transport.</summary>
        public string CoordinateSystemDefinition { get { return this.definition; } set { CheckText(value, true); this.definition = value; } }
        /// <summary>Gets or sets a single-line GeoRSS tag.</summary>
        public string GeoRssTag { get { return this.rss; } set { CheckText(value, false); this.rss = value; } }
        /// <summary>Gets or sets the single-line observation origin tag.</summary>
        public string ObservationFrom { get { return this.from; } set { CheckText(value, false); this.from = value; } }
        /// <summary>Gets or sets the single-line observation destination tag.</summary>
        public string ObservationTo { get { return this.to; } set { CheckText(value, false); this.to = value; } }
        /// <summary>Gets or sets the single-line observation coverage tag.</summary>
        public string ObservationCoverage { get { return this.coverage; } set { CheckText(value, false); this.coverage = value; } }
        /// <summary>Gets editable paired mesh points. Changing the point count can invalidate existing faces until repaired.</summary>
        public Collection<DxfGeoMeshPoint> MeshPoints { get { return this.points; } }
        /// <summary>Gets editable mesh faces, checked against the point count before writing or cloning.</summary>
        public Collection<DxfGeoMeshFace> MeshFaces { get { return this.faces; } }
        /// <summary>Atomically replaces the complete mesh after enumerating and validating both sequences.</summary>
        public void SetMesh(IEnumerable<DxfGeoMeshPoint> meshPoints, IEnumerable<DxfGeoMeshFace> meshFaces)
        {
            if (meshPoints == null || meshFaces == null) throw new ArgumentNullException(meshPoints == null ? nameof(meshPoints) : nameof(meshFaces));
            var newPoints = meshPoints.ToList(); var newFaces = meshFaces.ToList();
            if (newPoints.Any(p => p == null) || newFaces.Any(f => f == null)) throw new ArgumentException("Mesh members cannot be null.");
            foreach (DxfGeoMeshFace face in newFaces) if (face.First >= newPoints.Count || face.Second >= newPoints.Count || face.Third >= newPoints.Count) throw new ArgumentException("A mesh face index exceeds the point count.", nameof(meshFaces));
            this.points.Clear(); this.faces.Clear();
            foreach (DxfGeoMeshPoint point in newPoints) this.points.Add(point);
            foreach (DxfGeoMeshFace face in newFaces) this.faces.Add(face);
        }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { yield return this.hostBlock; } }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        { ((DxfGeoData)clone).SetLoadedHost(resolve(this.hostBlock) as BlockRecord); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            this.ValidateValues(database, errors);
            DxfDictionary owner = this.Owner as DxfDictionary;
            if (owner == null || owner.Owner != this.hostBlock || !owner.Contains("ACAD_GEOGRAPHICDATA") || owner["ACAD_GEOGRAPHICDATA"] != this)
                errors.Add("GEODATA must be stored under its host block extension dictionary's ACAD_GEOGRAPHICDATA entry.");
            if (this.Database != null && this.hostBlock?.ExtensionDictionary != owner) errors.Add("GEODATA host extension dictionary is not attached.");
        }
        internal void ValidateValues(DxfObjectDatabase database, List<string> errors)
        {
            if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2010) errors.Add("Typed GEODATA version 2 requires DXF 2010 or later.");
            if (this.hostBlock == null) errors.Add("GEODATA has no host block record.");
            foreach (DxfGeoMeshFace face in this.faces)
                if (face.First >= this.points.Count || face.Second >= this.points.Count || face.Third >= this.points.Count) errors.Add("GEODATA mesh face index is outside the point array.");
        }
        internal override DxfDatabaseObject CloneShell()
        {
            var copy = new DxfGeoData { CoordinateType = this.CoordinateType, DesignPoint = this.DesignPoint, ReferencePoint = this.ReferencePoint, UpDirection = this.UpDirection, NorthDirection = this.NorthDirection,
                HorizontalUnitScale = this.HorizontalUnitScale, VerticalUnitScale = this.VerticalUnitScale, HorizontalUnits = this.HorizontalUnits, VerticalUnits = this.VerticalUnits, ScaleEstimation = this.ScaleEstimation,
                UserScaleFactor = this.UserScaleFactor, SeaLevelCorrection = this.SeaLevelCorrection, SeaLevelElevation = this.SeaLevelElevation, CoordinateProjectionRadius = this.CoordinateProjectionRadius,
                CoordinateSystemDefinition = this.CoordinateSystemDefinition, GeoRssTag = this.GeoRssTag, ObservationFrom = this.ObservationFrom, ObservationTo = this.ObservationTo, ObservationCoverage = this.ObservationCoverage };
            copy.SetMesh(this.points, this.faces); return copy;
        }
        internal static void CheckFinite(double value, string name) { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "A finite value is required."); }
        internal static void CheckFinite(Vector2 value, string name) { CheckFinite(value.X, name); CheckFinite(value.Y, name); }
        internal static void CheckFinite(Vector3 value, string name) { CheckFinite(value.X, name); CheckFinite(value.Y, name); CheckFinite(value.Z, name); }
        private static void CheckPositive(double value) { CheckFinite(value, nameof(value)); if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static void CheckUnits(DrawingUnits value) { if ((int)value < 0 || (int)value > 24) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static void CheckText(string value, bool definition)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.IndexOf('\0') >= 0 || value.IndexOf('\r') >= 0 || (!definition && value.IndexOf('\n') >= 0) || (definition && value.Contains("^J")))
                throw new ArgumentException("The string contains an unsupported transport control sequence.", nameof(value));
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) throw new ArgumentException("The string contains an unpaired UTF-16 surrogate.", nameof(value));
                    i++;
                }
                else if (char.IsLowSurrogate(value[i])) throw new ArgumentException("The string contains an unpaired UTF-16 surrogate.", nameof(value));
            }
        }
        private sealed class NonNullCollection<T> : Collection<T> where T : class
        {
            protected override void InsertItem(int index, T item) { if (item == null) throw new ArgumentNullException(nameof(item)); base.InsertItem(index, item); }
            protected override void SetItem(int index, T item) { if (item == null) throw new ArgumentNullException(nameof(item)); base.SetItem(index, item); }
        }
    }
}
