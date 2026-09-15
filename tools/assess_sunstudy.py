#!/usr/bin/env python3
"""Inventory actual SUNSTUDY objects separately from CLASS and dictionary mentions.

This is an evidence scanner, not a SUNSTUDY decoder or producer fixture. It
supports text/binary DXF and compressed original fixtures, retaining failures.
"""
import argparse
from collections import Counter
import gzip
import hashlib
import io
import json
from pathlib import Path
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.tagwriter import BinaryTagWriter


def scan_data(data):
    binary = data.startswith(b'AutoCAD Binary DXF')
    tags = list(binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('latin1'), newline=None)))
    rows, row = [], []
    for tag in tags:
        if tag.code == 0:
            if row: rows.append(row)
            row = []
        row.append((tag.code, tag.value.hex() if isinstance(tag.value, bytes) else tag.value))
    if row: rows.append(row)
    section = profile = None
    objects, other, classes, dictionaries = [], [], [], []
    def first(row, code): return next((v for c,v in row if c == code), None)
    for row in rows:
        if row[0] == (0,'SECTION'):
            section = first(row,2)
            if section == 'HEADER': profile = next((row[i+1][1] for i,t in enumerate(row[:-1]) if t == (9,'$ACADVER')),None)
        elif row[0] == (0,'ENDSEC'): section = None
        if row[0] == (0,'SUNSTUDY'):
            (objects if section == 'OBJECTS' else other).append({'section':section,'handle':first(row,5),'packet':row})
        if row[0] == (0,'CLASS') and (1,'SUNSTUDY') in row:
            classes.append({'section':section,'packet':row})
        if row[0] == (0,'DICTIONARY'):
            for i,(code,value) in enumerate(row):
                if code == 3 and 'SUNSTUDY' in value.upper():
                    dictionaries.append({'section':section,'dictionaryHandle':first(row,5),'name':value,'followingTag':row[i+1] if i+1 < len(row) else None})
    return {'binary':binary,'profile':profile,'objects':objects,'otherSectionRecords':other,'classDeclarations':classes,'dictionaryMentions':dictionaries}


def controls():
    rows = [(0,'SECTION'),(2,'HEADER'),(9,'$ACADVER'),(1,'AC1032'),(0,'ENDSEC'),
            (0,'SECTION'),(2,'CLASSES'),(0,'CLASS'),(1,'SUNSTUDY'),(2,'AcDbSunStudy'),(0,'ENDSEC'),
            (0,'SECTION'),(2,'OBJECTS'),(0,'DICTIONARY'),(5,'A'),(3,'ACAD_SUNSTUDY'),(350,'B'),
            (0,'SUNSTUDY'),(5,'B'),(330,'A'),(100,'AcDbSunStudy'),(90,1),
            (0,'XRECORD'),(5,'C'),(100,'AcDbXrecord'),(1,'SUNSTUDY'),(0,'ENDSEC'),
            (0,'SECTION'),(2,'ENTITIES'),(0,'SUN'),(5,'D'),(100,'AcDbSun'),(90,1),(0,'ENDSEC'),(0,'EOF')]
    count = 0
    for include_object in (True,False):
        tags = list(rows)
        if not include_object:
            start=tags.index((0,'SUNSTUDY')); end=tags.index((0,'XRECORD')); del tags[start:end]
        data = [''.join(str(c)+newline+str(v)+newline for c,v in tags).encode('ascii') for newline in ('\n','\r\n')]
        stream=io.BytesIO(); writer=BinaryTagWriter(stream);writer.write_signature()
        for code,value in tags: writer.write_tag2(code,value)
        data.append(stream.getvalue())
        for raw in data:
            result=scan_data(raw)
            assert result['profile']=='AC1032' and len(result['objects'])==int(include_object)
            assert len(result['classDeclarations'])==len(result['dictionaryMentions'])==1
            assert not result['otherSectionRecords']
            count+=1
    return count


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory',type=Path);parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--libredwg-tree',type=Path,help='Verify flattened pinned LibreDWG files against this complete Git tree inventory.')
    args=parser.parse_args()
    paths=sorted(p for p in args.directory.rglob('*') if p.is_file() and (p.name.endswith('.dxf') or p.name.endswith('.dxf.gz')))
    if not paths: parser.error('No DXF candidates')
    expected = None
    if args.libredwg_tree:
        expected={t['path'].replace('/','__'):t['sha'] for t in json.loads(args.libredwg_tree.read_text())['tree'] if t['path'].endswith('.dxf')}
        if set(expected) != {p.name for p in paths}: raise ValueError('Pinned LibreDWG file inventory differs')
    files, failures = [], []
    for path in paths:
        original=path.read_bytes()
        blob=hashlib.sha1(b'blob '+str(len(original)).encode()+b'\0'+original).hexdigest()
        if expected is not None and blob != expected[path.name]: raise ValueError('Pinned Git blob mismatch: '+path.name)
        data=gzip.decompress(original) if path.name.endswith('.gz') else original
        info={'file':str(path.relative_to(args.directory)),'gitBlobSha':blob,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)}
        try: files.append({**info,**scan_data(data)})
        except Exception as error: failures.append({**info,'error':type(error).__name__+': '+str(error)})
    summary={'candidates':len(paths),'parsed':len(files),'failures':len(failures),'pinnedGitBlobsVerified':len(paths) if expected is not None else None,'profiles':dict(Counter(f['profile'] for f in files))}
    for field in ['objects','otherSectionRecords','classDeclarations','dictionaryMentions']: summary[field]=sum(len(f[field]) for f in files)
    report={'summary':summary,'scannerControls':controls(),'files':files,'failures':failures}
    args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(summary,indent=2));print('PASS six synthetic scanner controls; these are not producer evidence')
    if failures: raise SystemExit(1)


if __name__ == '__main__': main()
