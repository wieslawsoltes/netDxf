// Observation-only data snapshots; expected results use reflection on the unchanged assembly.
import * as api from '../index.js';
import {MLeaderField} from '../netDxf/Entities/MLeaderData.js';
import {mleaderSchema} from './mleader-schema.mjs';
const reference=v=>v==null?null:{type:v.constructor.name,code:v.CodeName,handle:v.Handle};
const children={MLeaderContext:['MText','Block','Leaders'],MLeaderMTextContent:['ColumnHeights'],MLeaderBlockContent:['TransformationMatrix'],MLeaderNode:['Breaks','Lines'],MLeaderLine:['Color','Vertices','Breaks'],MLeaderProperties:['ArrowHeads','BlockAttributes'],MLeaderBreak:['Start','End'],MLeaderLineBreaks:['Index','Breaks'],MultiLeader:['Properties','Context','StoredVersion','Version'],DxfMLeaderStyle:['Properties','StoredEnvelopeValue']};
export function mleaderWire(value,wire){
 if(value instanceof MLeaderField)return{type:'MLeaderField',code:value.Code,fieldType:typeof value.Type==='function'?value.Type().name:({double:'Double',short:'Int16',int:'Int32',bool:'Boolean',string:'String'}[value.Type]??value.Type),initial:wire(value.Default),reference:value.Reference,minimum:value.MinimumVersion};
 const name=value.constructor.name;
 if(!(value instanceof api.MLeaderData||value instanceof api.MultiLeader||value instanceof api.DxfMLeaderStyle||value instanceof api.MLeaderBreak||value instanceof api.MLeaderLineBreaks))return;
 const fields={};for(const key of [...(mleaderSchema[name]??[]).map(x=>x[0]),...(children[name]??[])])fields[key]=value[key] instanceof api.DxfObject?reference(value[key]):wire(value[key]);
 const parent=value instanceof api.MLeaderData?value.Parent?.constructor.name??null:null;
 const common=value instanceof api.DxfObject?{code:value.CodeName,handle:value.Handle,owner:reference(value.Owner),xdata:Array.from(value.XData.Values,wire)}:null;
 return {type:name,fields,parent,common};
}
