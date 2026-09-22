// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    // DesignCenter Data is a named subsection of ACAD. Its first two Int16
    // records are the schema version and insertion units, not arbitrary pairs.
    internal static class BlockRecordXData
    {
        private static bool Control(XDataRecord record, string value)
        {
            return record.Code == XDataCode.ControlString && (string)record.Value == value;
        }

        private static int UnitsSlot(IList<XDataRecord> records)
        {
            if (records == null) return -1;
            int slot = -1, depth = 0;
            for (int i = 0; i < records.Count; i++)
            {
                XDataRecord record = records[i];
                if (record == null) throw new FormatException("Null record in block ACAD extended data.");
                if (Control(record, "{")) depth++;
                else if (Control(record, "}"))
                {
                    if (depth == 0) throw new FormatException("Unbalanced block ACAD extended data.");
                    depth--;
                }
                else if (depth == 0 && record.Code == XDataCode.String &&
                    string.Equals((string)record.Value, "DesignCenter Data", StringComparison.OrdinalIgnoreCase))
                {
                    if (slot >= 0) throw new FormatException("Multiple DesignCenter Data lists are ambiguous.");
                    if (i + 3 >= records.Count || records[i + 1] == null || !Control(records[i + 1], "{") ||
                        records[i + 2] == null || records[i + 2].Code != XDataCode.Int16 ||
                        records[i + 3] == null || records[i + 3].Code != XDataCode.Int16)
                        throw new FormatException("DesignCenter Data requires a version and insertion-unit Int16.");
                    slot = i + 3;
                    // Continue through the entire list, validating balance and preserving
                    // unknown tail records, including any balanced nested extensions.
                }
            }
            if (depth != 0) throw new FormatException("Unbalanced block ACAD extended data.");
            return slot;
        }

        internal static short? ReadUnits(IList<XDataRecord> records)
        {
            int slot = UnitsSlot(records);
            return slot < 0 ? (short?)null : (short)records[slot].Value;
        }

        internal static IList<XDataRecord> WithUnits(IList<XDataRecord> records, short units)
        {
            int slot = UnitsSlot(records);
            var result = records == null ? new List<XDataRecord>() : new List<XDataRecord>(records);
            if (slot >= 0)
            {
                if ((short)records[slot].Value != units)
                    result[slot] = new XDataRecord(XDataCode.Int16, units);
            }
            else
            {
                result.Add(new XDataRecord(XDataCode.String, "DesignCenter Data"));
                result.Add(XDataRecord.OpenControlString);
                result.Add(new XDataRecord(XDataCode.Int16, (short)1));
                result.Add(new XDataRecord(XDataCode.Int16, units));
                result.Add(XDataRecord.CloseControlString);
            }
            return result;
        }
    }
}
