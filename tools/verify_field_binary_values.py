#!/usr/bin/env python3
"""Independently verify binary FIELD cache values and complete unchanged graphs.

The binary fixtures are explicit Buffer constructions, not additional native
producer discoveries. Expected bytes and chunk boundaries are calculated here.
"""
import argparse
import copy
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_field_results import body, cache_sections, changed_field, compare, exact, FIELDS
from verify_legacy_utilities import PROFILES
from verify_stored_fields import source_records
from verify_mleader_inputs import check

SIZES = (0, 1, 126, 127, 128, 254, 255, 1024)


def expected(before, profile, size, native):
    check({key for key, tags in before.items() if tags[0] == [0, 'FIELD']} == set(FIELDS), 'FIELD identity inventory changed')
    for key in FIELDS:
        check(exact(body(before[key])) == exact(body([list(tag) for tag in native[key]])), 'Before FIELD differs from pinned producer packet')
    result = dict(before)
    display = 'binary:' + str(size)
    child = changed_field(before['14F'], profile, 0, display, 'buffer-view')
    _, scalar, scalar_end, _, _, _, _ = cache_sections(child, profile >= 'AC1021')
    data = bytes((i * 73 + 19) % 256 for i in range(size))
    buffer = [[90, 128], [92, size]]
    buffer += [[310, {'hex': data[i:i + 127].hex()}] for i in range(0, size, 127)]
    child[scalar - 1:scalar_end] = buffer
    result['14F'] = child
    result['14E'] = changed_field(before['14E'], profile, display, display, display)
    return result


def verify_pair(before, after, profile, size, native):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted = expected(before, profile, size, native)
    compare(wanted, after)
    controls = 0

    def reject(candidate):
        nonlocal controls
        try:
            compare(wanted, candidate)
        except (ValueError, KeyError):
            controls += 1
        else:
            raise AssertionError('A binary FIELD or unrelated record corruption escaped the comparator')

    for key in ('14E', '14F'):
        for index, (code, value) in enumerate(after[key]):
            candidate = dict(after)
            candidate[key] = list(after[key])
            if isinstance(value, dict):
                payload = bytearray.fromhex(value['hex'])
                check(payload, 'Generated Buffer should not contain empty chunks')
                payload[0] ^= 0x80
                changed = {'hex': payload.hex()}
            else:
                changed = value + '_CORRUPT' if isinstance(value, str) else value + 1
            candidate[key][index] = [code, changed]
            reject(candidate)
            if code == 310:
                candidate = dict(after)
                candidate[key] = after[key][:index] + after[key][index + 1:]
                reject(candidate)
    child = after['14F']
    marker = child.index([7, 'ACFD_FIELD_VALUE'])
    kind = marker + (2 if profile >= 'AC1021' else 1)
    check(child[kind] == [90, 128] and child[kind + 1] == [92, size], 'Buffer type/size packet changed')
    for change in ('date', 'extra-chunk', 'wrong-size'):
        candidate = dict(after)
        candidate['14F'] = copy.deepcopy(child)
        if change == 'date':
            candidate['14F'][kind] = [90, 8]
        elif change == 'extra-chunk':
            candidate['14F'].insert(kind + 2, [310, {'hex': '00'}])
        else:
            candidate['14F'][kind + 1] = [92, size + 1]
        reject(candidate)
    for key in after:
        candidate = dict(after)
        if key in ('14E', '14F'):
            del candidate[key]
        else:
            candidate[key] = after[key] + [[999, 'CORRUPT']]
        reject(candidate)
    return controls


def inventory():
    return {f'field-binary-{phase}-{version}-{binary}-{size}.dxf'
            for version in PROFILES for binary in (False, True) for size in SIZES
            for phase in ('before', 'after')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    check({path.name for path in directory.glob('field-binary-*.dxf')} == inventory(), 'Exact binary FIELD fixture inventory required')
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'tests/fixtures/field-oracle/manifest.json').read_text(encoding='utf-8'))
    native = {item['profile']: source_records(item) for item in manifest['files']}
    check(set(native) == {'AC1015', 'AC1032'}, 'Producer FIELD inventory changed')
    pairs = controls = 0
    for version, profile in PROFILES.items():
        source = native['AC1015' if profile < 'AC1021' else 'AC1032']
        for binary in (False, True):
            for size in SIZES:
                paths = [directory / f'field-binary-{phase}-{version}-{binary}-{size}.dxf' for phase in ('before', 'after')]
                for path in paths:
                    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Binary FIELD transport changed')
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == profile, 'Binary FIELD profile changed')
                    audit = doc.audit()
                    check(not audit.errors and not audit.fixes, 'Binary FIELD output needs graph repairs')
                controls += verify_pair(records(paths[0]), records(paths[1]), profile, size, source)
                pairs += 1
    check(pairs == 96, 'Expected eight sizes across twelve source/profile transports')
    print(f'PASS ezdxf {ezdxf.__version__}: {pairs} binary FIELD pairs, {controls} corruptions rejected; binary values are inert')


if __name__ == '__main__':
    main()
