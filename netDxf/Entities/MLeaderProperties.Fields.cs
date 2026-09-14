// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderProperties fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderProperties : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(340, typeof(DxfMLeaderStyle), null, true, 2007),
            new MLeaderField(90, typeof(int), 0, false, 2007),
            new MLeaderField(170, typeof(short), (short)1, false, 2007),
            new MLeaderField(91, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(341, typeof(Linetype), null, true, 2007),
            new MLeaderField(171, typeof(short), (short)-2, false, 2007),
            new MLeaderField(290, typeof(bool), true, false, 2007),
            new MLeaderField(291, typeof(bool), true, false, 2007),
            new MLeaderField(41, typeof(double), 8.0, false, 2007),
            new MLeaderField(342, typeof(BlockRecord), null, true, 2007),
            new MLeaderField(42, typeof(double), 4.0, false, 2007),
            new MLeaderField(172, typeof(short), (short)2, false, 2007),
            new MLeaderField(343, typeof(TextStyle), null, true, 2007),
            new MLeaderField(173, typeof(short), (short)1, false, 2007),
            new MLeaderField(95, typeof(int), 1, false, 2007),
            new MLeaderField(174, typeof(short), (short)1, false, 2007),
            new MLeaderField(175, typeof(short), (short)2, false, 2007),
            new MLeaderField(92, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(292, typeof(bool), false, false, 2007),
            new MLeaderField(344, typeof(BlockRecord), null, true, 2007),
            new MLeaderField(93, typeof(int), unchecked((int)0xC1000000), false, 2007),
            new MLeaderField(10, typeof(Vector3), new Vector3(1,1,1), false, 2007),
            new MLeaderField(43, typeof(double), 0.0, false, 2007),
            new MLeaderField(176, typeof(short), (short)0, false, 2007),
            new MLeaderField(293, typeof(bool), false, false, 2007),
            new MLeaderField(294, typeof(bool), false, false, 2007),
            new MLeaderField(178, typeof(short), (short)0, false, 2007),
            new MLeaderField(179, typeof(short), (short)1, false, 2007),
            new MLeaderField(45, typeof(double), 1.0, false, 2007),
            new MLeaderField(271, typeof(short), null, false, 2010),
            new MLeaderField(272, typeof(short), null, false, 2010),
            new MLeaderField(273, typeof(short), null, false, 2010),
            new MLeaderField(295, typeof(bool), null, false, 2013),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public DxfMLeaderStyle Style { get { return this.Get<DxfMLeaderStyle>(340); } set { this.Set(340, value); } }
        /// <summary>Gets or sets the stored group 90 value.</summary>
        public int PropertyOverrideFlags { get { return this.Get<int>(90); } set { this.Set(90, value); } }
        /// <summary>Gets or sets the stored group 170 value.</summary>
        public short LeaderType { get { return this.Get<short>(170); } set { this.Set(170, value); } }
        /// <summary>Gets or sets the stored group 91 value.</summary>
        public int LeaderLineColor { get { return this.Get<int>(91); } set { this.Set(91, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public Linetype LeaderLinetype { get { return this.Get<Linetype>(341); } set { this.Set(341, value); } }
        /// <summary>Gets or sets the stored group 171 value.</summary>
        public short LeaderLineweight { get { return this.Get<short>(171); } set { this.Set(171, value); } }
        /// <summary>Gets or sets the stored group 290 value.</summary>
        public bool HasLanding { get { return this.Get<bool>(290); } set { this.Set(290, value); } }
        /// <summary>Gets or sets the stored group 291 value.</summary>
        public bool HasDogleg { get { return this.Get<bool>(291); } set { this.Set(291, value); } }
        /// <summary>Gets or sets the stored group 41 value.</summary>
        public double DoglegLength { get { return this.Get<double>(41); } set { this.Set(41, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord ArrowHead { get { return this.Get<BlockRecord>(342); } set { this.Set(342, value); } }
        /// <summary>Gets or sets the stored group 42 value.</summary>
        public double ArrowHeadSize { get { return this.Get<double>(42); } set { this.Set(42, value); } }
        /// <summary>Gets or sets the stored group 172 value.</summary>
        public short ContentType { get { return this.Get<short>(172); } set { this.Set(172, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public TextStyle TextStyle { get { return this.Get<TextStyle>(343); } set { this.Set(343, value); } }
        /// <summary>Gets or sets the stored group 173 value.</summary>
        public short TextLeftAttachment { get { return this.Get<short>(173); } set { this.Set(173, value); } }
        /// <summary>Gets or sets the stored group 95 value.</summary>
        public int TextRightAttachment { get { return this.Get<int>(95); } set { this.Set(95, value); } }
        /// <summary>Gets or sets the stored group 174 value.</summary>
        public short TextAngleType { get { return this.Get<short>(174); } set { this.Set(174, value); } }
        /// <summary>Gets or sets the stored group 175 value.</summary>
        public short TextAlignmentType { get { return this.Get<short>(175); } set { this.Set(175, value); } }
        /// <summary>Gets or sets the stored group 92 value.</summary>
        public int TextColor { get { return this.Get<int>(92); } set { this.Set(92, value); } }
        /// <summary>Gets or sets the stored group 292 value.</summary>
        public bool HasTextFrame { get { return this.Get<bool>(292); } set { this.Set(292, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord Block { get { return this.Get<BlockRecord>(344); } set { this.Set(344, value); } }
        /// <summary>Gets or sets the stored group 93 value.</summary>
        public int BlockColor { get { return this.Get<int>(93); } set { this.Set(93, value); } }
        /// <summary>Gets or sets the stored group 10 value.</summary>
        public Vector3 BlockScale { get { return this.Get<Vector3>(10); } set { this.Set(10, value); } }
        /// <summary>Gets or sets the stored group 43 value.</summary>
        public double BlockRotation { get { return this.Get<double>(43); } set { this.Set(43, value); } }
        /// <summary>Gets or sets the stored group 176 value.</summary>
        public short BlockConnectionType { get { return this.Get<short>(176); } set { this.Set(176, value); } }
        /// <summary>Gets or sets the stored group 293 value.</summary>
        public bool IsAnnotative { get { return this.Get<bool>(293); } set { this.Set(293, value); } }
        /// <summary>Gets or sets the stored group 294 value.</summary>
        public bool IsTextDirectionNegative { get { return this.Get<bool>(294); } set { this.Set(294, value); } }
        /// <summary>Gets or sets the stored group 178 value.</summary>
        public short TextEditorAlignment { get { return this.Get<short>(178); } set { this.Set(178, value); } }
        /// <summary>Gets or sets the stored group 179 value.</summary>
        public short TextAttachmentPoint { get { return this.Get<short>(179); } set { this.Set(179, value); } }
        /// <summary>Gets or sets the stored group 45 value.</summary>
        public double Scale { get { return this.Get<double>(45); } set { this.Set(45, value); } }
        /// <summary>Gets or sets the stored group 271 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? TextAttachmentDirection { get { return this.Get<short?>(271); } set { this.Set(271, value); } }
        /// <summary>Gets or sets the stored group 272 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? TextBottomAttachment { get { return this.Get<short?>(272); } set { this.Set(272, value); } }
        /// <summary>Gets or sets the stored group 273 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? TextTopAttachment { get { return this.Get<short?>(273); } set { this.Set(273, value); } }
        /// <summary>Gets or sets the stored group 295 value; available in the qualified R2013+ profile, null omits it.</summary>
        public bool? LeaderExtendToText { get { return this.Get<bool?>(295); } set { this.Set(295, value); } }
    }
}
