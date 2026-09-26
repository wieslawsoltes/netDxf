// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {BlockRecord} from '../Blocks/BlockRecord.js';
export class MLeaderBlockContent extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderBlockContent');}
}
InstallMLeaderFields(MLeaderBlockContent,[
  ['Block',341,()=>BlockRecord,null,true,2007],
  ['Normal',14,'Vector3',Vector3.UnitZ,false,2007],
  ['Position',15,'Vector3',Vector3.Zero,false,2007],
  ['Scale',16,'Vector3',new Vector3(1,1,1),false,2007],
  ['Rotation',46,'double',0.0,false,2007],
  ['Color',93,'int',-1056964608,false,2007],
]);
MLeaderComponentTypes.set('MLeaderBlockContent',MLeaderBlockContent);
InstallMLeaderChildren(MLeaderBlockContent,'MLeaderBlockContent');
