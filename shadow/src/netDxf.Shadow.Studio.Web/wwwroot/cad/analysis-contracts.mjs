export function healthOptions(values={}) {
  const tinyLength=Number(values.tinyLength??1e-7),gapTolerance=Number(values.gapTolerance??.01),maximumIssues=Number(values.maximumIssues??500);
  if(!Number.isFinite(tinyLength)||tinyLength<0||!Number.isFinite(gapTolerance)||gapTolerance<=tinyLength)throw new Error('Gap tolerance must be finite and greater than the nonnegative tiny-length threshold.');
  if(!Number.isInteger(maximumIssues)||maximumIssues<1||maximumIssues>2000)throw new Error('Choose 1–2000 maximum findings.');
  return {tinyLength,gapTolerance,maximumIssues};
}
export function moveProposal(x,y,z=0){
  const values=[x,y,z].map(Number);if(values.some(n=>!Number.isFinite(n)||Math.abs(n)>1e12)||values.every(n=>n===0))throw new Error('Enter a finite, nonzero move vector within the drawing coordinate limits.');
  return {description:'Move selected objects',operations:[{kind:'command',command:{name:'MOVE',points:[{x:0,y:0,z:0},{x:values[0],y:values[1],z:values[2]}]}}]};
}
export function healthSummary(report){
  const counts={errors:0,warnings:0,information:0};for(const issue of report?.issues||[]){const severity=issue.severity;counts[severity===2||severity==='Error'?'errors':severity===1||severity==='Warning'?'warnings':'information']++;}
  return {...counts,complete:report?.complete===true,total:(report?.issues||[]).length};
}
export function previewCurrent(preview,state){return preview?.current===true&&preview.documentId===state?.documentId&&preview.workspaceId===state?.workspaceId&&preview.viewVersion===state?.viewVersion&&preview.revision===state?.revision;}
export function visibleIssues(report,query='',severity='all'){
  const wanted={error:2,warning:1,information:0}[severity];return (report?.issues||[]).filter(i=>(wanted===undefined||i.severity===wanted||String(i.severity).toLowerCase()===severity)&&`${i.code} ${i.message} ${i.object?.value||''}`.toLowerCase().includes(query.toLowerCase()));
}
