// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {BlockRecord} from '../Blocks/BlockRecord.js';
import {DxfMLeaderStyle} from '../Objects/DxfMLeaderStyle.js';
import {Linetype} from '../Tables/Linetype.js';
import {TextStyle} from '../Tables/TextStyle.js';
export class MLeaderProperties extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderProperties');}
}
InstallMLeaderFields(MLeaderProperties,[
  ['Style',340,()=>DxfMLeaderStyle,null,true,2007],
  ['PropertyOverrideFlags',90,'int',0,false,2007],
  ['LeaderType',170,'short',1,false,2007],
  ['LeaderLineColor',91,'int',-1056964608,false,2007],
  ['LeaderLinetype',341,()=>Linetype,null,true,2007],
  ['LeaderLineweight',171,'short',-2,false,2007],
  ['HasLanding',290,'bool',true,false,2007],
  ['HasDogleg',291,'bool',true,false,2007],
  ['DoglegLength',41,'double',8.0,false,2007],
  ['ArrowHead',342,()=>BlockRecord,null,true,2007],
  ['ArrowHeadSize',42,'double',4.0,false,2007],
  ['ContentType',172,'short',2,false,2007],
  ['TextStyle',343,()=>TextStyle,null,true,2007],
  ['TextLeftAttachment',173,'short',1,false,2007],
  ['TextRightAttachment',95,'int',1,false,2007],
  ['TextAngleType',174,'short',1,false,2007],
  ['TextAlignmentType',175,'short',2,false,2007],
  ['TextColor',92,'int',-1056964608,false,2007],
  ['HasTextFrame',292,'bool',false,false,2007],
  ['Block',344,()=>BlockRecord,null,true,2007],
  ['BlockColor',93,'int',-1056964608,false,2007],
  ['BlockScale',10,'Vector3',new Vector3(1,1,1),false,2007],
  ['BlockRotation',43,'double',0.0,false,2007],
  ['BlockConnectionType',176,'short',0,false,2007],
  ['IsAnnotative',293,'bool',false,false,2007],
  ['IsTextDirectionNegative',294,'bool',false,false,2007],
  ['TextEditorAlignment',178,'short',0,false,2007],
  ['TextAttachmentPoint',179,'short',1,false,2007],
  ['Scale',45,'double',1.0,false,2007],
  ['TextAttachmentDirection',271,'short',null,false,2010],
  ['TextBottomAttachment',272,'short',null,false,2010],
  ['TextTopAttachment',273,'short',null,false,2010],
  ['LeaderExtendToText',295,'bool',null,false,2013],
]);
MLeaderComponentTypes.set('MLeaderProperties',MLeaderProperties);
InstallMLeaderChildren(MLeaderProperties,'MLeaderProperties');
