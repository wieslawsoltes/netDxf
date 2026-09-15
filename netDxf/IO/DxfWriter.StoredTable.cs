// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;
using netDxf.Entities;
namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareStoredTableClasses(netDxf.Collections.DxfClassCollection definitions)
        {
            if (!definitions.Contains("ACAD_TABLE")) return;
            var definition = definitions["ACAD_TABLE"];
            if (definition.CppClassName == "AcDbTable" && definition.IsEntity)
                definition.InstanceCount = this.doc.Blocks.Sum(b => b.Entities.OfType<StoredTable>().Count());
        }
        private void ValidateStoredTables()
        {
            foreach (var table in this.doc.Blocks.SelectMany(b => b.Entities).OfType<StoredTable>())
            {
                table.Validate(this.doc);
                foreach (DxfTag tag in table.Payload)
                {
                    if (!(tag.Value is string text)) continue;
                    CheckStyleUnicode(text);
                    if (!this.isBinary && text.Any(c => c == '\0' || c == '\r' || c == '\n'))
                        throw new InvalidOperationException("Stored TABLE contains an unsupported multiline text transport string.");
                }
            }
        }
        private void WriteStoredTable(StoredTable table)
        {
            foreach (DxfTag tag in table.Payload)
            {
                string name = table.ChangedDisplayName(tag);
                this.chunk.Write(tag.Code, name == null ? tag.Value : this.EncodeDatabaseString(name));
            }
            this.WriteXData(table.XData);
        }
    }
}
