// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import * as api from '../../index.js';
import { DatabaseRegistry } from '../../runtime/RegisteredDatabaseState.js';
import { OrdinalIgnoreCaseEquals } from '../../runtime/Collections.js';
import { ArgumentException, ArgumentNullException, InvalidOperationException, NotSupportedException } from '../../runtime/Errors.js';
const protectedSchemas = new Map([
  ['DxfStoredTableContent','Stored TABLECONTENT erasure requires its complete application schema.'],
  ['DxfStoredSunStudy','Stored SUNSTUDY erasure requires its complete application lifecycle.'],
  ['DxfStoredTableGeometry','Stored TABLEGEOMETRY erasure requires its complete application schema.'],
  ['DxfStoredCellStyleMap','Stored CELLSTYLEMAP erasure requires its complete application schema.'],
  ['DxfStoredField','Stored FIELD erasure requires its complete evaluator graph schema.'],
  ['DxfStoredDimAssoc','Stored DIMASSOC erasure requires the complete dimension association lifecycle.'],
]);
export function ErasureHandle(handle) {
  if (typeof handle !== 'string' || !/^[0-9a-f]{1,16}$/i.test(handle)) throw new InvalidOperationException('A retained handle cannot be safely inspected for erasure: ' + handle);
  return BigInt('0x' + handle);
}
export function ErasureReference(source, field, handle) { return new InvalidOperationException('Erasure would invalidate ' + source.CodeName + ' ' + source.Handle + ' ' + field + ' referencing ' + handle + '.'); }
export function InstallDatabaseErasure(Type) {
  Type.prototype.ErasureCarriers = function() {
    const found = new Set(), result = [], add = item => { if (item !== null && !found.has(item)) { found.add(item); result.push(item); } };
    for (const item of this.Document.AddedObjects.Values) {
      add(item);
      if (item instanceof api.Insert) for (const attribute of item.Attributes) add(attribute);
      if (item instanceof api.Block) add(item.End);
      if (item instanceof api.Layout) add(item.Viewport);
    }
    for (const item of DatabaseRegistry(this).Values) add(item);
    return result;
  };
  Type.prototype.EraseOwnedTree = function(root) { this.EraseOwnedTreeCore(root, null); };
  Type.prototype.EraseOwnedTreeCore = function(root, selectedManager) {
    if (root == null) throw new ArgumentNullException('root');
    if (root.IsErased) throw new InvalidOperationException('The object has already been erased.');
    if (root.Database !== this) throw new ArgumentException('The object must belong to this database.', 'root');
    this.CheckRegistered(root);
    if (root === this.Root) throw new InvalidOperationException('The named object dictionary root cannot be erased.');
    const carriers = this.ErasureCarriers(), children = new Map();
    for (const item of carriers) if (item.Owner !== null) { if (!children.has(item.Owner)) children.set(item.Owner, []); children.get(item.Owner).push(item); }
    const deleted = new Set(), tree = [], pending = [root];
    for (let at = 0; at < pending.length; at++) {
      const item = pending[at];
      if (deleted.has(item)) throw new InvalidOperationException('The erased ownership subtree contains a cycle.');
      deleted.add(item);
      if (!(item instanceof api.DxfDatabaseObject) || item === this.Root) throw new NotSupportedException('An ownership subtree containing a managed legacy object cannot be erased.');
      if (item.IsErased || item.Database !== this || !this.IsRegistered(item)) throw new InvalidOperationException('The erased ownership subtree has inconsistent registration.');
      for (const [name, message] of protectedSchemas) if (api[name] && item instanceof api[name]) throw new NotSupportedException(message);
      if (api.DxfStoredSectionManager && item instanceof api.DxfStoredSectionManager && item !== selectedManager) throw new NotSupportedException('Stored section-manager erasure requires the explicit manager lifecycle API.');
      if (item instanceof api.DxfOpaqueObject) throw new NotSupportedException('An opaque object requires its application schema before erasure: ' + item.CodeName);
      tree.push(item); if (children.has(item)) pending.push(...children.get(item));
    }
    if (root.Owner === null || !this.IsRegistered(root.Owner)) throw new InvalidOperationException('The erased root has no registered owner.');
    if (root.Owner === this.Document.Layers) throw new NotSupportedException('The LAYER table extension is managed by the layer-state collection.');
    const parent = root.Owner instanceof api.DxfDictionary ? root.Owner : null;
    const aliases = parent === null ? [] : Array.from(parent.Entries).filter(e => e.Target === root);
    if (parent === this.Root && aliases.some(e => Type.IsReservedName(e.Name))) throw new NotSupportedException('Managed legacy dictionary entries cannot be erased through this API.');
    const extension = root.Owner.ExtensionDictionary === root, sun = api.SunReferences.Get(root.Owner) === root;
    if (parent === null && !extension && !sun) throw new NotSupportedException('Erase an object through its owning dictionary or reciprocal extension attachment.');
    const handles = new Set();
    for (const item of tree) {
      const handle = ErasureHandle(item.Handle);
      if (handles.has(handle)) throw new InvalidOperationException('The erased ownership subtree contains duplicate numeric handles.');
      handles.add(handle);
      for (const data of item.XData.Values) if (!this.Document.ApplicationRegistries.References.ContainsKey(data.ApplicationRegistry.Name)) throw new InvalidOperationException('An erased object has inconsistent APPID reference bookkeeping: ' + data.ApplicationRegistry.Name);
    }
    for (const item of carriers) {
      if (deleted.has(item)) continue;
      const reference = (target, field) => { if (target !== null && deleted.has(target)) throw ErasureReference(item, field, target.Handle); };
      const handle = (target, field) => { if (handles.has(ErasureHandle(target))) throw ErasureReference(item, field, target); };
      reference(item.Owner, 'owner');
      if (!(sun && item === root.Owner)) reference(api.SunReferences.Get(item), 'SUN owner slot 361');
      if (!(extension && item === root.Owner && item.ExtensionDictionary === root)) reference(item.ExtensionDictionary, 'extension dictionary');
      for (const reactor of item.PersistentReactors) reference(reactor, 'persistent reactor');
      if (item instanceof api.EntityObject) for (const reactor of item.Reactors) reference(reactor, 'entity reactor');
      for (const data of item.XData.Values) for (const tag of data.XDataRecord) if (tag.Code === api.XDataCode.DatabaseHandle) handle(tag.Value, 'XData 1005');
      if (item instanceof api.DxfDatabaseObject) for (const target of item.DatabaseReferences) reference(target, 'typed reference');
      if (item instanceof api.DxfDictionary) for (const entry of item.Entries) if (!(item === parent && entry.Target === root)) reference(entry.Target, 'dictionary entry ' + entry.Name);
      if (item instanceof api.DxfDictionaryWithDefault) reference(item.Default, 'dictionary default');
      if (item instanceof api.DxfXRecord) for (const tag of item.Data) if (Type.IsReference(tag)) handle(tag.Value, 'XRECORD ' + tag.Code);
      if (item instanceof api.DxfOpaqueObject) for (const tag of item.Tags) if (tag.ValueType === api.DxfTagValueType.Handle) handle(tag.Value, 'opaque handle ' + tag.Code);
      if (item instanceof api.Section) reference(item.GeometrySettings, 'section settings');
      for (const [name, prefix] of [['Polyline3DRecord','polyline'], ['PolygonMeshRecord','polygon mesh'], ['PolyfaceMeshRecord','polyface'], ['Polyline2DRecord','legacy 2D']]) {
        if (item instanceof api[name]) { for (const target of item.References) reference(target, prefix + ' record reference'); for (const tag of item.OpaqueHandleTags) handle(tag.Value, prefix + ' record handle'); }
      }
      if (item instanceof api.PolyfaceMesh) for (const tag of item.StoredHeaderReferences) handle(tag.Value, 'polyface header handle');
      if (api.DxfOpaqueEntity && item instanceof api.DxfOpaqueEntity) for (const target of item.References) reference(target, 'unknown entity reference');
      if (item instanceof api.Polyline2D) for (const tag of item.StoredHeaderReferences) handle(tag.Value, 'legacy 2D header handle');
      if (api.StoredTable && item instanceof api.StoredTable) for (const target of item.References) reference(target, 'ACAD_TABLE reference');
      if (item instanceof api.MultiLeader) for (const data of item.Data) for (const target of data.References) reference(target, 'MULTILEADER reference');
      if (item instanceof api.Layout) reference(item.PlotSettings?.ShadePlotObject ?? null, 'layout shade plot 333');
    }
    for (const variable of this.Document.DrawingVariables.CustomValues()) {
      const kind = api.DxfGroupCode.GetHandleKind(variable.GroupCode);
      if (kind === api.DxfHandleKind.None || kind === api.DxfHandleKind.Arbitrary || OrdinalIgnoreCaseEquals(variable.Name, '$HANDSEED')) continue;
      const value = variable.Value instanceof api.BoxedString ? variable.Value.Value : variable.Value;
      if (typeof value !== 'string') throw new InvalidOperationException('A custom header handle has an invalid value: ' + variable.Name);
      if (handles.has(ErasureHandle(value))) throw ErasureReference(this.Document, 'header ' + variable.Name, value);
    }
    for (const alias of aliases) parent.Remove(alias.Name);
    if (extension) root.Owner.ExtensionDictionary = null;
    if (sun) api.SunReferences.Set(root.Owner, null, false);
    for (const item of tree) { this.Document.AddedObjects.Remove(item.Handle); DatabaseRegistry(this).Remove(item.Handle); item.Database = null; item.IsErased = true; }
    root.Owner = null;
  };
}
