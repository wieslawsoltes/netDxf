// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderArrowHead fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderArrowHead : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(94, typeof(int), 0, false, 2007),
            new MLeaderField(345, typeof(BlockRecord), null, true, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 94 value.</summary>
        public int Index { get { return this.Get<int>(94); } set { this.Set(94, value); } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public BlockRecord Block { get { return this.Get<BlockRecord>(345); } set { this.Set(345, value); } }
    }
}
