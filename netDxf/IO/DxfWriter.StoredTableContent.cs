// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredTableContentPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredTableContent content)) return false;
            foreach (DxfTag tag in content.Payload) this.WriteDatabaseTag(tag, false);
            return true;
        }
        private void PrepareStoredTableContentClass(DxfClassCollection definitions)
        {
            const string name = "TABLECONTENT";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            bool typed = this.doc.Objects.Items.Any(item => item is DxfStoredTableContent);
            if (definitions.Contains(name))
            {
                var definition = definitions[name];
                if (definition.CppClassName != "AcDbTableContent" || definition.IsEntity)
                { if (typed) throw new InvalidDataException("CLASS conflicts with stored TABLECONTENT."); return; }
                if (typed || count == 0) definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass(name, "AcDbTableContent", "ObjectDBX Classes") { ProxyFlags = 1152, IsEntity = false, InstanceCount = count });
        }
    }
}
