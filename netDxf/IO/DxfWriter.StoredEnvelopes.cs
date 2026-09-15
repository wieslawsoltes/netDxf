using System.IO;
using System.Linq;
using netDxf.Objects;
using netDxf.Collections;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredEnvelopePayload(DxfDatabaseObject item)
        {
            if (item is DxfSpatialIndex index)
            {
                this.chunk.Write(100, "AcDbIndex"); this.chunk.Write(40, index.Timestamp); this.chunk.Write(100, "AcDbSpatialIndex");
            }
            else if (item is DxfVbaProject project)
            {
                this.chunk.Write(100, "AcDbVbaProject"); this.chunk.Write(90, project.DataLength);
                foreach (byte[] chunk in project.StoredChunks) this.chunk.Write(310, chunk);
            }
            else return false;
            return true;
        }
        private void PrepareStoredEnvelopeClasses(DxfClassCollection definitions)
        {
            if (!this.doc.Objects.Items.Any(item => item is DxfSpatialIndex)) return;
            const string name = "SPATIAL_INDEX";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            if (definitions.Contains(name))
            {
                DxfClass definition = definitions[name];
                if (definition.CppClassName != "AcDbSpatialIndex" || definition.IsEntity)
                    throw new InvalidDataException("CLASS conflicts with typed SPATIAL_INDEX.");
                definition.InstanceCount = count;
            }
            else definitions.Add(new DxfClass(name, "AcDbSpatialIndex", "ObjectDBX Classes") { ProxyFlags = 0, IsEntity = false, InstanceCount = count });
        }
    }
}
