// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {AttributeDefinition} from './AttributeDefinition.js';
export class MLeaderBlockAttribute extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderBlockAttribute');}
}
InstallMLeaderFields(MLeaderBlockAttribute,[
  ['Definition',330,()=>AttributeDefinition,null,true,2007],
  ['Index',177,'short',0,false,2007],
  ['Width',44,'double',1.0,false,2007],
  ['Text',302,'string','',false,2007],
]);
MLeaderComponentTypes.set('MLeaderBlockAttribute',MLeaderBlockAttribute);
InstallMLeaderChildren(MLeaderBlockAttribute,'MLeaderBlockAttribute');
