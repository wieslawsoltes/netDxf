// netDxf is distributed under the MIT license; see LICENSE in the repository root.
using System;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Header;
using System.Diagnostics;
using System.IO;
using netDxf.Entities;

namespace netDxf.IO
{
    internal partial class DxfReader
    {
        private Polyline2D ReadLwPolyline()
        {
            double elevation = 0.0, thickness = 0.0;
            double? constantWidth = null;
            PolylineTypeFlags flags = PolylineTypeFlags.OpenPolyline;
            List<Polyline2DVertex> vertexes = new List<Polyline2DVertex>();
            Polyline2DVertex vertex = null;
            bool hasY = false;
            int? declaredCount = null;
            Vector3 normal = Vector3.UnitZ;
            List<XData> xData = new List<XData>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                switch (this.chunk.Code)
                {
                    case 38:
                        elevation = this.chunk.ReadDouble();
                        this.chunk.Next();
                        break;
                    case 39:
                        thickness = this.chunk.ReadDouble();
                        this.chunk.Next();
                        break;
                    case 43:
                        if (constantWidth.HasValue) throw new InvalidDataException("LWPOLYLINE has duplicate group 43 constant widths.");
                        constantWidth = this.chunk.ReadDouble();
                        if (double.IsNaN(constantWidth.Value) || double.IsInfinity(constantWidth.Value) || constantWidth.Value < 0)
                            throw new InvalidDataException("LWPOLYLINE group 43 must be finite and nonnegative.");
                        this.chunk.Next();
                        break;
                    case 70:
                        flags = (PolylineTypeFlags)this.chunk.ReadShort();
                        this.chunk.Next();
                        break;
                    case 90:
                        if (declaredCount.HasValue)
                            throw new InvalidDataException("LWPOLYLINE has duplicate group 90 vertex counts.");
                        int count = this.chunk.ReadInt();
                        if (count < 0)
                            throw new InvalidDataException("LWPOLYLINE group 90 vertex count is negative.");
                        // Never allocate a list based on an untrusted declaration.
                        declaredCount = count;
                        this.chunk.Next();
                        break;
                    case 10:
                        if (vertex != null && !hasY)
                            throw new InvalidDataException("LWPOLYLINE vertex is missing group 20.");
                        vertex = new Polyline2DVertex(this.chunk.ReadDouble(), 0.0);
                        vertexes.Add(vertex);
                        hasY = false;
                        this.chunk.Next();
                        break;
                    case 20:
                        if (vertex == null || hasY)
                            throw new InvalidDataException("LWPOLYLINE group 20 must belong to one group 10 vertex.");
                        vertex.Position = new Vector2(vertex.Position.X, this.chunk.ReadDouble());
                        hasY = true;
                        this.chunk.Next();
                        break;
                    case 91:
                        if (vertex == null) throw new InvalidDataException("LWPOLYLINE group 91 precedes its group 10 vertex.");
                        if (vertex.VertexIdentifier.HasValue) throw new InvalidDataException("LWPOLYLINE vertex has duplicate group 91 identifiers.");
                        vertex.VertexIdentifier = this.chunk.ReadInt();
                        this.chunk.Next();
                        break;
                    case 40:
                    case 41:
                    case 42:
                        if (vertex == null)
                            throw new InvalidDataException("LWPOLYLINE vertex data precedes its group 10 vertex.");
                        short code = this.chunk.Code;
                        double value = this.chunk.ReadDouble();
                        if (code == 42) vertex.Bulge = value;
                        else
                        {
                            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                                throw new InvalidDataException("LWPOLYLINE vertex width must be finite and nonnegative.");
                            if (code == 40)
                            {
                                if (vertex.StartWidthOverride.HasValue) throw new InvalidDataException("LWPOLYLINE vertex has duplicate group 40 widths.");
                                vertex.StartWidth = value;
                            }
                            else
                            {
                                if (vertex.EndWidthOverride.HasValue) throw new InvalidDataException("LWPOLYLINE vertex has duplicate group 41 widths.");
                                vertex.EndWidth = value;
                            }
                        }
                        this.chunk.Next();
                        break;
                    case 210:
                        normal.X = this.chunk.ReadDouble();
                        this.chunk.Next();
                        break;
                    case 220:
                        normal.Y = this.chunk.ReadDouble();
                        this.chunk.Next();
                        break;
                    case 230:
                        normal.Z = this.chunk.ReadDouble();
                        this.chunk.Next();
                        break;
                    case 1001:
                        string appId = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                        xData.Add(this.ReadXDataRecord(this.GetApplicationRegistry(appId)));
                        break;
                    default:
                        Debug.Assert(!(this.chunk.Code >= 1000 && this.chunk.Code <= 1071),
                            "The extended data of an entity must start with the application registry code.");
                        this.chunk.Next();
                        break;
                }
            }
            if (vertex != null && !hasY)
                throw new InvalidDataException("LWPOLYLINE final vertex is missing group 20.");
            if (!declaredCount.HasValue || declaredCount.Value != vertexes.Count)
                throw new InvalidDataException("LWPOLYLINE group 90 does not match its actual vertex count.");
            Polyline2D entity = new Polyline2D(vertexes)
            {
                Elevation = elevation, Thickness = thickness, Flags = flags, Normal = normal, ConstantWidth = constantWidth
            };
            entity.XData.AddRange(xData);
            return entity;
        }
    }
    internal partial class DxfWriter
    {
        private void ValidateLwPolylineFidelity()
        {
            foreach (Block block in this.doc.Blocks)
                foreach (EntityObject entity in block.Entities)
                {
                    Polyline2D polyline = entity as Polyline2D;
                    if (polyline == null) continue;
                    polyline.ValidateVertexFidelity();
                    bool hasIdentifiers = false;
                    foreach (Polyline2DVertex vertex in polyline.Vertexes) hasIdentifiers |= vertex.VertexIdentifier.HasValue;
                    if (hasIdentifiers && this.doc.DrawingVariables.AcadVer < DxfVersion.AutoCad2013)
                        throw new NotSupportedException("LWPOLYLINE group 91 vertex identifiers require the qualified DXF 2013 or later writer profile. Remove identifiers explicitly before down-saving.");
                    if (polyline.SmoothType != PolylineSmoothType.NoSmooth && (polyline.ConstantWidth.HasValue || hasIdentifiers))
                        throw new NotSupportedException("Smoothed POLYLINE output cannot retain LWPOLYLINE constant-width presence or vertex identifiers. Convert these fields explicitly before enabling smoothing.");
                }
        }
    }
}
