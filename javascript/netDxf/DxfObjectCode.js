// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class DxfObjectCode {
  // C# backing state is prefixed with $; public members retain their original names.
  static get Unknown() { return ""; }
  static get HeaderSection() { return "HEADER"; }
  static get ClassesSection() { return "CLASSES"; }
  static get Class() { return "CLASS"; }
  static get TablesSection() { return "TABLES"; }
  static get BlocksSection() { return "BLOCKS"; }
  static get EntitiesSection() { return "ENTITIES"; }
  static get ObjectsSection() { return "OBJECTS"; }
  static get ThumbnailImageSection() { return "THUMBNAILIMAGE"; }
  static get AcdsDataSection() { return "ACDSDATA"; }
  static get BeginSection() { return "SECTION"; }
  static get EndSection() { return "ENDSEC"; }
  static get LayerTable() { return "LAYER"; }
  static get VportTable() { return "VPORT"; }
  static get ViewTable() { return "VIEW"; }
  static get UcsTable() { return "UCS"; }
  static get BlockRecordTable() { return "BLOCK_RECORD"; }
  static get LinetypeTable() { return "LTYPE"; }
  static get TextStyleTable() { return "STYLE"; }
  static get DimensionStyleTable() { return "DIMSTYLE"; }
  static get ApplicationIdTable() { return "APPID"; }
  static get Table() { return "TABLE"; }
  static get EndTable() { return "ENDTAB"; }
  static get BeginBlock() { return "BLOCK"; }
  static get EndBlock() { return "ENDBLK"; }
  static get GroupDictionary() { return "ACAD_GROUP"; }
  static get LayoutDictionary() { return "ACAD_LAYOUT"; }
  static get MLineStyleDictionary() { return "ACAD_MLINESTYLE"; }
  static get ImageDefDictionary() { return "ACAD_IMAGE_DICT"; }
  static get ImageVarsDictionary() { return "ACAD_IMAGE_VARS"; }
  static get UnderlayDgnDefinitionDictionary() { return "ACAD_DGNDEFINITIONS"; }
  static get UnderlayDwfDefinitionDictionary() { return "ACAD_DWFDEFINITIONS"; }
  static get UnderlayPdfDefinitionDictionary() { return "ACAD_PDFDEFINITIONS"; }
  static get LayerStates() { return "ACAD_LAYERSTATES"; }
  static get EndOfFile() { return "EOF"; }
  static get AppId() { return "APPID"; }
  static get DimStyle() { return "DIMSTYLE"; }
  static get BlockRecord() { return "BLOCK_RECORD"; }
  static get Linetype() { return "LTYPE"; }
  static get Layer() { return "LAYER"; }
  static get VPort() { return "VPORT"; }
  static get TextStyle() { return "STYLE"; }
  static get MLineStyle() { return "MLINESTYLE"; }
  static get View() { return "VIEW"; }
  static get Ucs() { return "UCS"; }
  static get Block() { return "BLOCK"; }
  static get BlockEnd() { return "ENDBLK"; }
  static get Line() { return "LINE"; }
  static get Ray() { return "RAY"; }
  static get XLine() { return "XLINE"; }
  static get Ellipse() { return "ELLIPSE"; }
  static get Polyline() { return "POLYLINE"; }
  static get LwPolyline() { return "LWPOLYLINE"; }
  static get Circle() { return "CIRCLE"; }
  static get Point() { return "POINT"; }
  static get Arc() { return "ARC"; }
  static get Shape() { return "SHAPE"; }
  static get Spline() { return "SPLINE"; }
  static get Helix() { return "HELIX"; }
  static get Light() { return "LIGHT"; }
  static get Ole2Frame() { return "OLE2FRAME"; }
  static get OleFrame() { return "OLEFRAME"; }
  static get Body() { return "BODY"; }
  static get Region() { return "REGION"; }
  static get Solid3D() { return "3DSOLID"; }
  static get Solid() { return "SOLID"; }
  static get AcadTable() { return "ACAD_TABLE"; }
  static get Trace() { return "TRACE"; }
  static get Text() { return "TEXT"; }
  static get Mesh() { return "MESH"; }
  static get MText() { return "MTEXT"; }
  static get MLine() { return "MLINE"; }
  static get Face3d() { return "3DFACE"; }
  static get Insert() { return "INSERT"; }
  static get Hatch() { return "HATCH"; }
  static get Leader() { return "LEADER"; }
  static get Tolerance() { return "TOLERANCE"; }
  static get Wipeout() { return "WIPEOUT"; }
  static get Underlay() { return "UNDERLAY"; }
  static get UnderlayPdf() { return "PDFUNDERLAY"; }
  static get UnderlayDwf() { return "DWFUNDERLAY"; }
  static get UnderlayDgn() { return "DGNUNDERLAY"; }
  static get UnderlayDefinition() { return "UNDERLAYDEFINITION"; }
  static get UnderlayPdfDefinition() { return "PDFDEFINITION"; }
  static get UnderlayDwfDefinition() { return "DWFDEFINITION"; }
  static get UnderlayDgnDefinition() { return "DGNDEFINITION"; }
  static get AttributeDefinition() { return "ATTDEF"; }
  static get Attribute() { return "ATTRIB"; }
  static get Vertex() { return "VERTEX"; }
  static get EndSequence() { return "SEQEND"; }
  static get Dimension() { return "DIMENSION"; }
  static get ArcDimension() { return "ARC_DIMENSION"; }
  static get Dictionary() { return "DICTIONARY"; }
  static get XRecord() { return "XRECORD"; }
  static get Image() { return "IMAGE"; }
  static get Viewport() { return "VIEWPORT"; }
  static get ImageDef() { return "IMAGEDEF"; }
  static get ImageDefReactor() { return "IMAGEDEF_REACTOR"; }
  static get RasterVariables() { return "RASTERVARIABLES"; }
  static get Group() { return "GROUP"; }
  static get Layout() { return "LAYOUT"; }
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching DxfObjectCode constructor. Use DxfObjectCode.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
}
