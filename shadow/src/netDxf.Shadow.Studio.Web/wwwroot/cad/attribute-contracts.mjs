import {boundedNumber,lineweights} from './resource-contracts.mjs';
export const attributeAlignments=['TopLeft','TopCenter','TopRight','MiddleLeft','MiddleCenter','MiddleRight','BottomLeft','BottomCenter','BottomRight','BaselineLeft','BaselineCenter','BaselineRight','Aligned','Middle','Fit'];
export const attributeId=value=>typeof value==='string'?value:value?.value;
const numeric={height:[1e-9,1e9],rotation:[-1e9,1e9],width:[0,1e12],widthFactor:[.01,100],obliqueAngle:[-85,85],lineSpacing:[.25,4]};
const booleans=['backward','upsideDown','invisible','positionLocked','exactSpacing'];
/** Only authored differences are submitted. Hidden single-line fields never overwrite embedded content. */
export function attributeChanges(original,input) {
  const result={attribute:{value:attributeId(original.id)}};
  for(const [key,raw] of Object.entries(input)) {
    if(original.multiline&&['widthFactor','obliqueAngle','backward','upsideDown'].includes(key)||!original.multiline&&['lineSpacing','exactSpacing'].includes(key))continue;
    let value=raw;
    if(numeric[key])value=boundedNumber(raw,...numeric[key],key);
    else if(booleans.includes(key))value=!!raw;
    else if(['value','textStyle','alignment','layer','linetype','lineweight','color'].includes(key))value=String(raw);
    else continue;
    if(key==='value') {
      if(original.multiline)value=value.replace(/\r\n|\r|\n/g,'\\P');
      if(value.length>1_000_000||value.includes('\0')||!original.multiline&&/[\r\n]/.test(value))throw new Error('Invalid attribute text or text length.');
      if(original.constant&&value!==original.value)throw new Error('Edit constant values in the block definition.');
    }
    if(key==='alignment'&&!attributeAlignments.slice(0,original.multiline?9:15).includes(value))throw new Error('Choose a supported justification.');
    if(key==='lineweight'&&!['ByLayer','ByBlock',...lineweights].includes(value))throw new Error('Choose a supported lineweight.');
    if(key==='color') {
      value=value.trim();
      if(!/^(?:ByLayer|ByBlock|#[0-9a-f]{6}|\d{1,3})$/i.test(value)||/^\d/.test(value)&&Number(value)>256)throw new Error('Color requires ByLayer, ByBlock, ACI 1–255, or #RRGGBB.');
    }
    if(value!==original[key])result[key]=value;
  }
  return Object.keys(result).length>1?result:null;
}
