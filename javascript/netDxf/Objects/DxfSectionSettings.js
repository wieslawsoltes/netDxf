// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { IsDatabaseModel, RegisterDatabaseModel } from '../../runtime/DatabaseModel.js';
import { ReadOnlyArrayView } from '../../runtime/StoredRecord.js';
import { CheckStoredName } from '../../runtime/CheckedCollection.js';
import { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, InvalidCastException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
const values = new WeakMap();
const defaults = {SectionType:0,GeometryValue:0,Flags:0,ColorCode:63,ColorIndex:256,LayerName:'0',LinetypeName:'ByLayer',LinetypeScale:1,PlotStyleName:'ByColor',Lineweight:-1,FaceTransparency:0,EdgeTransparency:0,HatchPatternType:0,HatchPatternName:'',HatchAngle:0,HatchScale:1,HatchSpacing:1};
export class DxfSectionGeometrySettings {
  constructor() { values.set(this,{...defaults}); }
  Clone() { const copy=new DxfSectionGeometrySettings();values.set(copy,{...values.get(this)});return copy; }
  ValidateValues() {
    this.ColorCode=this.ColorCode;this.ColorIndex=this.ColorIndex;this.FaceTransparency=this.FaceTransparency;this.EdgeTransparency=this.EdgeTransparency;
    for(const key of ['LinetypeScale','HatchAngle','HatchScale','HatchSpacing'])DxfSectionSettings.Finite(this[key]);
    for(const [key,parameter] of [['LayerName','layerName'],['LinetypeName','linetypeName'],['PlotStyleName','plotStyleName'],['HatchPatternName','hatchPatternName']])DxfSectionSettings.StoredText(this[key],parameter);
  }
}
for(const key of Object.keys(defaults))Object.defineProperty(DxfSectionGeometrySettings.prototype,key,{
  get(){return values.get(this)[key];},set(value){
    if(['SectionType','GeometryValue','Flags'].includes(key))value=RequireInteger(value,-2147483648,2147483647);
    else if(key==='ColorCode'){if(value!==62&&value!==63)throw new ArgumentOutOfRangeException('value');}
    else if(key==='ColorIndex')value=RequireInteger(value,0,256);
    else if(['FaceTransparency','EdgeTransparency'].includes(key))value=RequireInteger(value,0,100);
    else if(['HatchPatternType','Lineweight'].includes(key))value=RequireInteger(value,-32768,32767);
    else if(typeof defaults[key]==='string')value=DxfSectionSettings.StoredText(value,'value');
    else value=DxfSectionSettings.Finite(value);
    values.get(this)[key]=value;
  }
});
export class DxfSectionTypeSettings {
  #sources;#geometry;
  constructor(sectionType,generationOptions,sourceObjects,destinationBlock,destinationFileName,geometrySettings,repeatGeometryMarkers=true){
    if(sourceObjects==null)throw new ArgumentNullException('sourceObjects');
    if(geometrySettings==null)throw new ArgumentNullException('geometrySettings');
    this.#sources=DxfSectionSettings.Snapshot(sourceObjects,DxfSectionSettings.MaximumSourceReferences,'sourceObjects');
    if(this.#sources.some(item=>IsDatabaseModel(item,'DxfDocument')))throw new ArgumentException('A document is not a source object; use null for the null handle.','sourceObjects');
    this.#geometry=DxfSectionSettings.Snapshot(geometrySettings,DxfSectionSettings.MaximumGeometrySettings,'geometrySettings').map(item=>{
      if(item==null)throw new ArgumentException('A geometry bundle cannot be null.','geometrySettings');return item.Clone();
    });
    for(const item of this.#geometry)item.ValidateValues();
    DxfSectionSettings.StoredText(destinationFileName,'destinationFileName');
    Object.defineProperties(this,{SectionType:{value:RequireInteger(sectionType,-2147483648,2147483647,'sectionType'),enumerable:true},GenerationOptions:{value:RequireInteger(generationOptions,-2147483648,2147483647,'generationOptions'),enumerable:true},
      DestinationBlock:{value:destinationBlock,enumerable:true},DestinationFileName:{value:destinationFileName,enumerable:true},RepeatGeometryMarkers:{value:repeatGeometryMarkers,enumerable:true}});
    Object.freeze(this);
  }
  get SourceObjects(){return ReadOnlyArrayView(this.#sources);}
  get GeometrySettings(){return ReadOnlyArrayView(this.#geometry);}
  Copy(resolve){
    if(resolve==null)throw new ArgumentNullException('selector');
    // C# evaluates the destination argument before enumerating deferred source Select.
    const destination=resolve(this.DestinationBlock);
    if(destination!==null&&!IsDatabaseModel(destination,'BlockRecord'))throw new InvalidCastException();
    const sources=this.#sources;
    return new DxfSectionTypeSettings(this.SectionType,this.GenerationOptions,(function*(){for(const item of sources)yield resolve(item);})(),destination,this.DestinationFileName,this.#geometry,this.RepeatGeometryMarkers);
  }
}
function* references(types){for(const type of types){yield* type.SourceObjects;yield type.DestinationBlock;}}
export class DxfSectionSettings extends DxfDatabaseObject {
  static get MaximumTypeSettings(){return 1024;}
  static get MaximumSourceReferences(){return 1048576;}
  static get MaximumGeometrySettings(){return 65536;}
  #types=[];#sectionType=0;
  constructor(codeName='SECTIONSETTINGS'){
    super(codeName);if(codeName!=='SECTIONSETTINGS'&&codeName!=='SECTION_SETTINGS')throw new ArgumentException('Unrecognized section-settings record name.','codeName');
  }
  get SectionType(){return this.#sectionType;}set SectionType(value){this.#sectionType=RequireInteger(value,-2147483648,2147483647);}
  get TypeSettings(){return ReadOnlyArrayView(this.#types);}
  SetTypeSettings(input){
    if(input==null)throw new ArgumentNullException('values');
    const snapshot=[];let sources=0,geometry=0;
    for(const item of input){
      if(snapshot.length===DxfSectionSettings.MaximumTypeSettings)throw new ArgumentException('The section type admission limit was exceeded.','values');
      if(item==null)throw new ArgumentException('A type bundle cannot be null.','values');
      if(item.SourceObjects.Count>DxfSectionSettings.MaximumSourceReferences-sources||item.GeometrySettings.Count>DxfSectionSettings.MaximumGeometrySettings-geometry)
        throw new ArgumentException('The aggregate section settings admission limit was exceeded.','values');
      sources+=item.SourceObjects.Count;geometry+=item.GeometrySettings.Count;snapshot.push(item.Copy(value=>value));
    }
    if(this.IsErased)throw new InvalidOperationException('Erased section settings cannot change references.');
    for(const item of references(snapshot)){
      if(item instanceof DxfDatabaseObject&&item.IsErased)throw new InvalidOperationException('An erased object cannot be a source reference.');
      if(item!==null&&this.Database!==null)this.Database.CheckRegistered(item);
    }
    this.#types=snapshot;
  }
  ValidateValues(){
    if(this.IsErased)throw new InvalidOperationException('Erased section settings cannot be registered or written.');
    if(this.#types.length>DxfSectionSettings.MaximumTypeSettings||this.#types.reduce((sum,type)=>sum+type.SourceObjects.Count,0)>DxfSectionSettings.MaximumSourceReferences||this.#types.reduce((sum,type)=>sum+type.GeometrySettings.Count,0)>DxfSectionSettings.MaximumGeometrySettings)
      throw new InvalidOperationException('The section settings admission limit was exceeded.');
    for(const type of this.#types)for(const geometry of type.GeometrySettings)geometry.ValidateValues();
    for(const item of this.DatabaseReferences)if(item instanceof DxfDatabaseObject&&item.IsErased)throw new InvalidOperationException('Section settings reference an erased object.');
  }
  get DatabaseReferences(){return references(this.#types);}
  CloneShell(){const copy=new DxfSectionSettings(this.CodeName);copy.SectionType=this.SectionType;return copy;}
  CopyDatabaseReferencesTo(clone,resolve){
    if(!(clone instanceof DxfSectionSettings))throw new InvalidCastException();
    const types=this.#types;clone.SetTypeSettings((function*(){for(const type of types)yield type.Copy(resolve);})());
  }
  ValidateDatabaseSchema(database,errors){try{this.ValidateValues();}catch(error){if(error instanceof InvalidOperationException||error instanceof ArgumentException)errors.Add(error.message);else throw error;}}
  static Snapshot(input,maximum,parameter){const result=[];for(const item of input){if(result.length===maximum)throw new ArgumentException('The section settings admission limit was exceeded.',parameter);result.push(item);}return result;}
  static Finite(value){if(!Number.isFinite(value))throw new ArgumentOutOfRangeException('value');return value;}
  static StoredText(value,parameter){CheckStoredName(value,parameter,{allowEmpty:true,nullIsArgumentNull:true});return value;}
}
RegisterDatabaseModel('DxfSectionSettings',DxfSectionSettings);
