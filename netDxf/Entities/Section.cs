// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.ObjectModel;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>A stored section plane, its independent boundaries and its owned geometry settings.</summary>
    /// <remarks>R2007 and later. This model does not generate cuts, evaluate section states or regenerate proxy graphics.</remarks>
    public sealed class Section : EntityObject
    {
        private string name = string.Empty;
        private string indicatorColorName;
        private Vector3 verticalDirection = Vector3.UnitZ;
        private double topHeight;
        private double bottomHeight;
        private readonly SectionVertices vertices = new SectionVertices();
        private readonly SectionVertices backLineVertices = new SectionVertices();
        internal bool PendingInputReferences;
        /// <summary>Gets whether this section was permanently erased with its ownership graph.</summary>
        public bool IsErased { get; internal set; }
        internal bool HasSettingsField = true;

        /// <summary>Maximum vertices in each independently counted boundary.</summary>
        public const int MaximumVertices = 1048576;
        /// <summary>Creates a section with the native SECTIONOBJECT wire name.</summary>
        public Section() : this("SECTIONOBJECT") { }
        /// <summary>Creates a section retaining exactly the selected SECTIONOBJECT or documented SECTION spelling.</summary>
        /// <param name="codeName">SECTIONOBJECT or SECTION; the value is retained through save and clone.</param>
        public Section(string codeName) : base(EntityType.Section, codeName)
        { if (codeName != "SECTION" && codeName != "SECTIONOBJECT") throw new ArgumentException("Unsupported section wire name.", nameof(codeName)); }
        /// <summary>Gets or sets the stored group-90 state without evaluating its effect.</summary>
        public int State { get; set; }
        /// <summary>Gets or sets the independent stored group-91 flags.</summary>
        public int Flags { get; set; }
        /// <summary>Gets or sets the group-1 name, including an explicitly empty name.</summary>
        public string Name { get { return this.name; } set { CheckText(value, false); this.name = value; } }
        /// <summary>Gets or sets the complete group-10 vertical vector without normalization.</summary>
        public Vector3 VerticalDirection { get { return this.verticalDirection; } set { CheckVector(value); this.verticalDirection = value; } }
        /// <summary>Gets or sets the independently stored group-40 top height.</summary>
        public double TopHeight { get { return this.topHeight; } set { CheckFinite(value); this.topHeight = value; } }
        /// <summary>Gets or sets the independently stored group-41 bottom height.</summary>
        public double BottomHeight { get { return this.bottomHeight; } set { CheckFinite(value); this.bottomHeight = value; } }
        /// <summary>Gets or sets the stored group-70 indicator transparency.</summary>
        public short IndicatorTransparency { get; set; }
        /// <summary>Gets or sets the optional documented group-63 indicator ACI value. Null preserves absence.</summary>
        public short? StoredIndicatorColor { get; set; }
        /// <summary>Gets or sets the optional subclass group-62 indicator ACI found in native SECTIONOBJECT packets.</summary>
        /// <remarks>This is independent of the common entity Color property and of group 63.</remarks>
        public short? StoredNativeIndicatorColor { get; set; }
        /// <summary>Gets or sets the optional group-411 indicator color name. Null omits it; empty retains presence.</summary>
        public string IndicatorColorName { get { return this.indicatorColorName; } set { CheckText(value, true); this.indicatorColorName = value; } }
        /// <summary>Gets ordered group-11 boundary vertices. The group-92 count is derived from this collection.</summary>
        public Collection<Vector3> Vertices { get { return this.vertices; } }
        /// <summary>Gets independently ordered group-12 back-line vertices. The group-93 count is derived from this collection.</summary>
        public Collection<Vector3> BackLineVertices { get { return this.backLineVertices; } }
        /// <summary>Gets the owned geometry settings, which can be a preserved private opaque object after loading.</summary>
        /// <remarks>Use Objects.SetSectionSettings to attach typed settings to a registered section.</remarks>
        public DxfDatabaseObject GeometrySettings { get; internal set; }
        /// <summary>Gets whether the group-360 settings field was physically supplied, including an explicit null handle.</summary>
        public bool HasStoredGeometrySettings { get { return this.HasSettingsField; } }

        internal static void CheckFinite(double value)
        { if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "Section values must be finite."); }
        internal static void CheckVector(Vector3 value) { CheckFinite(value.X); CheckFinite(value.Y); CheckFinite(value.Z); }
        internal static void CheckText(string value, bool optional)
        {
            if (value == null) { if (optional) return; throw new ArgumentNullException(nameof(value)); }
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\0' || ch == '\r' || ch == '\n') throw new ArgumentException("Section text cannot contain NUL or line breaks.", nameof(value));
                if (char.IsHighSurrogate(ch)) { if (++i == value.Length || !char.IsLowSurrogate(value[i])) throw new ArgumentException("Section text requires valid UTF-16.", nameof(value)); }
                else if (char.IsLowSurrogate(ch)) throw new ArgumentException("Section text requires valid UTF-16.", nameof(value));
            }
        }
        internal void Validate(DxfDocument document)
        {
            if (this.IsErased) throw new InvalidOperationException("An erased section cannot be adopted, cloned or written.");
            if (document.DrawingVariables.AcadVer < DxfVersion.AutoCad2007) throw new NotSupportedException("SECTION requires R2007 or later.");
            if (this.PendingInputReferences) return;
            if (this.ExtensionDictionary != null && (this.ExtensionDictionary.Database != document.Objects || !ReferenceEquals(this.ExtensionDictionary.Owner, this)))
                throw new InvalidOperationException("A section extension dictionary must be registered with its reciprocal owner.");
            foreach (DxfObject reactor in this.PersistentReactors) if (!document.Objects.IsRegistered(reactor)) throw new InvalidOperationException("Section reactors must be registered in the destination document.");
            foreach (DxfObject reactor in this.Reactors) if (!document.Objects.IsRegistered(reactor)) throw new InvalidOperationException("Section reactors must be registered in the destination document.");
            foreach (XData data in this.XData.Values) foreach (XDataRecord tag in data.XDataRecord)
                if (tag.Code == XDataCode.DatabaseHandle && ((string)tag.Value).TrimStart('0').Length != 0 && document.GetObjectByHandle((string)tag.Value) == null)
                    throw new InvalidOperationException("Section XData references must resolve in the destination document.");
            if (this.GeometrySettings != null && (this.GeometrySettings.Database != document.Objects || !ReferenceEquals(this.GeometrySettings.Owner, this)))
                throw new InvalidOperationException("Section geometry settings must be registered and owned by that section.");
        }
        /// <summary>Accepts the identity transform. Other transformations require an application to update all stored section data coherently.</summary>
        public override void TransformBy(Matrix3 transformation, Vector3 translation)
        {
            if (translation.X != 0 || translation.Y != 0 || translation.Z != 0) throw new NotSupportedException("Stored section transforms are not evaluated.");
            for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++)
                if (transformation[row, column] != (row == column ? 1.0 : 0.0)) throw new NotSupportedException("Stored section transforms are not evaluated.");
        }
        /// <summary>Accepts only the exact four-by-four identity transform.</summary>
        public override void TransformBy(Matrix4 transformation)
        {
            for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
                if (transformation[row, column] != (row == column ? 1.0 : 0.0)) throw new NotSupportedException("Stored section transforms are not evaluated.");
        }
        internal Section CloneValues(bool cloneResources = true)
        {
            var copy = new Section(this.CodeName) { Name = this.Name, State = this.State, Flags = this.Flags, VerticalDirection = this.VerticalDirection,
                TopHeight = this.TopHeight, BottomHeight = this.BottomHeight, IndicatorTransparency = this.IndicatorTransparency,
                StoredIndicatorColor = this.StoredIndicatorColor, StoredNativeIndicatorColor = this.StoredNativeIndicatorColor, IndicatorColorName = this.IndicatorColorName,
                HasSettingsField = this.HasSettingsField, Layer = cloneResources ? (Layer)this.Layer.Clone() : this.Layer, Linetype = cloneResources ? (Linetype)this.Linetype.Clone() : this.Linetype, Color = (AciColor)this.Color.Clone(),
                Lineweight = this.Lineweight, Transparency = (Transparency)this.Transparency.Clone(), LinetypeScale = this.LinetypeScale, IsVisible = this.IsVisible, Normal = this.Normal };
            foreach (Vector3 vertex in this.Vertices) copy.Vertices.Add(vertex);
            foreach (Vector3 vertex in this.BackLineVertices) copy.BackLineVertices.Add(vertex);
            foreach (XData data in this.XData.Values) copy.XData.Add(cloneResources ? (XData)data.Clone() : data.CopyStoredGraph());
            this.CopyCommonDataTo(copy); return copy;
        }
        /// <summary>Clones independent stored values when no ownership graph or common reference metadata is attached.</summary>
        /// <remarks>Use Objects.CloneSection for an attached settings or extension graph and explicit reference remapping.</remarks>
        public override object Clone()
        {
            if (this.IsErased) throw new InvalidOperationException("An erased section cannot be cloned.");
            if (this.GeometrySettings != null || this.ExtensionDictionary != null || this.PersistentReactors.Count != 0 || this.Reactors.Count != 0)
                throw new NotSupportedException("Use Objects.CloneSection to clone a section with owned objects or common references.");
            foreach (XData data in this.XData.Values) foreach (XDataRecord tag in data.XDataRecord)
                if (tag.Code == XDataCode.DatabaseHandle && ((string)tag.Value).TrimStart('0').Length != 0)
                    throw new NotSupportedException("Use Objects.CloneSection to remap section XData references.");
            return this.CloneValues();
        }
        private sealed class SectionVertices : Collection<Vector3>
        {
            protected override void InsertItem(int index, Vector3 item)
            { if (this.Count >= MaximumVertices) throw new ArgumentOutOfRangeException(nameof(item), "The section vertex limit was exceeded."); CheckVector(item); base.InsertItem(index, item); }
            protected override void SetItem(int index, Vector3 item) { CheckVector(item); base.SetItem(index, item); }
        }
    }
}
