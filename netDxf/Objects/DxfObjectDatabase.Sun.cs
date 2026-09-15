using System;
using System.Collections.Generic;

namespace netDxf.Objects
{
    public sealed partial class DxfObjectDatabase
    {
        /// <summary>Attaches detached SUN settings to an empty registered VIEW, VPORT or VIEWPORT owner.</summary>
        /// <remarks>Existing attachments are never replaced. EraseOwnedTree removes the previous typed subtree explicitly.</remarks>
        public void SetSun(DxfObject owner, DxfSun sun)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (sun == null) throw new ArgumentNullException(nameof(sun));
            this.CheckSunDestination(owner);
            if (sun.IsErased || sun.Database != null || sun.Owner != null) throw new ArgumentException("SUN must be detached and cannot be erased.", nameof(sun));
            var errors = new List<string>(); sun.ValidateDatabaseSchema(this, errors);
            if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors), nameof(sun));
            this.PrepareTarget(sun);
            sun.Owner = owner;
            SunReferences.Set(owner, sun);
        }
        /// <summary>Clones a registered SUN and its complete owned subtree into an empty registered destination host.</summary>
        /// <remarks>The source owner maps to the destination host. Other cross-document references require explicit mappings.</remarks>
        public DxfSun CloneSun(DxfSun source, DxfObject destinationOwner, IReadOnlyDictionary<DxfObject, DxfObject> externalReferences = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destinationOwner == null) throw new ArgumentNullException(nameof(destinationOwner));
            var mappings = new Dictionary<DxfObject, DxfObject>(ObjectIdentity);
            if (externalReferences != null) foreach (var pair in externalReferences) mappings.Add(pair.Key, pair.Value);
            this.CheckSunDestination(destinationOwner);
            if (source.IsErased || source.Database == null || !ReferenceEquals(SunReferences.Get(source.Owner), source)) throw new ArgumentException("The source must have a registered reciprocal SUN owner.", nameof(source));
            source.Database.CheckRegistered(source);
            source.Database.CheckRegistered(source.Owner);
            if (mappings.TryGetValue(source.Owner, out DxfObject supplied) && !ReferenceEquals(supplied, destinationOwner)) throw new ArgumentException("The source owner mapping conflicts with the destination host.", nameof(externalReferences));
            mappings[source.Owner] = destinationOwner;
            return (DxfSun)this.CloneOwnershipGraph(source, destinationOwner, null, false, mappings, true);
        }
        private void CheckSunDestination(DxfObject owner)
        {
            this.CheckRegistered(owner);
            SunReferences.CheckProfile(owner, this.Document.DrawingVariables.AcadVer);
            if (SunReferences.Get(owner) != null) throw new InvalidOperationException("The destination SUN slot is occupied.");
        }
    }
}
