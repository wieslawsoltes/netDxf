// Explicit, browser-safe building blocks for typed section IO. Not a document reader/writer.
export { DxfThumbnailImage } from '../netDxf/IO/DxfThumbnailImage.js';
export { EntityCommonDataReader, ReadEntityCommonData, WriteEntityCommonData,
  ValidateEntityCommonDataVersion, ValidateEntityCommonDataVersions } from '../netDxf/IO/DxfEntityCommonData.js';
export { TryReadMTextBackground, WriteMTextBackground, ValidateMTextBackgroundVersion,
  ValidateMTextBackgroundVersions } from '../netDxf/IO/DxfMTextBackground.js';
export { ValidateMeshOutput, ValidateDocumentMeshOutput } from '../netDxf/IO/DxfMeshWriteValidation.js';
export { ValidateMeshVersions } from '../netDxf/IO/DxfMeshVersion.js';

export { ReadOleFrame, WriteOleFrame } from '../netDxf/IO/DxfOleFrame.js';
export { ReadOle2Frame, WriteOle2Frame } from '../netDxf/IO/DxfOle2Frame.js';
export { ReadLight, WriteLight, ValidateLightVersions } from '../netDxf/IO/DxfLight.js';
export { ReadAcisEntity, WriteAcisEntity, ValidateAcisEntities } from '../netDxf/IO/DxfAcisSat.js';
export { ReadLwPolyline, WriteLwPolyline, ValidateLwPolylineFidelity } from '../netDxf/IO/DxfLwPolyline.js';

export { ReadArc, WriteArc, ReadCircle, WriteCircle, ReadEllipse, WriteEllipse,
  ReadLine, WriteLine, ReadPoint, WritePoint, ReadRay, WriteRay, ReadXLine, WriteXLine,
  ReadFace3d, ReadFace3D, WriteFace3D, ReadSolid, WriteSolid, ReadTrace, WriteTrace } from './PrimitiveEntityIO.js';
export { ReadSpline, WriteSpline } from './SplineIO.js';
export { ReadHelix, WriteHelix, ValidateHelixVersions, PrepareHelixClass } from '../netDxf/IO/DxfHelix.js';

export * from "./DatabasePayloadIO.js";

export * from '../netDxf/IO/DxfHatchBoundaryExport.js';
export * from '../netDxf/IO/DxfHatchSourceRelations.js';
export * from '../netDxf/IO/DxfHatchSplineData.js';
export * from '../netDxf/IO/DxfHatchSplineFitVersion.js';
export * from '../netDxf/IO/DxfHatchScalarEdge.js';
export * from '../netDxf/IO/DxfWriter.PolyfaceMesh.js';
export * from '../netDxf/IO/DxfWriter.TextStyle.js';
export * from '../netDxf/IO/DxfWriter.DimensionStyleParity.js';
export * from '../netDxf/IO/DxfReader.StoredTable.js';
export * from '../netDxf/IO/DxfWriter.StoredTable.js';
export * from '../netDxf/IO/DxfReader.Defaults.js';
export { HatchPatternXData } from '../netDxf/IO/HatchPatternXData.js';

export * from '../netDxf/IO/DxfReader.UcsBase.js';
export * from '../netDxf/IO/DxfViewSection.js';
export * from '../netDxf/IO/DxfViewUcs.js';
export * from '../netDxf/IO/DxfVPort.js';
export * from '../netDxf/IO/DxfReader.Views.js';
export * from '../netDxf/IO/DxfWriter.Views.js';

export { ReadMultiLeader, ResolveMultiLeaderReferences, MLeaderParser } from '../netDxf/IO/DxfReader.MultiLeader.js';
export { WriteMultiLeader, WriteMLeaderContext, WriteMLeaderFields, WriteMLeaderVector, ValidateMultiLeaders } from '../netDxf/IO/DxfWriter.MultiLeader.js';
export { ReadSection, ResolveSections, SectionParser } from '../netDxf/IO/DxfReader.Section.js';
export { WriteSection, ValidateSections } from '../netDxf/IO/DxfWriter.Section.js';

export { ReadNextClassTag, ReadClassFlag, ReadClassDefinitions, AddGeneratedClass, PrepareClassDefinitions, WriteClassDefinition } from '../netDxf/IO/DxfClasses.js';
export { ValidateOpaqueText, ValidateOpaqueEntityClasses, ValidateOpaqueEntities, PreflightOpaqueEntities, IsOpaqueEntityClass, WriteOpaqueEntity } from '../netDxf/IO/DxfWriter.OpaqueEntity.js';
export { IsOpaqueEntityCandidate, OpaqueExcludedSubclass, OpaqueEntityGroupEnd, ReadOpaqueCommon, ReadOpaqueEntity, ResolveOpaqueEntities } from '../netDxf/IO/DxfReader.OpaqueEntity.js';

export { ReadStoredPolylineRecord, ReadStoredPolylineSequence, ResolveStoredPolylineRecords } from '../netDxf/IO/DxfReader.PolylineRecords.js';

export { ReadStoredPolygonMeshRecord, ReadStoredPolygonMeshSequence, ResolveStoredPolygonMeshRecords } from '../netDxf/IO/DxfReader.PolygonMeshRecords.js';
export { WriteStoredPolylineRecord, WriteStoredPolylineRecords, ValidateStoredPolylineRecords, WritePolylineExtension, WritePolylineReactors } from '../netDxf/IO/DxfWriter.PolylineRecords.js';
export { WriteStoredPolygonMeshRecord, WriteStoredPolygonMeshRecords, ValidateStoredPolygonMeshRecords, WritePolygonMeshExtension, WritePolygonMeshReactors } from '../netDxf/IO/DxfWriter.PolygonMeshRecords.js';
export { WriteStoredPolyfaceMeshRecord, WriteStoredPolyfaceMeshRecords, ValidateStoredPolyfaceMeshRecords, WritePolyfaceMeshExtension, WritePolyfaceMeshReactors, WriteStoredPolyfaceMeshHeader } from '../netDxf/IO/DxfWriter.PolyfaceMeshRecords.js';
export { WriteStoredPolyline2DRecord, WriteStoredPolyline2DRecords, ValidateStoredPolyline2DRecords, WritePolyline2DExtension, WritePolyline2DReactors, WriteStoredPolyline2DHeader } from '../netDxf/IO/DxfWriter.Polyline2DRecords.js';

export * from '../netDxf/IO/DxfReader.PolyfaceMeshRecords.js';
export * from '../netDxf/IO/DxfReader.Polyline2DRecords.js';

export * from '../netDxf/IO/DxfMTextColumns.js';
