// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {BlockRecord} from '../Blocks/BlockRecord.js';
import {Linetype} from '../Tables/Linetype.js';
import {TextStyle} from '../Tables/TextStyle.js';
export class MLeaderStyleProperties extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderStyleProperties');}
}
InstallMLeaderFields(MLeaderStyleProperties,[
  ['ContentType',170,'short',2,false,2007],
  ['DrawMLeaderOrder',171,'short',1,false,2007],
  ['DrawLeaderOrder',172,'short',0,false,2007],
  ['MaximumLeaderPoints',90,'int',2,false,2007],
  ['FirstSegmentAngle',40,'double',0.0,false,2007],
  ['SecondSegmentAngle',41,'double',0.0,false,2007],
  ['LeaderType',173,'short',1,false,2007],
  ['LeaderLineColor',91,'int',-1056964608,false,2007],
  ['LeaderLinetype',340,()=>Linetype,null,true,2007],
  ['LeaderLineweight',92,'int',-2,false,2007],
  ['HasLanding',290,'bool',true,false,2007],
  ['LandingGap',42,'double',2.0,false,2007],
  ['HasDogleg',291,'bool',true,false,2007],
  ['DoglegLength',43,'double',8.0,false,2007],
  ['Description',3,'string','',false,2007],
  ['ArrowHead',341,()=>BlockRecord,null,true,2007],
  ['ArrowHeadSize',44,'double',4.0,false,2007],
  ['DefaultText',300,'string','',false,2007],
  ['TextStyle',342,()=>TextStyle,null,true,2007],
  ['TextLeftAttachment',174,'short',1,false,2007],
  ['TextAngleType',175,'short',1,false,2007],
  ['TextAlignmentType',176,'short',0,false,2007],
  ['TextRightAttachment',178,'short',1,false,2007],
  ['TextColor',93,'int',-1056964608,false,2007],
  ['TextHeight',45,'double',4.0,false,2007],
  ['HasTextFrame',292,'bool',false,false,2007],
  ['TextAlignAlwaysLeft',297,'bool',false,false,2007],
  ['AlignSpace',46,'double',4.0,false,2007],
  ['Block',343,()=>BlockRecord,null,true,2007],
  ['BlockColor',94,'int',-1056964608,false,2007],
  ['BlockScaleX',47,'double',1.0,false,2007],
  ['BlockScaleY',49,'double',1.0,false,2007],
  ['BlockScaleZ',140,'double',1.0,false,2007],
  ['HasBlockScaling',293,'bool',null,false,2007],
  ['BlockRotation',141,'double',0.0,false,2007],
  ['HasBlockRotation',294,'bool',true,false,2007],
  ['BlockConnectionType',177,'short',0,false,2007],
  ['Scale',142,'double',1.0,false,2007],
  ['OverwritePropertyValue',295,'bool',false,false,2007],
  ['IsAnnotative',296,'bool',false,false,2007],
  ['BreakGap',143,'double',3.75,false,2007],
  ['TextAttachmentDirection',271,'short',0,false,2007],
  ['TextBottomAttachment',272,'short',9,false,2007],
  ['TextTopAttachment',273,'short',9,false,2007],
]);
MLeaderComponentTypes.set('MLeaderStyleProperties',MLeaderStyleProperties);
InstallMLeaderChildren(MLeaderStyleProperties,'MLeaderStyleProperties');
