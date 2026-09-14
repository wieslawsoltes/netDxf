// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private Ole2Frame ReadOle2Frame()
        {
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.Ole2Frame)
                throw new InvalidDataException("OLE2FRAME requires AcDbOle2Frame.");
            var seen = new HashSet<short>();
            var xdata = new List<XData>();
            Vector3 upper = Vector3.Zero, lower = Vector3.Zero;
            short version = 2, type = 2, tile = 0;
            string description = string.Empty;
            int length = -1;
            bool terminated = false;
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
                    if (terminated) throw new InvalidDataException("OLE2FRAME has unexpected data after its OLE terminator.");
                    if (code != 310 && !seen.Add(code)) throw new InvalidDataException("Duplicate OLE2FRAME group " + code + ".");
                    switch (code)
                    {
                        case 70: version = this.chunk.ReadShort(); break;
                        case 3: description = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString()); break;
                        case 10: upper.X = this.chunk.ReadDouble(); break;
                        case 20: upper.Y = this.chunk.ReadDouble(); break;
                        case 30: upper.Z = this.chunk.ReadDouble(); break;
                        case 11: lower.X = this.chunk.ReadDouble(); break;
                        case 21: lower.Y = this.chunk.ReadDouble(); break;
                        case 31: lower.Z = this.chunk.ReadDouble(); break;
                        case 71: type = this.chunk.ReadShort(); break;
                        case 72: tile = this.chunk.ReadShort(); break;
                        case 90:
                            length = this.chunk.ReadInt();
                            if (length < 0 || payload.Length > length)
                                throw new InvalidDataException("Invalid OLE2FRAME binary length.");
                            break;
                        case 310:
                            byte[] part = this.chunk.ReadBytes();
                            if (part.Length > 127 || payload.Length + part.Length > int.MaxValue ||
                                (length >= 0 && payload.Length + part.Length > length))
                                throw new InvalidDataException("OLE2FRAME binary chunks exceed the allowed length.");
                            payload.Write(part, 0, part.Length);
                            break;
                        case 1:
                            if (this.chunk.ReadString() != "OLE") throw new InvalidDataException("Invalid OLE2FRAME terminator.");
                            terminated = true;
                            break;
                        default:
                            throw new InvalidDataException("Unsupported OLE2FRAME group " + code + "; use DxfRawDocument for private extensions.");
                    }
                    this.chunk.Next();
                }
                if (!terminated || length < 0 || payload.Length != length)
                    throw new InvalidDataException("Incomplete OLE2FRAME binary packet or length mismatch.");
                // Coordinate components can be reordered, but a supplied point must be complete.
                foreach (short start in new short[] { 10, 11 })
                {
                    int count = (seen.Contains(start) ? 1 : 0) + (seen.Contains((short)(start + 10)) ? 1 : 0) +
                        (seen.Contains((short)(start + 20)) ? 1 : 0);
                    if (count != 0 && count != 3) throw new InvalidDataException("Incomplete OLE2FRAME corner.");
                }
                try
                {
                    Ole2FrameMetadataFields fields = Ole2FrameMetadataFields.None;
                    if (seen.Contains(70)) fields |= Ole2FrameMetadataFields.OleVersion;
                    if (seen.Contains(3)) fields |= Ole2FrameMetadataFields.Description;
                    if (seen.Contains(10)) fields |= Ole2FrameMetadataFields.UpperLeftCorner;
                    if (seen.Contains(11)) fields |= Ole2FrameMetadataFields.LowerRightCorner;
                    if (seen.Contains(71)) fields |= Ole2FrameMetadataFields.ObjectType;
                    if (seen.Contains(72)) fields |= Ole2FrameMetadataFields.TileMode;
                    var frame = new Ole2Frame(payload.ToArray(), upper, lower, description, version,
                        (OleObjectType)type, tile, false, fields);
                    foreach (XData data in xdata) frame.XData.Add(data);
                    return frame;
                }
                catch (ArgumentException error)
                {
                    throw new InvalidDataException("Invalid OLE2FRAME metadata.", error);
                }
            }
        }
    }

    internal sealed partial class DxfWriter
    {
        private void WriteOle2Frame(Ole2Frame frame)
        {
            this.chunk.Write(100, SubclassMarker.Ole2Frame);
            Ole2FrameMetadataFields fields = frame.MetadataFields;
            if ((fields & Ole2FrameMetadataFields.OleVersion) != 0) this.chunk.Write(70, frame.OleVersion);
            if ((fields & Ole2FrameMetadataFields.Description) != 0)
                this.chunk.Write(3, this.EncodeNonAsciiCharacters(frame.Description.Replace("\\", "\\U+005C")));
            if ((fields & Ole2FrameMetadataFields.UpperLeftCorner) != 0)
            {
                this.chunk.Write(10, frame.UpperLeftCorner.X); this.chunk.Write(20, frame.UpperLeftCorner.Y); this.chunk.Write(30, frame.UpperLeftCorner.Z);
            }
            if ((fields & Ole2FrameMetadataFields.LowerRightCorner) != 0)
            {
                this.chunk.Write(11, frame.LowerRightCorner.X); this.chunk.Write(21, frame.LowerRightCorner.Y); this.chunk.Write(31, frame.LowerRightCorner.Z);
            }
            if ((fields & Ole2FrameMetadataFields.ObjectType) != 0) this.chunk.Write(71, (short)frame.ObjectType);
            if ((fields & Ole2FrameMetadataFields.TileMode) != 0) this.chunk.Write(72, frame.TileMode);
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
