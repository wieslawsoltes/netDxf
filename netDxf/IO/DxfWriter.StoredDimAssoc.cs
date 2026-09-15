// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteStoredDimAssocPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredDimAssoc association)) return false;
            foreach (DxfTag tag in association.Tags) this.WriteDatabaseTag(tag, false);
            return true;
        }
        private void PrepareStoredDimAssocClass(DxfClassCollection definitions)
        {
            const string name = "DIMASSOC";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            bool typed = this.doc.Objects.Items.Any(item => item is DxfStoredDimAssoc);
            if (definitions.Contains(name))
            {
                var definition = definitions[name];
                if (definition.CppClassName != "AcDbDimAssoc" || definition.IsEntity)
                { if (typed) throw new InvalidDataException("CLASS conflicts with stored DIMASSOC."); return; }
                if (typed || count == 0) definition.InstanceCount = count;
            }
            else if (typed) definitions.Add(new DxfClass(name, "AcDbDimAssoc", "ObjectDBX Classes") { ProxyFlags = 0, IsEntity = false, InstanceCount = count });
        }
    }
}
