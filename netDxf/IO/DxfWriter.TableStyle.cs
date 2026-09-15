// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.IO;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private bool WriteTableStylePayload(DxfDatabaseObject item)
        {
            if (!(item is DxfTableStyle style)) return false;
            foreach (DxfTag tag in style.Tags)
            {
                string name = style.ChangedStyleName(tag);
                if (name == null) this.WriteDatabaseTag(tag, false);
                else this.chunk.Write(tag.Code, this.EncodeDatabaseString(name));
            }
            return true;
        }
        private void PrepareTableStyleClass(DxfClassCollection definitions)
        {
            const string name = "TABLESTYLE";
            int count = this.doc.Objects.Items.Count(item => item.CodeName == name);
            bool typed = this.doc.Objects.Items.Any(item => item is DxfTableStyle);
            if (definitions.Contains(name))
            {
                var definition = definitions[name];
                if (definition.CppClassName != "AcDbTableStyle" || definition.IsEntity)
                {
                    if (typed) throw new InvalidDataException("CLASS conflicts with stored TABLESTYLE.");
                    return;
                }
                if (typed || count == 0) definition.InstanceCount = count;
            }
            else if (typed)
                definitions.Add(new DxfClass(name, "AcDbTableStyle", "ObjectDBX Classes") { ProxyFlags = 4095, IsEntity = false, InstanceCount = count });
        }
    }
}
