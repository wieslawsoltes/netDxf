// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
// Native JavaScript generated from the pinned C# file by tools/NativePort.
// Do not edit generated bodies: update the audited lowerer and regenerate.
import * as Errors from '../../runtime/Errors.js';
import { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../../runtime/GeometryRuntime.js';
const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;

export class HeaderVariableCode {
  // C# backing state is prefixed with $; public members retain their original names.
  static get AcadVer() { return "$ACADVER"; }
  static get HandleSeed() { return "$HANDSEED"; }
  static get Angbase() { return "$ANGBASE"; }
  static get Angdir() { return "$ANGDIR"; }
  static get AttMode() { return "$ATTMODE"; }
  static get AUnits() { return "$AUNITS"; }
  static get AUprec() { return "$AUPREC"; }
  static get CeColor() { return "$CECOLOR"; }
  static get CeLtScale() { return "$CELTSCALE"; }
  static get CeLweight() { return "$CELWEIGHT"; }
  static get CeLtype() { return "$CELTYPE"; }
  static get CLayer() { return "$CLAYER"; }
  static get CMLJust() { return "$CMLJUST"; }
  static get CMLScale() { return "$CMLSCALE"; }
  static get CMLStyle() { return "$CMLSTYLE"; }
  static get DimStyle() { return "$DIMSTYLE"; }
  static get TextSize() { return "$TEXTSIZE"; }
  static get TextStyle() { return "$TEXTSTYLE"; }
  static get LUnits() { return "$LUNITS"; }
  static get LUprec() { return "$LUPREC"; }
  static get DwgCodePage() { return "$DWGCODEPAGE"; }
  static get Extnames() { return "$EXTNAMES"; }
  static get InsBase() { return "$INSBASE"; }
  static get InsUnits() { return "$INSUNITS"; }
  static get LastSavedBy() { return "$LASTSAVEDBY"; }
  static get LwDisplay() { return "$LWDISPLAY"; }
  static get LtScale() { return "$LTSCALE"; }
  static get MirrText() { return "$MIRRTEXT"; }
  static get PdMode() { return "$PDMODE"; }
  static get PdSize() { return "$PDSIZE"; }
  static get PLineGen() { return "$PLINEGEN"; }
  static get PsLtScale() { return "$PSLTSCALE"; }
  static get SplineSegs() { return "$SPLINESEGS"; }
  static get SurfU() { return "$SURFU"; }
  static get SurfV() { return "$SURFV"; }
  static get TdCreate() { return "$TDCREATE"; }
  static get TduCreate() { return "$TDUCREATE"; }
  static get TdUpdate() { return "$TDUPDATE"; }
  static get TduUpdate() { return "$TDUUPDATE"; }
  static get TdinDwg() { return "$TDINDWG"; }
  static get UcsOrg() { return "$UCSORG"; }
  static get UcsXDir() { return "$UCSXDIR"; }
  static get UcsYDir() { return "$UCSYDIR"; }
  constructor(...args) {
    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }
    throw new ArgumentException("No matching HeaderVariableCode constructor. Use HeaderVariableCode.CreateOverload(signature, ...args) for ambiguous numeric overloads.");
  }
  static CreateOverload(signature, ...args) {
    throw new ArgumentException('Unknown constructor signature.', 'signature');
  }
}
