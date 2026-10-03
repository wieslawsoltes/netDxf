// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import { DxfDatabaseObject } from './DxfDatabaseObject.js';
import { RegisterDatabaseModel, IsDatabaseModel } from '../../runtime/DatabaseModel.js';
import { ArgumentOutOfRangeException, InvalidOperationException, RequireInteger } from '../../runtime/Errors.js';
export const DxfSunShadowType=Object.freeze({RayTraced:0,ShadowMaps:1,AreaSampled:2});
export class DxfSun extends DxfDatabaseObject {
  #values={Enabled:false,ColorIndex:7,TrueColor:0xffffff,Intensity:1,ShadowsEnabled:true,JulianDay:2451545,StoredTime:0,DaylightSavingTime:false,ShadowType:0,ShadowMapSize:256,ShadowSoftness:1};
  constructor(){super('SUN');}
  get StoredVersion(){return 1;}
  get Enabled(){return this.#values.Enabled;}set Enabled(value){this.#check();this.#values.Enabled=value;}
  get ColorIndex(){return this.#values.ColorIndex;}set ColorIndex(value){this.#check();this.#values.ColorIndex=RequireInteger(value,0,256);}
  get TrueColor(){return this.#values.TrueColor;}set TrueColor(value){this.#check();this.#values.TrueColor=value===null?null:RequireInteger(value,0,0xffffff);}
  get Intensity(){return this.#values.Intensity;}set Intensity(value){this.#check();if(!Number.isFinite(value))throw new ArgumentOutOfRangeException('value');this.#values.Intensity=value;}
  get ShadowsEnabled(){return this.#values.ShadowsEnabled;}set ShadowsEnabled(value){this.#check();this.#values.ShadowsEnabled=value;}
  get JulianDay(){return this.#values.JulianDay;}set JulianDay(value){this.#check();this.#values.JulianDay=RequireInteger(value,-2147483648,2147483647);}
  get StoredTime(){return this.#values.StoredTime;}set StoredTime(value){this.#check();this.#values.StoredTime=RequireInteger(value,-2147483648,2147483647);}
  get DaylightSavingTime(){return this.#values.DaylightSavingTime;}set DaylightSavingTime(value){this.#check();this.#values.DaylightSavingTime=value;}
  get ShadowType(){return this.#values.ShadowType;}set ShadowType(value){this.#check();this.#values.ShadowType=RequireInteger(value,0,2);}
  get ShadowMapSize(){return this.#values.ShadowMapSize;}set ShadowMapSize(value){this.#check();RequireInteger(value,64,4096);if((value&(value-1))!==0)throw new ArgumentOutOfRangeException('value');this.#values.ShadowMapSize=value;}
  get ShadowSoftness(){return this.#values.ShadowSoftness;}set ShadowSoftness(value){this.#check();this.#values.ShadowSoftness=RequireInteger(value,0,255);}
  #check(){if(this.IsErased)throw new InvalidOperationException('An erased SUN cannot be modified.');}
  CloneShell(){return Object.assign(new DxfSun(),this.#values);}
  ValidateDatabaseSchema(database,errors) {
    if(database.Document.DrawingVariables.AcadVer<15)errors.Add('Typed SUN requires R2007 or later.');
    const host=['VPort','View','Viewport'].some(name=>IsDatabaseModel(this.Owner,name));
    if(this.Owner!==null&&!host)errors.Add('SUN requires a view or viewport owner.');
    if(this.Database!==null&&(this.Owner===null||!host||this.Owner.Sun!==this))errors.Add('SUN requires a reciprocal view or viewport owner: '+(this.Handle??''));
  }
}
RegisterDatabaseModel('DxfSun',DxfSun);
