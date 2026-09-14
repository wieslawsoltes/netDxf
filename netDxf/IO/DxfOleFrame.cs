// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private OleFrame ReadOleFrame()
        {
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.OleFrame)
                throw new InvalidDataException("OLEFRAME requires AcDbOleFrame.");
            short version = 1;
            int length = -1;
            bool versionSeen = false, lengthSeen = false, terminated = false;
            var xdata = new List<XData>();
            using (var payload = new MemoryStream())
            {
                this.chunk.Next();
                while (this.chunk.Code != 0)
                {
                    short code = this.chunk.Code;
                    if (code == 1001 && terminated)
                    {
                        string app = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                        xdata.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app)));
                        continue;
                    }
                    if (terminated) throw new InvalidDataException("Unexpected data after OLEFRAME terminator.");
                    switch (code)
                    {
                        case 70:
                            if (versionSeen) throw new InvalidDataException("Duplicate OLEFRAME version.");
                            versionSeen = true; version = this.chunk.ReadShort();
                            if (version < 0) throw new InvalidDataException("Negative OLEFRAME version.");
                            break;
                        case 90:
                            if (lengthSeen) throw new InvalidDataException("Duplicate OLEFRAME byte count.");
                            lengthSeen = true; length = this.chunk.ReadInt();
                            if (length < 0 || payload.Length > length)
                                throw new InvalidDataException("Invalid OLEFRAME byte count.");
                            break;
                        case 310:
                            byte[] part = this.chunk.ReadBytes();
                            if (part.Length > 127 || payload.Length + part.Length > int.MaxValue ||
                                (lengthSeen && payload.Length + part.Length > length))
                                throw new InvalidDataException("OLEFRAME chunks exceed the allowed byte count.");
                            payload.Write(part, 0, part.Length);
                            break;
                        case 1:
                            if (this.chunk.ReadString() != "OLE") throw new InvalidDataException("Invalid OLEFRAME terminator.");
                            terminated = true;
                            break;
                        default:
                            throw new InvalidDataException("Unsupported OLEFRAME group " + code + "; use DxfRawDocument for private extensions.");
                    }
                    this.chunk.Next();
                }
                if (!lengthSeen || !terminated || payload.Length != length)
                    throw new InvalidDataException("Incomplete OLEFRAME packet or binary length mismatch.");
                var result = new OleFrame(payload.ToArray(), version, false);
                foreach (XData data in xdata) result.XData.Add(data);
                return result;
            }
        }
    }

    internal sealed partial class DxfWriter
    {
        private void WriteOleFrame(OleFrame frame)
        {
            this.chunk.Write(100, SubclassMarker.OleFrame);
            this.chunk.Write(70, frame.OleVersion);
            this.chunk.Write(90, frame.BinaryDataLength);
            byte[] data = frame.BinaryData;
            for (int offset = 0; offset < data.Length;)
            {
                int count = Math.Min(127, data.Length - offset);
                var part = new byte[count]; Buffer.BlockCopy(data, offset, part, 0, count);
                this.chunk.Write(310, part); offset += count;
            }
            this.chunk.Write(1, "OLE");
            this.WriteXData(frame.XData);
        }
    }
}
