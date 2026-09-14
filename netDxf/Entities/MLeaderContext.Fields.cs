// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderContext fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderContext : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(40, typeof(double), 1.0, false, 2007),
            new MLeaderField(10, typeof(Vector3), Vector3.Zero, false, 2007),
            new MLeaderField(41, typeof(double), 4.0, false, 2007),
            new MLeaderField(140, typeof(double), 4.0, false, 2007),
            new MLeaderField(145, typeof(double), 2.0, false, 2007),
            new MLeaderField(174, typeof(short), (short)1, false, 2007),
            new MLeaderField(175, typeof(short), (short)1, false, 2007),
            new MLeaderField(176, typeof(short), (short)0, false, 2007),
            new MLeaderField(177, typeof(short), (short)0, false, 2007),
            new MLeaderField(110, typeof(Vector3), Vector3.Zero, false, 2007),
            new MLeaderField(111, typeof(Vector3), Vector3.UnitX, false, 2007),
            new MLeaderField(112, typeof(Vector3), Vector3.UnitY, false, 2007),
            new MLeaderField(297, typeof(bool), false, false, 2007),
            new MLeaderField(272, typeof(short), null, false, 2010),
            new MLeaderField(273, typeof(short), null, false, 2010),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 40 value.</summary>
        public double Scale { get { return this.Get<double>(40); } set { this.Set(40, value); } }
        /// <summary>Gets or sets the stored group 10 value.</summary>
        public Vector3 BasePoint { get { return this.Get<Vector3>(10); } set { this.Set(10, value); } }
        /// <summary>Gets or sets the stored group 41 value.</summary>
        public double TextHeight { get { return this.Get<double>(41); } set { this.Set(41, value); } }
        /// <summary>Gets or sets the stored group 140 value.</summary>
        public double ArrowHeadSize { get { return this.Get<double>(140); } set { this.Set(140, value); } }
        /// <summary>Gets or sets the stored group 145 value.</summary>
        public double LandingGap { get { return this.Get<double>(145); } set { this.Set(145, value); } }
        /// <summary>Gets or sets the stored group 174 value.</summary>
        public short TextLeftAttachment { get { return this.Get<short>(174); } set { this.Set(174, value); } }
        /// <summary>Gets or sets the stored group 175 value.</summary>
        public short TextRightAttachment { get { return this.Get<short>(175); } set { this.Set(175, value); } }
        /// <summary>Gets or sets the stored group 176 value.</summary>
        public short TextAlignment { get { return this.Get<short>(176); } set { this.Set(176, value); } }
        /// <summary>Gets or sets the stored group 177 value.</summary>
        public short BlockConnectionType { get { return this.Get<short>(177); } set { this.Set(177, value); } }
        /// <summary>Gets or sets the stored group 110 value.</summary>
        public Vector3 PlaneOrigin { get { return this.Get<Vector3>(110); } set { this.Set(110, value); } }
        /// <summary>Gets or sets the stored group 111 value.</summary>
        public Vector3 PlaneXAxis { get { return this.Get<Vector3>(111); } set { this.Set(111, value); } }
        /// <summary>Gets or sets the stored group 112 value.</summary>
        public Vector3 PlaneYAxis { get { return this.Get<Vector3>(112); } set { this.Set(112, value); } }
        /// <summary>Gets or sets the stored group 297 value.</summary>
        public bool PlaneNormalReversed { get { return this.Get<bool>(297); } set { this.Set(297, value); } }
        /// <summary>Gets or sets the stored group 272 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? TopAttachment { get { return this.Get<short?>(272); } set { this.Set(272, value); } }
        /// <summary>Gets or sets the stored group 273 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? BottomAttachment { get { return this.Get<short?>(273); } set { this.Set(273, value); } }
    }
}
