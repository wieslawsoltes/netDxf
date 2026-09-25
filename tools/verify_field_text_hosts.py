#!/usr/bin/env python3
"""Independently compare FIELD-result and literal text-host updates.

The fixtures use declared synthetic TEXT/MTEXT/ATTRIB/ATTDEF hosts around pinned
producer FIELD bodies. Expected edits are computed here, never read from an
emitted expected-value manifest. Every unselected physical record stays exact.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import ezdxf
from verify_editable_table_styles import records, normalize_save_metadata
from verify_field_results import body, changed_field, compare, exact, FIELDS
from verify_legacy_utilities import PROFILES
from verify_stored_fields import source_records
from verify_table_row_settings import wire_text
from verify_table_styles import common, entries as dictionary_entries
from verify_mleader_inputs import check

KINDS = ('TEXT', 'MTEXT', 'ATTRIB', 'ATTDEF')
LITERAL = 'FIELD Ω {braces} \\path 😀'


def remove_proxy(tags):
    """Remove only a complete AcDbEntity proxy, never private binary data."""
    starts = [i for i, item in enumerate(tags) if item == [100, 'AcDbEntity']]
    check(len(starts) == 1, 'Missing/repeated common entity subclass')
    start = starts[0] + 1
    end = next((i for i in range(start, len(tags)) if tags[i][0] == 100), len(tags))
    sizes = [i for i in range(start, end) if tags[i][0] in (92, 160)]
    chunks = [i for i in range(start, end) if tags[i][0] == 310]
    check(len(sizes) <= 1 and (bool(sizes) or not chunks), 'Ambiguous common proxy framing')
    if sizes:
        count = tags[sizes[0]][1]
        check(type(count) is int and 0 <= count <= 16 * 1024 * 1024, 'Invalid proxy byte declaration')
        lengths = []
        for i in chunks:
            value = tags[i][1]
            check(isinstance(value, dict) and set(value) == {'hex'}, 'Invalid proxy chunk representation')
            lengths.append(len(bytes.fromhex(value['hex'])))
        check(all(n <= 127 for n in lengths) and sum(lengths) == count, 'Proxy byte count mismatch')
    removed = set(sizes + chunks)
    return [copy.deepcopy(tag) for i, tag in enumerate(tags) if i not in removed]


def literal_value(text, kind):
    if kind != 'MTEXT':
        return text
    # Independent, literal-only formatting policy. This is not a native evaluator.
    return text.replace('\\', '\\\\').replace('{', '\\{').replace('}', '\\}').replace('\r\n', '\\P').replace('\r', '\\P').replace('\n', '\\P')


def host_wire_text(text, profile):
    # Ordinary text writers encode non-ASCII characters for old profiles, unlike
    # FIELD cache writers, which additionally quote every literal backslash.
    if profile >= 'AC1021':
        return text
    units = text.encode('utf-16-le')
    values = [units[i] | units[i + 1] << 8 for i in range(0, len(units), 2)]
    return ''.join('\\U+%04X' % value if value > 127 else chr(value) for value in values)


def changed_host(tags, profile, kind):
    check(tags[0] == [0, kind], 'Host record kind changed')
    output = remove_proxy(tags)
    marker = 'AcDbMText' if kind == 'MTEXT' else 'AcDbText'
    start = output.index([100, marker]) + 1
    end = next((i for i in range(start, len(output)) if output[i][0] == 100), len(output))
    values = [i for i in range(start, end) if output[i][0] == 1]
    check(len(values) == 1 and not any(t[0] == 3 for t in output[start:end]), 'Short-fixture text chunk inventory changed')
    check(output[values[0]][1] == 'original0', 'Before host text is not the declared synthetic input')
    output[values[0]] = [1, host_wire_text(literal_value(LITERAL, kind), profile)]
    return output


def validate_attribute_sequences(items, kind):
    """Validate the synthetic graph without rewriting any SEQEND identity."""
    if kind != 'ATTRIB':
        return items
    ordered = list(items)
    terminators = []
    for position, handle in enumerate(ordered):
        tags = items[handle]
        if tags[0] != [0, 'INSERT']:
            continue
        check([t for t in tags if t[0] == 66] == [[66, 1]], 'Expected attribute-following INSERT')
        index = position + 1
        count = 0
        while index < len(ordered) and items[ordered[index]][0] == [0, 'ATTRIB']:
            check(common(items[ordered[index]])[0] == handle, 'Physical attribute is outside its INSERT scope')
            count += 1
            index += 1
        check(count == 1 and index < len(ordered), 'Generated attribute sequence inventory changed')
        end = ordered[index]
        layers = [t for t in tags if t[0] == 8]
        check(len(layers) == 1, 'Ambiguous INSERT layer')
        check(items[end] == [[0, 'SEQEND'], [5, end], [330, handle], [100, 'AcDbEntity'], layers[0]],
              'Stored INSERT sequence end has invalid identity, owner, metadata or payload')
        # These synthetic FIELD hosts declare no external terminator references.
        # Check that fixture constraint, but never canonicalize a stored handle.
        for record in items.values():
            check(not any(value == end for code, value in record
                          if 320 <= code <= 369 or 390 <= code <= 399 or code in (480, 481, 1005)),
                  'Unexpected reference to a synthetic FIELD-host terminator')
        terminators.append(end)
    check(len(terminators) == 2, 'Expected exactly two independent stored sequence ends')
    return items


def expected(before, profile, kind, native):
    check({key for key, tags in before.items() if tags[0] == [0, 'FIELD']} == set(FIELDS), 'FIELD inventory changed')
    for key in FIELDS:
        check(exact(body(before[key])) == exact(body([list(t) for t in native[key]])), 'Before FIELD differs from pinned source: ' + key)
    for stem in ('14', '15'):
        host, extension, fields, root = (stem + suffix for suffix in 'BCDE')
        check(before[host][0] == [0, kind], 'Wrong synthetic host type')
        check(common(before[host])[1] == extension, 'Host extension dictionary link changed')
        check(common(before[extension])[0] == host, 'Extension reciprocal owner changed')
        check(dictionary_entries(before[extension]) == [['ACAD_FIELD', 360, fields]], 'ACAD_FIELD ownership slot changed')
        check(common(before[fields])[0] == extension, 'Field dictionary reciprocal owner changed')
        check(dictionary_entries(before[fields]) == [['TEXT', 360, root]], 'TEXT field ownership slot changed')
        check(common(before[root])[0] == fields, 'Root FIELD reciprocal owner changed')
    output = dict(before)
    output['14E'] = changed_field(before['14E'], profile, LITERAL, LITERAL, LITERAL)
    output['14F'] = changed_field(before['14F'], profile, 42, LITERAL, LITERAL)
    output['14B'] = changed_host(before['14B'], profile, kind)
    selected = {'14B', '14E', '14F'}
    if kind == 'ATTRIB':
        owner = common(before['14B'])[0]
        check(owner in before and before[owner][0] == [0, 'INSERT'], 'Attribute INSERT owner missing')
        check(common(before['15B'])[0] != owner, 'Independent host-owner fixture changed')
        output[owner] = remove_proxy(before[owner])
        selected.add(owner)
    return output, selected


def check_pair(before, after, profile, kind, native):
    before, after = (validate_attribute_sequences(normalize_save_metadata(items), kind) for items in (before, after))
    wanted, selected = expected(before, profile, kind, native)
    compare(wanted, after)
    controls = 0

    def rejects(candidate):
        nonlocal controls
        try:
            compare(wanted, candidate)
        except (ValueError, KeyError):
            controls += 1
        else:
            raise AssertionError('FIELD/text-host corruption escaped the same complete-record comparator')

    for handle in selected:
        for index, (code, value) in enumerate(after[handle]):
            candidate = dict(after)
            candidate[handle] = list(after[handle])
            changed = value + '_CORRUPT' if isinstance(value, str) else value + 1 if isinstance(value, (int, float)) else 'CORRUPT'
            candidate[handle][index] = [code, changed]
            rejects(candidate)
        if handle not in FIELDS:
            candidate = dict(after)
            candidate[handle] = list(after[handle])
            at = candidate[handle].index([100, 'AcDbEntity']) + 1
            candidate[handle][at:at] = [[92, 1], [310, {'hex': '00'}]]
            rejects(candidate)
    # Terminators are unselected physical records: every field and their map
    # identities must remain exact, not just survive generated-ID normalization.
    for handle, tags in after.items():
        if tags[0] != [0, 'SEQEND']:
            continue
        for index, (code, value) in enumerate(tags):
            candidate = dict(after)
            candidate[handle] = copy.deepcopy(tags)
            candidate[handle][index] = [code, 'CORRUPT']
            rejects(candidate)
        candidate = {('CORRUPT' if key == handle else key): copy.deepcopy(row)
                     for key, row in after.items()}
        candidate['CORRUPT'][1] = [5, 'CORRUPT']
        rejects(candidate)
    for handle in after:
        candidate = dict(after)
        if handle in selected:
            del candidate[handle]
        else:
            candidate[handle] = after[handle] + [[999, 'CORRUPT']]
        rejects(candidate)
    return controls


def inventory():
    return {f'field-hosts-{phase}-{version}-{binary}-{kind}.dxf'
            for version in PROFILES for binary in (False, True)
            for kind in KINDS for phase in ('before', 'after')}


def check_inventory(directory):
    check({p.name for p in directory.glob('field-hosts-*.dxf')} == inventory(), 'Exact FIELD/text-host fixture inventory required')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    check_inventory(directory)
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'tests/fixtures/field-oracle/manifest.json').read_text(encoding='utf-8'))
    native = {item['profile']: source_records(item) for item in manifest['files']}
    check(set(native) == {'AC1015', 'AC1032'}, 'Native FIELD source inventory changed')
    controls = pairs = 0
    for version, profile in PROFILES.items():
        source = native['AC1015' if profile < 'AC1021' else 'AC1032']
        for binary in (False, True):
            for kind in KINDS:
                paths = [directory / f'field-hosts-{phase}-{version}-{binary}-{kind}.dxf' for phase in ('before', 'after')]
                for path in paths:
                    check(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Host transport changed')
                    doc = ezdxf.readfile(path)
                    check(doc.dxfversion == profile, 'Host source profile changed')
                    audit = doc.audit()
                    check(not audit.errors and not audit.fixes, 'FIELD/text-host graph needs independent repairs')
                controls += check_pair(records(paths[0]), records(paths[1]), profile, kind, source)
                pairs += 1
                print('PASS ' + paths[1].name)
    check(pairs == 48, 'Expected forty-eight text-host operation pairs')
    print(f'PASS ezdxf {ezdxf.__version__}: {pairs} FIELD/text-host pairs; {controls} actual-output corruptions rejected; no native evaluator/reflow claim')


if __name__ == '__main__':
    main()
