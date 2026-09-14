using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private bool TryReadMTextColumnTag(ref MTextColumns columns, ref double? definedHeight)
        {
            short code = this.chunk.Code;
            if (code == 46)
            {
                definedHeight = this.chunk.ReadDouble(); MTextColumns.NonNegative(definedHeight.Value, "DefinedHeight");
                this.chunk.Next(); return true;
            }
            if (code != 75 && code != 76 && code != 78 && code != 79 && code != 48 && code != 49 && !(code == 50 && columns != null && columns.Type == MTextColumnType.Dynamic && !columns.AutoHeight)) return false;
            if (columns == null) columns = new MTextColumns { Storage = MTextColumnStorage.Direct };
            if (columns.Storage != MTextColumnStorage.Direct) throw new InvalidDataException("Mixed MTEXT column representations.");
            switch (code)
            {
                case 75: columns.Type = (MTextColumnType)this.chunk.ReadShort(); break;
                case 76: columns.Count = this.chunk.ReadShort(); break;
                case 78: columns.FlowReversed = ReadColumnFlag(this.chunk.ReadShort()); break;
                case 79: columns.AutoHeight = ReadColumnFlag(this.chunk.ReadShort()); break;
                case 48: columns.Width = this.chunk.ReadDouble(); break;
                case 49: columns.Gutter = this.chunk.ReadDouble(); break;
                case 50:
                    double count = this.chunk.ReadDouble();
                    if (count != columns.Count || double.IsNaN(count) || double.IsInfinity(count) || count != Math.Truncate(count)) throw new InvalidDataException("Invalid MTEXT column height count.");
                    for (int i = 0; i < (int)count; i++)
                    {
                        this.chunk.Next();
                        if (this.chunk.Code != 50) throw new InvalidDataException("Truncated MTEXT column height sequence.");
                        columns.Heights.Add(this.chunk.ReadDouble());
                    }
                    break;
            }
            this.chunk.Next(); return true;
        }

        private static bool ReadColumnFlag(short value)
        {
            if (value != 0 && value != 1) throw new InvalidDataException("Invalid MTEXT column boolean flag.");
            return value == 1;
        }

        private MTextColumns ReadMTextEmbeddedColumns()
        {
            if (this.chunk.ReadString() != "Embedded Object") throw new InvalidDataException("Unrecognized MTEXT embedded object marker.");
            var columns = new MTextColumns { Storage = MTextColumnStorage.Embedded };
            int count = 0; double totalWidth = 0; bool hasType = false;
            var seen = new HashSet<short>();
            Vector3 embeddedDirection = Vector3.Zero, embeddedPosition = Vector3.Zero;
            this.chunk.Next();
            while (this.chunk.Code != 0 && this.chunk.Code != 1001)
            {
                short tag = this.chunk.Code;
                if (tag != 46 && !seen.Add(tag)) throw new InvalidDataException("Duplicate MTEXT embedded field: " + tag);
                switch (tag)
                {
                    case 10: embeddedDirection.X = this.chunk.ReadDouble(); break;
                    case 20: embeddedDirection.Y = this.chunk.ReadDouble(); break;
                    case 30: embeddedDirection.Z = this.chunk.ReadDouble(); break;
                    case 11: embeddedPosition.X = this.chunk.ReadDouble(); break;
                    case 21: embeddedPosition.Y = this.chunk.ReadDouble(); break;
                    case 31: embeddedPosition.Z = this.chunk.ReadDouble(); break;
                    case 40: columns.EmbeddedReferenceWidth = this.chunk.ReadDouble(); break;
                    case 70: if (this.chunk.ReadShort() != 1) throw new InvalidDataException("Unsupported MTEXT embedded object version."); break;
                    case 41: columns.DefinedHeight = this.chunk.ReadDouble(); break;
                    case 42: totalWidth = this.chunk.ReadDouble(); MTextColumns.NonNegative(totalWidth, "TotalWidth"); break;
                    case 43: columns.TotalHeight = this.chunk.ReadDouble(); break;
                    case 71: columns.Type = (MTextColumnType)this.chunk.ReadShort(); hasType = true; break;
                    case 72: count = this.chunk.ReadShort(); break;
                    case 73: columns.AutoHeight = ReadColumnFlag(this.chunk.ReadShort()); break;
                    case 74: columns.FlowReversed = ReadColumnFlag(this.chunk.ReadShort()); break;
                    case 44: columns.Width = this.chunk.ReadDouble(); break;
                    case 45: columns.Gutter = this.chunk.ReadDouble(); break;
                    case 46: columns.Heights.Add(this.chunk.ReadDouble()); break;
                    case 101: throw new InvalidDataException("Multiple embedded objects on one MTEXT are unsupported.");
                }
                this.chunk.Next();
            }
            if (!hasType) throw new InvalidDataException("MTEXT embedded object has no column type.");
            foreach (short required in new short[] { 70, 41, 42, 43, 71, 72, 44, 45, 73, 74 })
                if (!seen.Contains(required)) throw new InvalidDataException("Missing MTEXT embedded field: " + required);
            foreach (short first in new short[] { 10, 11 })
            {
                int components = (seen.Contains(first) ? 1 : 0) + (seen.Contains((short)(first + 10)) ? 1 : 0) + (seen.Contains((short)(first + 20)) ? 1 : 0);
                if (components != 0 && components != 3)
                    throw new InvalidDataException("An embedded MTEXT vector requires all three components: " + first);
            }
            if (seen.Contains(10)) columns.EmbeddedTextDirection = embeddedDirection;
            if (seen.Contains(11)) columns.EmbeddedInsertionPoint = embeddedPosition;
            if (columns.Type == MTextColumnType.None) count = 1;
            else if (columns.Type == MTextColumnType.Dynamic && columns.AutoHeight && count == 0)
            {
                double inferred = (totalWidth + columns.Gutter) / (columns.Width + columns.Gutter);
                if (double.IsNaN(inferred) || double.IsInfinity(inferred) || inferred < 1 || inferred > short.MaxValue || Math.Abs(inferred - Math.Round(inferred)) > 1e-5)
                    throw new InvalidDataException("Cannot infer an integral automatic MTEXT column count from total width.");
                count = (int)Math.Round(inferred);
            }
            columns.Count = count; columns.StoredTotalWidth = totalWidth; columns.Validate();
            return columns;
        }

        private void ReadMTextColumnXData(MText text)
        {
            XData acad;
            if (!text.XData.TryGetValue("ACAD", out acad)) return;
            var sections = new Dictionary<string, List<XDataRecord>>(StringComparer.Ordinal);
            var retained = new List<XDataRecord>();
            List<XDataRecord> active = null; string section = null;
            foreach (XDataRecord record in acad.XDataRecord)
            {
                string marker = record.Code == XDataCode.String ? record.Value as string : null;
                if (marker != null && (marker == "ACAD_MTEXT_COLUMN_INFO_BEGIN" || marker == "ACAD_MTEXT_COLUMNS_BEGIN" || marker == "ACAD_MTEXT_DEFINED_HEIGHT_BEGIN"))
                {
                    if (active != null || sections.ContainsKey(marker)) throw new InvalidDataException("Duplicate or nested MTEXT column XDATA section.");
                    section = marker; active = new List<XDataRecord>(); sections.Add(section, active); continue;
                }
                if (active != null)
                {
                    if (marker == section.Replace("_BEGIN", "_END")) { active = null; section = null; }
                    else active.Add(record);
                }
                else retained.Add(record);
            }
            if (active != null) throw new InvalidDataException("Unterminated MTEXT column XDATA section.");
            List<XDataRecord> info;
            MTextColumns columns = null;
            if (sections.TryGetValue("ACAD_MTEXT_COLUMN_INFO_BEGIN", out info))
            {
                if (text.Columns != null) throw new InvalidDataException("Mixed MTEXT column representations.");
                columns = new MTextColumns { Storage = MTextColumnStorage.LegacyLinked };
                for (int i = 0; i < info.Count; i++)
                {
                    short code = ColumnShort(info, i++);
                    if (i >= info.Count) throw new InvalidDataException("Truncated MTEXT column XDATA field.");
                    switch (code)
                    {
                        case 75: columns.Type = (MTextColumnType)ColumnShort(info, i); break;
                        case 76: columns.Count = ColumnShort(info, i); break;
                        case 78: columns.FlowReversed = ReadColumnFlag(ColumnShort(info, i)); break;
                        case 79: columns.AutoHeight = ReadColumnFlag(ColumnShort(info, i)); break;
                        case 48: columns.Width = ColumnReal(info, i); break;
                        case 49: columns.Gutter = ColumnReal(info, i); break;
                        case 50:
                            int count = ColumnShort(info, i);
                            if (count < 0) throw new InvalidDataException("Negative MTEXT column height count.");
                            for (int h = 0; h < count; h++) columns.Heights.Add(ColumnReal(info, ++i));
                            break;
                        default: throw new InvalidDataException("Unsupported MTEXT column XDATA field.");
                    }
                }
            }
            List<XDataRecord> linked;
            if (sections.TryGetValue("ACAD_MTEXT_COLUMNS_BEGIN", out linked))
            {
                if (columns == null) throw new InvalidDataException("MTEXT linked columns have no column definition.");
                if (linked.Count < 2 || ColumnShort(linked, 0) != 47 || ColumnShort(linked, 1) < 1)
                    throw new InvalidDataException("Invalid MTEXT linked column header.");
                for (int i = 2; i < linked.Count; i++)
                {
                    if (linked[i].Code != XDataCode.DatabaseHandle) throw new InvalidDataException("Expected MTEXT linked column handle.");
                    string handle = (string)linked[i].Value;
                    // Some independent writers pad unused handle slots with zero.
                    if (handle != "0") columns.PendingHandles.Add(handle);
                }
            }
            List<XDataRecord> height;
            if (sections.TryGetValue("ACAD_MTEXT_DEFINED_HEIGHT_BEGIN", out height))
            {
                if (height.Count != 2 || ColumnShort(height, 0) != 46) throw new InvalidDataException("Invalid MTEXT defined height XDATA.");
                text.DefinedHeight = ColumnReal(height, 1);
            }
            if (columns != null)
            {
                columns.DefinedHeight = text.DefinedHeight ?? 0;
                columns.TotalHeight = columns.DefinedHeight;
                foreach (double h in columns.Heights) columns.TotalHeight = Math.Max(columns.TotalHeight, h);
                text.Columns = columns;
            }
            if (sections.Count != 0)
            {
                acad.XDataRecord.Clear(); acad.XDataRecord.AddRange(retained);
                if (retained.Count == 0) text.XData.Remove("ACAD");
            }
        }

        private static short ColumnShort(IList<XDataRecord> records, int index)
        {
            if (index >= records.Count || records[index].Code != XDataCode.Int16) throw new InvalidDataException("Expected MTEXT column XDATA integer.");
            return (short)records[index].Value;
        }
        private static double ColumnReal(IList<XDataRecord> records, int index)
        {
            if (index >= records.Count || records[index].Code != XDataCode.Real) throw new InvalidDataException("Expected MTEXT column XDATA real.");
            return (double)records[index].Value;
        }

        private void ResolveMTextColumnLinks()
        {
            var claimed = new HashSet<MText>();
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    MText main = entity as MText;
                    if (main == null || main.Columns == null) continue;
                    MTextColumns columns = main.Columns;
                    foreach (string handle in columns.PendingHandles)
                    {
                        MText linked = this.doc.GetObjectByHandle(handle) as MText;
                        if (linked == null || linked == main || linked.Owner != main.Owner || linked.Columns != null || !claimed.Add(linked))
                            throw new InvalidDataException("MTEXT column link is missing, duplicated, nested, or belongs to another block: " + handle);
                        columns.LinkedColumns.Add(linked);
                    }
                    columns.PendingHandles.Clear();
                    // CAD output can store stale group 76. The actual linked graph is authoritative.
                    if (columns.Storage == MTextColumnStorage.LegacyLinked && columns.LinkedColumns.Count != 0)
                        columns.Count = columns.LinkedColumns.Count + 1;
                    if (columns.Storage == MTextColumnStorage.LegacyLinked && columns.Count != columns.LinkedColumns.Count + 1)
                        throw new InvalidDataException("MTEXT legacy column count has missing linked entities.");
                    try { columns.Validate(); }
                    catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                    { throw new InvalidDataException("Invalid MTEXT column definition.", exception); }
                }
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateMTextColumns()
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            var claimed = new HashSet<MText>(); bool acadNeeded = false;
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    MText text = entity as MText;
                    if (text == null) continue;
                    if (text.DefinedHeight.HasValue && version < DxfVersion.AutoCad2007) acadNeeded = true;
                    MTextColumns columns = text.Columns;
                    XData acad;
                    if ((columns != null || text.DefinedHeight.HasValue) && text.XData.TryGetValue("ACAD", out acad))
                        foreach (XDataRecord record in acad.XDataRecord)
                            if (record.Code == XDataCode.String &&
                                ((string)record.Value == "ACAD_MTEXT_COLUMN_INFO_BEGIN" || (string)record.Value == "ACAD_MTEXT_COLUMNS_BEGIN" ||
                                 (string)record.Value == "ACAD_MTEXT_DEFINED_HEIGHT_BEGIN"))
                                throw new InvalidOperationException("Typed MTEXT column data cannot be combined with raw ACAD column sections.");
                    if (columns == null) continue;
                    columns.Validate();
                    if (columns.PendingHandles.Count != 0) throw new InvalidOperationException("MTEXT contains unresolved linked column handles.");
                    if (columns.Storage == MTextColumnStorage.Embedded && version < DxfVersion.AutoCad2018)
                        throw new NotSupportedException("Embedded MTEXT columns require DXF 2018. Use ConvertToLinkedColumns with explicit text partitions before down-saving.");
                    if (columns.Storage == MTextColumnStorage.LegacyLinked && version >= DxfVersion.AutoCad2018)
                        throw new NotSupportedException("Legacy linked MTEXT columns require a pre-2018 document. Use ConvertToEmbeddedColumns and explicitly replace all old entities.");
                    if (columns.Storage == MTextColumnStorage.Direct && version < DxfVersion.AutoCad2007)
                        throw new NotSupportedException("Direct MTEXT column tags require the 2007 or later writer profile.");
                    if (columns.Storage == MTextColumnStorage.LegacyLinked)
                    {
                        acadNeeded = true;
                        if (columns.Count != columns.LinkedColumns.Count + 1) throw new InvalidOperationException("MTEXT linked column count does not match Count.");
                        foreach (MText linked in columns.LinkedColumns)
                            if (linked == text || linked.Owner != text.Owner || linked.Handle == null || !claimed.Add(linked))
                                throw new InvalidOperationException("Every linked MTEXT must be a distinct entity in the same document block as its first column.");
                    }
                }
            if (acadNeeded) this.doc.ApplicationRegistries.Add(ApplicationRegistry.Default);
        }

        private void WriteMTextColumnDefinition(MText text, Vector3 direction)
        {
            MTextColumns c = text.Columns;
            double? defined = c == null ? text.DefinedHeight : (double?)c.DefinedHeight;
            if (defined.HasValue && this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
                this.chunk.Write(46, defined.Value);
            if (c == null || c.Storage == MTextColumnStorage.LegacyLinked) return;
            if (c.Storage == MTextColumnStorage.Direct)
            {
                this.chunk.Write(75, (short)c.Type); this.chunk.Write(76, (short)c.Count);
                this.chunk.Write(78, (short)(c.FlowReversed ? 1 : 0)); this.chunk.Write(79, (short)(c.AutoHeight ? 1 : 0));
                this.chunk.Write(48, c.Width); this.chunk.Write(49, c.Gutter);
                if (c.Heights.Count != 0)
                {
                    this.chunk.Write(50, (double)c.Heights.Count);
                    foreach (double height in c.Heights) this.chunk.Write(50, height);
                }
                return;
            }
            this.chunk.Write(101, "Embedded Object"); this.chunk.Write(70, (short)1);
            Vector3 embeddedDirection = c.EmbeddedTextDirection ?? direction;
            Vector3 embeddedPosition = c.EmbeddedInsertionPoint ?? text.Position;
            this.chunk.Write(10, embeddedDirection.X); this.chunk.Write(20, embeddedDirection.Y); this.chunk.Write(30, embeddedDirection.Z);
            this.chunk.Write(11, embeddedPosition.X); this.chunk.Write(21, embeddedPosition.Y); this.chunk.Write(31, embeddedPosition.Z);
            this.chunk.Write(40, c.EmbeddedReferenceWidth ?? text.RectangleWidth); this.chunk.Write(41, c.DefinedHeight);
            this.chunk.Write(42, c.StoredTotalWidth ?? c.TotalWidth); this.chunk.Write(43, c.TotalHeight);
            this.chunk.Write(71, (short)c.Type);
            this.chunk.Write(72, (short)(c.Type == MTextColumnType.Dynamic && c.AutoHeight ? 0 : c.Count));
            this.chunk.Write(44, c.Width); this.chunk.Write(45, c.Gutter);
            this.chunk.Write(73, (short)(c.AutoHeight ? 1 : 0)); this.chunk.Write(74, (short)(c.FlowReversed ? 1 : 0));
            foreach (double height in c.Heights) this.chunk.Write(46, height);
        }

        private void WriteMTextColumnXData(MText text)
        {
            var records = new List<XDataRecord>();
            foreach (string app in text.XData.AppIds)
            {
                if (string.Equals(app, "ACAD", StringComparison.OrdinalIgnoreCase)) records.AddRange(text.XData[app].XDataRecord);
                else this.WriteXDataRecords(app, text.XData[app].XDataRecord);
            }
            MTextColumns c = text.Columns;
            if (c != null && c.Storage == MTextColumnStorage.LegacyLinked)
            {
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_COLUMN_INFO_BEGIN"));
                AddColumnField(records, 75, (short)c.Type); AddColumnField(records, 79, (short)(c.AutoHeight ? 1 : 0));
                AddColumnField(records, 76, (short)c.Count); AddColumnField(records, 78, (short)(c.FlowReversed ? 1 : 0));
                AddColumnField(records, 48, c.Width); AddColumnField(records, 49, c.Gutter);
                if (c.Heights.Count != 0)
                {
                    AddColumnField(records, 50, (short)c.Heights.Count);
                    foreach (double h in c.Heights) records.Add(new XDataRecord(XDataCode.Real, h));
                }
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_COLUMN_INFO_END"));
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_COLUMNS_BEGIN"));
                AddColumnField(records, 47, (short)c.Count);
                foreach (MText linked in c.LinkedColumns) records.Add(new XDataRecord(XDataCode.DatabaseHandle, linked.Handle));
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_COLUMNS_END"));
            }
            double? defined = c == null ? text.DefinedHeight : (double?)c.DefinedHeight;
            if (defined.HasValue && this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2018 &&
                (c == null || c.Type != MTextColumnType.Dynamic || c.AutoHeight))
            {
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_DEFINED_HEIGHT_BEGIN"));
                AddColumnField(records, 46, defined.Value);
                records.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_DEFINED_HEIGHT_END"));
            }
            if (records.Count != 0) this.WriteXDataRecords("ACAD", records);
        }

        private static void AddColumnField(List<XDataRecord> records, short code, object value)
        {
            records.Add(new XDataRecord(XDataCode.Int16, code));
            records.Add(new XDataRecord(value is short ? XDataCode.Int16 : XDataCode.Real, value));
        }
    }
}
