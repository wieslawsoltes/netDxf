// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.
import { HeaderVariable } from './HeaderVariable.js';
import { HeaderVariableCode } from './HeaderVariableCode.js';
import { Vector3 } from '../Vector3.js';
import { AciColor } from '../AciColor.js';
import { UCS } from '../Tables/UCS.js';
import { StringDictionary } from '../../runtime/StringDictionary.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { BoxedScalar } from '../../runtime/BoxedScalar.js';
import { HeaderEnum } from '../../runtime/HeaderBox.js';
import { HeaderDateTime, HeaderTimeSpan, GetHeaderEnvironment } from '../../runtime/HeaderTime.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidCastException, NullReferenceException, NotSupportedException, RequireInteger } from '../../runtime/Errors.js';
import { DxfVersion } from '../Header/DxfVersion.js';
import { AttMode } from '../Header/AttMode.js';
import { PointShape } from '../Header/PointShape.js';
import { AngleDirection } from '../Units/AngleDirection.js';
import { AngleUnitType } from '../Units/AngleUnitType.js';
import { LinearUnitType } from '../Units/LinearUnitType.js';
import { DrawingUnits } from '../Units/DrawingUnits.js';
import { Lineweight } from '../Lineweight.js';
import { MLineJustification } from '../Entities/MLineJustification.js';

const enumTypes={DxfVersion,AttMode,PointShape,AngleDirection,AngleUnitType,LinearUnitType,DrawingUnits,Lineweight,MLineJustification};
function box(type,value){
  if(type==='short')return new BoxedScalar('Int16',value);
  if(enumTypes[type])return new HeaderEnum(type,enumTypes[type],value);
  return Copy(value);
}
function unbox(type,value){
  if(value==null){if(['string','AciColor'].includes(type))return null;throw new NullReferenceException();}
  if(type==='short'&&value instanceof BoxedScalar&&value.Type==='Int16')return value.Value;
  if(enumTypes[type]&&(value instanceof HeaderEnum||value instanceof BoxedScalar&&value.Type==='Int32'))return value.Value;
  if(type==='double'&&(typeof value==='number'||value instanceof BoxedScalar&&value.Type==='Double'))return typeof value==='number'?value:value.Value;
  if(type==='bool'&&typeof value==='boolean'||type==='string'&&typeof value==='string')return value;
  if(type==='Vector3'&&value instanceof Vector3)return Copy(value);
  if(type==='AciColor'&&value instanceof AciColor)return value;
  if(type==='DateTime'&&value instanceof HeaderDateTime||type==='TimeSpan'&&value instanceof HeaderTimeSpan)return value;
  throw new InvalidCastException('The boxed header value does not match '+type+'.');
}
const records=new WeakMap();
const types=new Map([["AcadVer", "DxfVersion"], ["DwgCodePage", "string"], ["LastSavedBy", "string"], ["HandleSeed", "string"], ["Angbase", "double"], ["Angdir", "AngleDirection"], ["AttMode", "AttMode"], ["AUnits", "AngleUnitType"], ["AUprec", "short"], ["CeColor", "AciColor"], ["CeLtScale", "double"], ["CeLtype", "string"], ["CeLweight", "Lineweight"], ["CLayer", "string"], ["CMLJust", "MLineJustification"], ["CMLScale", "double"], ["CMLStyle", "string"], ["DimStyle", "string"], ["TextSize", "double"], ["TextStyle", "string"], ["LUnits", "LinearUnitType"], ["LUprec", "short"], ["MirrText", "bool"], ["Extnames", "bool"], ["InsBase", "Vector3"], ["InsUnits", "DrawingUnits"], ["LtScale", "double"], ["LwDisplay", "bool"], ["PdMode", "PointShape"], ["PdSize", "double"], ["PLineGen", "short"], ["PsLtScale", "short"], ["SplineSegs", "short"], ["SurfU", "short"], ["SurfV", "short"], ["TdCreate", "DateTime"], ["TduCreate", "DateTime"], ["TdUpdate", "DateTime"], ["TduUpdate", "DateTime"], ["TdinDwg", "TimeSpan"]]);
const nonempty=new Set(['CeLtype','CLayer','CMLStyle','DimStyle','TextStyle']);
const ranges={AUprec:[0,8],LUprec:[0,8],PLineGen:[0,1],PsLtScale:[0,1],SplineSegs:[1,32767],SurfU:[0,200],SurfV:[0,200]};
/** Detached drawing variables. Lists are snapshots containing live HeaderVariable references.
 * The optional environment argument is a JavaScript host adapter, not a C# overload.
 */
export class HeaderVariables {
  #ucs; #custom=new StringDictionary();
  constructor(environment=GetHeaderEnvironment()){
    const known=new StringDictionary();records.set(this,known);
    const defaults=[
      ['AcadVer',1,'DxfVersion',DxfVersion.AutoCad2000],
      ['DwgCodePage',3,'string',"ANSI_1252"],
      ['LastSavedBy',1,'string',environment.UserName()],
      ['HandleSeed',5,'string',"1"],
      ['Angbase',50,'double',0],
      ['Angdir',70,'AngleDirection',AngleDirection.CCW],
      ['AttMode',70,'AttMode',AttMode.Normal],
      ['AUnits',70,'AngleUnitType',AngleUnitType.DecimalDegrees],
      ['AUprec',70,'short',0],
      ['CeColor',62,'AciColor',AciColor.ByLayer],
      ['CeLtScale',40,'double',1.0],
      ['CeLtype',6,'string',"ByLayer"],
      ['CeLweight',370,'Lineweight',Lineweight.ByLayer],
      ['CLayer',8,'string',"0"],
      ['CMLJust',70,'MLineJustification',MLineJustification.Top],
      ['CMLScale',40,'double',20],
      ['CMLStyle',2,'string',"Standard"],
      ['DimStyle',2,'string',"Standard"],
      ['TextSize',40,'double',2.5],
      ['TextStyle',7,'string',"Standard"],
      ['LUnits',70,'LinearUnitType',LinearUnitType.Decimal],
      ['LUprec',70,'short',4],
      ['MirrText',70,'bool',false],
      ['Extnames',290,'bool',true],
      ['InsBase',10,'Vector3',Vector3.Zero],
      ['InsUnits',70,'DrawingUnits',DrawingUnits.Unitless],
      ['LtScale',40,'double',1.0],
      ['LwDisplay',290,'bool',false],
      ['PdMode',70,'PointShape',PointShape.Dot],
      ['PdSize',40,'double',0],
      ['PLineGen',70,'short',0],
      ['PsLtScale',70,'short',1],
      ['SplineSegs',70,'short',8],
      ['SurfU',70,'short',6],
      ['SurfV',70,'short',6],
      ['TdCreate',40,'DateTime',environment.Now()],
      ['TduCreate',40,'DateTime',environment.UtcNow()],
      ['TdUpdate',40,'DateTime',environment.Now()],
      ['TduUpdate',40,'DateTime',environment.UtcNow()],
      ['TdinDwg',40,'TimeSpan',new HeaderTimeSpan()],
    ];
    for(const [name,code,type,value] of defaults){
      known.Add(HeaderVariableCode[name],new HeaderVariable(HeaderVariableCode[name],code,box(type,value)));
    }
    this.#ucs=new UCS('Unnamed');
  }
  get CurrentUCS(){return this.#ucs;}
  set CurrentUCS(value){if(value==null)throw new ArgumentNullException('value');this.#ucs=value;}
  KnownValues(){return new ReferenceList(records.get(this).Values);}
  KnownNames(){return new ReferenceList(records.get(this).Keys);}
  CustomValues(){return new ReferenceList(this.#custom.Values);}
  CustomNames(){return new ReferenceList(this.#custom.Keys);}
  AddCustomVariable(variable){
    if(variable==null)throw new ArgumentNullException('variable');
    if(!variable.Name.startsWith('$'))throw new ArgumentException("A header variable name must start with '$'.",'variable');
    if(records.get(this).ContainsKey(variable.Name))throw new ArgumentException('A known header variable with the same name already exists.','variable');
    this.#custom.Add(variable.Name,variable);
  }
  ContainsCustomVariable(name){return this.#custom.ContainsKey(name);}
  TryGetCustomVariable(name,result){return this.#custom.TryGetValue(name,result);}
  RemoveCustomVariable(name){return this.#custom.Remove(name);}
  ClearCustomVariables(){this.#custom.Clear();}
}

for(const [name,type] of types)Object.defineProperty(HeaderVariables.prototype,name,{
  get(){return unbox(type,records.get(this).get_Item(HeaderVariableCode[name]).Value);},
  set(value){
    if(name==='AcadVer'&&value<DxfVersion.AutoCad2000)throw new NotSupportedException('Only AutoCad2000 and newer DXF versions are supported.');
    if(nonempty.has(name)&&(value==null||value==='')||name==='CeColor'&&value==null)throw new ArgumentNullException('value');
    if(ranges[name])RequireInteger(value,...ranges[name]);
    if(['CeLtScale','TextSize','LtScale'].includes(name)&&value<=0)throw new ArgumentOutOfRangeException('value',value);
    if(name==='LUnits'&&(value===LinearUnitType.Architectural||value===LinearUnitType.Engineering))this.InsUnits=DrawingUnits.Inches;
    records.get(this).get_Item(HeaderVariableCode[name]).Value=box(type,value);
  }
});
