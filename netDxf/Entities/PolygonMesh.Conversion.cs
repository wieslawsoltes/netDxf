// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public partial class PolygonMesh
    {
        /// <summary>Maximum number of generated surface samples per sampling or conversion call.</summary>
        /// <remarks>The limit is checked using wide arithmetic before allocation. Unsmoothed grids retain their original vertices.</remarks>
        public const int MaximumSurfaceSamples = 1000000;

        private void ResolveSurfaceDensity(out int countU, out int countV)
        {
            var document = this.Owner?.Record?.Owner?.Owner;
            countU = this.densityU == 0 ? (document == null ? DefaultSurfU : document.DrawingVariables.SurfU) + 1 : this.densityU;
            countV = this.densityV == 0 ? (document == null ? DefaultSurfV : document.DrawingVariables.SurfV) + 1 : this.densityV;
            countU = Math.Max(3, countU);
            countV = Math.Max(3, countV);
        }

        private IEnumerable<int[]> ConversionFaces(int countU, int countV)
        {
            int cellsU = this.IsClosedInU ? countU : countU - 1;
            int cellsV = this.IsClosedInV ? countV : countV - 1;
            for (int row = 0; row < cellsV; row++)
            {
                int nextRow = (row + 1) % countV;
                for (int col = 0; col < cellsU; col++)
                {
                    int nextCol = (col + 1) % countU;
                    yield return new[] { row * countU + col, row * countU + nextCol,
                        nextRow * countU + nextCol, nextRow * countU + col };
                }
            }
        }

        private void ValidateConversion()
        {
            this.ValidateSurface();
            Vector3 normal = base.Normal;
            if (double.IsNaN(normal.X) || double.IsInfinity(normal.X) || double.IsNaN(normal.Y) || double.IsInfinity(normal.Y)
                || double.IsNaN(normal.Z) || double.IsInfinity(normal.Z) || Math.Abs(Vector3.DotProduct(normal, normal) - 1) > 2e-15)
                throw new InvalidOperationException("Polygon mesh conversion requires a finite unit auxiliary normal.");
            if (this.ExtensionDictionary != null || this.PersistentReactors.Count != 0 || this.Reactors.Count != 0)
                throw new NotSupportedException("Associated polygon mesh graphs require explicit dependency conversion.");
            foreach (XData data in this.XData.Values)
                foreach (XDataRecord record in data.XDataRecord)
                    if (record.Code == XDataCode.DatabaseHandle)
                        throw new NotSupportedException("Polygon mesh XData handles require explicit dependency conversion.");
            // An ordinary loaded grid is usable, but converting child-specific data
            // without a mapping to the new faces would silently discard that data.
            foreach (PolygonMeshRecord record in this.StoredRecords)
            {
                if (!record.CanClone() || record.XData.Count != 0)
                    throw new NotSupportedException("Decorated polygon mesh children require explicit dependency conversion.");
                foreach (var tag in record.Tags)
                {
                    switch (tag.Code)
                    {
                        case 0: case 5: case 330: case 100: case 10: case 20: case 30: case 70: break;
                        case 40: case 41:
                            if ((double)tag.Value != 0.0)
                                throw new NotSupportedException("Nonzero polygon mesh child widths require explicit conversion.");
                            break;
                        case 62:
                            if ((short)tag.Value != this.Color.Index)
                                throw new NotSupportedException("Child-specific polygon mesh colors require explicit conversion.");
                            break;
                        case 420:
                            if (!this.Color.UseTrueColor || (int)tag.Value != ((this.Color.R << 16) | (this.Color.G << 8) | this.Color.B))
                                throw new NotSupportedException("Child-specific polygon mesh true colors require explicit conversion.");
                            break;
                        case 8:
                            if ((string)tag.Value != "0" && !string.Equals((string)tag.Value, this.Layer.Name, StringComparison.OrdinalIgnoreCase))
                                throw new NotSupportedException("Child-specific polygon mesh layers require explicit conversion.");
                            break;
                        default: throw new NotSupportedException("Decorated polygon mesh children require explicit dependency conversion.");
                    }
                }
            }
        }

        private void CopyConversionAppearance(EntityObject target)
        {
            target.Layer = (Layer)this.Layer.Clone();
            target.Linetype = (Linetype)this.Linetype.Clone();
            target.Color = (AciColor)this.Color.Clone();
            target.Transparency = (Transparency)this.Transparency.Clone();
            target.Lineweight = this.Lineweight;
            target.LinetypeScale = this.LinetypeScale;
            target.IsVisible = this.IsVisible;
            target.Normal = base.Normal;
            target.ColorName = this.ColorName;
            target.ShadowMode = this.ShadowMode;
            foreach (XData data in this.XData.Values) target.XData.Add((XData)data.Clone());
            // New geometry has new identities and no reusable proxy packet.
        }
    }
}
