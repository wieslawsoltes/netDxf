// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderBlockContent fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderBlockContent : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(341, typeof(BlockRecord), null, true, 2007),
            new MLeaderField(14, typeof(Vector3), Vector3.UnitZ, false, 2007),
            new MLeaderField(15, typeof(Vector3), Vector3.Zero, false, 2007),
            new MLeaderField(16, typeof(Vector3), new Vector3(1,1,1), false, 2007),
            new MLeaderField(46, typeof(double), 0.0, false, 2007),
            new MLeaderField(93, typeof(int), unchecked((int)0xC1000000), false, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord Block { get { return this.Get<BlockRecord>(341); } set { this.Set(341, value); } }
        /// <summary>Gets or sets the stored group 14 value.</summary>
        public Vector3 Normal { get { return this.Get<Vector3>(14); } set { this.Set(14, value); } }
        /// <summary>Gets or sets the stored group 15 value.</summary>
        public Vector3 Position { get { return this.Get<Vector3>(15); } set { this.Set(15, value); } }
        /// <summary>Gets or sets the stored group 16 value.</summary>
        public Vector3 Scale { get { return this.Get<Vector3>(16); } set { this.Set(16, value); } }
        /// <summary>Gets or sets the stored group 46 value.</summary>
        public double Rotation { get { return this.Get<double>(46); } set { this.Set(46, value); } }
        /// <summary>Gets or sets the stored group 93 value.</summary>
        public int Color { get { return this.Get<int>(93); } set { this.Set(93, value); } }
    }
}
