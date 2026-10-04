// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {BlockRecord} from '../Blocks/BlockRecord.js';
export class MLeaderArrowHead extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderArrowHead');}
}
InstallMLeaderFields(MLeaderArrowHead,[
  ['Index',94,'int',0,false,2007],
  ['Block',345,()=>BlockRecord,null,true,2007],
]);
MLeaderComponentTypes.set('MLeaderArrowHead',MLeaderArrowHead);
InstallMLeaderChildren(MLeaderArrowHead,'MLeaderArrowHead');
