// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf.Blocks;

namespace netDxf.Entities
{
    public static partial class DimensionBlock
    {
        // Adoption knows the destination before public ownership is assigned.
        // Pass that context explicitly; do not mutate the entity or use ambient state.
        internal static Block BuildForOwner(Dimension dim, string name, Block owner)
        {
            Block block;
            switch (dim.DimensionType)
            {
                case DimensionType.Linear:
                    block = Build((LinearDimension)dim, name, owner);
                    break;
                case DimensionType.Aligned:
                    block = Build((AlignedDimension)dim, name, owner);
                    break;
                case DimensionType.Angular:
                    block = Build((Angular2LineDimension)dim, name, owner);
                    break;
                case DimensionType.Angular3Point:
                    block = Build((Angular3PointDimension)dim, name, owner);
                    break;
                case DimensionType.Diameter:
                    block = Build((DiametricDimension)dim, name, owner);
                    break;
                case DimensionType.Radius:
                    block = Build((RadialDimension)dim, name, owner);
                    break;
                case DimensionType.Ordinate:
                    block = Build((OrdinateDimension)dim, name, owner);
                    break;
                case DimensionType.ArcLength:
                    block = Build((ArcLengthDimension)dim, name, owner);
                    break;
                default:
                    block = null;
                    break;
            }

            return block;
        }
    }
}
