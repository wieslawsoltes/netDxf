// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderStyleProperties fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderStyleProperties : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(170, typeof(short), (short)2, false, 2007),
            new MLeaderField(171, typeof(short), (short)1, false, 2007),
            new MLeaderField(172, typeof(short), (short)0, false, 2007),
            new MLeaderField(90, typeof(int), 2, false, 2007),
            new MLeaderField(40, typeof(double), 0.0, false, 2007),
            new MLeaderField(41, typeof(double), 0.0, false, 2007),
            new MLeaderField(173, typeof(short), (short)1, false, 2007),
            new MLeaderField(91, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(340, typeof(Linetype), null, true, 2007),
            new MLeaderField(92, typeof(int), -2, false, 2007),
            new MLeaderField(290, typeof(bool), true, false, 2007),
            new MLeaderField(42, typeof(double), 2.0, false, 2007),
            new MLeaderField(291, typeof(bool), true, false, 2007),
            new MLeaderField(43, typeof(double), 8.0, false, 2007),
            new MLeaderField(3, typeof(string), string.Empty, false, 2007),
            new MLeaderField(341, typeof(BlockRecord), null, true, 2007),
            new MLeaderField(44, typeof(double), 4.0, false, 2007),
            new MLeaderField(300, typeof(string), string.Empty, false, 2007),
            new MLeaderField(342, typeof(TextStyle), null, true, 2007),
            new MLeaderField(174, typeof(short), (short)1, false, 2007),
            new MLeaderField(175, typeof(short), (short)1, false, 2007),
            new MLeaderField(176, typeof(short), (short)0, false, 2007),
            new MLeaderField(178, typeof(short), (short)1, false, 2007),
            new MLeaderField(93, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(45, typeof(double), 4.0, false, 2007),
            new MLeaderField(292, typeof(bool), false, false, 2007),
            new MLeaderField(297, typeof(bool), false, false, 2007),
            new MLeaderField(46, typeof(double), 4.0, false, 2007),
            new MLeaderField(343, typeof(BlockRecord), null, true, 2007),
            new MLeaderField(94, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(47, typeof(double), 1.0, false, 2007),
            new MLeaderField(49, typeof(double), 1.0, false, 2007),
            new MLeaderField(140, typeof(double), 1.0, false, 2007),
            new MLeaderField(293, typeof(bool), null, false, 2007),
            new MLeaderField(141, typeof(double), 0.0, false, 2007),
            new MLeaderField(294, typeof(bool), true, false, 2007),
            new MLeaderField(177, typeof(short), (short)0, false, 2007),
            new MLeaderField(142, typeof(double), 1.0, false, 2007),
            new MLeaderField(295, typeof(bool), false, false, 2007),
            new MLeaderField(296, typeof(bool), false, false, 2007),
            new MLeaderField(143, typeof(double), 3.75, false, 2007),
            new MLeaderField(271, typeof(short), (short)0, false, 2007),
            new MLeaderField(272, typeof(short), (short)9, false, 2007),
            new MLeaderField(273, typeof(short), (short)9, false, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 170 value.</summary>
        public short ContentType { get { return this.Get<short>(170); } set { this.Set(170, value); } }
        /// <summary>Gets or sets the stored group 171 value.</summary>
        public short DrawMLeaderOrder { get { return this.Get<short>(171); } set { this.Set(171, value); } }
        /// <summary>Gets or sets the stored group 172 value.</summary>
        public short DrawLeaderOrder { get { return this.Get<short>(172); } set { this.Set(172, value); } }
        /// <summary>Gets or sets the stored group 90 value.</summary>
        public int MaximumLeaderPoints { get { return this.Get<int>(90); } set { this.Set(90, value); } }
        /// <summary>Gets or sets the stored group 40 value.</summary>
        public double FirstSegmentAngle { get { return this.Get<double>(40); } set { this.Set(40, value); } }
        /// <summary>Gets or sets the stored group 41 value.</summary>
        public double SecondSegmentAngle { get { return this.Get<double>(41); } set { this.Set(41, value); } }
        /// <summary>Gets or sets the stored group 173 value.</summary>
        public short LeaderType { get { return this.Get<short>(173); } set { this.Set(173, value); } }
        /// <summary>Gets or sets the stored group 91 value.</summary>
        public int LeaderLineColor { get { return this.Get<int>(91); } set { this.Set(91, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public Linetype LeaderLinetype { get { return this.Get<Linetype>(340); } set { this.Set(340, value); } }
        /// <summary>Gets or sets the stored group 92 value.</summary>
        public int LeaderLineweight { get { return this.Get<int>(92); } set { this.Set(92, value); } }
        /// <summary>Gets or sets the stored group 290 value.</summary>
        public bool HasLanding { get { return this.Get<bool>(290); } set { this.Set(290, value); } }
        /// <summary>Gets or sets the stored group 42 value.</summary>
        public double LandingGap { get { return this.Get<double>(42); } set { this.Set(42, value); } }
        /// <summary>Gets or sets the stored group 291 value.</summary>
        public bool HasDogleg { get { return this.Get<bool>(291); } set { this.Set(291, value); } }
        /// <summary>Gets or sets the stored group 43 value.</summary>
        public double DoglegLength { get { return this.Get<double>(43); } set { this.Set(43, value); } }
        /// <summary>Gets or sets the stored group 3 value.</summary>
        public string Description { get { return this.Get<string>(3); } set { this.Set(3, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord ArrowHead { get { return this.Get<BlockRecord>(341); } set { this.Set(341, value); } }
        /// <summary>Gets or sets the stored group 44 value.</summary>
        public double ArrowHeadSize { get { return this.Get<double>(44); } set { this.Set(44, value); } }
        /// <summary>Gets or sets the stored group 300 value.</summary>
        public string DefaultText { get { return this.Get<string>(300); } set { this.Set(300, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public TextStyle TextStyle { get { return this.Get<TextStyle>(342); } set { this.Set(342, value); } }
        /// <summary>Gets or sets the stored group 174 value.</summary>
        public short TextLeftAttachment { get { return this.Get<short>(174); } set { this.Set(174, value); } }
        /// <summary>Gets or sets the stored group 175 value.</summary>
        public short TextAngleType { get { return this.Get<short>(175); } set { this.Set(175, value); } }
        /// <summary>Gets or sets the stored group 176 value.</summary>
        public short TextAlignmentType { get { return this.Get<short>(176); } set { this.Set(176, value); } }
        /// <summary>Gets or sets the stored group 178 value.</summary>
        public short TextRightAttachment { get { return this.Get<short>(178); } set { this.Set(178, value); } }
        /// <summary>Gets or sets the stored group 93 value.</summary>
        public int TextColor { get { return this.Get<int>(93); } set { this.Set(93, value); } }
        /// <summary>Gets or sets the stored group 45 value.</summary>
        public double TextHeight { get { return this.Get<double>(45); } set { this.Set(45, value); } }
        /// <summary>Gets or sets the stored group 292 value.</summary>
        public bool HasTextFrame { get { return this.Get<bool>(292); } set { this.Set(292, value); } }
        /// <summary>Gets or sets the stored group 297 value.</summary>
        public bool TextAlignAlwaysLeft { get { return this.Get<bool>(297); } set { this.Set(297, value); } }
        /// <summary>Gets or sets the stored group 46 value.</summary>
        public double AlignSpace { get { return this.Get<double>(46); } set { this.Set(46, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord Block { get { return this.Get<BlockRecord>(343); } set { this.Set(343, value); } }
        /// <summary>Gets or sets the stored group 94 value.</summary>
        public int BlockColor { get { return this.Get<int>(94); } set { this.Set(94, value); } }
        /// <summary>Gets or sets the stored group 47 value.</summary>
        public double BlockScaleX { get { return this.Get<double>(47); } set { this.Set(47, value); } }
        /// <summary>Gets or sets the stored group 49 value.</summary>
        public double BlockScaleY { get { return this.Get<double>(49); } set { this.Set(49, value); } }
        /// <summary>Gets or sets the stored group 140 value.</summary>
        public double BlockScaleZ { get { return this.Get<double>(140); } set { this.Set(140, value); } }
        /// <summary>Gets or sets the stored group 293 value.</summary>
        public bool? HasBlockScaling { get { return this.Get<bool?>(293); } set { this.Set(293, value); } }
        /// <summary>Gets or sets the stored group 141 value.</summary>
        public double BlockRotation { get { return this.Get<double>(141); } set { this.Set(141, value); } }
        /// <summary>Gets or sets the stored group 294 value.</summary>
        public bool HasBlockRotation { get { return this.Get<bool>(294); } set { this.Set(294, value); } }
        /// <summary>Gets or sets the stored group 177 value.</summary>
        public short BlockConnectionType { get { return this.Get<short>(177); } set { this.Set(177, value); } }
        /// <summary>Gets or sets the stored group 142 value.</summary>
        public double Scale { get { return this.Get<double>(142); } set { this.Set(142, value); } }
        /// <summary>Gets or sets the stored group 295 value.</summary>
        public bool OverwritePropertyValue { get { return this.Get<bool>(295); } set { this.Set(295, value); } }
        /// <summary>Gets or sets the stored group 296 value.</summary>
        public bool IsAnnotative { get { return this.Get<bool>(296); } set { this.Set(296, value); } }
        /// <summary>Gets or sets the stored group 143 value.</summary>
        public double BreakGap { get { return this.Get<double>(143); } set { this.Set(143, value); } }
        /// <summary>Gets or sets the stored group 271 value.</summary>
        public short TextAttachmentDirection { get { return this.Get<short>(271); } set { this.Set(271, value); } }
        /// <summary>Gets or sets the stored group 272 value.</summary>
        public short TextBottomAttachment { get { return this.Get<short>(272); } set { this.Set(272, value); } }
        /// <summary>Gets or sets the stored group 273 value.</summary>
        public short TextTopAttachment { get { return this.Get<short>(273); } set { this.Set(273, value); } }
    }
}
