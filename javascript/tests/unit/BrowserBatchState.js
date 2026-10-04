import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createGeometryCaller } from '../../tools/geometry-wire.mjs';
import { doubleBits } from '../../tools/wire.mjs';
const manifest=JSON.parse(fs.readFileSync(new URL('../../native-port-manifest.json',import.meta.url)));
const D=x=>({d:doubleBits(x)});
// Keep the exact oracle batch together; splitting it resets Epsilon in createGeometryCaller.
test('browser geometry preserves static state within the identical oracle request batch',()=>{
  const requests=[
    {type:'netDxf.MathHelper',action:'set',static:true,member:'Epsilon',value:D(0.1)},
    {type:'netDxf.MathHelper',action:'call',static:true,member:'Sign',signature:'double',args:[D(0.05)]},
  ];
  const call=createGeometryCaller(manifest),result=call({requests});
  assert.equal(result[0].ok,true);assert.equal(result[1].ok,true);
  const split=call({requests:[requests[1]]});
  assert.notDeepEqual(result[1],split[0],'Regression case must expose a change when the oracle batching is discarded.');
});
