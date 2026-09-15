#!/usr/bin/env python3
"""Require all fourteen private XRECORD outputs and reject actual field corruption."""
import argparse
import copy
import gzip
import hashlib
import io
import json
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader,tag_compiler
from verify_fourth_mixed_modules import check,load,metadata,dictionary_edges,xdata

ROOT=Path(__file__).resolve().parents[1]
YEARS=(2000,2004,2007,2010,2013,2018)


def native():
    name='sample_AC1018_ascii.dxf'
    entry=next(r for r in json.loads((ROOT/'tools/table_oracle/fixtures.json').read_text())['files'] if r['file']==name)
    data=gzip.decompress((ROOT/'tests/fixtures/table-oracle'/(name+'.gz')).read_bytes())
    check(hashlib.sha256(data).hexdigest()==entry['sha256'],'Native145A source SHA256 differs')
    rows=[];current=[]
    for t in tag_compiler(ascii_tags_loader(io.StringIO(data.decode('cp1252'),newline=None))):
        if t.code==0 and current:rows.append(current);current=[]
        current.append((t.code,t.value))
    rows.append(current)
    return next(r for r in rows if (5,'145A') in r and r[0]==(0,'XRECORD'))


def target(rows,name):
    matches=[h for record in rows.values() if record[0]==(0,'DICTIONARY') for n,c,h in dictionary_edges(record) if n==name]
    check(len(matches)==1,'Private fixture alias differs: '+name);return matches[0]


def inspect(rows,source,is_native):
    if is_native:
        check(rows['145A']==source,'Complete native145A packet differs');return '145A'
    owner=target(rows,'PRIVATE_OWNER');handle=target(rows,'PRIVATE_RECORD');reactor=target(rows,'PRIVATE_REACTOR')
    record=rows[handle];check(record[0]==(0,'XRECORD') and (5,handle) in record,'Private physical identity/type differs');common_owner,reactors,extension=metadata(record)
    check(common_owner==owner and reactors==[reactor] and extension in rows,'Private common metadata differs')
    check(rows[reactor][0]==(0,'ACDBPLACEHOLDER'),'Private reactor identity/type differs')
    check(metadata(rows[extension])[0]==handle,'Private extension owner differs')
    note=next(h for n,c,h in dictionary_edges(rows[extension]) if n=='NOTE')
    check(rows[note][0]==(0,'DICTIONARYVAR') and (1,'retained extension') in rows[note] and metadata(rows[note])[0]==extension,'Private extension contents differ')
    start=record.index((100,'AcDbXrecord'))
    check(record[start:]==[(100,'AcDbXrecord'),(280,1),(1070,0),(1070,1),(1070,1),(1001,'PRIVATE_XDATA'),(1000,'actual XData'),(1005,reactor)],'Exact private body and real XData differs')
    check(any(r[0]==(0,'APPID') and (2,'PRIVATE_XDATA') in r for r in rows.values()),'Real XData registry absent')
    return handle


def mutate(rows,handle,variant):
    r=rows[handle]
    def replace(code,value):
        i=next(i for i,t in enumerate(r) if t[0]==code);r[i]=(code,value)
    if variant==0:replace(1070,9)
    if variant==1:replace(330,'0')
    if variant==2:replace(280,5)
    if variant==3:r.append((1001,'UNEXPECTED'))
    if variant==4:rows.pop(handle)
    if variant==5:replace(5,'0')
    if variant==6:r.pop(next(i for i,t in enumerate(r) if t[0]==1070))


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);a=p.parse_args()
    names={f'private-xrecord-AutoCad{year}-{binary}.dxf' for year in YEARS for binary in (False,True)}|{f'private-xrecord-native-{binary}.dxf' for binary in (False,True)}
    check({p.name for p in a.directory.glob('private-xrecord-*.dxf')}==names,'All fourteen private XRECORD drawings are mandatory')
    source=native();controls=0
    for name in sorted(names):
        pieces=name[:-4].split('-');is_native=pieces[-2]=='native';year=2004 if is_native else int(pieces[-2].removeprefix('AutoCad'));binary=pieces[-1]=='True'
        doc,rows,_=load(a.directory/name,year,binary);audit=doc.audit();check(not audit.errors and not audit.fixes,'Private output audit errors/repairs')
        handle=inspect(rows,source,is_native)
        for variant in range(7):
            bad=copy.deepcopy(rows);mutate(bad,handle,variant)
            try:inspect(bad,source,is_native)
            except (ValueError,KeyError,StopIteration):controls+=1
            else:raise ValueError('Private XRECORD corruption accepted: '+str(variant))
        print('PASS '+name)
    check(controls==98,'Private corruption inventory differs');print('PASS fourteen private XRECORD outputs, exact native145A bytes-as-tags, common metadata, and98 corruption controls')


if __name__=='__main__':main()
