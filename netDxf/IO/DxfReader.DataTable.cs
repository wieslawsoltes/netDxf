using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Header;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private sealed class DataTableColumnInput
        {
            internal DxfDataCellType Type;
            internal string Name;
            internal readonly List<object> Values = new List<object>();
        }
        private readonly List<Tuple<DxfDataTable, int, List<DataTableColumnInput>>> dataTableReferences = new List<Tuple<DxfDataTable, int, List<DataTableColumnInput>>>();

        private DatabaseRecord ReadDataTableRecord(List<DxfTag> tags)
        {
            var record = new DatabaseRecord();
            string handle = null; bool reactors = false, extension = false;
            var privateHeader = new List<DxfTag>();
            int start = 0;
            for (; start < tags.Count; start++)
            {
                DxfTag tag = tags[start];
                if (tag.Code == 100 || tag.Code == 1001) break;
                if (tag.Code == 5)
                {
                    if (handle != null) throw new FormatException("DATATABLE repeats its identity.");
                    handle = (string)tag.Value;
                }
                else if (tag.Code == 330)
                {
                    if (record.Metadata.Owner != null) throw new FormatException("DATATABLE repeats its owner.");
                    record.Metadata.Owner = (string)tag.Value;
                }
                else if (tag.Code == 102)
                {
                    string group = (string)tag.Value; int begin = start;
                    if (!group.StartsWith("{", StringComparison.Ordinal)) throw new FormatException("Invalid DATATABLE control group.");
                    while (++start < tags.Count && !(tags[start].Code == 102 && (string)tags[start].Value == "}"))
                        if (tags[start].Code == 102 || tags[start].Code == 100 || tags[start].Code == 1001) throw new FormatException("Invalid DATATABLE control framing.");
                    if (start == tags.Count) throw new FormatException("Unterminated DATATABLE control group.");
                    var content = tags.GetRange(begin + 1, start - begin - 1);
                    if (group == "{ACAD_REACTORS")
                    {
                        if (reactors || content.Any(value => value.Code != 330)) throw new FormatException("Invalid DATATABLE reactor group.");
                        reactors = true; record.Metadata.Reactors.AddRange(content.Select(value => (string)value.Value));
                    }
                    else if (group == "{ACAD_XDICTIONARY")
                    {
                        if (extension || content.Count != 1 || content[0].Code != 360) throw new FormatException("Invalid DATATABLE extension group.");
                        extension = true; record.Metadata.Extension = (string)content[0].Value;
                    }
                    else privateHeader.AddRange(tags.GetRange(begin, start - begin + 1));
                }
                else privateHeader.Add(tag);
            }
            if (handle == null) throw new FormatException("DATATABLE requires an identity.");
            if (privateHeader.Count != 0 || !this.ReadDataTablePayload(record, tags, start))
            {
                privateHeader.AddRange(tags.Skip(start));
                record.Object = new DxfOpaqueObject("DATATABLE", privateHeader);
            }
            record.Object.Handle = handle;
            return record;
        }

        private bool ReadDataTablePayload(DatabaseRecord record, List<DxfTag> tags, int start)
        {
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2004) return false;
            int end = tags.FindIndex(start, tag => tag.Code == 1001);
            if (end < 0) end = tags.Count;
            if (start == end || tags[start].Code != 100 || (string)tags[start].Value != "AcDbDataTable") return false;
            // Unknown versions, cell types and private tags require preservation of the entire payload.
            var publicCodes = new HashSet<short> { 100, 70, 90, 91, 1, 92, 2, 71, 93, 40, 3, 10, 20, 30, 11, 21, 31, 331, 360, 350, 340, 330 };
            for (int j = start + 1; j < end; j++)
            {
                DxfTag tag = tags[j];
                if (!publicCodes.Contains(tag.Code) || tag.Code == 100 && (string)tag.Value != "AcDbDataTable") return false;
                if (tag.Code == 70 && (short)tag.Value != 2 || tag.Code == 92 && ((int)tag.Value < 1 || (int)tag.Value > 11)) return false;
            }
            int i = start + 1;
            Func<short, object> take = code =>
            {
                if (i >= end || tags[i].Code != code) throw new FormatException("DATATABLE requires ordered group " + code + " at payload slot " + (i - start) + ".");
                return tags[i++].Value;
            };
            take(70);
            int count = (int)take(90), rows = (int)take(91);
            if (count < 0 || count > DxfDataTable.MaximumCells || rows < 0 || rows > DxfDataTable.MaximumCells || (long)count * rows > DxfDataTable.MaximumCells)
                throw new FormatException("DATATABLE dimensions exceed the admitted range.");
            var table = new DxfDataTable();
            var columns = new List<DataTableColumnInput>();
            try
            {
                table.Name = this.DecodeEncodedNonAsciiCharacters((string)take(1));
                for (int c = 0; c < count; c++)
                {
                    var column = new DataTableColumnInput { Type = (DxfDataCellType)(int)take(92), Name = this.DecodeEncodedNonAsciiCharacters((string)take(2)) };
                    DxfDataColumn.CheckText(column.Name);
                    for (int r = 0; r < rows; r++)
                    {
                        object value;
                        switch (column.Type)
                        {
                            case DxfDataCellType.Integer: value = take(93); break;
                            case DxfDataCellType.Double: value = take(40); break;
                            case DxfDataCellType.String: value = this.DecodeEncodedNonAsciiCharacters((string)take(3)); break;
                            case DxfDataCellType.Boolean:
                                short bit = (short)take(71);
                                if (bit != 0 && bit != 1) throw new FormatException("DATATABLE Boolean must be 0 or 1.");
                                value = bit == 1; break;
                            case DxfDataCellType.Point: value = new Vector3((double)take(10), (double)take(20), (double)take(30)); break;
                            case DxfDataCellType.Vector: value = new Vector3((double)take(11), (double)take(21), (double)take(31)); break;
                            case DxfDataCellType.ObjectId: value = take(331); break;
                            case DxfDataCellType.HardOwner: value = take(360); break;
                            case DxfDataCellType.SoftOwner: value = take(350); break;
                            case DxfDataCellType.HardPointer: value = take(340); break;
                            case DxfDataCellType.SoftPointer: value = take(330); break;
                            default: throw new FormatException("Invalid DATATABLE column type.");
                        }
                        column.Values.Add(value);
                    }
                    // Validate scalar values before retaining any deferred references.
                    if (column.Type < DxfDataCellType.ObjectId || column.Type > DxfDataCellType.SoftPointer)
                        new DxfDataColumn(column.Type, column.Name, column.Values);
                    columns.Add(column);
                }
            }
            catch (ArgumentException error) { throw new FormatException("Invalid DATATABLE value.", error); }
            if (i != end) throw new FormatException("DATATABLE contains trailing or repeated public fields.");
            if (end < tags.Count) this.ReadDatabaseXData(table, tags, end);
            record.Object = table;
            this.dataTableReferences.Add(Tuple.Create(table, rows, columns));
            return true;
        }
        private void ResolveDataTableReferences()
        {
            foreach (var pending in this.dataTableReferences)
            {
                var columns = new List<DxfDataColumn>();
                try
                {
                    foreach (DataTableColumnInput column in pending.Item3)
                    {
                        var values = new List<object>();
                        foreach (object input in column.Values)
                        {
                            if (column.Type < DxfDataCellType.ObjectId || column.Type > DxfDataCellType.SoftPointer) { values.Add(input); continue; }
                            string handle = (string)input;
                            DxfObject target = handle == "0" ? null : this.doc.GetObjectByHandle(handle);
                            if (target == null && handle != "0") throw new FormatException("Unresolved DATATABLE cell reference: " + handle);
                            values.Add(target);
                        }
                        columns.Add(new DxfDataColumn(column.Type, column.Name, values));
                    }
                    pending.Item1.SetLoadedColumns(pending.Item2, columns);
                }
                catch (ArgumentException error) { throw new FormatException("Invalid DATATABLE ownership or reference graph.", error); }
            }
        }
    }
}
