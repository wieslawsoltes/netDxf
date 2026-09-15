// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Entities
{
    public sealed partial class DxfOpaqueEntity
    {
        internal readonly HashSet<int> QualifiedReactorIndices = new HashSet<int>();
        private readonly HashSet<int> releasedHatchReactors = new HashSet<int>();
        private DxfObject[] permittedManagedReactors;
        private DxfObject[] permittedPersistentReactors;

        internal void ValidateHatchSourceAddition()
        {
            // Source contours are initially attached before the reader resolves this packet.
            if (!this.pending) throw new NotSupportedException("A retained unknown HATCH source cannot acquire a new boundary association.");
        }

        internal void ValidateHatchSourceRelease()
        {
            // Retired sources can occur later in an already prevalidated containing-block removal.
            if (!this.pending && !this.retired) this.Validate(this.source);
        }

        internal void ReleaseHatchSourceBacklink(Hatch hatch)
        {
            if (this.pending || this.retired) return;
            DxfObject[] expected = this.permittedManagedReactors ?? this.originalManagedReactors;
            DxfObject[] current = this.Reactors.ToArray();
            int count = current.Count(item => ReferenceEquals(item, hatch));
            int removed = expected.Count(item => ReferenceEquals(item, hatch)) - count;
            if (removed < 0) throw new InvalidOperationException("Unknown HATCH source acquired an unauthorized reactor.");
            var remaining = new List<DxfObject>(expected);
            for (int i = 0; i < removed; i++) remaining.Remove(hatch);
            if (!current.SequenceEqual(remaining)) throw new InvalidOperationException("Unknown HATCH source reactor changes exceed the authorized release.");
            this.permittedManagedReactors = current;
            if (count != 0) return;
            foreach (int index in this.QualifiedReactorIndices)
                if (this.links.TryGetValue(index, out DxfObject target) && ReferenceEquals(target, hatch)) this.releasedHatchReactors.Add(index);
            this.permittedPersistentReactors = (this.permittedPersistentReactors ?? this.originalReactors)
                .Where(item => !ReferenceEquals(item, hatch)).ToArray();
        }

        internal bool ReferencesRemoval(HashSet<DxfObject> removed)
        {
            foreach (DxfObject target in this.References)
            {
                if (!removed.Contains(target)) continue;
                if (!(target is Hatch hatch) || !this.Reactors.Any(item => ReferenceEquals(item, hatch))) return true;
                // Removing that HATCH will release only its qualified common backlink. Any
                // private standard pointer or current XData reference remains a dependency.
                if (this.links.Any(link => !this.releasedHatchReactors.Contains(link.Key)
                    && ReferenceEquals(link.Value, hatch) && !this.QualifiedReactorIndices.Contains(link.Key))) return true;
                if (this.XData.Values.Any(data => data.XDataRecord.Any(tag => ReferenceEquals(this.XDataTarget(tag), hatch)))) return true;
            }
            return false;
        }
    }

    public partial class Hatch
    {
        internal void ValidateOpaqueSourceRelease()
        {
            // Validate the entire operation before even its first path can be removed by Clear.
            foreach (DxfOpaqueEntity source in this.BoundaryPaths.SelectMany(path => path.Entities).OfType<DxfOpaqueEntity>().Distinct())
                source.ValidateHatchSourceRelease();
        }
    }
}
