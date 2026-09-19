// Test-only observation. Identity summaries avoid recursively expanding ownership cycles.
import * as api from '../index.js';
const reference=value=>value==null?null:{type:value.constructor.name,code:value.CodeName,handle:value.Handle};
export function databaseModelWire(value,wire) {
  if(value instanceof api.DxfLightListEntry)return {type:'DxfLightListEntry',light:reference(value.Light),name:wire(value.Name)};
  if(value instanceof api.DxfLayerIndexEntry)return {type:'DxfLayerIndexEntry',name:wire(value.LayerName),buffer:reference(value.Buffer),count:value.Count};
  if(value instanceof api.DxfDictionaryEntry)return {type:'DxfDictionaryEntry',name:value.Name,target:reference(value.Target),hard:value.IsHardOwner};
  if(value instanceof api.DxfDataColumn)return {type:'DxfDataColumn',kind:value.Type,name:wire(value.Name),values:Array.from(value.Values,v=>v instanceof api.DxfObject?{reference:reference(v)}:wire(v))};
  if(!(value instanceof api.DxfDatabaseObject))return undefined;
  const common={type:value.constructor.name,code:value.CodeName,handle:value.Handle,owner:reference(value.Owner),extension:reference(value.ExtensionDictionary),erased:value.IsErased,registered:value.Database!==null,
    xdata:Array.from(value.XData.Values,wire),reactors:Array.from(value.PersistentReactors,reference),owned:Array.from(value.DeclaredOwnedObjects,reference),references:Array.from(value.DatabaseReferences,reference)};
  if(value instanceof api.DxfLightList)return {common,version:value.StoredVersion,entries:Array.from(value.Entries,wire)};
  if(value instanceof api.DxfIdBuffer)return {common};
  if(value instanceof api.DxfSpatialIndex)return {common,timestamp:wire(value.Timestamp)};
  if(value instanceof api.DxfLayerIndex)return {common,timestamp:wire(value.Timestamp),entries:Array.from(value.Entries,wire)};
  if(value instanceof api.DxfLayerFilter)return {common,names:Array.from(value.LayerNames,wire)};
  if(value instanceof api.DxfSpatialFilter)return {common,boundary:Array.from(value.Boundary,wire),normal:wire(value.Normal),origin:wire(value.Origin),enabled:value.IsClippingEnabled,front:wire(value.FrontClippingDistance),back:wire(value.BackClippingDistance),inverse:wire(value.InverseInsertTransform),transform:wire(value.ClipBoundaryTransform)};
  if(value instanceof api.DxfDictionary)return {common,hard:value.IsHardOwner,cloning:value.Cloning,entries:Array.from(value.Entries,v=>wire(v)),fallback:value instanceof api.DxfDictionaryWithDefault?reference(value.Default):null};
  if(value instanceof api.DxfXRecord)return {common,cloning:value.Cloning,managed:value.IsSchemaManaged,table:value.IsTableRoundtripRecord,composite:value.IsCompositeTableRoundtripRecord,data:Array.from(value.Data,wire)};
  if(value instanceof api.DxfDictionaryVariable)return {common,schema:value.Schema,value:wire(value.Value)};
  if(value instanceof api.DxfOpaqueObject)return {common,tags:Array.from(value.Tags,wire)};
  if(value instanceof api.DxfDataTable)return {common,name:wire(value.Name),rows:value.RowCount,version:value.StoredVersion,columns:Array.from(value.Columns,wire)};
  if(value instanceof api.DxfSun)return {common,version:value.StoredVersion,enabled:value.Enabled,color:wire(value.ColorIndex),rgb:wire(value.TrueColor),intensity:wire(value.Intensity),shadows:value.ShadowsEnabled,day:value.JulianDay,time:value.StoredTime,daylight:value.DaylightSavingTime,shadow:value.ShadowType,map:value.ShadowMapSize,softness:value.ShadowSoftness};
  return {common};
}
