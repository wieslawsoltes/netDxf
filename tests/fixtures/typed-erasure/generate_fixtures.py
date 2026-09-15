#!/usr/bin/env python3
"""Native ezdxf ownership graphs, with explicitly patched per-entry350/360 aliases.

The patch changes only a dictionary edge's pointer strength. ezdxf's dictionary
writer has one uniform entry code, while DXF permits each edge to choose350/360.
These fixtures qualify stored ownership graphs, not native AutoCAD erasure.
"""
import hashlib
import io
import json
from pathlib import Path

import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, tag_compiler

ROOT = Path(__file__).resolve().parent
PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021',
            2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


def json_value(value):
    if isinstance(value, bytes):
        return {'hex': value.hex()}
    if hasattr(value, 'xyz'):
        return list(value)
    return value


def records(text):
    result, record, handle = {}, [], None
    for tag in tag_compiler(ascii_tags_loader(io.StringIO(text, newline=None))):
        if tag.code == 0:
            if handle is not None:
                result[handle] = record
            record, handle = [], None
        if tag.code == 5 and handle is None:
            handle = tag.value
        record.append([tag.code, json_value(tag.value)])
    return result


def metadata(obj, owner, role, keep=None):
    obj.set_reactors([owner.dxf.handle])
    payload = [(1000, role), (1070, 23)]
    if keep is not None:
        payload.append((1005, keep.dxf.handle))
    obj.set_xdata('QA_ERASURE', payload)


def generate(year):
    doc = ezdxf.new(f'R{year}')
    doc.appids.new('QA_ERASURE')
    root = doc.rootdict
    keep = root.add_xrecord('QA_KEEP')
    keep.reset([(1, 'Survivor Żółć 測試'), (40, -7.125), (90, 2147483646),
                (310, b'\x00\x01\xff\x10\x00'), (290, 1)])
    root.add('QA_KEEP_ALIAS', keep)
    metadata(keep, root, 'survivor')
    line = doc.modelspace().add_line((17.25, -4.5, 2), (18.5, 9.25, -3))
    line.set_xdata('QA_ERASURE', [(1000, 'following geometry'), (1005, keep.dxf.handle)])
    erase = root.add_new_dict('QA_ERASE', hard_owned=False)
    metadata(erase, root, 'erase dictionary', keep)
    child = erase.add_xrecord('PRIMARY')
    child.reset([(1, 'erased primary'), (40, 1.25), (310, b'\x00\xff')])
    erase.add('ALIAS', child)
    metadata(child, erase, 'aliased child', keep)
    empty = erase.add_xrecord('EMPTY')
    metadata(empty, erase, 'empty child')
    extension = child.new_extension_dict().dictionary
    metadata(extension, child, 'owned extension')
    grandchild = extension.add_xrecord('GRANDCHILD')
    grandchild.reset([(1, 'owned descendant'), (40, 3.25)])
    metadata(grandchild, extension, 'extension descendant')
    stream = io.StringIO()
    doc.write(stream)
    text = stream.getvalue()
    lines = text.splitlines()
    active, record_handle, pending_key = False, None, None
    edits = []
    for index in range(0, len(lines), 2):
        code, value = int(lines[index]), lines[index + 1]
        if code == 0:
            active, record_handle, pending_key = value == 'DICTIONARY', None, None
        elif active and code == 5:
            record_handle = value
        elif active and code == 3:
            pending_key = value
        elif active and code in (350, 360):
            if record_handle == erase.dxf.handle and pending_key in ('PRIMARY', 'EMPTY'):
                lines[index] = '360'
                edits.append({'dictionary': record_handle, 'key': pending_key,
                              'from_code': code, 'to_code': 360, 'target': value})
            pending_key = None
    assert len(edits) == 2
    text = '\n'.join(lines) + '\n'
    path = ROOT / f'independent-typed-erasure-R{year}.dxf'
    # Unicode uses ASCII DXF escapes for old profiles and UTF-8 for modern ones.
    path.write_bytes(text.encode('utf-8' if year >= 2007 else 'cp1252', 'dxfreplace'))
    after = ezdxf.readfile(path)
    audit = after.audit()
    assert not audit.errors and not audit.fixes, (audit.errors, audit.fixes)
    wire = records(path.read_bytes().decode('utf-8' if year >= 2007 else 'cp1252'))
    erased = {'dictionary': erase.dxf.handle, 'child': child.dxf.handle,
              'empty_child': empty.dxf.handle, 'extension_dictionary': extension.dxf.handle,
              'grandchild': grandchild.dxf.handle}
    kept = {'root': root.dxf.handle, 'record': keep.dxf.handle, 'line': line.dxf.handle,
            'appid': doc.appids.get('QA_ERASURE').dxf.handle}
    return {'file': path.name, 'year': year, 'version': PROFILES[year],
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'erased_handles': erased, 'kept_handles': kept, 'patches': edits,
            'deleted_max': max(int(handle, 16) for handle in erased.values()),
            'handle_seed': after.header['$HANDSEED'],
            'wire_records': {handle: wire[handle] for handle in sorted(
                set(erased.values()) | set(kept.values()), key=lambda value: int(value, 16))}}


def main():
    ezdxf.options.write_fixed_meta_data_for_testing = True
    manifest = {'producer': f'ezdxf {ezdxf.__version__}',
                'qualification': 'Stored erasure/ownership graph; no native application deletion claim.',
                'post_export_patch': 'Two QA_ERASE dictionary entries use360; ALIAS remains350. No payload or identity patch.',
                'fixtures': [generate(year) for year in PROFILES]}
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print('Generated six independent erasure graphs with zero ezdxf audit errors/repairs.')


if __name__ == '__main__':
    main()
