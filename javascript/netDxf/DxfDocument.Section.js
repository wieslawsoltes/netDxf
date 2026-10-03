// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../index.js';
export function InstallDocumentSection(Type) {
  Type.prototype.SectionReferencesRemoval = function(removed) {
    // The source initializes Objects even when no section is being removed.
    const objects = this.Objects;
    for (const section of removed) if (section instanceof api.Section && Array.from(objects.Items).some(item => api.DxfObjectDatabase.IsAncestor(section,item))) return true;
    for (const settings of objects.Items) if (settings instanceof api.DxfSectionSettings && !removed.has(settings) && Array.from(settings.DatabaseReferences).some(target => target !== null && removed.has(target))) return true;
    const sections = new Set(Array.from(removed).filter(item => item instanceof api.Section));
    if (sections.size === 0) return false;
    for (const view of this.Views) if (!removed.has(view) && view.LiveSection !== null && sections.has(view.LiveSection)) return true;
    for (const item of this.RetainedMetadataObjects()) {
      if (removed.has(item)) continue;
      if (Array.from(item.PersistentReactors).some(target => sections.has(target))) return true;
      if (item instanceof api.EntityObject && Array.from(item.Reactors).some(target => sections.has(target))) return true;
      for (const data of item.XData.Values) for (const tag of data.XDataRecord)
        if (tag.Code === api.XDataCode.DatabaseHandle && sections.has(this.GetObjectByHandle(tag.Value))) return true;
      if (item instanceof api.DxfDatabaseObject && Array.from(item.DatabaseReferences).some(target => sections.has(target))) return true;
      if (item instanceof api.DxfXRecord) for (const tag of item.Data)
        if (api.DxfObjectDatabase.IsReference(tag) && sections.has(this.GetObjectByHandle(tag.Value))) return true;
      if (item instanceof api.DxfOpaqueObject) for (const tag of item.Tags)
        if (tag.ValueType === api.DxfTagValueType.Handle && sections.has(this.GetObjectByHandle(tag.Value))) return true;
    }
    for (const variable of this.DrawingVariables.CustomValues()) {
      const kind = api.DxfGroupCode.GetHandleKind(variable.GroupCode);
      const value = variable.Value instanceof api.BoxedString ? variable.Value.Value : variable.Value;
      if (kind !== api.DxfHandleKind.None && kind !== api.DxfHandleKind.Arbitrary && typeof value === 'string' && sections.has(this.GetObjectByHandle(value))) return true;
    }
    return false;
  };
}
