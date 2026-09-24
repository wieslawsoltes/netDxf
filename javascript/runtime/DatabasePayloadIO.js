// Browser-safe original-path database payload adapters, not document Load/Save.
export { DatabaseIOContext,DatabaseRecord } from './DatabaseIOContext.js';
export { SourceIdentityContext,SourceRecordIdentity } from '../netDxf/IO/DxfReader.SourceIdentity.js';
export { DatabaseMetadataReader } from '../netDxf/IO/DxfReader.DatabaseMetadata.js';
export { ReadContainerPayload,ReadSpatialFilterPayload,ResolveContainerReferences } from '../netDxf/IO/DxfReader.Containers.js';
export { WriteContainerPayload } from '../netDxf/IO/DxfWriter.Containers.js';
export { ReadDataTableRecord,ReadDataTablePayload,ResolveDataTableReferences } from '../netDxf/IO/DxfReader.DataTable.js';
export { WriteDataTablePayload,PrepareDataTableClass } from '../netDxf/IO/DxfWriter.DataTable.js';
export { ReadStoredObjectHeader,ReadLayerFilterPointerRecord } from '../netDxf/IO/DxfReader.LayerFilterPointer.js';
export { WriteLayerFilterPointerPayload,PrepareLayerFilterPointerClasses,PrepareStoredEnvelopeClass } from '../netDxf/IO/DxfWriter.LayerFilterPointer.js';
export { ReadLayerIndexRecord,ResolveLayerIndexReferences } from '../netDxf/IO/DxfReader.LayerIndex.js';
export { WriteLayerIndexPayload,PrepareLayerIndexClass } from '../netDxf/IO/DxfWriter.LayerIndex.js';
export { ReadLightListPayload,ResolveLightListReferences } from '../netDxf/IO/DxfReader.LightList.js';
export { WriteLightListPayload,PrepareLightListClass } from '../netDxf/IO/DxfWriter.LightList.js';

export { ParsePlotSettings,ReadOutputSettingsPayload,ResolveOutputSettingsReferences } from '../netDxf/IO/DxfReader.OutputSettings.js';
export { ValidateOutputSettings,WriteOutputSettingsPayload,WritePlotSettingsPayload } from '../netDxf/IO/DxfWriter.OutputSettings.js';
export { ReadGeoDataPayload,ReadGeoTag,ResolveGeoDataHosts } from '../netDxf/IO/DxfReader.GeoData.js';
export { PrepareGeoDataClass,WriteGeoDataPayload,WriteGeoVector,SplitGeoDefinition } from '../netDxf/IO/DxfWriter.GeoData.js';
export { SunOwnerContext,AddSunReference,ReadSunRecord,ReadSunPayload,ResolveSunReferences,IsNullSourceHandle } from '../netDxf/IO/DxfReader.Sun.js';
export { PrepareSunClass,WriteSunPayload,WriteSunReference } from '../netDxf/IO/DxfWriter.Sun.js';
