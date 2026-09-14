using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private DxfObjectDatabase objectDatabase;
        /// <summary>Gets the registered nongraphical object database.</summary>
        public DxfObjectDatabase Objects { get { return this.objectDatabase ?? (this.objectDatabase = new DxfObjectDatabase(this)); } }
        /// <summary>Gets application-owned entries in the named object dictionary.</summary>
        /// <remarks>Existing layout, group, style and definition collections keep their established public APIs.</remarks>
        public DxfDictionary NamedObjects { get { return this.Objects.Root; } }
    }
}
