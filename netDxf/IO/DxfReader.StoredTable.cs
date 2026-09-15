// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;
namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<StoredTable> storedTables = new List<StoredTable>();
        private StoredTable ReadStoredTable()
        {
            var tags = new List<DxfTag>(); var data = new List<XData>(); bool extended = false;
            while (this.chunk.Code != 0)
            {
                if (this.chunk.Code == 1001)
                {
                    extended = true;
                    string app = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    data.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app))); continue;
                }
                if (extended) throw new InvalidDataException("TABLE XData must follow its complete subclass payload.");
                if (this.chunk.Code >= 1000 && this.chunk.Code <= 1071)
                    throw new InvalidDataException("TABLE extended data must begin with an application registry.");
                tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value)); this.chunk.Next();
            }
            var table = new StoredTable(this.doc, tags, this.DecodeEncodedNonAsciiCharacters);
            table.XData.AddRange(data); this.storedTables.Add(table); return table;
        }
        private void ResolveStoredTables() { foreach (var table in this.storedTables) table.Resolve(); }
    }
}
