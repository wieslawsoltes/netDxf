#!/usr/bin/env python3
"""Verify persisted FIELD failures, cache-retention policies and unchanged graphs.

Expected field bodies derive from pinned producer packets plus explicit operations,
not emitted manifests. Failure status is distinct from a transaction exception.
"""
import argparse
import copy
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_field_results import body, cache_sections, changed_field, compare, exact, FIELDS, utf16_length
from verify_legacy_utilities import PROFILES
from verify_stored_fields import source_records
from verify_table_row_settings import wire_text
from verify_mleader_inputs import check

STATUSES = {'EvaluatorNotFound': 4, 'SyntaxError': 8, 'InvalidCode': 16, 'InvalidContext': 32, 'OtherError': 64}
POLICIES = ('retained-empty', 'retained-populated', 'fallback')
MESSAGE = r'Evaluation Ω \U+0041 😀'


def failure_field(tags, profile, status, policy, child):
    modern = profile >= 'AC1021'
    start, scalar, scalar_end, cache_end, _, _, _ = cache_sections(tags, modern)
    output = copy.deepcopy(tags)
    data = next(i for i, tag in enumerate(tags) if tag[0] == 93)
    for code in (94, 95, 96, 300):
        matches = [i for i in range(data) if tags[i][0] == code]
        check(len(matches) == 1, 'Ambiguous public evaluation metadata')
        index = matches[0]
        value = ((tags[index][1] & ~4) | 8 | (48 if policy == 'fallback' else 0)) if code == 94 else status if code == 95 else -901 if code == 96 else wire_text(MESSAGE, profile)
        output[index] = [code, value]
    if policy != 'fallback':
        # The complete original cache and display chunks remain exact, including
        # null padding, inner flags, source spelling and absence of cache flags.
        return output
    output = output[:start]
    if modern:
        output.append(copy.deepcopy(tags[start]))
    output += [[90, 1 if child else 4], [91 if child else 1, -7 if child else '#ERR']]
    if modern:
        output.extend(copy.deepcopy(tags[scalar_end:cache_end]))
        output[-2] = [302, 'fallback display']
    output += [[301, '#ERR'], [98, utf16_length('#ERR')]]
    return output


def expected(before, profile, status, policy, native):
    check({key for key, value in before.items() if value[0] == [0, 'FIELD']} == set(FIELDS), 'Unexpected FIELD inventory')
    output = dict(before)
    for key in FIELDS:
        original = [list(tag) for tag in native[key]]
        child = key.endswith('F')
        if policy != 'retained-empty':
            original = changed_field(original, profile, 42 if child else 'retained', 'retained', 'retained')
        check(exact(body(original)) == exact(body(before[key])), 'Before FIELD is not the expected native/populated snapshot: ' + key)
        output[key] = failure_field(before[key], profile, status, policy, child)
    return output


def challenge(before, after, profile, status, policy, native):
    before, after = normalize_save_metadata(before), normalize_save_metadata(after)
    wanted = expected(before, profile, status, policy, native)
    compare(wanted, after)
    controls = 0

    def reject(candidate):
        nonlocal controls
        try:
            compare(wanted, candidate)
        except (ValueError, KeyError):
            controls += 1
        else:
            raise AssertionError('FIELD failure corruption escaped the complete-record comparator')

    for key in FIELDS:
        for index, (code, value) in enumerate(after[key]):
            candidate = dict(after)
            candidate[key] = list(after[key])
            candidate[key][index] = [code, value + '_CORRUPT' if isinstance(value, str) else value + 1]
            reject(candidate)
        candidate = dict(after)
        candidate[key] = after[key] + [[95, 2]]
        reject(candidate)
    for key in after:
        candidate = dict(after)
        if key in FIELDS:
            del candidate[key]
        else:
            candidate[key] = after[key] + [[999, 'CORRUPT']]
        reject(candidate)
    return controls


def inventory():
    return {f'field-failures-{phase}-{version}-{binary}-{status}-{policy}.dxf'
            for version in PROFILES for binary in (False, True)
            for status in STATUSES for policy in POLICIES for phase in ('before', 'after')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    check({p.name for p in directory.glob('field-failures-*.dxf')} == inventory(), 'Exact FIELD failure output inventory required')
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'tests/fixtures/field-oracle/manifest.json').read_text(encoding='utf-8'))
    native = {item['profile']: source_records(item) for item in manifest['files']}
    check(set(native) == {'AC1015', 'AC1032'}, 'Native FIELD source inventory changed')
    controls = pairs = 0
    for version, profile in PROFILES.items():
        source = native['AC1015' if profile < 'AC1021' else 'AC1032']
        for binary in (False, True):
            for name, status in STATUSES.items():
                for policy in POLICIES:
                    paths = [directory / f'field-failures-{phase}-{version}-{binary}-{name}-{policy}.dxf' for phase in ('before', 'after')]
                    for path in paths:
                        check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Failure transport changed')
                        doc = ezdxf.readfile(path)
                        check(doc.dxfversion == profile, 'Failure source profile changed')
                        audit = doc.audit()
                        check(not audit.errors and not audit.fixes, 'Failed FIELD graph requires independent repairs')
                    controls += challenge(records(paths[0]), records(paths[1]), profile, status, policy, source)
                    pairs += 1
                    print('PASS ' + paths[1].name)
    check(pairs == 180, 'Expected all five statuses, three cache policies and twelve transports/profiles')
    print(f'PASS ezdxf {ezdxf.__version__}: {pairs} FIELD failure pairs; {controls} actual-output corruptions; no native evaluator execution claim')


if __name__ == '__main__':
    main()
