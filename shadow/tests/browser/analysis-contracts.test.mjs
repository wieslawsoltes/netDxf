import test from 'node:test';
import assert from 'node:assert/strict';
import {healthOptions,moveProposal,healthSummary,previewCurrent,visibleIssues} from '../../src/netDxf.Shadow.Studio.Web/wwwroot/cad/analysis-contracts.mjs';

test('health thresholds are finite bounded and ordered',()=>{
  assert.deepEqual(healthOptions({tinyLength:'0.001',gapTolerance:'.02',maximumIssues:'200'}),{tinyLength:.001,gapTolerance:.02,maximumIssues:200});
  for(const values of [{tinyLength:-1},{gapTolerance:0},{gapTolerance:Infinity},{tinyLength:.1,gapTolerance:.01},{maximumIssues:2001},{maximumIssues:1.5}])assert.throws(()=>healthOptions(values));
});
test('move preview preserves three-dimensional intent and defers selection to server receipt',()=>{
  const value=moveProposal('5','-2','3');assert.equal(value.operations[0].command.name,'MOVE');assert.deepEqual(value.operations[0].command.points,[{x:0,y:0,z:0},{x:5,y:-2,z:3}]);assert.equal(value.operations[0].command.entities,undefined);
  for(const vector of [[0,0,0],[Infinity,1,0],[1e13,0,0],['no',1,0]])assert.throws(()=>moveProposal(...vector));
});
test('health summary never turns budget exhaustion into clean completion',()=>{
  assert.deepEqual(healthSummary({complete:false,issues:[{severity:2},{severity:1},{severity:0}]}),{errors:1,warnings:1,information:1,complete:false,total:3});
  assert.equal(healthSummary({complete:true,issues:[]}).complete,true);
});
test('preview current binds document workspace revision and view epoch',()=>{
  const state={documentId:'d',workspaceId:'live',revision:3,viewVersion:4},value={...state,current:true};assert.equal(previewCurrent(value,state),true);
  for(const change of [{documentId:'other'},{workspaceId:'branch'},{revision:4},{viewVersion:5},{current:false}])assert.equal(previewCurrent({...value,...change},state),false);
});
test('findings filter severity and searchable identities without mutating report',()=>{
  const report={issues:[{code:'geometry.duplicate',message:'Duplicates H:2',object:{value:'H:1'},severity:1},{code:'geometry.invalid',message:'Radius',severity:2}]};
  assert.equal(visibleIssues(report,'H:1','warning').length,1);assert.equal(visibleIssues(report,'radius','error').length,1);assert.equal(visibleIssues(report,'radius','warning').length,0);assert.equal(report.issues.length,2);
});
