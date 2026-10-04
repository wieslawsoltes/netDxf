#!/usr/bin/env python3
"""Independent DXF 2018 ATTRIB/ATTDEF checks against separate ezdxf fixtures."""
from pathlib import Path
import hashlib
import itertools
import json
import math
import sys
import ezdxf
from verify_raw_line_geometry import require, audit_signature
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
import io

def load_tags(path):
    data=path.read_bytes()
    # This checker exclusively verifies AC1032, whose text transport is UTF-8.
    tags=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'),newline=None))
    return [(t.code,cast_tag_value(t.code,t.value)) for t in tags]

ROOT = Path(__file__).resolve().parents[1]

def records(tags):
    result=[];current=[]
    for tag in tags:
        if tag[0]==0:
            if current and current[0][1] in ('ATTRIB','ATTDEF'):result.append(current)
            current=[]
        current.append(tag)
    return result

def payload(row):
    markers=[i for i,t in enumerate(row) if t==(101,'Embedded Object')]
    require(len(markers)==1,'Exactly one embedded marker is required')
    values={};order=[];chunks=[];ended=False
    for code,value in row[markers[0]+1:]:
        if code>=1000:break
        if code==999:continue
        if code in (1,3):
            require(not ended,'Content after terminal group 1')
            chunks.append(value);ended=code==1
        else:
            require(code not in values,'Duplicate embedded field')
            values[code]=value;order.append(code)
    require(ended,'Missing terminal embedded content')
    values[1]=''.join(chunks)
    orientation=tuple(c for c in order if c in (11,21,31,50))
    return values,orientation

def equal_payload(expected,actual):
    require(expected==actual,'Embedded fields/content/orientation order changed')

def inspect_document(doc,profile=None,attachment=None):
    roots=list(doc.modelspace().query('INSERT'))
    require(len(roots)==1,'Root INSERT inventory')
    root=roots[0];defs=list(doc.blocks[root.dxf.name].query('ATTDEF'))
    require(len(root.attribs)==1 and len(defs)==1,'Attribute inventory')
    for index,host in enumerate((defs[0],root.attribs[0])):
        require(host.has_embedded_mtext_entity,'Missing typed embedded MTEXT')
        m=host.virtual_mtext_entity()
        require(host.dxf.text_generation_flag==6,'Attribute type confused with fallback generation flags')
        if profile is not None:
            require(host.dxf.text==('fallback-definition' if index==0 else 'fallback-instance'),'Fallback content changed')
            require(tuple(host.dxf.insert)==(-1-index,-2.,-3.),'Fallback position changed')
            require(host.dxf.style=='FALLBACK' and host.dxf.height==1.25 and host.dxf.width==.75,'Fallback style/dimensions changed')
            require(m.dxf.style=='BODY' and tuple(m.dxf.insert)==(10.+index,20.,30.),'Embedded style/position changed')
            require(m.dxf.char_height==3.5 and m.dxf.width==12.25,'Embedded dimensions changed')
        else:
            require(host.dxf.text=='fallback','Authored fallback changed')
            require(m.dxf.attachment_point==attachment,'Authored attachment changed')
            require(m.text==('rich\\Pcontent 😀' if index==0 else 'edited\\P多行'),'Authored logical content changed')
            require(tuple(m.dxf.insert)==(10.,20.,30.) and m.dxf.char_height==3.,'Authored geometry changed')
        require(m.dxf.style in doc.styles,'Missing embedded style resource')
    require(not any(any(v.values()) for v in audit_signature(doc)),'Independent audit made repairs or found errors')

def inventory(directory):
    expected={f'attribute-mtext-independent-{p}-{b}-{s}.dxf' for p,b,s in itertools.product(range(6),(False,True),range(3))}
    expected|={f'attribute-mtext-authored-{a}-{b}-{s}.dxf' for a,b,s in itertools.product(range(1,10),(False,True),range(3))}
    actual={p.name for p in directory.glob('attribute-mtext-*.dxf')}
    require(actual==expected,f'Fixture inventory differs: missing={expected-actual}, extra={actual-expected}')
    return expected

def main(directory):
    names=inventory(directory);manifest=json.loads((ROOT/'tests/fixtures/attribute-mtext/manifest.json').read_text());source={}
    for row in manifest['files']:
        p=ROOT/'tests/fixtures/attribute-mtext'/row['file']
        require(hashlib.sha256(p.read_bytes()).hexdigest()==row['sha256'],'Independent source hash')
        source[(row['profile'],row['binary'])]=records(load_tags(p))
    controls=0
    for name in sorted(names):
        p=directory/name;parts=p.stem.split('-');doc=ezdxf.readfile(p)
        require(doc.dxfversion=='AC1032','Wrong output family')
        number=int(parts[3]);binary=parts[4]=='True';stage=int(parts[5])
        require(p.read_bytes().startswith(b'AutoCAD Binary DXF')==(not binary if parts[2]=='independent' and stage==1 else binary),'Wrong transport')
        if parts[2]=='independent':
            inspect_document(doc,profile=number)
            actual=records(load_tags(p));expected=source[(number,binary)]
            require(len(actual)==len(expected)==2,'Physical attribute record inventory')
            for left,right in zip(expected,actual):
                require(left[0]==right[0],'Physical attribute record order')
                wanted=payload(left);equal_payload(wanted,payload(right))
                # Deliberately change, remove or duplicate every embedded scalar.
                start=right.index((101,'Embedded Object'))
                for i in range(start+1,len(right)):
                    code,value=right[i]
                    if code>=1000 or code==999:continue
                    for mode in ('change','remove','duplicate'):
                        broken=list(right)
                        if mode=='change':broken[i]=(code,value+'_bad' if isinstance(value,str) else value+1)
                        elif mode=='remove':broken.pop(i)
                        else:broken.insert(i,broken[i])
                        # Removing a continuation or duplicating it must also alter content.
                        try:equal_payload(wanted,payload(broken))
                        except (ValueError,AssertionError):controls+=1
                        else:raise ValueError(f'Corruption escaped detection: {name}/{i}/{mode}')
        else:inspect_document(doc,attachment=number)
    print(f'PASS: {len(names)} actual C# drawings, 12 independent producer inputs, {controls} corruptions rejected, zero audit errors/repairs.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_attribute_mtext.py ARTIFACTS')
    main(Path(sys.argv[1]))
