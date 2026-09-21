// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {DxfDatabaseObject} from './DxfDatabaseObject.js';
import {MLeaderStyleProperties} from '../Entities/MLeaderStyleProperties.Fields.js';
import {ArgumentException,ArgumentOutOfRangeException,InvalidOperationException,NotSupportedException} from '../../runtime/Errors.js';
/** Detached MLEADERSTYLE. Registered DxfObjectDatabase adoption is not supplied by this model. */
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
