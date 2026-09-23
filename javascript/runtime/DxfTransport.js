// Explicit, browser-safe building blocks for typed section IO. Not a document reader/writer.
export { DxfThumbnailImage } from '../netDxf/IO/DxfThumbnailImage.js';
export { EntityCommonDataReader, ReadEntityCommonData, WriteEntityCommonData,
  ValidateEntityCommonDataVersion, ValidateEntityCommonDataVersions } from '../netDxf/IO/DxfEntityCommonData.js';
export { TryReadMTextBackground, WriteMTextBackground, ValidateMTextBackgroundVersion,
  ValidateMTextBackgroundVersions } from '../netDxf/IO/DxfMTextBackground.js';
export { ValidateMeshOutput, ValidateDocumentMeshOutput } from '../netDxf/IO/DxfMeshWriteValidation.js';
export { ValidateMeshVersions } from '../netDxf/IO/DxfMeshVersion.js';
