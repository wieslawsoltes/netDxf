// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredTableGeometryPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredTableGeometry content)) return false;
            foreach (DxfTag tag in content.Payload) this.WriteDatabaseTag(tag, false);
            return true;
        }
        private void PrepareStoredTableGeometryClass(DxfClassCollection definitions)
        {
            const string name = "TABLEGEOMETRY";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            bool typed = this.doc.Objects.Items.Any(item => item is DxfStoredTableGeometry);
            if (definitions.Contains(name))
            {
                var definition = definitions[name];
                if (definition.CppClassName != "AcDbTableGeometry" || definition.IsEntity)
                { if (typed) throw new InvalidDataException("CLASS conflicts with stored TABLEGEOMETRY."); return; }
                if (typed || count == 0) definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass(name, "AcDbTableGeometry", "ObjectDBX Classes") { ProxyFlags = 1152, IsEntity = false, InstanceCount = count });
        }
    }
}
