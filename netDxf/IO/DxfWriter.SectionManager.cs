// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteSectionManagerPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredSectionManager manager)) return false;
            foreach (DxfTag tag in manager.Tags) this.WriteDatabaseTag(tag, false);
            return true;
        }

        private void PrepareSectionManagerClasses(DxfClassCollection definitions)
        {
            foreach (string name in new[] { "SECTION_MANAGER", "SECTIONMANAGER" })
            {
                bool typed = this.doc.Objects.Items.Any(item => item is DxfStoredSectionManager && item.CodeName == name);
                if (!typed) continue;
                int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
                if (definitions.Contains(name))
                {
                    DxfClass definition = definitions[name];
                    if (definition.CppClassName != "AcDbSectionManager" || definition.IsEntity)
                        throw new InvalidDataException("CLASS conflicts with a stored section manager.");
                    definition.InstanceCount = count;
                }
                else definitions.Add(new DxfClass(name, "AcDbSectionManager", "ObjectDBX Classes")
                    { ProxyFlags = 1024, IsEntity = false, InstanceCount = count });
            }
        }
    }
}
