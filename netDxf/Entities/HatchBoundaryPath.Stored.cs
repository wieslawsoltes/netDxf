// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class HatchBoundaryPath
    {
        /// <summary>Reconstructs a stored boundary and its source-entity relationships.</summary>
        /// <param name="edges">Stored analytic boundary edges.</param>
        /// <param name="pathType">Stored boundary classification flags.</param>
        /// <param name="sources">Referenced boundary entities, in stored order.</param>
        /// <returns>A detached boundary path retaining the supplied geometry and relationships.</returns>
        /// <remarks>Does not recompute geometry from sources or mutate their reactors. Source
        /// ownership is validated when the path joins a hatch; associative hatches establish
        /// reactor links then. The supplied objects are adopted, not cloned.</remarks>
        public static HatchBoundaryPath FromStoredEdges(IEnumerable<Edge> edges, HatchBoundaryPathTypeFlags pathType,
            IEnumerable<EntityObject> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var sourceEntities = new List<EntityObject>(sources);
            foreach (EntityObject entity in sourceEntities)
                if (entity == null) throw new ArgumentException("Boundary sources cannot contain null.", nameof(sources));
            var path = new HatchBoundaryPath(edges);
            if ((path.pathType & HatchBoundaryPathTypeFlags.Polyline) != (pathType & HatchBoundaryPathTypeFlags.Polyline))
                throw new ArgumentException("Stored polyline flags must match the supplied edge representation.", nameof(pathType));
            path.pathType = pathType;
            path.entities.AddRange(sourceEntities);
            return path;
        }
    }
}
