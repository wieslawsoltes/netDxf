#!/usr/bin/env python3
"""Verify retained populated layer-state packets and identities, with no normalization."""
import io
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import audit_signature
from verify_layer_state_identity import require, reject, one, canonical, records

VERSIONS = {2000:'AC1015',2004:'AC1018',2007:'AC1021',2010:'AC1024',2013:'AC1027',2018:'AC1032'}
STAGES = ('source','g0','g1','g2')


def load(path):
    data=path.read_bytes()
    source=binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(io.StringIO(data.decode('utf-8')))
    result=[]
    for tag in source:
        value=cast_tag_value(tag.code,tag.value)
        if isinstance(value,str): value=decode_dxf_unicode(value)
        result.append((tag.code,value))
    return result


def select(rows,count):
    indexed={}
    for row in rows:
        if row[0] in ((0,'SECTION'),(0,'ENDSEC'),(0,'EOF')):continue
        handles=[v for c,v in row if c in (5,105)]
        if handles:
            require(len(handles)==1,'Duplicate record identity declaration')
            key=canonical(handles[0]); require(key not in indexed,'Duplicate global identity'); indexed[key]=row
    tables=[r for r in rows if r[:2]==[(0,'TABLE'),(2,'LAYER')]]
    require(len(tables)==1,'LAYER table inventory')
    table=tables[0]; outer_id=one(table,360); require(outer_id in indexed,'Missing LAYER extension')
    outer=indexed[outer_id]; inner_id=one(outer,360); require(inner_id in indexed,'Missing state dictionary')
    inner=indexed[inner_id]
    require(outer==[(0,'DICTIONARY'),(5,outer_id),(330,one(table,5)),(100,'AcDbDictionary'),
                    (280,1),(281,1),(3,'ACAD_LAYERSTATES'),(360,inner_id)],'Outer identity/owner/flags')
    require(inner[:6]==[(0,'DICTIONARY'),(5,inner_id),(330,outer_id),(100,'AcDbDictionary'),(280,1),(281,1)],'Inner identity/owner/flags')
    require(len(inner)==6+count*2,'State dictionary size')
    layers={one(r,2):one(r,5) for r in rows if r[0]==(0,'LAYER')}
    types={one(r,2):one(r,5) for r in rows if r[0]==(0,'LTYPE')}
    result=[tuple(table),tuple(outer),tuple(inner)]
    state_ids=[]
    for i in range(count):
        name,link=inner[6+2*i:8+2*i]
        require(name==(3,f'STATE_{i}') and link[0]==350,'State membership name/order/link type')
        handle=canonical(link[1]);require(handle in indexed,'Missing state record');state_ids.append(handle)
        row=indexed[handle]
        prefix=[(0,'XRECORD'),(5,handle),(102,'{ACAD_REACTORS'),(330,inner_id),(102,'}'),(330,inner_id),
                (100,'AcDbXrecord'),(280,1),(91,2047),(301,f'Résumé {i}'),(290,i%2),(302,'Detailed' if i%2==0 else 'Hidden')]
        # Independently specified layer snapshot: ordered flags, colors, lineweights,
        # linetype references and stored transparency, not values captured from output.
        props=[(330,layers['0']),(90,i+4),(62,7),(370,-3),(331,types['Continuous']),(440,0),
               (330,layers['Detailed']),(90,12),(62,147),(370,35),(331,types['Continuous']),
               (440,33554592),(92,-1037939583),
               (330,layers['Hidden']),(90,3),(62,2),(370,-3),(331,types['Continuous']),(440,0)]
        require(row==prefix+props,'State record common metadata, identity or payload differs')
        result.append(tuple(row))
    require(len(set(state_ids))==count,'Shared state record identity')
    require(sum(r[0]==(0,'XRECORD') for r in rows)==count,'Extra state XRECORD')
    lines=[r for r in rows if r[0]==(0,'LINE')];require(len(lines)==1,'LINE inventory')
    for code,value in ((10,1.),(20,2.),(30,3.),(11,4.),(21,5.),(31,6.)):
        require(struct.pack('>d',one(lines[0],code))==struct.pack('>d',value),'Unrelated coordinate bits changed')
    result.append(tuple(lines[0]))
    result.extend(tuple(r) for r in rows if r[0] in ((0,'LAYER'),(0,'LTYPE')))
    return tuple(result)


def compare(a,b): require(a==b,'Selected identifiers or unedited packets changed across generation')


def inspect(path,version,binary,count):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF')==binary,'Transport mismatch')
    tags=load(path);at=tags.index((9,'$ACADVER'));require(tags[at+1]==(1,VERSIONS[version]),'Version mismatch')
    rows=records(tags);chosen=select(rows,count);controls=0
    for selected in chosen[1:3+count]:
        index=next(i for i,r in enumerate(rows) if tuple(r)==selected)
        for pos in range(1,len(selected)):
            for mode in ('change','remove','duplicate'):
                row=list(selected);code,value=row[pos]
                if mode=='change':row[pos]=(code,value+'_changed' if isinstance(value,str) else value^1)
                elif mode=='remove':row.pop(pos)
                else:row.insert(pos,row[pos])
                changed=list(rows);changed[index]=row
                controls+=reject(lambda:select(changed,count))
    doc=ezdxf.readfile(path)
    for row in chosen[1:3+count]:
        entity=doc.entitydb[one(row,5)];require(entity.dxftype()==row[0][1],'Independent object type')
    child=doc.entitydb[one(chosen[2],5)]
    require(list(child.keys())==[f'STATE_{i}' for i in range(count)],'Independent dictionary entries')
    for i in range(count):
        record=child[f'STATE_{i}'];require(record.dxf.handle==one(chosen[3+i],5) and record.dxf.owner==child.dxf.handle,'Independent state ownership')
        require(decode_dxf_unicode(record.tags[1].value)==f'Résumé {i}','Independent state description')
    require(not any(any(v.values()) for v in audit_signature(doc)),'Independent audit errors/repairs')
    return chosen,controls


def main(directory):
    names={f'layer-state-populated-AutoCad{v}-{b}-{count}-{stage}.dxf' for v,b,count,stage in itertools.product(VERSIONS,(False,True),(1,3),STAGES)}
    def inventory(actual):require(actual==names,'Populated-state drawing inventory')
    inventory({p.name for p in directory.glob('layer-state-populated-*.dxf')})
    controls=reject(lambda:inventory(names- {next(iter(names))}))+reject(lambda:inventory(names|{'extra.dxf'}))
    for v,b,count in itertools.product(VERSIONS,(False,True),(1,3)):
        before=None
        for stage in STAGES:
            path=directory/f'layer-state-populated-AutoCad{v}-{b}-{count}-{stage}.dxf'
            chosen,n=inspect(path,v,not b if stage in ('g0','g2') else b,count);controls+=n
            if before is None:before=chosen
            else:
                compare(before,chosen)
                # Coherent identity renaming remains invalid across saves even when
                # each single-file graph would be valid.
                target=one(chosen[3],5)
                renamed=tuple(tuple((c,'FFFFFFFF' if value==target else value) for c,value in row) for row in chosen)
                controls+=reject(lambda:compare(before,renamed))
    print(f'PASS: {len(names)} drawings / 192 layer-state XRECORDs; {controls} corruptions/inventory changes rejected; exact identities and packets, zero independent audit errors/repairs. No native AutoCAD claim.')

if __name__=='__main__':
    require(len(sys.argv)==2,'Usage: verify_populated_layer_states.py ARTIFACTS');main(Path(sys.argv[1]))
