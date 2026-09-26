// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { SunReferences } from './SunReferences.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ReferenceMap } from '../../runtime/DatabaseReferenceMap.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException } from '../../runtime/Errors.js';
export function InstallDatabaseSun(Type) {
  Type.prototype.SetSun = function(owner, sun) {
    if (owner == null) throw new ArgumentNullException('owner'); if (sun == null) throw new ArgumentNullException('sun'); this.CheckSunDestination(owner);
    if (sun.IsErased || sun.Database !== null || sun.Owner !== null) throw new ArgumentException('SUN must be detached and cannot be erased.', 'sun');
    const errors = new ReferenceList(); sun.ValidateDatabaseSchema(this, errors);
    if (errors.Count !== 0) throw new ArgumentException(Array.from(errors).join('; '), 'sun');
    this.PrepareTarget(sun); sun.Owner = owner; SunReferences.Set(owner, sun);
  };
  Type.prototype.CloneSun = function(source, destinationOwner, externalReferences = null) {
    if (source == null) throw new ArgumentNullException('source'); if (destinationOwner == null) throw new ArgumentNullException('destinationOwner');
    const mappings = ReferenceMap(externalReferences); this.CheckSunDestination(destinationOwner);
    if (source.IsErased || source.Database === null || SunReferences.Get(source.Owner) !== source) throw new ArgumentException('The source must have a registered reciprocal SUN owner.', 'source');
    source.Database.CheckRegistered(source); source.Database.CheckRegistered(source.Owner);
    if (mappings.has(source.Owner) && mappings.get(source.Owner) !== destinationOwner) throw new ArgumentException('The source owner mapping conflicts with the destination host.', 'externalReferences');
    mappings.set(source.Owner, destinationOwner); return this.CloneOwnershipGraph(source, destinationOwner, null, false, mappings, true);
  };
  Type.prototype.CheckSunDestination = function(owner) { this.CheckRegistered(owner); SunReferences.CheckProfile(owner, this.Document.DrawingVariables.AcadVer); if (SunReferences.Get(owner) !== null) throw new InvalidOperationException('The destination SUN slot is occupied.'); };
}
