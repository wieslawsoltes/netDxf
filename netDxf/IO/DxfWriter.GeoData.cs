using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using netDxf.Collections;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void PrepareGeoDataClass(DxfClassCollection definitions)
        {
            if (!this.doc.Objects.Items.Any(o => o is DxfGeoData)) return;
            int count = this.doc.Objects.Items.Count(o => o.CodeName == "GEODATA");
            if (definitions.Contains("GEODATA"))
            {
                DxfClass definition = definitions["GEODATA"];
                if (definition.CppClassName != "AcDbGeoData" || definition.IsEntity) throw new System.IO.InvalidDataException("CLASS conflicts with typed GEODATA.");
                definition.InstanceCount = count;
            }
            else definitions.Add(new DxfClass("GEODATA", "AcDbGeoData", "ObjectDBX Classes") { ProxyFlags = 4095, IsEntity = false, InstanceCount = count });
        }
        private bool WriteGeoDataPayload(DxfDatabaseObject item)
        {
            if (!(item is DxfGeoData data)) return false;
            this.chunk.Write(100, "AcDbGeoData"); this.chunk.Write(90, data.Version); this.chunk.Write(330, data.HostBlock.Handle);
            this.chunk.Write(70, (short)data.CoordinateType);
            this.WriteGeoVector(10, data.DesignPoint); this.WriteGeoVector(11, data.ReferencePoint);
            this.chunk.Write(40, data.HorizontalUnitScale); this.chunk.Write(91, (int)data.HorizontalUnits);
            this.chunk.Write(41, data.VerticalUnitScale); this.chunk.Write(92, (int)data.VerticalUnits);
            this.WriteGeoVector(210, data.UpDirection);
            this.chunk.Write(12, data.NorthDirection.X); this.chunk.Write(22, data.NorthDirection.Y);
            this.chunk.Write(95, (int)data.ScaleEstimation); this.chunk.Write(141, data.UserScaleFactor);
            this.chunk.Write(294, data.SeaLevelCorrection); this.chunk.Write(142, data.SeaLevelElevation); this.chunk.Write(143, data.CoordinateProjectionRadius);
            List<string> chunks = SplitGeoDefinition(this.EncodeDatabaseString(data.CoordinateSystemDefinition.Replace("\n", "^J")));
            for (int i = 0; i < chunks.Count; i++) this.chunk.Write(i == chunks.Count - 1 ? (short)301 : (short)303, chunks[i]);
            this.chunk.Write(302, this.EncodeDatabaseString(data.GeoRssTag)); this.chunk.Write(305, this.EncodeDatabaseString(data.ObservationFrom));
            this.chunk.Write(306, this.EncodeDatabaseString(data.ObservationTo)); this.chunk.Write(307, this.EncodeDatabaseString(data.ObservationCoverage));
            this.chunk.Write(93, data.MeshPoints.Count);
            foreach (DxfGeoMeshPoint point in data.MeshPoints)
            {
                this.chunk.Write(13, point.Source.X); this.chunk.Write(23, point.Source.Y);
                this.chunk.Write(14, point.Target.X); this.chunk.Write(24, point.Target.Y);
            }
            this.chunk.Write(96, data.MeshFaces.Count);
            foreach (DxfGeoMeshFace face in data.MeshFaces)
            { this.chunk.Write(97, face.First); this.chunk.Write(98, face.Second); this.chunk.Write(99, face.Third); }
            return true;
        }
        private void WriteGeoVector(short code, Vector3 value)
        { this.chunk.Write(code, value.X); this.chunk.Write((short)(code + 10), value.Y); this.chunk.Write((short)(code + 20), value.Z); }
        private static List<string> SplitGeoDefinition(string text)
        {
            var result = new List<string>(); var chunk = new StringBuilder();
            for (int i = 0; i < text.Length;)
            {
                int length = text[i] == '\\' && i + 6 < text.Length && text[i + 1] == 'U' && text[i + 2] == '+' ? 7 : char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                if (chunk.Length + length > 255) { result.Add(chunk.ToString()); chunk.Clear(); }
                chunk.Append(text, i, length); i += length;
            }
            result.Add(chunk.ToString()); return result;
        }
    }
}
