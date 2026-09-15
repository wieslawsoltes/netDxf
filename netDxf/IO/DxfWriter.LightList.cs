using System;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareLightListClass(DxfClassCollection definitions)
        {
            bool typed = this.doc.Objects.Items.Any(o => o is DxfLightList);
            int count = this.doc.Objects.Items.Count(o => o.CodeName == "LIGHTLIST");
            if (definitions.Contains("LIGHTLIST"))
            {
                DxfClass definition = definitions["LIGHTLIST"];
                if (definition.CppClassName != "AcDbLightList" || definition.IsEntity)
                {
                    if (typed) throw new System.IO.InvalidDataException("CLASS conflicts with typed LIGHTLIST.");
                    return;
                }
                definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass("LIGHTLIST", "AcDbLightList", "SCENEOE") { ProxyFlags = 1025, IsEntity = false, InstanceCount = count });
        }
        private bool WriteLightListPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfLightList list)) return false;
            this.chunk.Write(100, "AcDbLightList");
            this.chunk.Write(90, list.StoredVersion);
            this.chunk.Write(90, list.Entries.Count);
            foreach (DxfLightListEntry entry in list.Entries)
            {
                this.chunk.Write(5, entry.Light.Handle);
                this.chunk.Write(1, this.EncodeDatabaseString(entry.Name));
            }
            return true;
        }
    }
}
