// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Restricted to the AcDbEntity subclass: group 92 and 310 have unrelated
        // meanings in entity-specific subclasses, notably HATCH, MESH and OLE.
        private sealed class EntityCommonDataReader
        {
            internal string ColorName;
            internal EntityShadowMode? ShadowMode;
            internal bool ColorNameSeen;
            internal int? DeclaredLength;
            internal MemoryStream Payload;
            internal int ActualLength;
            internal byte[] ProxyGraphics;

            internal void Complete()
            {
                if (!this.DeclaredLength.HasValue)
                {
                    if (this.Payload != null) throw new InvalidDataException("AcDbEntity proxy graphics chunks require a byte count.");
                    return;
                }
                if (this.ActualLength != this.DeclaredLength.Value)
                    throw new InvalidDataException("AcDbEntity proxy graphics byte count does not match the payload.");
                // Allocate only after actual received bytes establish the size.
                this.ProxyGraphics = this.Payload.ToArray();
                this.Payload.Dispose();
            }
        }

        private void ReadEntityCommonData(EntityCommonDataReader data)
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            switch (this.chunk.Code)
            {
                case 430:
                    if (version < DxfVersion.AutoCad2004 || data.ColorNameSeen)
                        throw new InvalidDataException("AcDbEntity color name requires DXF 2004 or later and one group 430.");
                    data.ColorNameSeen = true;
                    data.ColorName = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    break;
                case 284:
                    short mode = this.chunk.ReadShort();
                    if (version < DxfVersion.AutoCad2007 || data.ShadowMode.HasValue || mode < 0 || mode > 3)
                        throw new InvalidDataException("AcDbEntity shadow mode requires DXF 2007 or later and one value from 0 to 3.");
                    data.ShadowMode = (EntityShadowMode)mode;
                    break;
                case 92:
                case 160:
                    if (data.DeclaredLength.HasValue || (this.chunk.Code == 160 && version < DxfVersion.AutoCad2013))
                        throw new InvalidDataException("AcDbEntity proxy graphics require one byte count; group 160 requires DXF 2013 or later.");
                    long length = this.chunk.Code == 160 ? this.chunk.ReadLong() : this.chunk.ReadInt();
                    if (length < data.ActualLength || length > EntityObject.MaximumProxyGraphicsBytes)
                        throw new InvalidDataException("AcDbEntity proxy graphics byte count exceeds the allowed 0 to 16 MiB range.");
                    data.DeclaredLength = (int)length;
                    if (data.Payload == null) data.Payload = new MemoryStream();
                    break;
                case 310:
                    byte[] bytes = this.chunk.ReadBytes();
                    if (bytes.Length > 128 || data.ActualLength + bytes.Length > EntityObject.MaximumProxyGraphicsBytes ||
                        (data.DeclaredLength.HasValue && data.ActualLength + bytes.Length > data.DeclaredLength.Value))
                        throw new InvalidDataException("AcDbEntity proxy graphics chunks exceed their byte count or 128-byte packet limit.");
                    // Grow from actual input, never from the untrusted declared length.
                    if (data.Payload == null) data.Payload = new MemoryStream();
                    data.Payload.Write(bytes, 0, bytes.Length);
                    data.ActualLength += bytes.Length;
                    break;
            }
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateEntityCommonDataVersions()
        {
            foreach (Block block in this.doc.Blocks)
            {
                foreach (AttributeDefinition definition in block.AttributeDefinitions.Values)
                    this.ValidateEntityCommonDataVersion(definition.CommonData);
                foreach (EntityObject entity in block.Entities)
                {
                    this.ValidateEntityCommonDataVersion(entity.CommonData);
                    Insert insert = entity as Insert;
                    if (insert != null)
                        foreach (Entities.Attribute attribute in insert.Attributes)
                            this.ValidateEntityCommonDataVersion(attribute.CommonData);
                }
            }
        }

        private void ValidateEntityCommonDataVersion(CommonEntityData data)
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            if (data.ColorName != null && version < DxfVersion.AutoCad2004)
                throw new DxfVersionNotSupportedException("Entity color names require DXF 2004 or later; clear ColorName before downgrade.", version);
            if (data.ShadowMode.HasValue && version < DxfVersion.AutoCad2007)
                throw new DxfVersionNotSupportedException("Entity shadow modes require DXF 2007 or later; clear ShadowMode before downgrade.", version);
        }

        private void WriteEntityCommonData(CommonEntityData entity)
        {
            if (entity.ColorName != null) this.chunk.Write(430, this.EncodeDatabaseString(entity.ColorName).Replace("\0", "\\U+0000").Replace("\r", "\\U+000D").Replace("\n", "\\U+000A"));
            if (entity.ShadowMode.HasValue) this.chunk.Write(284, (short)entity.ShadowMode.Value);
            byte[] bytes = entity.ProxyGraphics;
            if (bytes == null) return;
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2013)
                this.chunk.Write(92, bytes.Length);
            else
                this.chunk.Write(160, (long)bytes.Length);
            for (int offset = 0; offset < bytes.Length;)
            {
                int size = Math.Min(127, bytes.Length - offset);
                var part = new byte[size];
                Buffer.BlockCopy(bytes, offset, part, 0, size);
                this.chunk.Write(310, part);
                offset += size;
            }
        }
    }
}
