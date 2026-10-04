import test from 'node:test';
import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {javascriptRoot} from '../../tools/dotnet.mjs';

test('bounded browser JSON transfer preserves complete Unicode payloads and rejects partial transfers',()=>{
  const result=spawnSync(process.env.PYTHON||'python',['-c',String.raw`
import json, sys
sys.path.insert(0,'tools')
from browser_json_transport import transfer_json_payloads
class Page:
    def __init__(self, fail=False): self.data={}; self.sizes=[]; self.deleted=False; self.fail=fail
    def evaluate(self, expression, args=None):
        if 'Object.create(null)' in expression: self.data={}
        elif expression.startswith('delete '): self.deleted=True; self.data={}
        elif 'chunks.push' in expression:
            if self.fail: raise RuntimeError('transport failed')
            name, text=args
            assert text.isascii()
            self.sizes.append(len(text)); self.data[name]['chunks'].append(text)
        else:
            name, length=args; self.data[name]={'length':length,'chunks':[]}
payloads={'sources':{'one':'Zażółć 東京 😀\ud800\udc00\ud800'},'corpus':{'rows':['abc'*100000,{'value':-0.0,'null':None,'flag':False}]},'__proto__':{'safe':True}}
for size in [1,17,262144]:
    page=Page(); transfer_json_payloads(page,payloads,size)
    for name, entry in page.data.items():
        text=''.join(entry['chunks'])
        assert len(text)==entry['length']
        assert json.loads(text)==json.loads(json.dumps(payloads[name],ensure_ascii=True))
    assert max(page.sizes)<=size
    assert not page.deleted
for size in [0,-1,1048577,True,1.5,None]:
    try: transfer_json_payloads(Page(),payloads,size)
    except ValueError: pass
    else: raise AssertionError('invalid chunk size admitted')
page=Page(True)
try: transfer_json_payloads(page,payloads)
except RuntimeError as e: assert str(e)=='transport failed'
else: raise AssertionError('failed transfer passed')
assert page.deleted and not page.data
page=Page()
try: transfer_json_payloads(page,{1:'invalid'})
except TypeError: pass
else: raise AssertionError('invalid name admitted')
assert page.deleted
print('bounded JSON transport tests passed')
`],{cwd:javascriptRoot,encoding:'utf8'});
  assert.equal(result.error,undefined);
  assert.equal(result.status,0,result.stdout+'\n'+result.stderr);
  assert.match(result.stdout,/bounded JSON transport tests passed/);
});
