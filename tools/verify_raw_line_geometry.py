#!/usr/bin/env python3
"""Independently verify nine-profile raw LINE edits against complete source tags."""
from __future__ import annotations
import collections
import io
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value

PROFILES = {'AutoCad12':'AC1009','AutoCad13':'AC1012','AutoCad14':'AC1014',
 'AutoCad2000':'AC1015','AutoCad2004':'AC1018','AutoCad2007':'AC1021',
 'AutoCad2010':'AC1024','AutoCad2013':'AC1027','AutoCad2018':'AC1032'}
NATIVE = ('ASCII_R12.dxf','bin_dxf_r12.dxf','bin_dxf_r13.dxf','bin_dxf_r14.dxf')
CODES=(10,20,30,11,21,31)
EXPECTED=(-8.5,16.25,-32.,64.,-128.5,256.25)

def require(ok, message):
    if not ok: raise ValueError(message)

def load_tags(path):
    data=path.read_bytes()
    if data.startswith(b'AutoCAD Binary DXF'):
        tags=binary_tags_loader(data)
    else:
        tags=ascii_tags_loader(io.StringIO(data.decode('cp1252'),newline=None))
    return [(tag.code,cast_tag_value(tag.code,tag.value)) for tag in tags]

def key(tag):
    c,v=tag
    if isinstance(v,float): v=struct.pack('>d',v)
    return c,v

def line_range(tags):
    start=next(i for i,t in enumerate(tags) if t==(0,'LINE'))
    end=next(i for i in range(start+1,len(tags)) if tags[i][0]==0)
    return start,end

def check_pair(before, after):
    start,end=line_range(before); a,b=line_range(after)
    require(a==start,'LINE record moved')
    require(list(map(key,before[:start]))==list(map(key,after[:a])),'Prefix changed')
    require(list(map(key,before[end:]))==list(map(key,after[b:])),'Suffix changed')
    source=before[start:end]; result=after[a:b]
    require([key(t) for t in source if t[0] not in CODES]==[key(t) for t in result if t[0] not in CODES],
            'Non-coordinate tag value, order or presence changed')
    for code,value in zip(CODES,EXPECTED):
        actual=[v for c,v in result if c==code]
        require(len(actual)==1 and struct.pack('>d',float(actual[0]))==struct.pack('>d',value),
                f'Incorrect, missing or duplicated LINE component {code}')
    old_codes=[c for c,v in source]
    new_codes=[c for c,v in result]
    extras=[c for c in CODES if c not in old_codes]
    require(all(c in (30,31) for c in extras),'Missing source X/Y')
    require([c for c in new_codes if c not in extras]==old_codes,'Existing tag order changed')
    for c in extras:
        require(new_codes[new_codes.index(c)-1]==c-10,'New Z is not next to its Y component')

def reject(action):
    try: action()
    except ValueError: return 1
    raise AssertionError('Actual corruption escaped the positive validator')

def audit_signature(doc):
    audit=doc.audit()
    return (collections.Counter(e.code for e in audit.errors),collections.Counter(e.code for e in audit.fixes))

def main(directory):
    stems=[]
    for version,binary,block,variant,output in itertools.product(PROFILES,(False,True),(False,True),range(4),(False,True)):
        stems.append((f'raw-line-{version}-{binary}-{block}-{variant}-{output}',PROFILES[version],output))
    for name,output in itertools.product(NATIVE,(False,True)):
        profile='AC1009' if 'R12' in name or 'r12' in name else 'AC1012' if 'r13' in name else 'AC1014'
        stems.append((f'raw-line-native-{name}-{output}',profile,output))
    expected={stem+'-'+side+'.dxf' for stem,_,_ in stems for side in ('before','after')}
    def inventory(actual): require(actual==expected,'Missing or extra raw LINE fixtures')
    inventory({p.name for p in directory.glob('raw-line-*.dxf')})
    reject(lambda:inventory(expected-{next(iter(expected))}))
    reject(lambda:inventory(expected|{'raw-line-extra.dxf'}))
    corruptions=0; inherited_errors=0; inherited_fixes=0; pairs=0
    for stem,version,binary in stems:
        paths=[directory/(stem+'-'+side+'.dxf') for side in ('before','after')]
        before,after=map(load_tags,paths)
        for path,tags in zip(paths,(before,after)):
            require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport changed')
            at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,version),'Declared version changed')
            if binary:
                require((path.read_bytes()[23]!=0)==(version=='AC1009'),'Wrong legacy binary framing')
        check_pair(before,after)
        start,end=line_range(after)
        for c in CODES:
            at=next(i for i in range(start,end) if after[i][0]==c)
            for kind in ('changed','omitted','repeated'):
                bad=list(after)
                if kind=='changed': bad[at]=(c,float(after[at][1])+.5)
                elif kind=='omitted': del bad[at]
                else: bad.insert(at,bad[at])
                corruptions+=reject(lambda:check_pair(before,bad))
        # Changes outside the selected coordinate slots must be rejected too.
        at=next(i for i in range(start,end) if after[i][0]==8)
        bad=list(after);bad[at]=(8,'UNEXPECTED')
        corruptions+=reject(lambda:check_pair(before,bad))
        docs=[ezdxf.readfile(path) for path in paths]
        a,b=map(audit_signature,docs)
        require(a==b,'Editing added or changed independent graph diagnostics')
        inherited_errors+=sum(a[0].values());inherited_fixes+=sum(a[1].values())
        # R13/R14 are upgraded in-memory by this reader, so use physical $ACADVER
        # above to verify the retained dialect, not doc.dxfversion after loading.
        target_handle=next((str(v) for c,v in after[start:end] if c==5),None)
        if target_handle:
            line=docs[1].entitydb[target_handle]
            require(line.dxftype()=='LINE','Edited identity no longer resolves to LINE')
            actual=tuple(line.dxf.start)+tuple(line.dxf.end)
            require(actual==EXPECTED,'Independent typed LINE coordinates differ')
        pairs+=1
    print(f'PASS: {pairs} source/edit pairs / {pairs*2} drawings across 9 declared raw profiles; '
          f'{corruptions} actual-tag corruptions and 2 inventory controls rejected. '
          f'No added graph diagnostics; inherited source errors={inherited_errors}, repairs={inherited_fixes}. '
          'R13/R14 importer upgrades are not native AutoCAD qualification.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_raw_line_geometry.py ARTIFACTS')
    main(Path(sys.argv[1]))
