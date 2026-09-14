// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private void ReadHatchPolylineHeader(out bool closed, out int vertexCount)
        {
            closed = false; vertexCount = 0;
            int seen = 0;
            while (this.chunk.Code == 72 || this.chunk.Code == 73 || this.chunk.Code == 93)
            {
                short code = this.chunk.Code;
                int bit = code == 72 ? 1 : code == 73 ? 2 : 4;
                if ((seen & bit) != 0) throw this.InvalidHatchPolylineData("duplicate polyline header field");
                seen |= bit;
                if (code == 93) vertexCount = this.ReadHatchPolylineCount(93);
                else
                {
                    bool flag = this.ReadHatchPolylineFlag(code);
                    if (code == 73) closed = flag;
                }
                this.ReadNextHatchPolylineTag();
            }
            if (seen != 7) throw this.InvalidHatchPolylineData("incomplete polyline header");
        }

        private void ReadHatchSplineHeader(out int degree, out bool rational, out bool periodic,
            out int knotCount, out int controlCount)
        {
            degree = 0; rational = false; periodic = false; knotCount = 0; controlCount = 0;
            int seen = 0;
            while (this.chunk.Code == 94 || this.chunk.Code == 73 || this.chunk.Code == 74 ||
                this.chunk.Code == 95 || this.chunk.Code == 96)
            {
                short code = this.chunk.Code;
                int bit = code == 94 ? 1 : code == 73 ? 2 : code == 74 ? 4 : code == 95 ? 8 : 16;
                if ((seen & bit) != 0) throw this.InvalidHatchEdgeData("duplicate spline header field");
                seen |= bit;
                switch (code)
                {
                    case 94:
                        degree = this.chunk.ReadInt();
                        if (degree < 1 || degree > short.MaxValue)
                            throw this.InvalidHatchEdgeData("spline degree cannot be represented by the positive Int16 typed model");
                        this.ReadNextHatchEdgeTag(); break;
                    case 73: rational = this.ReadHatchEdgeFlag(73); break;
                    case 74: periodic = this.ReadHatchEdgeFlag(74); break;
                    case 95: knotCount = this.ReadHatchEdgeCount(95); break;
                    case 96: controlCount = this.ReadHatchEdgeCount(96); break;
                }
            }
            if (seen != 31) throw this.InvalidHatchEdgeData("incomplete spline header");
        }

        // Unique scalar fields are unordered; repeated SPLINE/POLYLINE lists are not.
        // The fixed mask avoids per-edge dictionaries/arrays and never allocates from
        // file counts. Group 72/97 or an entity boundary cannot supply a missing field.
        private HatchBoundaryPath.Edge ReadHatchScalarEdge(HatchBoundaryPath.EdgeType type)
        {
            int required;
            switch (type)
            {
                case HatchBoundaryPath.EdgeType.Line: required = 15; break;
                case HatchBoundaryPath.EdgeType.Arc: required = 243; break;
                case HatchBoundaryPath.EdgeType.Ellipse: required = 255; break;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
            Vector2 point = Vector2.Zero, axis = Vector2.Zero;
            double radius = 0, start = 0, end = 0;
            bool ccw = false;
            int seen = 0;
            while (this.chunk.Code != 0 && this.chunk.Code != 72 && this.chunk.Code != 97)
            {
                short code = this.chunk.Code;
                int bit;
                switch (code)
                {
                    case 10: bit = 1; break;
                    case 20: bit = 2; break;
                    case 11: bit = 4; break;
                    case 21: bit = 8; break;
                    case 40: bit = 16; break;
                    case 50: bit = 32; break;
                    case 51: bit = 64; break;
                    case 73: bit = 128; break;
                    default: bit = 0; break;
                }
                if ((required & bit) == 0)
                    throw this.InvalidHatchEdgeData("unexpected scalar group " + code + " in " + type + " edge");
                if ((seen & bit) != 0)
                    throw this.InvalidHatchEdgeData("duplicate scalar group " + code + " in " + type + " edge");
                seen |= bit;
                if (code == 73) ccw = this.ReadHatchEdgeFlag(73);
                else
                {
                    double value = this.ReadHatchEdgeDouble(code);
                    switch (code)
                    {
                        case 10: point.X = value; break;
                        case 20: point.Y = value; break;
                        case 11: axis.X = value; break;
                        case 21: axis.Y = value; break;
                        case 40: radius = value; break;
                        case 50: start = value; break;
                        case 51: end = value; break;
                    }
                }
            }
            if (seen != required)
                throw this.InvalidHatchEdgeData("incomplete scalar packet for " + type + " edge");
            switch (type)
            {
                case HatchBoundaryPath.EdgeType.Line:
                    return new HatchBoundaryPath.Line { Start = point, End = axis };
                case HatchBoundaryPath.EdgeType.Arc:
                    return new HatchBoundaryPath.Arc
                    { Center = point, Radius = radius, StartAngle = start, EndAngle = end, IsCounterclockwise = ccw };
                default:
                    return new HatchBoundaryPath.Ellipse
                    { Center = point, EndMajorAxis = axis, MinorRatio = radius, StartAngle = start, EndAngle = end, IsCounterclockwise = ccw };
            }
        }
    }
}
