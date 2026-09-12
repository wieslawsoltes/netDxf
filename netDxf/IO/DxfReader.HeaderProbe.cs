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

using System;
using System.IO;
using System.Text;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private static bool IsBinary(Stream stream)
        {
            const string signature = "AutoCAD Binary DXF\r\n\u001a\0";
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                byte[] bytes = reader.ReadBytes(signature.Length);
                if (bytes.Length != signature.Length) return false;
                for (int i = 0; i < signature.Length; i++)
                {
                    if (bytes[i] != (byte)signature[i]) return false;
                }
                return true;
            }
        }

        public static string CheckHeaderVariable(Stream stream, string headerVariable, out bool isBinary)
        {
            isBinary = false;
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (string.IsNullOrEmpty(headerVariable)) throw new ArgumentNullException(nameof(headerVariable));
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException("A readable, seekable stream is required for a DXF header probe.", nameof(stream));
            }
            long startPosition;
            try
            {
                startPosition = stream.Position;
            }
            catch (NotSupportedException exception)
            {
                throw new ArgumentException("Streams with an inaccessible Position property are not supported.", nameof(stream), exception);
            }

            try
            {
                isBinary = IsBinary(stream);
                stream.Position = startPosition;
                if (isBinary)
                {
                    using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII, true))
                    {
                        return ProbeStringHeaderVariable(new BinaryCodeValueReader(reader, Encoding.ASCII), headerVariable);
                    }
                }
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
                {
                    return ProbeStringHeaderVariable(new TextCodeValueReader(reader), headerVariable);
                }
            }
            finally
            {
                // Includes malformed input and read failures; wrappers leave the caller's stream open.
                stream.Position = startPosition;
            }
        }

        private static string ProbeStringHeaderVariable(ICodeValueReader reader, string headerVariable)
        {
            NextProbeTag(reader);
            while (!IsProbeTag(reader, 0, DxfObjectCode.EndOfFile))
            {
                if (!IsProbeTag(reader, 0, DxfObjectCode.BeginSection))
                {
                    throw new FormatException("Expected a DXF SECTION or EOF record while probing the header.");
                }
                NextProbeTag(reader);
                if (reader.Code != 2)
                {
                    throw new FormatException("A DXF SECTION name must use group code 2.");
                }
                bool isHeader = string.Equals(reader.ReadString(), DxfObjectCode.HeaderSection, StringComparison.Ordinal);
                NextProbeTag(reader);
                while (!IsProbeTag(reader, 0, DxfObjectCode.EndSection))
                {
                    if (IsProbeTag(reader, 0, DxfObjectCode.EndOfFile))
                    {
                        throw new EndOfStreamException("The DXF section ended without its ENDSEC record.");
                    }
                    if (isHeader)
                    {
                        if (reader.Code == 0)
                        {
                            throw new FormatException("Unexpected control record in the DXF HEADER section.");
                        }
                        if (IsProbeTag(reader, 9, headerVariable))
                        {
                            NextProbeTag(reader);
                            if (reader.Code == 0 || reader.Code == 9 ||
                                (headerVariable == HeaderVariableCode.AcadVer && reader.Code != 1) ||
                                (headerVariable == HeaderVariableCode.DwgCodePage && reader.Code != 3))
                            {
                                throw new FormatException("The requested DXF header variable has a missing or incorrectly typed value.");
                            }
                            string value = reader.Value as string;
                            if (value == null)
                            {
                                throw new FormatException("The requested DXF header variable must have a string value.");
                            }
                            // This is a probe, not validation of the rest of the document.
                            return value;
                        }
                    }
                    NextProbeTag(reader);
                }
                if (isHeader) return string.Empty;
                NextProbeTag(reader);
            }
            return string.Empty;
        }

        private static bool IsProbeTag(ICodeValueReader reader, short code, string value)
        {
            return reader.Code == code && string.Equals(reader.Value as string, value, StringComparison.Ordinal);
        }

        private static void NextProbeTag(ICodeValueReader reader)
        {
            do
            {
                reader.Next();
            } while (reader.Code == 999);
        }
    }
}
