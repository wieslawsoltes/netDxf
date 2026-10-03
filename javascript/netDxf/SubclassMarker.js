// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class SubclassMarker {
  // C# backing state is prefixed with $; public members retain their original names.
  static get ApplicationId() { return "AcDbRegAppTableRecord"; }
  static get Table() { return "AcDbSymbolTable"; }
  static get TableRecord() { return "AcDbSymbolTableRecord"; }
  static get Layer() { return "AcDbLayerTableRecord"; }
  static get VPort() { return "AcDbViewportTableRecord"; }
  static get View() { return "AcDbViewTableRecord"; }
  static get Linetype() { return "AcDbLinetypeTableRecord"; }
  static get TextStyle() { return "AcDbTextStyleTableRecord"; }
  static get MLineStyle() { return "AcDbMlineStyle"; }
  static get DimensionStyleTable() { return "AcDbDimStyleTable"; }
  static get DimensionStyle() { return "AcDbDimStyleTableRecord"; }
  static get Ucs() { return "AcDbUCSTableRecord"; }
  static get Dimension() { return "AcDbDimension"; }
  static get AlignedDimension() { return "AcDbAlignedDimension"; }
  static get LinearDimension() { return "AcDbRotatedDimension"; }
  static get RadialDimension() { return "AcDbRadialDimension"; }
  static get DiametricDimension() { return "AcDbDiametricDimension"; }
  static get Angular3PointDimension() { return "AcDb3PointAngularDimension"; }
  static get Angular2LineDimension() { return "AcDb2LineAngularDimension"; }
  static get OrdinateDimension() { return "AcDbOrdinateDimension"; }
  static get ArcDimension() { return "AcDbArcDimension"; }
  static get BlockRecord() { return "AcDbBlockTableRecord"; }
  static get BlockBegin() { return "AcDbBlockBegin"; }
  static get BlockEnd() { return "AcDbBlockEnd"; }
  static get Entity() { return "AcDbEntity"; }
  static get Arc() { return "AcDbArc"; }
  static get Circle() { return "AcDbCircle"; }
  static get Ellipse() { return "AcDbEllipse"; }
  static get Spline() { return "AcDbSpline"; }
  static get Face3D() { return "AcDbFace"; }
  static get Helix() { return "AcDbHelix"; }
  static get Light() { return "AcDbLight"; }
  static get Ole2Frame() { return "AcDbOle2Frame"; }
  static get OleFrame() { return "AcDbOleFrame"; }
  static get ModelerGeometry() { return "AcDbModelerGeometry"; }
  static get Solid3D() { return "AcDb3dSolid"; }
  static get Insert() { return "AcDbBlockReference"; }
  static get MInsert() { return "AcDbMInsertBlock"; }
  static get Line() { return "AcDbLine"; }
  static get Ray() { return "AcDbRay"; }
  static get XLine() { return "AcDbXline"; }
  static get MLine() { return "AcDbMline"; }
  static get Point() { return "AcDbPoint"; }
  static get Vertex() { return "AcDbVertex"; }
  static get Leader() { return "AcDbLeader"; }
  static get Polyline() { return "AcDbPolyline"; }
  static get Polyline2D() { return "AcDb2dPolyline"; }
  static get Polyline2DVertex() { return "AcDb2dVertex"; }
  static get Polyline3D() { return "AcDb3dPolyline"; }
  static get Polyline3DVertex() { return "AcDb3dPolylineVertex"; }
  static get PolyfaceMesh() { return "AcDbPolyFaceMesh"; }
  static get PolyfaceMeshVertex() { return "AcDbPolyFaceMeshVertex"; }
  static get PolyfaceMeshFace() { return "AcDbFaceRecord"; }
  static get PolygonMesh() { return "AcDbPolygonMesh"; }
  static get PolygonMeshVertex() { return "AcDbPolygonMeshVertex"; }
  static get Shape() { return "AcDbShape"; }
  static get Solid() { return "AcDbTrace"; }
  static get Trace() { return "AcDbTrace"; }
  static get Text() { return "AcDbText"; }
  static get Tolerance() { return "AcDbFcf"; }
  static get Wipeout() { return "AcDbWipeout"; }
  static get Mesh() { return "AcDbSubDMesh"; }
  static get MText() { return "AcDbMText"; }
  static get Hatch() { return "AcDbHatch"; }
  static get Underlay() { return "AcDbUnderlayReference"; }
  static get UnderlayDefinition() { return "AcDbUnderlayDefinition"; }
  static get Viewport() { return "AcDbViewport"; }
  static get Attribute() { return "AcDbAttribute"; }
  static get AttributeDefinition() { return "AcDbAttributeDefinition"; }
  static get Dictionary() { return "AcDbDictionary"; }
  static get XRecord() { return "AcDbXrecord"; }
  static get RasterImage() { return "AcDbRasterImage"; }
  static get RasterImageDef() { return "AcDbRasterImageDef"; }
  static get RasterImageDefReactor() { return "AcDbRasterImageDefReactor"; }
  static get RasterVariables() { return "AcDbRasterVariables"; }
  static get Group() { return "AcDbGroup"; }
  static get Layout() { return "AcDbLayout"; }
  static get PlotSettings() { return "AcDbPlotSettings"; }
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching SubclassMarker constructor. Use SubclassMarker.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
}
