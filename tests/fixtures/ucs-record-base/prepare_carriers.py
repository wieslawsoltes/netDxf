#!/usr/bin/env python3
"""Remove invalid producer DIMSTYLE/STYLE defaults without rewriting other source bytes."""
from pathlib import Path
import hashlib, io, json, struct
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import BINARY_DATA, INT16, DOUBLE, INT32, INT64, BYTES

ROOT = Path(__file__).resolve().parent

def packets(data):
    if data.startswith(b'AutoCAD Binary DXF'):
        tags = list(binary_tags_loader(data)); index = 22
        for tag in tags:
            start = index; code = struct.unpack_from('<H', data, index)[0]; index += 2
            assert code == tag.code
            if code in BINARY_DATA: index += 1 + data[index]
            elif code in INT16: index += 2
            elif code in DOUBLE or code in INT64: index += 8
            elif code in INT32: index += 4
            elif code in BYTES: index += 1
            else: index = data.index(b'\0', index) + 1
            yield tag.code, tag.value, data[start:index]
        assert index == len(data)
    else:
        lines = data.splitlines(keepends=True)
        for i in range(0, len(lines), 2):
            yield int(lines[i]), lines[i+1].strip().decode('ascii'), lines[i] + lines[i+1]

def prepare(data):
    output = [data[:22]] if data.startswith(b'AutoCAD Binary DXF') else []
    kind = ''; removed = []; orphan_style = 0; xdata = False
    for code, value, packet in packets(data):
        if code == 0: kind = value; xdata = False
        if code == 1001: xdata = True
        if kind == 'DIMSTYLE' and 340 <= code <= 344 and value == '':
            removed.append(code); continue
        if kind == 'STYLE' and code == 1071 and not xdata and int(value) == 0:
            orphan_style += 1; continue
        output.append(packet)
    assert sorted(removed) == [340,340,341,341,342,342,343,343,344,344], removed
    assert orphan_style in (0, 2), orphan_style
    return b''.join(output), removed, orphan_style

def main():
    manifest = json.loads((ROOT/'manifest.json').read_text()); carriers=[]
    directory = ROOT/'carriers'; directory.mkdir(exist_ok=True)
    for source in manifest['files']:
        data = (ROOT/source['file']).read_bytes()
        assert hashlib.sha256(data).hexdigest() == source['sha256']
        result, removed, orphan_style = prepare(data); name = source['file'].replace('ixmilia-', 'carrier-')
        (directory/name).write_bytes(result)
        carriers.append({'file': name, 'sha256': hashlib.sha256(result).hexdigest(), 'source': source['file'], 'source_sha256':source['sha256'], 'removed_empty_dimstyle_codes':removed, 'removed_orphan_style1071':orphan_style})
    (directory/'manifest.json').write_text(json.dumps({'adaptation':'Delete ten empty DIMSTYLE pointers340–344 and, in R2010+, two orphan STYLE1071=0 values without an application marker. Every remaining source byte, including complete UCS records and LINE, is unchanged.', 'files':carriers},indent=2)+'\n')
    print('Prepared',len(carriers),'carriers with exact source UCS bytes.')
if __name__ == '__main__': main()
