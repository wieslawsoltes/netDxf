// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace netDxf.Entities
{
    public partial class Polyline2D
    {
        // Complete admission before Reverse changes list/record order. Vertex setters
        // are nonvirtual and will receive only these already validated values.
        private void ValidateReversalVertices()
        {
            this.ValidateVertexFidelity();
            var identities = new HashSet<Polyline2DVertex>(ReversalVertexComparer.Instance);
            foreach (Polyline2DVertex vertex in this.vertexes)
            {
                if (!identities.Add(vertex))
                    throw new InvalidOperationException("Reversal requires distinct vertex objects; shared vertices cannot carry independent reversed segment attributes.");
                if (!LegacyFinite(vertex.Position.X) || !LegacyFinite(vertex.Position.Y) || !LegacyFinite(vertex.Bulge))
                    throw new InvalidOperationException("Polyline reversal requires finite coordinates and bulges.");
            }
        }

        // A user-derived vertex may override Equals/GetHashCode. Admission must
        // compare identity without calling those user methods or confusing equal points.
        private sealed class ReversalVertexComparer : IEqualityComparer<Polyline2DVertex>
        {
            internal static readonly ReversalVertexComparer Instance = new ReversalVertexComparer();
            public bool Equals(Polyline2DVertex x, Polyline2DVertex y) { return ReferenceEquals(x, y); }
            public int GetHashCode(Polyline2DVertex value) { return RuntimeHelpers.GetHashCode(value); }
        }
    }
}
