#region netDxf library licensed under the MIT License
//
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//
#endregion

using System.Collections.Generic;

namespace netDxf.IO
{
    // This recognizes only netDxf's conventional unbraced origin tuple, not a complete ACAD schema.
    internal static class HatchPatternXData
    {
        internal static int FindOrigin(IList<XDataRecord> records)
        {
            int depth = 0;
            for (int i = 0; i < records.Count; i++)
            {
                XDataRecord record = records[i];
                if (record.Code == XDataCode.ControlString)
                {
                    if ((string) record.Value == "{") depth++;
                    else if (depth > 0) depth--;
                }
                else if (depth == 0 && record.Code == XDataCode.RealX && i < records.Count - 2 &&
                    records[i + 1].Code == XDataCode.RealY && records[i + 2].Code == XDataCode.RealZ)
                    return i;
            }
            return -1;
        }

        internal static IList<XDataRecord> WithOrigin(IList<XDataRecord> records, Vector2 origin)
        {
            // Retain order and opaque values, replacing only the first unbraced complete point.
            List<XDataRecord> result = records == null ? new List<XDataRecord>() : new List<XDataRecord>(records);
            int index = FindOrigin(result);
            if (index < 0)
            {
                result.InsertRange(0, new[]
                {
                    new XDataRecord(XDataCode.RealX, origin.X),
                    new XDataRecord(XDataCode.RealY, origin.Y),
                    new XDataRecord(XDataCode.RealZ, 0.0)
                });
            }
            else
            {
                result[index] = new XDataRecord(XDataCode.RealX, origin.X);
                result[index + 1] = new XDataRecord(XDataCode.RealY, origin.Y);
                result[index + 2] = new XDataRecord(XDataCode.RealZ, 0.0);
            }
            return result;
        }

        internal static IList<XDataRecord> WithColorIndex(IList<XDataRecord> records, short colorIndex)
        {
            List<XDataRecord> result = records == null ? new List<XDataRecord>() : new List<XDataRecord>(records);
            XDataRecord color = new XDataRecord(XDataCode.Int16, colorIndex);
            if (result.Count > 0 && result[0].Code == XDataCode.Int16) result[0] = color;
            else result.Insert(0, color);
            return result;
        }
    }
}
