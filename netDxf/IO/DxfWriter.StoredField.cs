// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareStoredFieldClass(DxfClassCollection definitions)
        {
            var fields = this.doc.Objects.Items.OfType<DxfStoredField>().ToList();
            foreach (var field in fields) field.ValidateSource(this.doc);
            int count = this.doc.Objects.Items.Count(item => item.CodeName == "FIELD");
            if (fields.Count == 0) return;
            if (definitions.Contains("FIELD"))
            {
                DxfClass definition = definitions["FIELD"];
                if (definition.CppClassName != "AcDbField" || definition.IsEntity)
                    throw new System.IO.InvalidDataException("CLASS conflicts with stored FIELD objects.");
                definition.InstanceCount = count;
            }
            else definitions.Add(new DxfClass("FIELD", "AcDbField", "ObjectDBX Classes")
                { ProxyFlags = 1152, IsEntity = false, InstanceCount = count });
        }
        private bool WriteStoredFieldPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfStoredField field)) return false;
            field.ValidateSource(this.doc);
            foreach (DxfTag tag in field.Payload) this.WriteDatabaseTag(tag, false);
            return true;
        }
    }
}
