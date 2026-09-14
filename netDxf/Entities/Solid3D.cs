// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    /// <summary>An inert Solid3D SAT payload, supported in DXF 2000 through 2010.</summary>
    public sealed class Solid3D : AcisEntity
    {
        /// <summary>Creates an empty entity; set a SAT payload before saving.</summary>
        public Solid3D() : base(EntityType.Solid3D, DxfObjectCode.Solid3D) { }

        private string historyHandle;
        /// <summary>Gets or sets absent (null) or explicitly null ("0") modeler history, DXF 2007–2010.</summary>
        /// <remarks>Live history references require an unsupported history object graph and are rejected.</remarks>
        public string HistoryHandle
        {
            get { return this.historyHandle; }
            set
            {
                if (value != null && value != "0") throw new System.NotSupportedException("Live ACIS history references are unsupported; use DxfRawDocument to preserve the complete graph.");
                this.historyHandle = value;
            }
        }

        /// <summary>Creates a detached copy of the payload and common entity metadata.</summary>
        public override object Clone()
        {
            var copy = new Solid3D();
            this.CopyTo(copy);
            copy.HistoryHandle = this.HistoryHandle;
            return copy;
        }
    }
}
