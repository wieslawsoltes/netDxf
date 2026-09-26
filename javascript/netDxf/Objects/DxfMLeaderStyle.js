// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {DxfDatabaseObject,DxfDictionary} from './DxfDatabaseObject.js';
import {MLeaderStyleProperties} from '../Entities/MLeaderStyleProperties.Fields.js';
import {ArgumentException,ArgumentNullException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
/** Stored MLEADERSTYLE values and explicit registered adoption. */
export class DxfMLeaderStyle extends DxfDatabaseObject{
  #properties;#envelope=2;
  constructor(){super('MLEADERSTYLE');this.#properties=new MLeaderStyleProperties();this.#properties.Parent=this;}
  get Properties(){return this.#properties;}
  get StoredEnvelopeValue(){return this.#envelope;}set StoredEnvelopeValue(value){if(value!==null&&value!==2)throw new ArgumentOutOfRangeException('value');this.#envelope=value;}
  get DatabaseReferences(){return this.#properties.References;}
  CloneShell(){const copy=new DxfMLeaderStyle();copy.StoredEnvelopeValue=this.#envelope;this.#properties.CopyValuesTo(copy.#properties);return copy;}
  CopyDatabaseReferencesTo(target,resolve){target.#properties.MapReferences(resolve);}
  ValidateDatabaseSchema(database,errors){try{this.ValidateValues(database.Document.DrawingVariables.AcadVer);}catch(error){if(!(error instanceof ArgumentException||error instanceof InvalidOperationException||error instanceof NotSupportedException))throw error;errors.Add(error.message);}}
  ValidateValues(version){this.#properties.ValidateValues(version);if(this.#properties.ContentType<0||this.#properties.ContentType>2)throw new NotSupportedException('MLEADERSTYLE tolerance content is not qualified.');if(this.#properties.TextStyle===null)throw new InvalidOperationException('MLEADERSTYLE requires a text-style reference.');}
}

export function InstallDatabaseMLeaderStyle(Type) {
  Type.prototype.AddMLeaderStyle = function(name,style) {
    if (style == null) throw new ArgumentNullException('style'); DxfDictionary.ValidateName(name);
    if (style.Owner !== null || style.Database !== null) throw new ArgumentException('The MLEADERSTYLE must be detached and unowned.','style');
    style.ValidateValues(this.Document.DrawingVariables.AcadVer); style.Properties.CheckDocument(this.Document);
    if (this.Root.Contains('ACAD_MLEADERSTYLE')) {
      const dictionary = this.Root.get_Item('ACAD_MLEADERSTYLE');
      if (!(dictionary instanceof DxfDictionary)) throw new InvalidOperationException('ACAD_MLEADERSTYLE is not a dictionary.');
      dictionary.Add(name,style);
    } else {
      const dictionary = new DxfDictionary(); dictionary.Add(name,style);
      try { this.Root.Add('ACAD_MLEADERSTYLE',dictionary); }
      catch (error) { dictionary.Remove(name); style.Owner = null; throw error; }
    }
  };
}
