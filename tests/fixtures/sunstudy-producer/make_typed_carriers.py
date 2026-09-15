#!/usr/bin/env python3
"""Adapt pinned raw SUNSTUDY carriers for typed import, disclosing orphan STYLE metadata."""
import hashlib
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / 'tools'))
from verify_sunstudy_producer import records, encode_records, single

folder = Path(__file__).resolve().parent
out = folder / 'typed-carriers'
out.mkdir(exist_ok=True)
manifest = {'source': 'source-manifest.json raw carriers', 'operation': 'Remove orphan STYLE group 1071=0 outside an APPID envelope; set invalid PLOTSETTINGS scale 142/143 from 0 to 1 and restore reciprocal NOD owner; no SUNSTUDY changes', 'files': []}
for path in sorted((folder / 'carriers').glob('*.dxf')):
    source = path.read_bytes()
    rows = records(source)
    changed = []
    for row in rows:
        if row[0] != [0, 'STYLE']:
            continue
        appid = False
        for index in range(len(row)-1, -1, -1):
            code, value = row[index]
            if code == 1071 and value == 0 and not any(c == 1001 for c,v in row[:index]):
                changed.append({'recordType': 'STYLE', 'handle': single(row, 5), 'index': index, 'removed': [1071,0]})
                del row[index]
    if len(changed) != 3:
        raise ValueError('Unexpected STYLE adaptation count: ' + path.name)
    for row in rows:
        if row[0] != [0, 'PLOTSETTINGS']:
            continue
        for index, tag in enumerate(row):
            if tag[0] == 330:
                if tag[1] != '22': raise ValueError('Unexpected plot owner')
                changed.append({'recordType': 'PLOTSETTINGS', 'handle': single(row,5), 'index': index, 'before': list(tag), 'after': [330,'1F']})
                tag[1] = '1F'
            if tag[0] in (142,143):
                if tag[1] != 0.0: raise ValueError('Unexpected plot scale')
                changed.append({'recordType': 'PLOTSETTINGS', 'handle': single(row,5), 'index': index, 'before': list(tag), 'after': [tag[0],1.0]})
                tag[1] = 1.0
    if len(changed) != 6: raise ValueError('Unexpected adaptation count')
    data = encode_records(rows, source.startswith(b'AutoCAD Binary DXF'))
    (out / path.name).write_bytes(data)
    manifest['files'].append({'name': path.name, 'sourceSha256': hashlib.sha256(source).hexdigest(), 'sha256': hashlib.sha256(data).hexdigest(), 'bytes': len(data), 'changes': changed})
(folder / 'typed-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
