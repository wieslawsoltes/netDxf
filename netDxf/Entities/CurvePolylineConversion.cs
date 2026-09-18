// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    // Shared detached-conversion policy. Identity and private dependency graphs
    // cannot be copied onto a different entity kind without an explicit remap.
    internal static class CurvePolylineConversion
    {
        internal const int MaximumVertices = 1000000;

        internal static void CheckCount(int count, string parameter)
        {
            if (count > MaximumVertices)
                throw new ArgumentOutOfRangeException(parameter, count, "Converted polyline vertex budget exceeded.");
        }

        internal static void CheckElevation(double elevation)
        {
            if (double.IsNaN(elevation) || double.IsInfinity(elevation))
                throw new ArgumentOutOfRangeException(nameof(elevation), elevation, "Projection elevation must be finite.");
        }

        internal static void CheckPoint(Vector3 point)
        {
            if (double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.Y)
                || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                throw new InvalidOperationException("Polyline conversion requires finite source and sampled coordinates.");
        }

        internal static void CheckNormal(Vector3 normal)
        {
            CheckPoint(normal);
            if (Math.Abs(Vector3.DotProduct(normal, normal) - 1.0) > 2e-15)
                throw new InvalidOperationException("Polyline conversion requires a finite unit normal.");
        }

        internal static void CheckDependencies(EntityObject source)
        {
            if (source.ExtensionDictionary != null || source.PersistentReactors.Count != 0 || source.Reactors.Count != 0)
                throw new NotSupportedException("Associated curve graphs require explicit dependency conversion.");
            foreach (XData data in source.XData.Values)
                foreach (XDataRecord record in data.XDataRecord)
                    if (record.Code == XDataCode.DatabaseHandle)
                        throw new NotSupportedException("Curve XData handles require explicit dependency conversion.");
        }

        internal static void CopyAppearance(EntityObject source, EntityObject target, Vector3 normal)
        {
            target.Layer = (Layer)source.Layer.Clone();
            target.Linetype = (Linetype)source.Linetype.Clone();
            target.Color = (AciColor)source.Color.Clone();
            target.Transparency = (Transparency)source.Transparency.Clone();
            target.Lineweight = source.Lineweight;
            target.LinetypeScale = source.LinetypeScale;
            target.IsVisible = source.IsVisible;
            target.ColorName = source.ColorName;
            target.ShadowMode = source.ShadowMode;
            target.Normal = normal;
            foreach (XData data in source.XData.Values) target.XData.Add((XData)data.Clone());
        }

        internal static Polyline2D Project(EntityObject source, IList<Vector3> points,
            Vector3 normal, bool closed, double elevation)
        {
            CheckElevation(elevation); CheckNormal(normal); CheckCount(points.Count, nameof(points));
            Matrix3 projection = MathHelper.ArbitraryAxis(normal).Transpose();
            // Only XY is part of the projection. Computing a discarded normal
            // coordinate can overflow even when every requested output is finite.
            projection.M31 = 0; projection.M32 = 0; projection.M33 = 0;
            var vertices = new List<Vector2>(points.Count);
            foreach (Vector3 point in points)
            {
                CheckPoint(point);
                Vector3 local = InfiniteLineTransform.TransformPoint(projection, point, Vector3.Zero);
                vertices.Add(new Vector2(local.X, local.Y));
            }
            var result = new Polyline2D(vertices, closed) { Elevation = elevation };
            CopyAppearance(source, result, normal);
            return result;
        }
    }
}
