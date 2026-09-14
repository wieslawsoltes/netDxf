// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>The three standard DXF light sources.</summary>
    public enum LightType
    {
        /// <summary>A distant directional light.</summary>
        Distant = 1,
        /// <summary>An omnidirectional point light.</summary>
        Point = 2,
        /// <summary>A targeted spot light.</summary>
        Spot = 3
    }

    /// <summary>Distance-dependent attenuation of a light.</summary>
    public enum LightAttenuationType
    {
        /// <summary>No distance attenuation.</summary>
        None = 0,
        /// <summary>Inverse distance attenuation.</summary>
        InverseLinear = 1,
        /// <summary>Inverse squared distance attenuation.</summary>
        InverseSquare = 2
    }

    /// <summary>Shadow representation requested by a light.</summary>
    public enum LightShadowType
    {
        /// <summary>Ray traced shadows.</summary>
        RayTraced = 0,
        /// <summary>Shadow maps.</summary>
        ShadowMap = 1
    }

    /// <summary>A LIGHT entity with the published AcDbLight parameter set.</summary>
    /// <remarks>
    /// Positions are WCS points, angles are degrees and attenuation distances use drawing units.
    /// Parameters remain authored data, not a rendered-light simulation. No private photometric
    /// extension is inferred from these standard fields. Typed output requires AutoCAD 2007 or later.
    /// </remarks>
    public sealed class Light : EntityObject
    {
        private string name = string.Empty;
        private int versionNumber;
        private LightType lightType = LightType.Distant;
        private Vector3 position;
        private Vector3 target = Vector3.UnitZ;
        private double intensity = 1.0;
        private LightAttenuationType attenuation = LightAttenuationType.InverseSquare;
        private double attenuationStart, attenuationEnd;
        private double hotspotAngle = 45.0, falloffAngle = 90.0;
        private LightShadowType shadowType;
        private int shadowMapSize = 512;
        private short shadowMapSoftness = 1;

        /// <summary>Initializes a distant light using the standard parameter defaults.</summary>
        public Light() : base(EntityType.Light, DxfObjectCode.Light)
        {
            this.IsOn = true;
            this.CastShadows = true;
        }

        /// <summary>Gets or sets the nonnegative AcDbLight version number.</summary>
        public int VersionNumber
        {
            get { return this.versionNumber; }
            set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.versionNumber = value; }
        }

        /// <summary>Gets or sets the light name. Empty names are retained; null and CR/LF/NUL transport delimiters are invalid.</summary>
        public string Name
        {
            get { return this.name; }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\0') >= 0)
                    throw new ArgumentException("A LIGHT name cannot contain CR, LF or NUL transport delimiters.", nameof(value));
                this.name = value;
            }
        }

        /// <summary>Gets or sets the distant, point or spot source type.</summary>
        public LightType LightType
        {
            get { return this.lightType; }
            set { if (value < LightType.Distant || value > LightType.Spot) throw new ArgumentOutOfRangeException(nameof(value)); this.lightType = value; }
        }

        /// <summary>Gets or sets whether the light is enabled, independently of glyph visibility.</summary>
        public bool IsOn { get; set; }
        /// <summary>Gets or sets whether the light glyph is plotted.</summary>
        public bool PlotGlyph { get; set; }
        /// <summary>Gets or sets the finite, nonnegative authored intensity.</summary>
        public double Intensity
        {
            get { return this.intensity; }
            set { Nonnegative(value, nameof(value)); this.intensity = value; }
        }
        /// <summary>Gets or sets the finite WCS source position.</summary>
        public Vector3 Position
        {
            get { return this.position; }
            set { Finite(value, nameof(value)); this.position = value; }
        }
        /// <summary>Gets or sets the finite WCS target point, including dormant values for point lights.</summary>
        public Vector3 Target
        {
            get { return this.target; }
            set { Finite(value, nameof(value)); this.target = value; }
        }
        /// <summary>Gets or sets the attenuation law.</summary>
        public LightAttenuationType AttenuationType
        {
            get { return this.attenuation; }
            set { if (value < LightAttenuationType.None || value > LightAttenuationType.InverseSquare) throw new ArgumentOutOfRangeException(nameof(value)); this.attenuation = value; }
        }
        /// <summary>Gets or sets whether the authored attenuation limits are enabled.</summary>
        public bool UseAttenuationLimits { get; set; }
        /// <summary>Gets or sets the finite, nonnegative start distance. No end-limit mutation is performed.</summary>
        public double AttenuationStartLimit
        {
            get { return this.attenuationStart; }
            set { Nonnegative(value, nameof(value)); this.attenuationStart = value; }
        }
        /// <summary>Gets or sets the finite, nonnegative end distance. Dormant limits are retained.</summary>
        public double AttenuationEndLimit
        {
            get { return this.attenuationEnd; }
            set { Nonnegative(value, nameof(value)); this.attenuationEnd = value; }
        }
        /// <summary>Gets or sets the finite hotspot angle in degrees, without normalization or refitting.</summary>
        public double HotspotAngle
        {
            get { return this.hotspotAngle; }
            set { Nonnegative(value, nameof(value)); this.hotspotAngle = value; }
        }
        /// <summary>Gets or sets the finite falloff angle in degrees, independently of hotspot angle.</summary>
        public double FalloffAngle
        {
            get { return this.falloffAngle; }
            set { Nonnegative(value, nameof(value)); this.falloffAngle = value; }
        }
        /// <summary>Gets or sets whether shadows are cast.</summary>
        public bool CastShadows { get; set; }
        /// <summary>Gets or sets the shadow representation.</summary>
        public LightShadowType ShadowType
        {
            get { return this.shadowType; }
            set { if (value != LightShadowType.RayTraced && value != LightShadowType.ShadowMap) throw new ArgumentOutOfRangeException(nameof(value)); this.shadowType = value; }
        }
        /// <summary>Gets or sets the nonnegative shadow-map size, including a dormant zero value.</summary>
        public int ShadowMapSize
        {
            get { return this.shadowMapSize; }
            set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.shadowMapSize = value; }
        }
        /// <summary>Gets or sets the stored nonnegative Int16 shadow-map softness.</summary>
        public short ShadowMapSoftness
        {
            get { return this.shadowMapSoftness; }
            set { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.shadowMapSoftness = value; }
        }

        /// <summary>Applies a finite nonsingular similarity transformation, including reflection.</summary>
        /// <remarks>
        /// Positions transform as points; attenuation distances scale by the common magnitude.
        /// Intensity, angles and shadow-map settings remain authored values. A nonuniform scale
        /// or shear cannot represent the same spherical attenuation and spot cone and is rejected.
        /// All results are validated before mutation. This operation does not rescale photometric power.
        /// </remarks>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            Finite(translation, nameof(translation));
            Vector3 x = transformation * Vector3.UnitX, y = transformation * Vector3.UnitY, z = transformation * Vector3.UnitZ;
            double scale = StableLength(x), sy = StableLength(y), sz = StableLength(z);
            if (!IsFinite(scale) || scale == 0 || !IsFinite(sy) || !IsFinite(sz))
                throw new ArgumentException("LIGHT requires a finite nonsingular similarity transform.", nameof(transformation));
            x = Divide(x, scale); y = Divide(y, scale); z = Divide(z, scale);
            if (Math.Abs(sy / scale - 1) > 1e-10 || Math.Abs(sz / scale - 1) > 1e-10 ||
                Math.Abs(Vector3.DotProduct(x, y)) > 1e-10 || Math.Abs(Vector3.DotProduct(x, z)) > 1e-10 ||
                Math.Abs(Vector3.DotProduct(y, z)) > 1e-10)
                throw new ArgumentException("LIGHT does not support shear or nonuniform scale.", nameof(transformation));
            Vector3 p = transformation * this.position + translation, t = transformation * this.target + translation;
            double start = this.attenuationStart * scale, end = this.attenuationEnd * scale;
            Finite(p, nameof(transformation)); Finite(t, nameof(transformation));
            Nonnegative(start, nameof(transformation)); Nonnegative(end, nameof(transformation));
            this.position = p; this.target = t; this.attenuationStart = start; this.attenuationEnd = end;
        }

        /// <summary>Creates an independent light without copying handle, owner or reactors.</summary>
        public override object Clone()
        {
            var copy = new Light
            {
                Layer = (Layer)this.Layer.Clone(), Linetype = (Linetype)this.Linetype.Clone(),
                Color = (AciColor)this.Color.Clone(), Lineweight = this.Lineweight,
                Transparency = (Transparency)this.Transparency.Clone(), LinetypeScale = this.LinetypeScale,
                IsVisible = this.IsVisible, Normal = this.Normal,
                VersionNumber = this.versionNumber, Name = this.name, LightType = this.lightType,
                IsOn = this.IsOn, PlotGlyph = this.PlotGlyph, Intensity = this.intensity,
                Position = this.position, Target = this.target, AttenuationType = this.attenuation,
                UseAttenuationLimits = this.UseAttenuationLimits, AttenuationStartLimit = this.attenuationStart,
                AttenuationEndLimit = this.attenuationEnd, HotspotAngle = this.hotspotAngle,
                FalloffAngle = this.falloffAngle, CastShadows = this.CastShadows, ShadowType = this.shadowType,
                ShadowMapSize = this.shadowMapSize, ShadowMapSoftness = this.shadowMapSoftness
            };
            foreach (XData data in this.XData.Values) copy.XData.Add((XData)data.Clone());
            this.CopyCommonDataTo(copy);
            return copy;
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Nonnegative(double value, string name)
        {
            if (!IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name, "LIGHT parameter must be finite and nonnegative.");
        }
        private static void Finite(Vector3 value, string name)
        {
            if (!IsFinite(value.X) || !IsFinite(value.Y) || !IsFinite(value.Z))
                throw new ArgumentOutOfRangeException(name, "LIGHT point must be finite.");
        }
        private static Vector3 Divide(Vector3 v, double scale) { return new Vector3(v.X / scale, v.Y / scale, v.Z / scale); }
        private static double StableLength(Vector3 v)
        {
            double m = Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z)));
            if (m == 0) return 0;
            v = Divide(v, m);
            return m * Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        }
    }
}
