using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareLayerIndexClass(DxfClassCollection definitions)
        {
            this.PrepareStoredEnvelopeClass(definitions, "LAYER_INDEX", "AcDbLayerIndex", "ObjectDBX Classes", 0,
                this.doc.Objects.Items.Any(item => item is DxfLayerIndex));
        }
        private bool WriteLayerIndexPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfLayerIndex index)) return false;
            this.chunk.Write(100, "AcDbIndex"); this.chunk.Write(40, index.Timestamp);
            this.chunk.Write(100, "AcDbLayerIndex");
            foreach (DxfLayerIndexEntry entry in index.Entries)
            {
                this.chunk.Write(8, this.EncodeDatabaseString(entry.LayerName));
                this.chunk.Write(360, entry.Buffer.Handle);
                this.chunk.Write(90, entry.Count);
            }
            return true;
        }
    }
}
