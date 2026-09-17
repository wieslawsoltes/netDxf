import test from 'node:test';
import assert from 'node:assert/strict';
import { ParseInvariantFloat } from '../../runtime/InvariantFloat.js';
import { HatchPattern, Linetype } from '../../index.js';
import { FormatException } from '../../runtime/Errors.js';
import { doubleBits } from '../../tools/wire.mjs';

test('invariant numeric parsing admits only the CLR terminal-NUL form',()=>{
  for(const [text,expected] of [['1\0',1],['1\0\0',1],['1 \0',1],[' 1\0',1],['-0\0',-0],['1e309\0',Infinity],['1.23\t\0',1.23]])assert.ok(Object.is(ParseInvariantFloat(text),expected),JSON.stringify(text));
  for(const text of ['1\0 ','1\0\t','1\0\n','NaN\0','+NaN\0','Infinity\0','NaN \0','\0','\0 1','1\0x'])assert.throws(()=>ParseInvariantFloat(text),FormatException,JSON.stringify(text));
});
test('signed canonical parsed NaNs survive cold and warmed call paths',()=>{
  const variants=['NaN','nan','+NaN','-NaN',' \tNaN\r ','\u0085NaN\u0085'];
  for(let i=0;i<10000;i++)assert.equal(doubleBits(ParseInvariantFloat(variants[i%variants.length])),'FFF8000000000000');
});
test('PAT and LIN share exact numeric parsing without sharing header whitespace behavior',()=>{
  const pat=HatchPattern.LoadText(' *P,d\n0,1\0,-0\0,0,1','P');assert.equal(pat.LineDefinitions.get_Item(0).Origin.X,1);assert.ok(Object.is(pat.LineDefinitions.get_Item(0).Origin.Y,-0));
  assert.equal(Linetype.LoadText(' *L,d\nA,1\0','L'),null);assert.equal(Linetype.LoadText('*L,d\nA,1\0','L').Segments.get_Item(0).Length,1);
  const lin=Linetype.LoadText('*L,d\nA,.5,[ZIG,shapes.shx,Y=NaN],-.25','L');assert.equal(doubleBits(lin.Segments.get_Item(0).Offset.Y),'FFF8000000000000');
});
