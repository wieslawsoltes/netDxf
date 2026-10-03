import test from 'node:test';
import assert from 'node:assert/strict';
import {Exp,Log} from '../../runtime/reference-math/exp-log.js';
import {DotNetMath} from '../../runtime/GeometryRuntime.js';
import {doubleBits,fromBits} from '../../tools/wire.mjs';
test('runtime exp/log use the pinned implementation and preserve exact anchor results',()=>{assert.equal(DotNetMath.Exp,Exp);assert.equal(DotNetMath.Log,Log);assert.equal(Exp(0),1);assert.equal(Exp(-0),1);assert.equal(Log(1),0);assert.equal(Log(0),-Infinity);assert.equal(Log(-0),-Infinity);assert.equal(Exp(-Infinity),0);assert.equal(Exp(Infinity),Infinity);assert.equal(Log(Infinity),Infinity);assert.equal(Exp(-745),Number.MIN_VALUE);assert.equal(Exp(-746),0);});
test('System.Math negative log domain and quieted payload NaNs survive warmed calls',()=>{for(let i=0;i<3000;i++){assert.equal(doubleBits(Log(-1-i)),'7FF8000000000000');const value=fromBits(i%2?'FFF0000000001234':'7FF0000000001234'),expected=i%2?'FFF8000000001234':'7FF8000000001234';assert.equal(doubleBits(Exp(value)),expected);assert.equal(doubleBits(Log(value)),expected);}});
test('exp/log direct comparison inputs have unique nonempty identities',async()=>{const {expLogCorpus}=await import('../../tools/exp-log-corpus.mjs');const c=expLogCorpus();assert.equal(c.length,13512);assert.equal(new Set(c.map(v=>v.id)).size,c.length);});
