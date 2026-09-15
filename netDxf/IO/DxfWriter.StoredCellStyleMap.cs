// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredCellStyleMapPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredCellStyleMap content)) return false;
            foreach (DxfTag tag in content.Payload) this.WriteDatabaseTag(tag, false);
            return true;
        }
        private void PrepareStoredCellStyleMapClass(DxfClassCollection definitions)
        {
            const string name = "CELLSTYLEMAP";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            bool typed = this.doc.Objects.Items.Any(item => item is DxfStoredCellStyleMap);
            if (definitions.Contains(name))
            {
                var definition = definitions[name];
                if (definition.CppClassName != "AcDbCellStyleMap" || definition.IsEntity)
                { if (typed) throw new InvalidDataException("CLASS conflicts with stored CELLSTYLEMAP."); return; }
                if (typed || count == 0) definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass(name, "AcDbCellStyleMap", "ObjectDBX Classes") { ProxyFlags = 1152, IsEntity = false, InstanceCount = count });
        }
    }
}
