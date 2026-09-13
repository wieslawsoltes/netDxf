using netDxf.Collections;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private readonly DxfClassCollection classes = new DxfClassCollection();

        /// <summary>Gets the ordered class definitions read from or explicitly added to this document.</summary>
        /// <remarks>
        /// Definitions are not entity instances and have no object handles. Saving reconciles
        /// generated raster definitions without mutating this collection. Retaining a declaration
        /// does not implement or preserve the application-specific objects it describes.
        /// </remarks>
        public DxfClassCollection Classes { get { return this.classes; } }
    }
}
