#!/usr/bin/env python3
"""Independently check explicit FIELD results and unchanged persistent graphs.

Native source packets determine the unchanged prefix/data/format envelope. The
requested scalar and two display strings are derived here, not read from an
expected-value manifest emitted by the library. Host geometry/text stays exact.
"""
import argparse
import copy
import json
from pathlib import Path
import struct
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_legacy_utilities import PROFILES
from verify_stored_fields import source_records
from verify_table_row_settings import wire_text
from verify_mleader_inputs import check, decode_once

KINDS = ('empty', 'integer', 'double', 'string')
FIELDS = ('14E', '14F', '15E', '15F')
ROOT = Path(__file__).resolve().parents[1]


def utf16_length(text):
    return len(text.encode('utf-16-le')) // 2


def exact(value):
    if isinstance(value, float):
        return ('float64', struct.pack('>d', value))
    if isinstance(value, (list, tuple)):
        return tuple(exact(item) for item in value)
    if isinstance(value, dict):
        return tuple((key, exact(item)) for key, item in value.items())
    return value


def body(tags):
    starts = [i for i, tag in enumerate(tags) if tag[0] == 100]
    check(len(starts) == 1 and tags[starts[0]] == [100, 'AcDbField'], 'Unexpected FIELD subclass inventory')
    return tags[starts[0]:]


def cache_sections(tags, modern):
    """Identify one final cache by the explicit key and trailing AcValue marker."""
    keys = [i for i, tag in enumerate(tags) if tag == [7, 'ACFD_FIELD_VALUE']]
    check(len(keys) == 1, 'Missing/repeated cache key')
    start = keys[0] + 1
    # All preceding named evaluator data is protected as part of the prefix.
    if modern:
        check(tags[start][0] == 93 and tags[start + 1][0] == 90, 'Modern AcValue prefix changed')
        scalar = start + 2
    else:
        check(tags[start][0] == 90, 'Compact AcValue prefix changed')
        scalar = start + 1
    kind = tags[scalar - 1][1]
    scalar_end = scalar
    if kind == 0:
        if tags[scalar][0] == 91:
            check(tags[scalar][1] == 0, 'Invalid empty-value padding')
            scalar_end += 1
        value = None
    else:
        codes = {1: 91, 2: 140, 4: 1}
        check(kind in codes and tags[scalar][0] == codes[kind], 'Unsupported cached scalar kind')
        value = decode_once(tags[scalar][1]) if kind == 4 else tags[scalar][1]
        scalar_end += 1
    cache_end = scalar_end
    if modern:
        check([t[0] for t in tags[scalar_end:scalar_end + 4]] == [94, 300, 302, 304]
              and tags[scalar_end + 3][1] == 'ACVALUE_END', 'Modern cache tail changed')
        cache_end += 4
    check(tags[cache_end][0] == 301 and tags[-1][0] == 98
          and all(t[0] == 9 for t in tags[cache_end + 1:-1]), 'FIELD display framing changed')
    display = decode_once(''.join(t[1] for t in tags[cache_end:-1]))
    check(tags[-1][1] == utf16_length(display), 'FIELD display UTF-16 length differs')
    return start, scalar, scalar_end, cache_end, kind, value, display


def explicit_result(kind):
    if kind == 'empty': return None, 'empty', 'empty'
    if kind == 'integer': return 42, '42 units', '42 units'
    if kind == 'double': return -12.5, '-12.500', 'inner numeric display'
    text = r'Literal \U+0041 Ω 😀'
    return text, text, text


def changed_field(tags, profile, value, display, value_display):
    modern = profile >= 'AC1021'
    start, scalar, scalar_end, cache_end, _, old_value, _ = cache_sections(tags, modern)
    check(old_value is None, 'Native before cache is no longer the pinned empty shape')
    result = copy.deepcopy(tags[:start])
    # Only top-level pre-data flags are changed, not duplicate groups in AcValues.
    data = next(i for i, tag in enumerate(result) if tag[0] == 93)
    for code in (94, 95, 96, 300):
        indices = [i for i in range(data) if result[i][0] == code]
        check(len(indices) == 1, 'Ambiguous FIELD evaluation metadata')
        i = indices[0]
        result[i] = [code, (result[i][1] & ~4) | 56 if code == 94 else 2 if code == 95 else 0 if code == 96 else '']
    if value is None:
        result.extend(copy.deepcopy(tags[start:scalar_end]))
    else:
        if modern: result.append(copy.deepcopy(tags[start]))
        kind = 1 if type(value) is int else 2 if type(value) is float else 4
        result += [[90, kind], [{1: 91, 2: 140, 4: 1}[kind], wire_text(value, profile) if kind == 4 else value]]
    if modern:
        result.extend(copy.deepcopy(tags[scalar_end:cache_end]))
        result[-2] = [302, wire_text(value_display, profile)]
    encoded = wire_text(display, profile)
    # The exact 48 operation pairs deliberately fit in one FIELD display chunk.
    check(utf16_length(encoded) <= 250, 'Update oracle chunk inventory for longer explicit fixtures')
    result += [[301, encoded], [98, utf16_length(display)]]
    return result


def expected_records(before, profile, kind, native):
    check({key for key, tags in before.items() if tags[0] == [0, 'FIELD']} == set(FIELDS), 'Persistent FIELD identity inventory changed')
    for key in FIELDS:
        wanted = [list(tag) for tag in native[key]]
        check(exact(body(before[key])) == exact(body(wanted)), 'Before cache/code differs from pinned native packet: ' + key)
    value, display, inner = explicit_result(kind)
    result = dict(before)
    result['14F'] = changed_field(before['14F'], profile, value, display, inner)
    result['14E'] = changed_field(before['14E'], profile, display, display, display)
    check(before['14E'][0] == [0, 'FIELD'] and [360, '14F'] in before['14E'], 'Child ownership slot changed')
    check([330, '14E'] in before['14F'], 'Child reciprocal owner changed')
    for host in ('14B', '15B'):
        check(before[host][0] == [0, 'LINE'], 'Documented synthetic host identity changed')
    return result


def compare(wanted, actual):
    check(list(wanted) == list(actual), 'Ordered physical identity inventory changed')
    for key in wanted:
        check(exact(wanted[key]) == exact(actual[key]), 'Unexpected complete-record change: ' + key)


def check_pair(before, after, profile, kind, native):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted = expected_records(before, profile, kind, native)
    compare(wanted, after)
    controls = 0

    def rejected(candidate):
        nonlocal controls
        try: compare(wanted, candidate)
        except (ValueError, KeyError): controls += 1
        else: raise AssertionError('FIELD/graph corruption escaped the whole-record comparison')

    # Challenge every field in both actually changed records, including code,
    # child links, evaluator data, format flags and the resulting caches.
    for key in ('14E', '14F'):
        for index, (code, value) in enumerate(after[key]):
            candidate = dict(after)
            candidate[key] = list(after[key])
            candidate[key][index] = [code, value + '_CORRUPT' if isinstance(value, str) else value + 1]
            rejected(candidate)
        candidate = dict(after)
        candidate[key] = after[key] + [[98, 123]]
        rejected(candidate)
    # The other FIELD tree, FIELDLIST, owner dictionaries, hosts and every other
    # physical record are checked, not normalized away or omitted from controls.
    for key in after:
        candidate = dict(after)
        if key in ('14E', '14F'): del candidate[key]
        else: candidate[key] = after[key] + [[999, 'CORRUPT']]
        rejected(candidate)
    return controls


def inventory():
    return {f'field-results-{phase}-{version}-{binary}-{kind}.dxf'
            for version in PROFILES for binary in (False, True)
            for kind in KINDS for phase in ('before', 'after')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    check({p.name for p in directory.glob('field-results-*.dxf')} == inventory(), 'Exact FIELD result fixture inventory required')
    manifest = json.loads((ROOT / 'tests/fixtures/field-oracle/manifest.json').read_text())
    native = {item['profile']: source_records(item) for item in manifest['files']}
    check(set(native) == {'AC1015', 'AC1032'}, 'FIELD producer profile inventory changed')
    controls = pairs = 0
    for version, profile in PROFILES.items():
        source = native['AC1015' if profile < 'AC1021' else 'AC1032']
        for binary in (False, True):
            for kind in KINDS:
                paths = [directory / f'field-results-{phase}-{version}-{binary}-{kind}.dxf' for phase in ('before', 'after')]
                for path in paths:
                    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'FIELD transport changed')
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == profile, 'FIELD source profile changed')
                    audit = doc.audit()
                    check(not audit.errors and not audit.fixes, 'FIELD graph requires independent repairs')
                controls += check_pair(records(paths[0]), records(paths[1]), profile, kind, source)
                pairs += 1
                print('PASS ' + paths[1].name)
    check(pairs == 48, 'Expected 48 FIELD result pairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {pairs} FIELD pairs; {controls} actual-output corruptions rejected; no native evaluator/host regeneration claim')


if __name__ == '__main__':
    main()
