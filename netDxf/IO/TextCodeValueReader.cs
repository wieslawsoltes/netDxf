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
using System.Globalization;
using System.IO;

namespace netDxf.IO
{
    internal class TextCodeValueReader :
        ICodeValueReader
    {
        #region private fields

        private readonly TextReader reader;
        private short code;
        private object value;
        private long currentPosition;

        #endregion

        #region constructors

        public TextCodeValueReader(TextReader reader)
        {
            this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
            this.code = 0;
            this.value = null;
            this.currentPosition = 0;
        }

        #endregion

        #region public properties

        // The DIMSTYLE table's obsolete DIMBLK field is the sole raw group-5 name exception.
        public bool Code5IsString { get; set; }

        // Opted into only by DxfReader after leading comments are captured.
        // Standalone codecs, version probes and DxfRawDocument retain every tag.
        internal bool SkipComments { get; set; }

        public short Code
        {
            get { return this.code; }
        }

        public object Value
        {
            get { return this.value; }
        }

        public long CurrentPosition
        {
            get { return this.currentPosition; }
        }

        #endregion

        #region public methods

        public void Next()
        {
            do
            {
                string readCode = this.reader.ReadLine();
                if (readCode == null)
                {
                    // Physical EOF is not a synthetic DXF 0/EOF record. Fabricating
                    // one can accept truncated documents or keep section readers looping.
                    throw new EndOfStreamException(string.Format(CultureInfo.InvariantCulture,
                        "Missing DXF group code at line {0}.", this.currentPosition + 1));
                }

                this.currentPosition += 1;
                // Numeric parsing permits trailing NULs on some runtimes. They are
                // not whitespace or valid DXF group-code spelling.
                if (readCode.IndexOf('\0') >= 0 ||
                    !short.TryParse(readCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out this.code))
                {
                    throw new FormatException(string.Format(CultureInfo.InvariantCulture,
                        "Invalid DXF group code at line {0}.", this.currentPosition));
                }

                string valueString = this.reader.ReadLine();
                if (valueString == null)
                {
                    throw new EndOfStreamException(string.Format(CultureInfo.InvariantCulture,
                        "Missing value for group code {0} at line {1}.", this.code, this.currentPosition + 1));
                }
                this.value = this.ReadValue(valueString);
                this.currentPosition += 1;
            }
            while (this.SkipComments && this.code == 999);
        }

        public byte ReadByte()
        {
            return (byte) this.value;
        }

        public byte[] ReadBytes()
        {
            return (byte[]) this.value;
        }

        public short ReadShort()
        {
            return (short) this.value;
        }

        public int ReadInt()
        {
            return (int) this.value;
        }

        public long ReadLong()
        {
            return (long) this.value;
        }

        public bool ReadBool()
        {
            return (bool) this.value;
        }

        public double ReadDouble()
        {
            return (double) this.value;
        }

        public string ReadString()
        {
            return (string) this.value;
        }

        public string ReadHex()
        {
            return (string) this.value;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", this.code, this.value);
        }

        #endregion

        #region private methods

        private object ReadValue(string valueString)
        {
            if ((this.code == 5 && !this.Code5IsString) || this.code == 1005) // object or extended-data handle
            {
                return this.ReadHex(valueString);
            }
            if (this.code >= 0 && this.code <= 9) // string
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 10 && this.code <= 39) // double precision 3D point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 40 && this.code <= 59) // double precision floating point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 60 && this.code <= 79) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 90 && this.code <= 99) // 32-bit integer value
            {
                return this.ReadInt(valueString);
            }
            if (this.code == 100) // string (255-character maximum; less for Unicode strings)
            {
                return this.ReadString(valueString);
            }
            if (this.code == 101) // string (255-character maximum; less for Unicode strings). This code is undocumented and seems to affect only the AcdsData in dxf version 2013
            {
                return this.ReadString(valueString);
            }
            if (this.code == 102) // string (255-character maximum; less for Unicode strings)
            {
                return this.ReadString(valueString);
            }
            if (this.code == 105) // string representing hexadecimal (hex) handle value
            {
                return this.ReadHex(valueString);
            }
            if (this.code >= 110 && this.code <= 119) // double precision floating point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 120 && this.code <= 129) // double precision floating point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 130 && this.code <= 139) // double precision floating point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 140 && this.code <= 149) // double precision scalar floating-point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 160 && this.code <= 169) // 64-bit integer value
            {
                return this.ReadLong(valueString);
            }
            if (this.code >= 170 && this.code <= 179) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 210 && this.code <= 239) // double precision scalar floating-point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 270 && this.code <= 279) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 280 && this.code <= 289) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 290 && this.code <= 299) // byte (boolean flag value)
            {
                return this.ReadBool(valueString);
            }
            if (this.code >= 300 && this.code <= 309) // arbitrary text string
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 310 && this.code <= 319) // string representing hex value of binary chunk
            {
                return this.ReadBytes(valueString);
            }
            if (this.code >= 320 && this.code <= 329) // string representing hex handle value
            {
                return this.ReadHex(valueString);
            }
            if (this.code >= 330 && this.code <= 369) // string representing hex object IDs
            {
                return this.ReadHex(valueString);
            }
            if (this.code >= 370 && this.code <= 379) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 380 && this.code <= 389) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 390 && this.code <= 399) // string representing hex handle value
            {
                return this.ReadHex(valueString);
            }
            if (this.code >= 400 && this.code <= 409) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code >= 410 && this.code <= 419) // string
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 420 && this.code <= 429) // 32-bit integer value
            {
                return this.ReadInt(valueString);
            }
            if (this.code >= 430 && this.code <= 439) // string
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 440 && this.code <= 449) // 32-bit integer value
            {
                return this.ReadInt(valueString);
            }
            if (this.code >= 450 && this.code <= 459) // 32-bit integer value
            {
                return this.ReadInt(valueString);
            }
            if (this.code >= 460 && this.code <= 469) // double-precision floating-point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 470 && this.code <= 479) // string
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 480 && this.code <= 481) // string representing hex handle value
            {
                return this.ReadHex(valueString);
            }
            if (this.code == 999) // comment (string)
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 1010 && this.code <= 1059) // double-precision floating-point value
            {
                return this.ReadDouble(valueString);
            }
            if (this.code >= 1000 && this.code <= 1003) // string (same limits as indicated with 0-9 code range)
            {
                return this.ReadString(valueString);
            }
            if (this.code == 1004) // string representing hex value of binary chunk
            {
                return this.ReadBytes(valueString);
            }
            if (this.code >= 1006 && this.code <= 1009) // string (same limits as indicated with 0-9 code range)
            {
                return this.ReadString(valueString);
            }
            if (this.code >= 1060 && this.code <= 1070) // 16-bit integer value
            {
                return this.ReadShort(valueString);
            }
            if (this.code == 1071) // 32-bit integer value
            {
                return this.ReadInt(valueString);
            }

            throw new Exception(string.Format("Code \"{0}\" not valid at line {1}", this.code, this.currentPosition));
        }

        private byte[] ReadBytes(string valueString)
        {
            // Next has consumed the group-code line; the value is on the following line.
            long valueLine = this.currentPosition + 1;
            if (valueString == null)
            {
                throw new EndOfStreamException(string.Format(CultureInfo.InvariantCulture,
                    "Missing binary chunk value for group code {0} at line {1}.", this.code, valueLine));
            }
            if ((valueString.Length & 1) != 0)
            {
                throw new FormatException(string.Format(CultureInfo.InvariantCulture,
                    "Binary chunk for group code {0} at line {1} must contain an even number of hexadecimal digits.", this.code, valueLine));
            }

            byte[] bytes = new byte[valueString.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int high = HexDigit(valueString[2 * i]);
                int low = HexDigit(valueString[2 * i + 1]);
                if (high < 0 || low < 0)
                {
                    throw new FormatException(string.Format(CultureInfo.InvariantCulture,
                        "Invalid hexadecimal digit in binary chunk for group code {0} at line {1}, byte {2}.", this.code, valueLine, i));
                }
                bytes[i] = (byte) ((high << 4) | low);
            }

            return bytes;
        }

        private static int HexDigit(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }
            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }
            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }
            return -1;
        }

        private short ReadShort(string valueString)
        {
            if (valueString.IndexOf('\0') < 0 &&
                short.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out short result))
            {
                return result;
            }
            throw this.InvalidValue("16-bit integer");
        }

        private int ReadInt(string valueString)
        {
            if (valueString.IndexOf('\0') < 0 &&
                int.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                return result;
            }
            throw this.InvalidValue("32-bit integer");
        }

        private long ReadLong(string valueString)
        {
            if (valueString.IndexOf('\0') < 0 &&
                long.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            {
                return result;
            }
            throw this.InvalidValue("64-bit integer");
        }

        private bool ReadBool(string valueString)
        {
            if (valueString.IndexOf('\0') < 0 &&
                byte.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result) && result <= 1)
            {
                return result == 1;
            }
            throw this.InvalidValue("boolean (0 or 1)");
        }

        private double ReadDouble(string valueString)
        {
            if (DxfDoubleParser.TryParse(valueString, out double result))
                return result;
            throw this.InvalidValue("finite double-precision number");
        }

        private string ReadString(string valueString)
        {
            return valueString;
        }

        private string ReadHex(string valueString)
        {
            // A handle is an unsigned value with at most sixteen ASCII hexadecimal
            // digits. Do not let integer parsing accept a sign, prefix or trailing NUL.
            int start = 0;
            int end = valueString.Length;
            while (start < end && char.IsWhiteSpace(valueString[start])) start++;
            while (end > start && char.IsWhiteSpace(valueString[end - 1])) end--;
            if (end == start || end - start > 16)
            {
                throw this.InvalidValue("hexadecimal handle (1 to 16 digits)");
            }

            ulong result = 0;
            for (int i = start; i < end; i++)
            {
                int digit = HexDigit(valueString[i]);
                if (digit < 0)
                {
                    throw this.InvalidValue("hexadecimal handle (1 to 16 digits)");
                }
                result = (result << 4) | (uint) digit;
            }
            return result.ToString("X", CultureInfo.InvariantCulture);
        }

        private FormatException InvalidValue(string kind)
        {
            // Avoid echoing arbitrary, possibly very large input into diagnostics.
            return new FormatException(string.Format(CultureInfo.InvariantCulture,
                "Invalid {0} value for group code {1} at line {2}.", kind, this.code, this.currentPosition + 1));
        }

        #endregion
    }
}
