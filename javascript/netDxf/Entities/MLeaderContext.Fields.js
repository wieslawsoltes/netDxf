// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
export class MLeaderContext extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderContext');}
}
InstallMLeaderFields(MLeaderContext,[
  ['Scale',40,'double',1.0,false,2007],
  ['BasePoint',10,'Vector3',Vector3.Zero,false,2007],
  ['TextHeight',41,'double',4.0,false,2007],
  ['ArrowHeadSize',140,'double',4.0,false,2007],
  ['LandingGap',145,'double',2.0,false,2007],
  ['TextLeftAttachment',174,'short',1,false,2007],
  ['TextRightAttachment',175,'short',1,false,2007],
  ['TextAlignment',176,'short',0,false,2007],
  ['BlockConnectionType',177,'short',0,false,2007],
  ['PlaneOrigin',110,'Vector3',Vector3.Zero,false,2007],
  ['PlaneXAxis',111,'Vector3',Vector3.UnitX,false,2007],
  ['PlaneYAxis',112,'Vector3',Vector3.UnitY,false,2007],
  ['PlaneNormalReversed',297,'bool',false,false,2007],
  ['TopAttachment',272,'short',null,false,2010],
  ['BottomAttachment',273,'short',null,false,2010],
]);
MLeaderComponentTypes.set('MLeaderContext',MLeaderContext);
InstallMLeaderChildren(MLeaderContext,'MLeaderContext');
