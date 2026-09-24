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
