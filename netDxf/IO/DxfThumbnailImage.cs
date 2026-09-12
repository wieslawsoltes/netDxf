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
    // The payload is opaque DXF preview data, not a BMP/PNG file or a rendered image.
    internal static class DxfThumbnailImage
    {
        public static byte[] Read(ICodeValueReader chunk)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            int? declaredLength = null;
            using (MemoryStream data = new MemoryStream())
            {
                chunk.Next();
                while (chunk.Code != 0)
                {
                    switch (chunk.Code)
                    {
                        case 90:
                            if (declaredLength.HasValue)
                            {
                                throw Invalid(chunk, "Duplicate byte count (group code 90).");
                            }
                            declaredLength = chunk.ReadInt();
                            if (declaredLength.Value < 0)
                            {
                                throw Invalid(chunk, "The byte count must not be negative.");
                            }
                            break;
                        case 310:
                            byte[] bytes = chunk.ReadBytes();
                            // Do not allocate from an untrusted declared count. Grow only
                            // with bytes that actually exist in the input stream.
                            if (data.Length + bytes.Length > int.MaxValue)
                            {
                                throw Invalid(chunk, "The preview exceeds the supported byte count.");
                            }
                            data.Write(bytes, 0, bytes.Length);
                            break;
                        case 999:
                            // Text DXF comments do not contribute to the preview bytes.
                            break;
                        default:
                            throw Invalid(chunk, "Unexpected group code in preview data.");
                    }

                    if (declaredLength.HasValue && data.Length > declaredLength.Value)
                    {
                        throw Invalid(chunk, "The preview contains more bytes than its declared count.");
                    }
                    chunk.Next();
                }

                if (chunk.ReadString() == DxfObjectCode.EndOfFile)
                {
                    throw new EndOfStreamException("Unexpected end of DXF while reading THUMBNAILIMAGE; ENDSEC is required.");
                }
                if (chunk.ReadString() != DxfObjectCode.EndSection)
                {
                    throw Invalid(chunk, "Expected ENDSEC after preview data.");
                }
                if (!declaredLength.HasValue || data.Length != declaredLength.Value)
                {
                    throw Invalid(chunk, "The preview byte count is missing or does not match its data.");
                }
                // Leave ENDSEC current, as expected by the document section dispatcher.
                return data.ToArray();
            }
        }

        public static void Write(ICodeValueWriter chunk, byte[] data)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            if (data.Length == 0)
            {
                return;
            }

            chunk.Write(0, DxfObjectCode.BeginSection);
            chunk.Write(2, DxfObjectCode.ThumbnailImageSection);
            chunk.Write(90, data.Length);
            for (int offset = 0; offset < data.Length;)
            {
                // 127 bytes satisfies both the general binary-chunk limit and
                // the preview section's 256-character text-line maximum.
                int count = Math.Min(127, data.Length - offset);
                byte[] part = new byte[count];
                Buffer.BlockCopy(data, offset, part, 0, count);
                chunk.Write(310, part);
                offset += count;
            }
            chunk.Write(0, DxfObjectCode.EndSection);
        }

        private static InvalidDataException Invalid(ICodeValueReader chunk, string reason)
        {
            return new InvalidDataException(string.Format(CultureInfo.InvariantCulture,
                "Invalid THUMBNAILIMAGE at position {0}, group code {1}: {2}",
                chunk.CurrentPosition, chunk.Code, reason));
        }
    }
}
