// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderNode fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderNode : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(290, typeof(bool), false, false, 2007),
            new MLeaderField(291, typeof(bool), false, false, 2007),
            new MLeaderField(10, typeof(Vector3), Vector3.Zero, false, 2007),
            new MLeaderField(11, typeof(Vector3), Vector3.UnitX, false, 2007),
            new MLeaderField(90, typeof(int), 0, false, 2007),
            new MLeaderField(40, typeof(double), 1.0, false, 2007),
            new MLeaderField(271, typeof(short), null, false, 2010),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 290 value.</summary>
        public bool HasLastLeaderPoint { get { return this.Get<bool>(290); } set { this.Set(290, value); } }
        /// <summary>Gets or sets the stored group 291 value.</summary>
        public bool HasDoglegVector { get { return this.Get<bool>(291); } set { this.Set(291, value); } }
        /// <summary>Gets or sets the stored group 10 value.</summary>
        public Vector3 LastLeaderPoint { get { return this.Get<Vector3>(10); } set { this.Set(10, value); } }
        /// <summary>Gets or sets the stored group 11 value.</summary>
        public Vector3 DoglegVector { get { return this.Get<Vector3>(11); } set { this.Set(11, value); } }
        /// <summary>Gets or sets the stored group 90 value.</summary>
        public int Index { get { return this.Get<int>(90); } set { this.Set(90, value); } }
        /// <summary>Gets or sets the stored group 40 value.</summary>
        public double DoglegLength { get { return this.Get<double>(40); } set { this.Set(40, value); } }
        /// <summary>Gets or sets the stored group 271 value; available in the qualified R2010+ profile, null omits it.</summary>
        public short? AttachmentDirection { get { return this.Get<short?>(271); } set { this.Set(271, value); } }
    }
}
