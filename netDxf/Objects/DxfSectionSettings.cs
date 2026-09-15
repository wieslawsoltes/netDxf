using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Blocks;

namespace netDxf.Objects
{
    /// <summary>Stored geometry-generation appearance values; no geometry is generated or evaluated.</summary>
    public sealed class DxfSectionGeometrySettings
    {
        private short colorCode = 63, colorIndex = 256, faceTransparency, edgeTransparency, hatchPatternType;
        private double linetypeScale = 1, hatchAngle, hatchScale = 1, hatchSpacing = 1;
        private string layerName = "0", linetypeName = "ByLayer", plotStyleName = "ByColor", hatchPatternName = "";
        /// <summary>Gets or sets the explicit group-90 integer without deriving it from a list.</summary>
        public int SectionType { get; set; }
        /// <summary>Gets or sets the explicit group-91 integer, whose native values can be 1, 2, 4 or 8.</summary>
        /// <remarks>The published label is geometry count. This value is preserved, not interpreted as a structural list length or evaluated flag.</remarks>
        public int GeometryValue { get; set; }
        /// <summary>Gets or sets the stored group-92 bits, including unrecognized bits.</summary>
        public int Flags { get; set; }
        /// <summary>Gets or sets the indexed-color spelling: documented 63 or observed native 62.</summary>
        public short ColorCode { get { return this.colorCode; } set { if (value != 62 && value != 63) throw new ArgumentOutOfRangeException(nameof(value)); this.colorCode = value; } }
        /// <summary>Gets or sets indexed color 0 through 256, retaining ByBlock and ByLayer.</summary>
        public short ColorIndex { get { return this.colorIndex; } set { if (value < 0 || value > 256) throw new ArgumentOutOfRangeException(nameof(value)); this.colorIndex = value; } }
        /// <summary>Gets or sets the independent stored layer name, including unresolved reserved names.</summary>
        public string LayerName { get { return this.layerName; } set { this.layerName = DxfSectionSettings.StoredText(value, nameof(value)); } }
        /// <summary>Gets or sets the independent stored linetype name.</summary>
        public string LinetypeName { get { return this.linetypeName; } set { this.linetypeName = DxfSectionSettings.StoredText(value, nameof(value)); } }
        /// <summary>Gets or sets finite stored group-40 linetype scale.</summary>
        public double LinetypeScale { get { return this.linetypeScale; } set { this.linetypeScale = DxfSectionSettings.Finite(value); } }
        /// <summary>Gets or sets the inert stored plot-style name.</summary>
        public string PlotStyleName { get { return this.plotStyleName; } set { this.plotStyleName = DxfSectionSettings.StoredText(value, nameof(value)); } }
        /// <summary>Gets or sets the raw group-370 lineweight value.</summary>
        public short Lineweight { get; set; } = -1;
        /// <summary>Gets or sets stored face transparency from zero through 100.</summary>
        public short FaceTransparency { get { return this.faceTransparency; } set { this.faceTransparency = Percent(value); } }
        /// <summary>Gets or sets stored edge transparency from zero through 100.</summary>
        public short EdgeTransparency { get { return this.edgeTransparency; } set { this.edgeTransparency = Percent(value); } }
        /// <summary>Gets or sets the raw hatch-pattern type without calculating a hatch.</summary>
        public short HatchPatternType { get { return this.hatchPatternType; } set { this.hatchPatternType = value; } }
        /// <summary>Gets or sets the stored hatch name; an empty name remains empty.</summary>
        public string HatchPatternName { get { return this.hatchPatternName; } set { this.hatchPatternName = DxfSectionSettings.StoredText(value, nameof(value)); } }
        /// <summary>Gets or sets finite stored group-41 hatch angle without normalization.</summary>
        public double HatchAngle { get { return this.hatchAngle; } set { this.hatchAngle = DxfSectionSettings.Finite(value); } }
        /// <summary>Gets or sets finite stored group 42, labeled hatch scale in the published DXF schema.</summary>
        public double HatchScale { get { return this.hatchScale; } set { this.hatchScale = DxfSectionSettings.Finite(value); } }
        /// <summary>Gets or sets finite stored group 43, labeled hatch spacing in the published DXF schema.</summary>
        public double HatchSpacing { get { return this.hatchSpacing; } set { this.hatchSpacing = DxfSectionSettings.Finite(value); } }
        private static short Percent(short value) { if (value < 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value)); return value; }
        /// <summary>Copies all stored values independently.</summary>
        public DxfSectionGeometrySettings Clone() { return (DxfSectionGeometrySettings)this.MemberwiseClone(); }
        internal void ValidateValues()
        {
            this.ColorCode = this.ColorCode; this.ColorIndex = this.ColorIndex;
            this.FaceTransparency = this.FaceTransparency; this.EdgeTransparency = this.EdgeTransparency;
            DxfSectionSettings.Finite(this.LinetypeScale); DxfSectionSettings.Finite(this.HatchAngle);
            DxfSectionSettings.Finite(this.HatchScale); DxfSectionSettings.Finite(this.HatchSpacing);
            DxfSectionSettings.StoredText(this.LayerName, "layerName"); DxfSectionSettings.StoredText(this.LinetypeName, "linetypeName");
            DxfSectionSettings.StoredText(this.PlotStyleName, "plotStyleName"); DxfSectionSettings.StoredText(this.HatchPatternName, "hatchPatternName");
        }
    }

    /// <summary>An immutable reference-bearing type bundle with independently editable geometry appearance values.</summary>
    public sealed class DxfSectionTypeSettings
    {
        private readonly List<DxfObject> sources;
        private readonly List<DxfSectionGeometrySettings> geometry;
        /// <summary>Creates a stored type bundle, preserving source order, duplicates and null handles.</summary>
        /// <param name="sectionType">Explicit group-90 value.</param>
        /// <param name="generationOptions">Raw group-91 integer flags, including values such as 17.</param>
        /// <param name="sourceObjects">Source object references; null entries denote the DXF null handle.</param>
        /// <param name="destinationBlock">Destination BLOCK_RECORD or null.</param>
        /// <param name="destinationFileName">Stored inert file name; no file is accessed.</param>
        /// <param name="geometrySettings">Geometry appearance values copied independently.</param>
        /// <param name="repeatGeometryMarkers">True emits a marker before each geometry; false emits one marker before the sequence, including an empty sequence.</param>
        public DxfSectionTypeSettings(int sectionType, int generationOptions, IEnumerable<DxfObject> sourceObjects,
            BlockRecord destinationBlock, string destinationFileName, IEnumerable<DxfSectionGeometrySettings> geometrySettings, bool repeatGeometryMarkers = true)
        {
            if (sourceObjects == null) throw new ArgumentNullException(nameof(sourceObjects));
            if (geometrySettings == null) throw new ArgumentNullException(nameof(geometrySettings));
            this.sources = DxfSectionSettings.Snapshot(sourceObjects, DxfSectionSettings.MaximumSourceReferences, nameof(sourceObjects));
            if (this.sources.Any(item => item is DxfDocument)) throw new ArgumentException("A document is not a source object; use null for the null handle.", nameof(sourceObjects));
            this.geometry = DxfSectionSettings.Snapshot(geometrySettings, DxfSectionSettings.MaximumGeometrySettings, nameof(geometrySettings))
                .Select(item => item == null ? throw new ArgumentException("A geometry bundle cannot be null.", nameof(geometrySettings)) : item.Clone()).ToList();
            foreach (DxfSectionGeometrySettings item in this.geometry) item.ValidateValues();
            this.SectionType = sectionType; this.GenerationOptions = generationOptions; this.DestinationBlock = destinationBlock;
            this.DestinationFileName = DxfSectionSettings.StoredText(destinationFileName, nameof(destinationFileName));
            this.RepeatGeometryMarkers = repeatGeometryMarkers;
        }
        /// <summary>Gets the stored type integer.</summary>
        public int SectionType { get; }
        /// <summary>Gets the raw generation-option integer without Boolean conversion.</summary>
        public int GenerationOptions { get; }
        /// <summary>Gets source identities in stored order, including duplicate and null references.</summary>
        public IReadOnlyList<DxfObject> SourceObjects { get { return this.sources.AsReadOnly(); } }
        /// <summary>Gets the referenced destination BLOCK_RECORD, or null.</summary>
        public BlockRecord DestinationBlock { get; }
        /// <summary>Gets the inert destination file name.</summary>
        public string DestinationFileName { get; }
        /// <summary>Gets the editable geometry value bundles.</summary>
        public IReadOnlyList<DxfSectionGeometrySettings> GeometrySettings { get { return this.geometry.AsReadOnly(); } }
        /// <summary>Gets whether the stored layout repeats the geometry marker before every record.</summary>
        public bool RepeatGeometryMarkers { get; }
        internal DxfSectionTypeSettings Copy(Func<DxfObject, DxfObject> resolve)
        { return new DxfSectionTypeSettings(this.SectionType, this.GenerationOptions, this.sources.Select(resolve), (BlockRecord)resolve(this.DestinationBlock), this.DestinationFileName, this.geometry, this.RepeatGeometryMarkers); }
    }

    /// <summary>Stored SECTIONSETTINGS generation parameters and references, with observed wire aliases retained.</summary>
    /// <remarks>These values do not generate geometry, change layer resources, or read or write destination files.</remarks>
    public sealed class DxfSectionSettings : DxfDatabaseObject
    {
        /// <summary>Maximum number of stored type bundles admitted in one settings object.</summary>
        public const int MaximumTypeSettings = 1024;
        /// <summary>Maximum aggregate source-reference slots admitted in one settings object.</summary>
        public const int MaximumSourceReferences = 1048576;
        /// <summary>Maximum aggregate geometry bundles admitted in one settings object.</summary>
        public const int MaximumGeometrySettings = 65536;
        private List<DxfSectionTypeSettings> types = new List<DxfSectionTypeSettings>();
        /// <summary>Creates an empty detached object using the published SECTIONSETTINGS name.</summary>
        public DxfSectionSettings() : this("SECTIONSETTINGS") { }
        internal DxfSectionSettings(string codeName) : base(codeName)
        { if (codeName != "SECTIONSETTINGS" && codeName != "SECTION_SETTINGS") throw new ArgumentException("Unrecognized section-settings record name.", nameof(codeName)); }
        /// <summary>Gets or sets the explicit outer group-90 section-type value.</summary>
        public int SectionType { get; set; }
        /// <summary>Gets the ordered type bundles.</summary>
        public IReadOnlyList<DxfSectionTypeSettings> TypeSettings { get { return this.types.AsReadOnly(); } }
        /// <summary>Atomically replaces all type bundles after validating registered source and destination identities.</summary>
        public void SetTypeSettings(IEnumerable<DxfSectionTypeSettings> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var snapshot = new List<DxfSectionTypeSettings>();
            int sources = 0, geometry = 0;
            foreach (DxfSectionTypeSettings item in values)
            {
                if (snapshot.Count == MaximumTypeSettings) throw new ArgumentException("The section type admission limit was exceeded.", nameof(values));
                if (item == null) throw new ArgumentException("A type bundle cannot be null.", nameof(values));
                if (item.SourceObjects.Count > MaximumSourceReferences - sources || item.GeometrySettings.Count > MaximumGeometrySettings - geometry)
                    throw new ArgumentException("The aggregate section settings admission limit was exceeded.", nameof(values));
                sources += item.SourceObjects.Count; geometry += item.GeometrySettings.Count;
                snapshot.Add(item.Copy(value => value));
            }
            if (this.IsErased) throw new InvalidOperationException("Erased section settings cannot change references.");
            foreach (DxfObject item in References(snapshot))
            {
                if (item is DxfDatabaseObject erased && erased.IsErased) throw new InvalidOperationException("An erased object cannot be a source reference.");
                if (item != null && this.Database != null) this.Database.CheckRegistered(item);
            }
            this.types = snapshot;
        }
        internal void ValidateValues()
        {
            if (this.IsErased) throw new InvalidOperationException("Erased section settings cannot be registered or written.");
            if (this.types.Count > MaximumTypeSettings || this.types.Sum(type => (long)type.SourceObjects.Count) > MaximumSourceReferences || this.types.Sum(type => (long)type.GeometrySettings.Count) > MaximumGeometrySettings)
                throw new InvalidOperationException("The section settings admission limit was exceeded.");
            foreach (DxfSectionTypeSettings type in this.types) foreach (DxfSectionGeometrySettings geometry in type.GeometrySettings) geometry.ValidateValues();
            foreach (DxfObject item in this.DatabaseReferences)
                if (item is DxfDatabaseObject erased && erased.IsErased) throw new InvalidOperationException("Section settings reference an erased object.");
        }
        private static IEnumerable<DxfObject> References(IEnumerable<DxfSectionTypeSettings> values)
        { foreach (DxfSectionTypeSettings type in values) { foreach (DxfObject source in type.SourceObjects) yield return source; yield return type.DestinationBlock; } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return References(this.types); } }
        internal override DxfDatabaseObject CloneShell() { return new DxfSectionSettings(this.CodeName) { SectionType = this.SectionType }; }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        { ((DxfSectionSettings)clone).SetTypeSettings(this.types.Select(type => type.Copy(resolve))); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        { try { this.ValidateValues(); } catch (Exception error) when (error is InvalidOperationException || error is ArgumentException) { errors.Add(error.Message); } }
        internal static List<T> Snapshot<T>(IEnumerable<T> values, int maximum, string parameter)
        {
            var result = new List<T>();
            foreach (T item in values) { if (result.Count == maximum) throw new ArgumentException("The section settings admission limit was exceeded.", parameter); result.Add(item); }
            return result;
        }
        internal static double Finite(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "A stored section value must be finite."); return value; }
        internal static string StoredText(string value, string parameter)
        {
            if (value == null) throw new ArgumentNullException(parameter);
            if (value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new ArgumentException("Stored section text must be a single line.", parameter);
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw new ArgumentException("Stored section text contains an unpaired surrogate.", parameter); }
                else if (char.IsLowSurrogate(value[i])) throw new ArgumentException("Stored section text contains an unpaired surrogate.", parameter);
            }
            return value;
        }
    }
}
