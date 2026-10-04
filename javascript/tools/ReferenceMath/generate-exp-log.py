#!/usr/bin/env python3
# SPDX-License-Identifier: LGPL-2.1-or-later
# Reproduce the table translation from bundled, hash-pinned preferred C sources.
import pathlib, hashlib, json, re, sys
ROOT=pathlib.Path(__file__).resolve().parents[2]
SOURCE=ROOT/'third_party/glibc-math'
MANIFEST=ROOT/'tools/ReferenceMath/exp-log-manifest.json'
manifest=json.loads(MANIFEST.read_text())
for name,expected in manifest['files'].items():
    if hashlib.sha256((SOURCE/name).read_bytes()).hexdigest()!=expected:raise ValueError('Unpinned exp/log source: '+name)
def clean(text):
    text=re.sub(r'/\*.*?\*/','',text,flags=re.S)
    text=re.sub(r'//[^\n]*','',text)
    return re.sub(r'^\s*#.*$','',text,flags=re.M)
def section(text,name):
    start=text.index('{',text.index('.'+name+' ='));depth=1;end=start+1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start+1:end-1]
def vals(text):return [float.fromhex(v) for v in re.findall(r'-?0x[0-9a-f]+(?:\.[0-9a-f]*)?p[+-]?\d+',text)]
def number(text,name):
    literal=re.search(r'\.'+name+r' = (-?0x[0-9a-f.]+p[+-]?\d+)',text).group(1)
    return float.fromhex(literal)
exp=clean((SOURCE/'sysdeps/ieee754/dbl-64/e_exp_data.c').read_text())
log=clean((SOURCE/'sysdeps/ieee754/dbl-64/e_log_data.c').read_text())
if '#define EXP_TABLE_BITS 7' not in (SOURCE/'sysdeps/ieee754/dbl-64/math_config.h').read_text():raise ValueError('Wrong exp profile')
et=re.findall(r'0x[0-9a-f]+',section(exp,'tab'));lt=vals(section(log,'tab'));lp=vals(section(log,'poly'));lb=vals(section(log,'poly1'));ep=vals(section(exp,'poly'))
assert (len(et),len(lt),len(lp),len(lb),len(ep))==(256,256,5,11,4)
header='// SPDX-License-Identifier: LGPL-2.1-or-later\n// Adapted from glibc 2.35. Copyright (C) 2018-2022 Free Software Foundation, Inc.\n// Generated from bundled preferred sources; see exp-log-source.json and LICENSE.LGPL-2.1.\n'
data=header+'export const EXP = Object.freeze({'+','.join(n+':'+repr(number(exp,s)*(128 if n=='InvLn2N' else 1)) for n,s in [('InvLn2N','invln2N'),('NegLn2hiN','negln2hiN'),('NegLn2loN','negln2loN'),('Shift','shift')])+',poly:Object.freeze('+json.dumps(ep)+'),tab:Object.freeze(['+','.join(v+'n' for v in et)+'])});\n'
data+='export const LOG = Object.freeze({Ln2hi:'+repr(number(log,'ln2hi'))+',Ln2lo:'+repr(number(log,'ln2lo'))+',poly:Object.freeze('+json.dumps(lp)+'),poly1:Object.freeze('+json.dumps(lb)+'),tab:Object.freeze('+json.dumps(lt)+')});\n'
outputs={'exp-log-data.js':data,'exp-log.js':(ROOT/'tools/ReferenceMath/exp-log.template.js').read_text(),'exp-log-source.json':json.dumps(manifest,indent=2)+'\n'}
for name,content in outputs.items():
    target=ROOT/'runtime/reference-math'/name
    if '--check' in sys.argv:
        if not target.exists() or target.read_text()!=content:raise ValueError('Generated exp/log drift: '+name)
    else:target.write_text(content)
print('Verified exp/log preferred sources and '+str(len(outputs))+' generated outputs.')
