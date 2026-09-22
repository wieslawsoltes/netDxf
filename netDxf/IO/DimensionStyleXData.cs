// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.IO
{
    // DSTYLE is one top-level named list inside ACAD, not the entire application.
    // Keep the original records immutable and share this boundary between reading and writing.
    internal static class DimensionStyleXData
    {
        private static bool IsControl(XDataRecord record, string value)
        {
            return record.Code == XDataCode.ControlString && (string)record.Value == value;
        }

        private static XDataCode? ManagedType(short code, bool toleranceOnly)
        {
            if (toleranceOnly) return code == 140 ? (XDataCode?)XDataCode.Real : null;
            // These are the fields materialized by ReadDimensionStyleOverrideXData.
            // In particular 43 (DIMDLI), which that reader ignores, remains opaque.
            switch (code)
            {
                case 3: case 4:
                    return XDataCode.String;
                case 40: case 41: case 42: case 44: case 45: case 46: case 47: case 48: case 49:
                case 140: case 141: case 142: case 143: case 144: case 145: case 146: case 147: case 148:
                    return XDataCode.Real;
                case 69: case 70: case 71: case 72: case 73: case 74: case 75: case 76: case 77: case 78: case 79:
                case 170: case 171: case 172: case 173: case 174: case 175: case 176: case 177: case 178: case 179:
                case 271: case 272: case 273: case 274: case 275: case 276: case 277: case 278: case 279:
                case 280: case 281: case 282: case 283: case 284: case 285: case 286: case 288: case 289:
                case 290: case 294: case 371: case 372:
                    return XDataCode.Int16;
                case 340: case 341: case 342: case 343: case 344: case 345: case 346: case 347:
                    return XDataCode.DatabaseHandle;
                default:
                    return null;
            }
        }

        // Return the inclusive marker/close indexes. Reject ambiguous or malformed framing
        // rather than discard data or expose a partially decoded override dictionary.
        private static void Locate(IList<XDataRecord> records, bool toleranceOnly, out int start, out int end)
        {
            start = -1;
            end = -1;
            if (records == null) return;
            int depth = 0;
            for (int i = 0; i < records.Count; i++)
            {
                XDataRecord record = records[i];
                if (record == null) throw new FormatException("Null record in ACAD extended data.");
                if (IsControl(record, "{")) depth++;
                else if (IsControl(record, "}"))
                {
                    if (depth == 0) throw new FormatException("Unbalanced ACAD extended data.");
                    depth--;
                }
                else if (depth == 0 && record.Code == XDataCode.String &&
                    string.Equals((string)record.Value, "DSTYLE", StringComparison.OrdinalIgnoreCase))
                {
                    if (start >= 0) throw new FormatException("Multiple top-level DSTYLE lists are ambiguous.");
                    start = i;
                    if (++i >= records.Count || records[i] == null || !IsControl(records[i], "{"))
                        throw new FormatException("DSTYLE must be followed by an opening control string.");
                    var identifiers = new HashSet<short>();
                    for (i++; ; i += 2)
                    {
                        if (i >= records.Count || records[i] == null)
                            throw new FormatException("Unterminated DSTYLE list.");
                        if (IsControl(records[i], "}")) { end = i; break; }
                        if (records[i].Code != XDataCode.Int16)
                            throw new FormatException("DSTYLE identifiers must be Int16 records.");
                        short code = (short)records[i].Value;
                        if (!identifiers.Add(code)) throw new FormatException("Duplicate DSTYLE identifier.");
                        if (i + 1 >= records.Count || records[i + 1] == null ||
                            records[i + 1].Code == XDataCode.ControlString)
                            throw new FormatException("DSTYLE requires complete scalar identifier/value pairs.");
                        XDataCode? expected = ManagedType(code, toleranceOnly);
                        if (expected.HasValue && records[i + 1].Code != expected.Value)
                            throw new FormatException("DSTYLE value has the wrong record type.");
                    }
                }
            }
            if (depth != 0) throw new FormatException("Unbalanced ACAD extended data.");
        }

        internal static IList<XDataRecord> ForReading(IList<XDataRecord> records, bool toleranceOnly = false)
        {
            Locate(records, toleranceOnly, out int start, out int end);
            var selected = new List<XDataRecord>();
            if (start < 0) return selected;
            // The old semantic decoder receives only the real top-level list. A DSTYLE
            // string inside an opaque nested list cannot be mistaken for a dimension override.
            for (int i = start; i <= end; i++) selected.Add(records[i]);
            return selected;
        }

        internal static IList<XDataRecord> WithOverrides(IList<XDataRecord> original,
            IList<XDataRecord> generated, bool toleranceOnly = false)
        {
            Locate(original, toleranceOnly, out int start, out int end);
            Locate(generated, toleranceOnly, out int generatedStart, out int generatedEnd);
            var replacements = new Dictionary<short, int>();
            if (generatedStart >= 0)
                for (int i = generatedStart + 2; i < generatedEnd; i += 2)
                    replacements.Add((short)generated[i].Value, i);

            var body = new List<XDataRecord>();
            var emitted = new HashSet<short>();
            if (start >= 0)
            {
                for (int i = start + 2; i < end; i += 2)
                {
                    short code = (short)original[i].Value;
                    if (!ManagedType(code, toleranceOnly).HasValue)
                    {
                        // Unsupported scalar pairs are not owned by this serializer.
                        body.Add(original[i]);
                        body.Add(original[i + 1]);
                    }
                    else if (replacements.TryGetValue(code, out int replacement))
                    {
                        body.Add(generated[replacement]);
                        body.Add(generated[replacement + 1]);
                        emitted.Add(code);
                    }
                    // A removed typed override must not be resurrected from stale XData.
                }
            }
            if (generatedStart >= 0)
                for (int i = generatedStart + 2; i < generatedEnd; i += 2)
                    if (emitted.Add((short)generated[i].Value))
                    {
                        body.Add(generated[i]);
                        body.Add(generated[i + 1]);
                    }

            var result = new List<XDataRecord>();
            int count = original == null ? 0 : original.Count;
            int insertion = start < 0 ? count : start;
            for (int i = 0; i < insertion; i++) result.Add(original[i]);
            if (body.Count != 0)
            {
                result.Add(start >= 0 ? original[start] : new XDataRecord(XDataCode.String, "DSTYLE"));
                result.Add(start >= 0 ? original[start + 1] : XDataRecord.OpenControlString);
                result.AddRange(body);
                result.Add(start >= 0 ? original[end] : XDataRecord.CloseControlString);
            }
            if (start >= 0)
                for (int i = end + 1; i < count; i++) result.Add(original[i]);
            return result;
        }
    }
}
