// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    /// <summary>An inert Region SAT payload, supported in DXF 2000 through 2010.</summary>
    public sealed class Region : AcisEntity
    {
        /// <summary>Creates an empty entity; set a SAT payload before saving.</summary>
        public Region() : base(EntityType.Region, DxfObjectCode.Region) { }

        /// <summary>Creates a detached copy of the payload and common entity metadata.</summary>
        public override object Clone()
        {
            var copy = new Region();
            this.CopyTo(copy);
            return copy;
        }
    }
}
