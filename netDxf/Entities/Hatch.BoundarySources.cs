// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Entities
{
    public partial class Hatch
    {
        /// <summary>Replaces source-entity relationships without recomputing stored boundary geometry.</summary>
        /// <param name="associative">Whether the source entities maintain hatch reactor links.</param>
        /// <param name="sources">One ordered source-entity sequence for each boundary path.</param>
        /// <remarks>All sequences and ownership constraints are checked before changing relationships.
        /// Nonassociative hatches require empty source sequences. Detached sources of an owned hatch
        /// are adopted into its block using the ordinary boundary-added events. Previous source entities
        /// remain in their block. Analytic edges, path identities and proxy graphics are preserved.
        /// As with other native collection mutators, user event callbacks are not transactional.</remarks>
        public void SetBoundarySources(bool associative, IEnumerable<IEnumerable<EntityObject>> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var desired = new List<EntityObject[]>();
            foreach (IEnumerable<EntityObject> sequence in sources)
            {
                if (sequence == null) throw new ArgumentException("Boundary source sequences cannot be null.", nameof(sources));
                EntityObject[] entities = sequence.ToArray();
                if (!associative && entities.Length != 0)
                    throw new ArgumentException("Nonassociative hatches cannot retain source entities.", nameof(sources));
                foreach (EntityObject entity in entities)
                {
                    if (entity == null || ReferenceEquals(entity, this))
                        throw new ArgumentException("Boundary sources cannot be null or the containing hatch.", nameof(sources));
                    if (this.Owner != null && entity.Owner != null && !ReferenceEquals(entity.Owner, this.Owner))
                        throw new ArgumentException("Boundary sources must belong to the same block as their hatch.", nameof(sources));
                    if (entity.Owner == null && this.Owner?.Record.Owner != null)
                        this.Owner.Record.Owner.Owner.ValidateStoredTableEntityAdoption(entity);
                }
                desired.Add(entities);
            }
            if (desired.Count != this.boundaryPaths.Count)
                throw new ArgumentException("Supply one source sequence per boundary path.", nameof(sources));
            bool unchanged = this.associative == associative;
            for (int i = 0; i < desired.Count; i++) unchanged &= this.boundaryPaths[i].Entities.SequenceEqual(desired[i]);
            if (unchanged) return;
            this.ValidateOpaqueSourceRelease();
            // Unknown stored packets cannot acquire new source associations; preserve
            // their existing validation contract instead of rewriting private links.
            foreach (DxfOpaqueEntity opaque in desired.SelectMany(items => items).OfType<DxfOpaqueEntity>().Distinct())
                opaque.ValidateHatchSourceAddition();
            var previous = this.boundaryPaths.SelectMany(path => path.Entities).ToArray();
            if (this.associative)
                foreach (EntityObject entity in previous) entity.RemoveReactor(this);
            this.associative = associative;
            for (int i = 0; i < desired.Count; i++)
            {
                HatchBoundaryPath path = this.boundaryPaths[i];
                path.ClearContour();
                foreach (EntityObject entity in desired[i])
                {
                    path.AddContour(entity);
                    if (associative) entity.AddReactor(this);
                }
            }
            foreach (EntityObject entity in previous.Distinct()) this.RemoveUnusedSourceBacklink(entity);
            if (this.Owner != null)
                foreach (HatchBoundaryPath path in this.boundaryPaths) this.OnHatchBoundaryPathAddedEvent(path);
        }
    }
}
