// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
export class MLeaderNode extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderNode');}
}
InstallMLeaderFields(MLeaderNode,[
  ['HasLastLeaderPoint',290,'bool',false,false,2007],
  ['HasDoglegVector',291,'bool',false,false,2007],
  ['LastLeaderPoint',10,'Vector3',Vector3.Zero,false,2007],
  ['DoglegVector',11,'Vector3',Vector3.UnitX,false,2007],
  ['Index',90,'int',0,false,2007],
  ['DoglegLength',40,'double',1.0,false,2007],
  ['AttachmentDirection',271,'short',null,false,2010],
]);
MLeaderComponentTypes.set('MLeaderNode',MLeaderNode);
InstallMLeaderChildren(MLeaderNode,'MLeaderNode');
