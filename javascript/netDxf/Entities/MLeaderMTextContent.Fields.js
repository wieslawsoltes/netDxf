// Copyright (c) netDxf contributors. MIT License; see package LICENSE.
import {MLeaderData,InstallMLeaderFields,MLeaderComponentTypes} from './MLeaderData.js';
import {InitializeMLeaderChildren,InstallMLeaderChildren} from './MLeaderContext.js';
import {Vector3} from '../Vector3.js';
import {TextStyle} from '../Tables/TextStyle.js';
export class MLeaderMTextContent extends MLeaderData {
  constructor(){super();InitializeMLeaderChildren(this,'MLeaderMTextContent');}
}
InstallMLeaderFields(MLeaderMTextContent,[
  ['Text',304,'string','',false,2007],
  ['Normal',11,'Vector3',Vector3.UnitZ,false,2007],
  ['Style',340,()=>TextStyle,null,true,2007],
  ['Position',12,'Vector3',Vector3.Zero,false,2007],
  ['Direction',13,'Vector3',Vector3.UnitX,false,2007],
  ['Rotation',42,'double',0.0,false,2007],
  ['Width',43,'double',0.0,false,2007],
  ['DefinedHeight',44,'double',0.0,false,2007],
  ['LineSpacingFactor',45,'double',1.0,false,2007],
  ['LineSpacingStyle',170,'short',1,false,2007],
  ['Color',90,'int',-1056964608,false,2007],
  ['Attachment',171,'short',1,false,2007],
  ['FlowDirection',172,'short',1,false,2007],
  ['BackgroundColor',91,'int',-939524096,false,2007],
  ['BackgroundScale',141,'double',1.5,false,2007],
  ['BackgroundTransparency',92,'int',0,false,2007],
  ['UseWindowBackgroundColor',291,'bool',false,false,2007],
  ['HasBackgroundFill',292,'bool',false,false,2007],
  ['ColumnType',173,'short',0,false,2007],
  ['AutoHeight',293,'bool',false,false,2007],
  ['ColumnWidth',142,'double',0.0,false,2007],
  ['ColumnGutter',143,'double',0.0,false,2007],
  ['ColumnFlowReversed',294,'bool',false,false,2007],
  ['UseWordBreak',295,'bool',true,false,2007],
]);
MLeaderComponentTypes.set('MLeaderMTextContent',MLeaderMTextContent);
InstallMLeaderChildren(MLeaderMTextContent,'MLeaderMTextContent');
