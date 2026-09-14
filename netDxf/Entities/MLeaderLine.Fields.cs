// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderLine fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderLine : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(91, typeof(int), 0, false, 2007),
            new MLeaderField(92, typeof(int), unchecked((int)0xC1000000), false, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the stored group 91 value.</summary>
        public int Index { get { return this.Get<int>(91); } set { this.Set(91, value); } }
        /// <summary>Gets or sets the stored group 92 value.</summary>
        public int Color { get { return this.Get<int>(92); } set { this.Set(92, value); } }
    }
}
