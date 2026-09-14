// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    /// <summary>An inert Body SAT payload, supported in DXF 2000 through 2010.</summary>
    public sealed class Body : AcisEntity
    {
        /// <summary>Creates an empty entity; set a SAT payload before saving.</summary>
        public Body() : base(EntityType.Body, DxfObjectCode.Body) { }

        /// <summary>Creates a detached copy of the payload and common entity metadata.</summary>
        public override object Clone()
        {
            var copy = new Body();
            this.CopyTo(copy);
            return copy;
        }
    }
}
