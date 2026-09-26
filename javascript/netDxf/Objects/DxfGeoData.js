// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject, DxfDictionary } from './DxfDatabaseObject.js';
import { BlockRecord } from '../Blocks/BlockRecord.js';
import { Vector2 } from '../Vector2.js';
import { Vector3 } from '../Vector3.js';
import { Copy } from '../../runtime/GeometryRuntime.js';
import { ReferenceList } from '../../runtime/ReferenceList.js';
import { InstallViewFields } from '../../runtime/ViewFields.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, FormatException, RequireInteger } from '../../runtime/Errors.js';

export const DxfGeoCoordinateType = Object.freeze({Unknown:0,LocalGrid:1,ProjectedGrid:2,Geographic:3});
export const DxfGeoScaleEstimation = Object.freeze({None:1,UserScale:2,GridScale:3,Prismoidal:4});
export class DxfGeoMeshPoint {
  #source; #target;
  constructor(source,target) {
    DxfGeoData.CheckFinite(source,'source'); DxfGeoData.CheckFinite(target,'target');
    this.#source=Copy(source); this.#target=Copy(target);
  }
  get Source(){return Copy(this.#source);}
  get Target(){return Copy(this.#target);}
}
export class DxfGeoMeshFace {
  constructor(first,second,third) {
    // The C# constructor names first even when a different index is negative.
    for(const value of [first,second,third]) RequireInteger(value,0,2147483647,'first');
    Object.defineProperties(this,{
      First:{value:first,enumerable:true},Second:{value:second,enumerable:true},Third:{value:third,enumerable:true}
    });
  }
}
class NonNullCollection extends ReferenceList {
  #index(index,append=false) {
    if(!Number.isInteger(index)||index<0||index>=this.Count+(append?1:0))throw new ArgumentOutOfRangeException('index',index);
  }
  Add(item){this.Insert(this.Count,item);}
  Insert(index,item){this.#index(index,true);if(item==null)throw new ArgumentNullException('item');super.Insert(index,item);}
  set_Item(index,item){this.#index(index);if(item==null)throw new ArgumentNullException('item');super.set_Item(index,item);}
}
const finite=value=>{DxfGeoData.CheckFinite(value,'value');return Copy(value);};
const positive=value=>{finite(value);if(value<=0)throw new ArgumentOutOfRangeException('value');return value;};
const nonzero=value=>{
  finite(value);
  if(value.X===0&&value.Y===0&&(!(value instanceof Vector3)||value.Z===0))throw new ArgumentOutOfRangeException('value');
  return Copy(value);
};
const range=(min,max)=>value=>RequireInteger(value,min,max,'value');
function text(value,definition) {
  if(value==null)throw new ArgumentNullException('value');
  if(typeof value!=='string')throw new ArgumentException('Expected a Unicode string.','value');
  if(/[\0\r]/.test(value)||(!definition&&value.includes('\n'))||(definition&&value.includes('^J')))
    throw new ArgumentException('The string contains an unsupported transport control sequence.','value');
  for(let i=0;i<value.length;i++) {
    const code=value.charCodeAt(i);
    if(code>=0xd800&&code<=0xdbff) {
      const low=value.charCodeAt(++i);
      if(!(low>=0xdc00&&low<=0xdfff))throw new ArgumentException('The string contains an unpaired UTF-16 surrogate.','value');
    } else if(code>=0xdc00&&code<=0xdfff)throw new ArgumentException('The string contains an unpaired UTF-16 surrogate.','value');
  }
  return value;
}
/** Public v2 GEODATA metadata. Coordinate definitions and mesh mappings stay uninterpreted. */
export class DxfGeoData extends DxfDatabaseObject {
  #host=null; #points=new NonNullCollection(); #faces=new NonNullCollection();
  SeaLevelCorrection=false;
  constructor(hostBlock) {
    super('GEODATA');
    // No arguments mirrors the internal loader/clone constructor.
    if(arguments.length) {
      if(hostBlock==null)throw new ArgumentNullException('hostBlock');
      this.#host=hostBlock;
    }
  }
  get Version(){return 2;}
  get HostBlock(){return this.#host;}
  SetLoadedHost(host){if(host==null)throw new FormatException('GEODATA host must resolve to a BLOCK_RECORD.');this.#host=host;}
  get MeshPoints(){return this.#points;}
  get MeshFaces(){return this.#faces;}
  SetMesh(meshPoints,meshFaces) {
    if(meshPoints==null||meshFaces==null)throw new ArgumentNullException(meshPoints==null?'meshPoints':'meshFaces');
    const points=Array.from(meshPoints),faces=Array.from(meshFaces);
    if(points.some(p=>p==null)||faces.some(f=>f==null))throw new ArgumentException('Mesh members cannot be null.');
    for(const f of faces)if(f.First>=points.length||f.Second>=points.length||f.Third>=points.length)
      throw new ArgumentException('A mesh face index exceeds the point count.','meshFaces');
    this.#points.Clear();this.#faces.Clear();
    for(const p of points)this.#points.Add(p);for(const f of faces)this.#faces.Add(f);
  }
  get DatabaseReferences(){const owner=this;return { *[Symbol.iterator](){yield owner.#host;} };}
  CopyDatabaseReferencesTo(clone,resolve){const host=resolve(this.#host);clone.SetLoadedHost(host instanceof BlockRecord?host:null);}
  ValidateValues(database,errors) {
    if(database.Document.DrawingVariables.AcadVer<16)errors.Add('Typed GEODATA version 2 requires DXF 2010 or later.');
    if(this.#host===null)errors.Add('GEODATA has no host block record.');
    for(const f of this.#faces)if(f.First>=this.#points.Count||f.Second>=this.#points.Count||f.Third>=this.#points.Count)
      errors.Add('GEODATA mesh face index is outside the point array.');
  }
  ValidateDatabaseSchema(database,errors) {
    this.ValidateValues(database,errors);
    const owner=this.Owner instanceof DxfDictionary?this.Owner:null;
    if(owner===null||owner.Owner!==this.#host||!owner.Contains('ACAD_GEOGRAPHICDATA')||owner.get_Item('ACAD_GEOGRAPHICDATA')!==this)
      errors.Add("GEODATA must be stored under its host block extension dictionary's ACAD_GEOGRAPHICDATA entry.");
    if(this.Database!==null&&(this.#host?.ExtensionDictionary??null)!==owner)errors.Add('GEODATA host extension dictionary is not attached.');
  }
  CloneShell() {
    const copy=new DxfGeoData();
    for(const key of ['CoordinateType','DesignPoint','ReferencePoint','UpDirection','NorthDirection','HorizontalUnitScale','VerticalUnitScale',
      'HorizontalUnits','VerticalUnits','ScaleEstimation','UserScaleFactor','SeaLevelCorrection','SeaLevelElevation','CoordinateProjectionRadius',
      'CoordinateSystemDefinition','GeoRssTag','ObservationFrom','ObservationTo','ObservationCoverage'])copy[key]=this[key];
    copy.SetMesh(this.#points,this.#faces);return copy;
  }
  static CheckFinite(value,name) {
    const values=value instanceof Vector3?[value.X,value.Y,value.Z]:value instanceof Vector2?[value.X,value.Y]:[value];
    for(const number of values)if(!Number.isFinite(number))throw new ArgumentOutOfRangeException(name,'A finite value is required.');
  }
}
InstallViewFields(DxfGeoData,{
  CoordinateType:[3,range(0,3)],DesignPoint:[()=>Vector3.Zero,finite],ReferencePoint:[()=>Vector3.Zero,finite],
  UpDirection:[()=>Vector3.UnitZ,nonzero],NorthDirection:[()=>Vector2.UnitY,nonzero],
  HorizontalUnitScale:[1,positive],VerticalUnitScale:[1,positive],HorizontalUnits:[6,range(0,24)],VerticalUnits:[6,range(0,24)],
  ScaleEstimation:[1,range(1,4)],UserScaleFactor:[1,positive],SeaLevelElevation:[0,finite],
  CoordinateProjectionRadius:[0,value=>{finite(value);if(value<0)throw new ArgumentOutOfRangeException('value');return value;}],
  CoordinateSystemDefinition:['',value=>text(value,true)],GeoRssTag:['',value=>text(value,false)],
  ObservationFrom:['',value=>text(value,false)],ObservationTo:['',value=>text(value,false)],ObservationCoverage:['',value=>text(value,false)]
});
