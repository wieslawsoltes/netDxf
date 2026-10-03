// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
export class MLeaderLine extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderLine');}
  get Color(){return this.StoredColor??-1056964608;} set Color(value){this.Set(92,value);}
}
InstallMLeaderFields(MLeaderLine,[
  ['Index',91,'int',0,false,2007],
  ['StoredColor',92,'int',null,false,2007],
]);
MLeaderComponentTypes.set('MLeaderLine',MLeaderLine);
InstallMLeaderChildren(MLeaderLine,'MLeaderLine');
