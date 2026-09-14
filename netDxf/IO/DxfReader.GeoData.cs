using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Units;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Tuple<DxfGeoData, string>> geoDataHosts = new List<Tuple<DxfGeoData, string>>();
        private bool ReadGeoDataPayload(DatabaseRecord result, string codeName, List<DxfTag> tags, int start)
        {
            if (codeName != "GEODATA") return false;
            int end = tags.FindIndex(start, t => t.Code == 1001);
            if (end < 0) end = tags.Count;
            var payload = tags.Skip(start).Take(end - start).ToList();
            // Unknown implementation versions and private payload extensions remain opaque.
            if (this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2010 || payload.Count == 0 || payload[0].Code != 100 || (string)payload[0].Value != "AcDbGeoData") return false;
            var versions = payload.Where(t => t.Code == 90).ToList();
            if (versions.Count == 1 && (int)versions[0].Value != 2) return false;
            var codes = new HashSet<short> { 100, 90, 330, 70, 10, 20, 30, 11, 21, 31, 40, 91, 41, 92, 210, 220, 230, 12, 22, 95, 141, 294, 142, 143, 303, 301, 302, 305, 306, 307, 93, 13, 23, 14, 24, 96, 97, 98, 99 };
            if (payload.Any(t => !codes.Contains(t.Code)) || payload.Skip(1).Any(t => t.Code == 100)) return false;
            try
            {
                var values = new Dictionary<short, DxfTag>();
                var definition = new StringBuilder(); bool finalDefinition = false;
                int cursor = 1;
                for (; cursor < payload.Count && payload[cursor].Code != 93; cursor++)
                {
                    DxfTag tag = payload[cursor];
                    if (tag.Code == 301 || tag.Code == 303)
                    {
                        if (finalDefinition) throw new FormatException("GEODATA coordinate definition has chunks after its final 301 tag.");
                        definition.Append((string)tag.Value); finalDefinition = tag.Code == 301;
                    }
                    else
                    {
                        if (values.ContainsKey(tag.Code)) throw new FormatException("Duplicate GEODATA scalar group: " + tag.Code);
                        values.Add(tag.Code, tag);
                    }
                }
                short[] required = { 90, 330, 70, 10, 20, 30, 11, 21, 31, 40, 91, 41, 92, 210, 220, 230, 12, 22, 95, 141, 294, 142, 143 };
                foreach (short code in required) if (!values.ContainsKey(code)) throw new FormatException("Missing GEODATA scalar group: " + code);
                if (!finalDefinition) throw new FormatException("GEODATA coordinate definition requires a final 301 tag.");
                if (values.Keys.Any(c => !required.Contains(c) && c != 302 && c != 305 && c != 306 && c != 307)) throw new FormatException("Unexpected GEODATA mesh data before its count.");
                Func<short, double> number = c => (double)values[c].Value;
                Func<short, string> text = c => values.TryGetValue(c, out DxfTag tag) ? this.DecodeEncodedNonAsciiCharacters((string)tag.Value) : string.Empty;
                var data = new DxfGeoData
                {
                    CoordinateType = (DxfGeoCoordinateType)(short)values[70].Value,
                    DesignPoint = new Vector3(number(10), number(20), number(30)), ReferencePoint = new Vector3(number(11), number(21), number(31)),
                    HorizontalUnitScale = number(40), HorizontalUnits = (DrawingUnits)(int)values[91].Value,
                    VerticalUnitScale = number(41), VerticalUnits = (DrawingUnits)(int)values[92].Value,
                    UpDirection = new Vector3(number(210), number(220), number(230)), NorthDirection = new Vector2(number(12), number(22)),
                    ScaleEstimation = (DxfGeoScaleEstimation)(int)values[95].Value, UserScaleFactor = number(141),
                    SeaLevelCorrection = (bool)values[294].Value, SeaLevelElevation = number(142), CoordinateProjectionRadius = number(143),
                    CoordinateSystemDefinition = this.DecodeEncodedNonAsciiCharacters(definition.ToString()).Replace("^J", "\n"),
                    GeoRssTag = text(302), ObservationFrom = text(305), ObservationTo = text(306), ObservationCoverage = text(307)
                };
                int count = (int)ReadGeoTag(payload, ref cursor, 93).Value;
                if (count < 0 || count > (payload.Count - cursor) / 4) throw new FormatException("GEODATA mesh point count exceeds the available data.");
                for (int i = 0; i < count; i++)
                {
                    double sx = (double)ReadGeoTag(payload, ref cursor, 13).Value, sy = (double)ReadGeoTag(payload, ref cursor, 23).Value;
                    double tx = (double)ReadGeoTag(payload, ref cursor, 14).Value, ty = (double)ReadGeoTag(payload, ref cursor, 24).Value;
                    data.MeshPoints.Add(new DxfGeoMeshPoint(new Vector2(sx, sy), new Vector2(tx, ty)));
                }
                int faceCount = (int)ReadGeoTag(payload, ref cursor, 96).Value;
                if (faceCount < 0 || faceCount > (payload.Count - cursor) / 3) throw new FormatException("GEODATA face count exceeds the available data.");
                for (int i = 0; i < faceCount; i++)
                {
                    int a = (int)ReadGeoTag(payload, ref cursor, 97).Value, b = (int)ReadGeoTag(payload, ref cursor, 98).Value, c = (int)ReadGeoTag(payload, ref cursor, 99).Value;
                    if (a < 0 || b < 0 || c < 0 || a >= count || b >= count || c >= count) throw new FormatException("GEODATA face index is outside the point array.");
                    data.MeshFaces.Add(new DxfGeoMeshFace(a, b, c));
                }
                if (cursor != payload.Count) throw new FormatException("Unexpected GEODATA trailing mesh data.");
                if (end < tags.Count) this.ReadDatabaseXData(data, tags, end);
                result.Object = data; this.geoDataHosts.Add(Tuple.Create(data, (string)values[330].Value));
                return true;
            }
            catch (ArgumentException error) { throw new FormatException("Invalid GEODATA public-schema value.", error); }
        }
        private static DxfTag ReadGeoTag(List<DxfTag> tags, ref int cursor, short code)
        {
            if (cursor >= tags.Count || tags[cursor].Code != code) throw new FormatException("Expected GEODATA group " + code + ".");
            return tags[cursor++];
        }
        private void ResolveGeoDataHosts()
        {
            foreach (Tuple<DxfGeoData, string> pair in this.geoDataHosts)
            {
                pair.Item1.SetLoadedHost(this.doc.GetObjectByHandle(pair.Item2) as BlockRecord);
                var errors = new List<string>(); pair.Item1.ValidateDatabaseSchema(this.doc.Objects, errors);
                if (errors.Count > 0) throw new FormatException(string.Join("; ", errors));
            }
        }
    }
}
