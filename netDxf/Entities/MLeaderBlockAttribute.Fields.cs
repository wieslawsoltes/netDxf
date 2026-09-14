// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Blocks;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Entities
{
    /// <summary>Editable stored MLeaderBlockAttribute fields. Redundant values are retained independently; angles are radians and colors are DXF raw-color integers.</summary>
    public sealed partial class MLeaderBlockAttribute : MLeaderData
    {
        private static readonly MLeaderField[] fields = new MLeaderField[]
        {
            new MLeaderField(330, typeof(AttributeDefinition), null, true, 2007),
            new MLeaderField(177, typeof(short), (short)0, false, 2007),
            new MLeaderField(44, typeof(double), 1.0, false, 2007),
            new MLeaderField(302, typeof(string), string.Empty, false, 2007),
        };
        internal override MLeaderField[] Fields { get { return fields; } }
        /// <summary>Gets or sets the registered object reference; null is the absent DXF handle.</summary>
        public AttributeDefinition Definition { get { return this.Get<AttributeDefinition>(330); } set { this.Set(330, value); } }
        /// <summary>Gets or sets the stored group 177 value.</summary>
        public short Index { get { return this.Get<short>(177); } set { this.Set(177, value); } }
        /// <summary>Gets or sets the stored group 44 value.</summary>
        public double Width { get { return this.Get<double>(44); } set { this.Set(44, value); } }
        /// <summary>Gets or sets the stored group 302 value.</summary>
        public string Text { get { return this.Get<string>(302); } set { this.Set(302, value); } }
    }
}
