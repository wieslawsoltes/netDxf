// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private AcisEntity ReadAcisEntity(string entityCode)
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            if (version >= DxfVersion.AutoCad2013)
                throw new NotSupportedException("DXF 2013+ ACIS requires SAB and ACDSDATA lifecycle support; use DxfRawDocument to preserve the complete file.");
            if (this.chunk.Code != 100 || this.chunk.ReadString() != SubclassMarker.ModelerGeometry)
                throw new InvalidDataException("ACIS entities require AcDbModelerGeometry.");
            AcisEntity entity = entityCode == DxfObjectCode.Body ? (AcisEntity)new Body() :
                entityCode == DxfObjectCode.Region ? new Region() : (AcisEntity)new Solid3D();
            var parts = new List<AcisSatChunk>();
            var xdata = new List<XData>();
            bool hasVersion = false, historySubclass = false, hasHistory = false, ended = false;
            int total = 0, lineLength = 0;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                short code = this.chunk.Code;
                if (code == 1001)
                {
                    ended = true;
                    string app = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    xdata.Add(this.ReadXDataRecord(this.GetApplicationRegistry(app)));
                    continue;
                }
                if (ended) throw new InvalidDataException("Unexpected ACIS data after XData.");
                switch (code)
                {
                    case 70:
                        if (hasVersion || parts.Count > 0 || historySubclass)
                            throw new InvalidDataException("Misplaced or duplicate ACIS modeler format version.");
                        if (this.chunk.ReadShort() != 1) throw new InvalidDataException("Unsupported ACIS modeler format version; use DxfRawDocument for opaque preservation.");
                        hasVersion = true;
                        break;
                    case 1:
                    case 3:
                        if (!hasVersion || historySubclass || (code == 3 && parts.Count == 0))
                            throw new InvalidDataException("Misplaced ACIS SAT chunk.");
                        string text = this.chunk.ReadString();
                        if (code == 1) lineLength = 0;
                        if (parts.Count >= AcisEntity.MaximumSatChunks || text.Length > AcisEntity.MaximumSatCharacters - total ||
                            text.Length > AcisEntity.MaximumSatLineCharacters - lineLength)
                            throw new InvalidDataException("ACIS SAT payload limit exceeded.");
                        total += text.Length; lineLength += text.Length;
                        try { parts.Add(new AcisSatChunk(code, text)); }
                        catch (ArgumentException error) { throw new InvalidDataException("Invalid ACIS SAT chunk.", error); }
                        break;
                    case 100:
                        if (!(entity is Solid3D) || historySubclass || !hasVersion || parts.Count == 0 ||
                            version < DxfVersion.AutoCad2007 || this.chunk.ReadString() != SubclassMarker.Solid3D)
                            throw new InvalidDataException("Unsupported or misplaced ACIS subclass.");
                        historySubclass = true;
                        break;
                    case 350:
                        if (!historySubclass || hasHistory) throw new InvalidDataException("Misplaced or duplicate ACIS history handle.");
                        string handle = this.chunk.ReadString();
                        if (handle != "0") throw new NotSupportedException("Live ACIS history requires an unsupported object graph; use DxfRawDocument to preserve it.");
                        ((Solid3D)entity).HistoryHandle = handle;
                        hasHistory = true;
                        break;
                    default:
                        throw new InvalidDataException("Unsupported ACIS group " + code + "; use DxfRawDocument for private extensions.");
                }
                this.chunk.Next();
            }
            if (!hasVersion || parts.Count == 0) throw new InvalidDataException("ACIS modeler format version and SAT payload are required.");
            try { entity.SetEncodedSatChunks(parts); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid encoded ACIS SAT payload.", error); }
            foreach (XData data in xdata) entity.XData.Add(data);
            return entity;
        }
    }

    internal sealed partial class DxfWriter
    {
        private void ValidateAcisEntities()
        {
            DxfVersion version = this.doc.DrawingVariables.AcadVer;
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject item in block.Entities)
                {
                    var entity = item as AcisEntity;
                    if (entity == null) continue;
                    if (version >= DxfVersion.AutoCad2013)
                        throw new NotSupportedException("SAT ACIS entities support DXF 2000 through 2010 only. DXF 2013+ requires SAB and ACDSDATA lifecycle support.");
                    if (entity.EncodedSatChunks.Count == 0)
                        throw new InvalidOperationException("An ACIS entity requires a SAT payload before saving.");
                    if (entity is Solid3D solid && solid.HistoryHandle != null && version < DxfVersion.AutoCad2007)
                        throw new NotSupportedException("Explicit ACIS history metadata is qualified for DXF 2007 through 2010 only.");
                }
        }

        private void WriteAcisEntity(AcisEntity entity)
        {
            this.chunk.Write(100, SubclassMarker.ModelerGeometry);
            this.chunk.Write(70, entity.ModelerFormatVersion);
            // SAT text is already encoded; general DXF Unicode escaping would corrupt this envelope.
            foreach (AcisSatChunk part in entity.EncodedSatChunks) this.chunk.Write(part.GroupCode, part.Text);
            if (entity is Solid3D solid && this.doc.DrawingVariables.AcadVer >= DxfVersion.AutoCad2007)
            {
                this.chunk.Write(100, SubclassMarker.Solid3D);
                if (solid.HistoryHandle != null) this.chunk.Write(350, solid.HistoryHandle);
            }
            this.WriteXData(entity.XData);
        }
    }
}
