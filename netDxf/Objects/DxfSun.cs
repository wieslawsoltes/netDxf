using System;
using System.Collections.Generic;
using netDxf.Header;

namespace netDxf.Objects
{
    /// <summary>The AcGiShadowParameters shadow algorithm stored by SUN group 70.</summary>
    public enum DxfSunShadowType
    {
        /// <summary>Ray-traced shadows, stored value zero.</summary>
        RayTraced = 0,
        /// <summary>Shadow maps, stored value one.</summary>
        ShadowMaps = 1,
        /// <summary>Area-sampled shadows, stored value two.</summary>
        AreaSampled = 2
    }

    /// <summary>The stored version-one AcDbSun object, with no solar or rendering evaluation.</summary>
    /// <remarks>Use DxfObjectDatabase.SetSun to attach a new object to a registered view or viewport.
    /// The raw group-92 integer is retained because the published DXF seconds label conflicts with native millisecond packets.</remarks>
    public sealed class DxfSun : DxfDatabaseObject
    {
        private bool enabled, shadows = true, daylightSaving;
        private short colorIndex = 7, mapSize = 256;
        private int? trueColor = 0xFFFFFF;
        private double intensity = 1.0;
        private int julianDay = 2451545, storedTime;
        private DxfSunShadowType shadowType;
        private byte softness = 1;

        /// <summary>Creates detached stored Sun settings with white light and ray-traced shadows.</summary>
        public DxfSun() : base("SUN") { }
        /// <summary>Gets the admitted stored version in group 90.</summary>
        public int StoredVersion { get { return 1; } }
        /// <summary>Gets or sets the stored status flag (290).</summary>
        public bool Enabled { get { return this.enabled; } set { this.CheckEditable(); this.enabled = value; } }
        /// <summary>Gets or sets the independent indexed color in group 63, from zero through 256.</summary>
        public short ColorIndex { get { return this.colorIndex; } set { this.CheckEditable(); if (value < 0 || value > 256) throw new ArgumentOutOfRangeException(nameof(value)); this.colorIndex = value; } }
        /// <summary>Gets or sets optional 24-bit RGB group 421 without replacing the indexed color.</summary>
        public int? TrueColor { get { return this.trueColor; } set { this.CheckEditable(); if (value < 0 || value > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(value)); this.trueColor = value; } }
        /// <summary>Gets or sets the finite stored intensity in group 40, without evaluating lighting.</summary>
        public double Intensity { get { return this.intensity; } set { this.CheckEditable(); if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value)); this.intensity = value; } }
        /// <summary>Gets or sets the stored shadow flag (291).</summary>
        public bool ShadowsEnabled { get { return this.shadows; } set { this.CheckEditable(); this.shadows = value; } }
        /// <summary>Gets or sets the raw signed Julian-day integer (91), without calendar conversion.</summary>
        public int JulianDay { get { return this.julianDay; } set { this.CheckEditable(); this.julianDay = value; } }
        /// <summary>Gets or sets raw group 92. Native fixtures use milliseconds past midnight; no unit conversion is applied.</summary>
        public int StoredTime { get { return this.storedTime; } set { this.CheckEditable(); this.storedTime = value; } }
        /// <summary>Gets or sets the stored daylight-saving flag (292).</summary>
        public bool DaylightSavingTime { get { return this.daylightSaving; } set { this.CheckEditable(); this.daylightSaving = value; } }
        /// <summary>Gets or sets the shadow algorithm (70), including native area-sampled value two.</summary>
        public DxfSunShadowType ShadowType { get { return this.shadowType; } set { this.CheckEditable(); if (value < DxfSunShadowType.RayTraced || value > DxfSunShadowType.AreaSampled) throw new ArgumentOutOfRangeException(nameof(value)); this.shadowType = value; } }
        /// <summary>Gets or sets the shadow-map size (71): a power of two from 64 through 4096.</summary>
        public short ShadowMapSize { get { return this.mapSize; } set { this.CheckEditable(); if (value < 64 || value > 4096 || (value & (value - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(value)); this.mapSize = value; } }
        /// <summary>Gets or sets the stored shadow-map edge blending width (280).</summary>
        public byte ShadowSoftness { get { return this.softness; } set { this.CheckEditable(); this.softness = value; } }
        private void CheckEditable() { if (this.IsErased) throw new InvalidOperationException("An erased SUN cannot be modified."); }
        internal override DxfDatabaseObject CloneShell()
        {
            return new DxfSun { Enabled = this.Enabled, ColorIndex = this.ColorIndex, TrueColor = this.TrueColor,
                Intensity = this.Intensity, ShadowsEnabled = this.ShadowsEnabled, JulianDay = this.JulianDay,
                StoredTime = this.StoredTime, DaylightSavingTime = this.DaylightSavingTime, ShadowType = this.ShadowType,
                ShadowMapSize = this.ShadowMapSize, ShadowSoftness = this.ShadowSoftness };
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (database.Document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) errors.Add("Typed SUN requires R2007 or later.");
            if (this.Owner != null && !SunReferences.IsHost(this.Owner)) errors.Add("SUN requires a view or viewport owner.");
            if (this.Database != null && (this.Owner == null || !SunReferences.IsHost(this.Owner) || !ReferenceEquals(SunReferences.Get(this.Owner), this)))
                errors.Add("SUN requires a reciprocal view or viewport owner: " + this.Handle);
        }
    }
}
