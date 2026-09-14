// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderMTextContent fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderMTextContent : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(304, typeof(string), string.Empty, false, 2007),
            new MLeaderField(11, typeof(Vector3), Vector3.UnitZ, false, 2007),
            new MLeaderField(340, typeof(TextStyle), null, true, 2007),
            new MLeaderField(12, typeof(Vector3), Vector3.Zero, false, 2007),
            new MLeaderField(13, typeof(Vector3), Vector3.UnitX, false, 2007),
            new MLeaderField(42, typeof(double), 0.0, false, 2007),
            new MLeaderField(43, typeof(double), 0.0, false, 2007),
            new MLeaderField(44, typeof(double), 0.0, false, 2007),
            new MLeaderField(45, typeof(double), 1.0, false, 2007),
            new MLeaderField(170, typeof(short), (short)1, false, 2007),
            new MLeaderField(90, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(171, typeof(short), (short)1, false, 2007),
            new MLeaderField(172, typeof(short), (short)1, false, 2007),
            new MLeaderField(91, typeof(int), unchecked((int)0xC8000000), false, 2007),
            new MLeaderField(141, typeof(double), 1.5, false, 2007),
            new MLeaderField(92, typeof(int), 0, false, 2007),
            new MLeaderField(291, typeof(bool), false, false, 2007),
            new MLeaderField(292, typeof(bool), false, false, 2007),
            new MLeaderField(173, typeof(short), (short)0, false, 2007),
            new MLeaderField(293, typeof(bool), false, false, 2007),
            new MLeaderField(142, typeof(double), 0.0, false, 2007),
            new MLeaderField(143, typeof(double), 0.0, false, 2007),
            new MLeaderField(294, typeof(bool), false, false, 2007),
            new MLeaderField(295, typeof(bool), true, false, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 304 value.</summary>
        public string Text { get { return this.Get<string>(304); } set { this.Set(304, value); } }
        /// <summary>Gets or sets the stored group 11 value.</summary>
        public Vector3 Normal { get { return this.Get<Vector3>(11); } set { this.Set(11, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public TextStyle Style { get { return this.Get<TextStyle>(340); } set { this.Set(340, value); } }
        /// <summary>Gets or sets the stored group 12 value.</summary>
        public Vector3 Position { get { return this.Get<Vector3>(12); } set { this.Set(12, value); } }
        /// <summary>Gets or sets the stored group 13 value.</summary>
        public Vector3 Direction { get { return this.Get<Vector3>(13); } set { this.Set(13, value); } }
        /// <summary>Gets or sets the stored group 42 value.</summary>
        public double Rotation { get { return this.Get<double>(42); } set { this.Set(42, value); } }
        /// <summary>Gets or sets the stored group 43 value.</summary>
        public double Width { get { return this.Get<double>(43); } set { this.Set(43, value); } }
        /// <summary>Gets or sets the stored group 44 value.</summary>
        public double DefinedHeight { get { return this.Get<double>(44); } set { this.Set(44, value); } }
        /// <summary>Gets or sets the stored group 45 value.</summary>
        public double LineSpacingFactor { get { return this.Get<double>(45); } set { this.Set(45, value); } }
        /// <summary>Gets or sets the stored group 170 value.</summary>
        public short LineSpacingStyle { get { return this.Get<short>(170); } set { this.Set(170, value); } }
        /// <summary>Gets or sets the stored group 90 value.</summary>
        public int Color { get { return this.Get<int>(90); } set { this.Set(90, value); } }
        /// <summary>Gets or sets the stored group 171 value.</summary>
        public short Attachment { get { return this.Get<short>(171); } set { this.Set(171, value); } }
        /// <summary>Gets or sets the stored group 172 value.</summary>
        public short FlowDirection { get { return this.Get<short>(172); } set { this.Set(172, value); } }
        /// <summary>Gets or sets the stored group 91 value.</summary>
        public int BackgroundColor { get { return this.Get<int>(91); } set { this.Set(91, value); } }
        /// <summary>Gets or sets the stored group 141 value.</summary>
        public double BackgroundScale { get { return this.Get<double>(141); } set { this.Set(141, value); } }
        /// <summary>Gets or sets the stored group 92 value.</summary>
        public int BackgroundTransparency { get { return this.Get<int>(92); } set { this.Set(92, value); } }
        /// <summary>Gets or sets the stored group 291 value.</summary>
        public bool UseWindowBackgroundColor { get { return this.Get<bool>(291); } set { this.Set(291, value); } }
        /// <summary>Gets or sets the stored group 292 value.</summary>
        public bool HasBackgroundFill { get { return this.Get<bool>(292); } set { this.Set(292, value); } }
        /// <summary>Gets or sets the stored group 173 value.</summary>
        public short ColumnType { get { return this.Get<short>(173); } set { this.Set(173, value); } }
        /// <summary>Gets or sets the stored group 293 value.</summary>
        public bool AutoHeight { get { return this.Get<bool>(293); } set { this.Set(293, value); } }
        /// <summary>Gets or sets the stored group 142 value.</summary>
        public double ColumnWidth { get { return this.Get<double>(142); } set { this.Set(142, value); } }
        /// <summary>Gets or sets the stored group 143 value.</summary>
        public double ColumnGutter { get { return this.Get<double>(143); } set { this.Set(143, value); } }
        /// <summary>Gets or sets the stored group 294 value.</summary>
        public bool ColumnFlowReversed { get { return this.Get<bool>(294); } set { this.Set(294, value); } }
        /// <summary>Gets or sets the stored group 295 value.</summary>
        public bool UseWordBreak { get { return this.Get<bool>(295); } set { this.Set(295, value); } }
    }
}
