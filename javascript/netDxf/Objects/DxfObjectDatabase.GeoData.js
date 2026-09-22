// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDictionary } from './DxfDatabaseObject.js';
import { DxfGeoData } from './DxfGeoData.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException } from '../../runtime/Errors.js';
export function InstallDatabaseGeoData(Type) {
  Type.prototype.GetGeoData = function(hostBlock) {
    if (hostBlock == null) throw new ArgumentNullException('hostBlock'); this.CheckRegistered(hostBlock);
    const extension = hostBlock.ExtensionDictionary, value = extension !== null && extension.Contains('ACAD_GEOGRAPHICDATA') ? extension.get_Item('ACAD_GEOGRAPHICDATA') : null;
    return value instanceof DxfGeoData ? value : null;
  };
  Type.prototype.SetGeoData = function(data) {
    if (data == null) throw new ArgumentNullException('data'); this.CheckRegistered(data.HostBlock);
    if (data.Database !== null || data.Owner !== null) throw new ArgumentException('GEODATA must be detached and unowned.', 'data');
    const errors = new ReferenceList(); data.ValidateValues(this, errors);
    if (errors.Count > 0) throw new InvalidOperationException(Array.from(errors).join('; '));
    let extension = data.HostBlock.ExtensionDictionary;
    if (extension !== null && extension.Contains('ACAD_GEOGRAPHICDATA')) throw new InvalidOperationException('The host already has geographic metadata.');
    if (extension !== null) extension.Add('ACAD_GEOGRAPHICDATA', data);
    else { extension = new DxfDictionary(); extension.Add('ACAD_GEOGRAPHICDATA', data); try { this.SetExtensionDictionary(data.HostBlock, extension); } catch (error) { if (data.Database === null) data.Owner = null; throw error; } }
  };
}
