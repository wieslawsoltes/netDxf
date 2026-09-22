#!/usr/bin/env python3
"""Verify scoped DSTYLE edits without losing neighboring or unsupported ACAD data."""
import io
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from verify_raw_line_geometry import key, require, reject
from verify_dimlfac_fidelity import PROFILES, records


def load(path, year):
    data = path.read_bytes()
    source = binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else ascii_tags_loader(
        io.StringIO(data.decode('utf-8' if year >= 2007 else 'cp1252'), newline=None))
    return [(t.code, (bytes.fromhex(t.value) if isinstance(t.value, str) else t.value) if t.code == 1004
             else cast_tag_value(t.code, t.value)) for t in source]


def expected_acad(year, kind, stage, handle):
    # The nested named list deliberately resembles DSTYLE but belongs to opaque data.
    prefix = [(1000, 'BEFORE_Ł' if year >= 2007 else 'BEFORE_\\U+0141'),
              (1002, '{'), (1000, 'DSTYLE'), (1002, '{'), (1070, 140), (1040, 99.), (1002, '}'), (1002, '}'),
              (1004, bytes([0, 255, 125, 10])), (1005, handle),
              (1010, 1e-13), (1020, -0.), (1030, 3.75)]
    body = [(1000, 'dStYlE'), (1002, '{'), (1070, 43), (1040, 42.)]
    if stage < 2 or kind == 9:
        body += [(1070, 140), (1040, (1.25, 2.75, 1.)[stage])]
    body += [(1070, 999), (1000, 'PRIVATE')]
    if stage == 0 or kind == 9:
        body += [(1070, 144), (1040, 2.)]
    return prefix + body + [(1002, '}'), (1000, 'AFTER'), (1002, '{'), (1000, 'tail'), (1071, 123456), (1002, '}')]


def packet(record, year, kind, stage, handle):
    app = next((i for i, t in enumerate(record) if t[0] == 1001), None)
    require(app is not None, 'Missing XData')
    wanted = [(1001, 'DXDATA_BEFORE'), (1000, 'first'), (1001, 'ACAD')]
    wanted += expected_acad(year, kind, stage, handle)
    wanted += [(1001, 'DXDATA_AFTER'), (1000, 'last')]
    require([key(t) for t in record[app:]] == [key(t) for t in wanted], 'XData content/order/presence or double bits changed')
    return range(app, len(record))


def inspect(path, year, binary, kind, placement, stage):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load(path, year)
    at = tags.index((9, '$ACADVER')); require(tags[at+1] == (1, PROFILES[year]), 'Physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    hosts = [r for r in entries if (8, 'DXDATA_HOST') in r and r[0][1] in ('DIMENSION', 'ARC_DIMENSION', 'LEADER', 'TOLERANCE')]
    require(len(hosts) == 1, 'Host inventory')
    lines = [r for r in entries if r[0] == (0, 'LINE') and (10, 17.25) in r and (20, -4.5) in r]
    require(len(lines) == 1, 'Sentinel LINE inventory')
    handle, = [value for code, value in lines[0] if code == 5]
    host = hosts[0]; positions = packet(host, year, kind, stage, handle)
    controls = 0
    for at in positions:
        for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
            bad = list(host); code, value = bad[at]
            if operation == 'change':
                value = value + b'!' if isinstance(value, bytes) else value + '_BAD' if isinstance(value, str) else value + 1
                bad[at] = (code, value)
            elif operation == 'remove': del bad[at]
            elif operation == 'duplicate': bad.insert(at, bad[at])
            else: bad[at] = (999, value)
            controls += reject(lambda: packet(bad, year, kind, stage, handle))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent profile')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('DXDATA_PAPER') if placement == 1 else doc.blocks['DXDATA_HOLDER']
    objects = list(space.query('DIMENSION ARC_DIMENSION LEADER TOLERANCE'))
    require(len(objects) == 1, 'Independent host placement/count')
    entity = objects[0]
    require(entity.dxftype() == {0:'DIMENSION', 7:'ARC_DIMENSION', 8:'LEADER', 9:'TOLERANCE'}[kind], 'Independent family')
    require(entity.dxf.owner == space.block_record_handle, 'Independent owner')
    # Inspect the independent reader's actual XData rather than its named-list convenience
    # lookup: this corpus includes a nested DSTYLE lookalike and mixed-case top-level marker.
    actual = []
    for tag in entity.get_xdata('ACAD'):
        if tag.code == 1010:
            actual += [(1010 + 10*i, component) for i, component in enumerate(tag.value)]
        else: actual.append((tag.code, tag.value))
    require([key(t) for t in actual] == [key(t) for t in expected_acad(year, kind, stage, handle)], 'Independently loaded ACAD records changed')
    for app, value in [('DXDATA_BEFORE', 'first'), ('DXDATA_AFTER', 'last')]:
        require([(t.code, t.value) for t in entity.get_xdata(app)] == [(1000, value)], 'Other application changed')
    sentinel = doc.entitydb[handle]
    require(sentinel.dxftype() == 'LINE' and tuple(sentinel.dxf.start) == (17.25, -4.5, 2.)
            and tuple(sentinel.dxf.end) == (18.5, 9.25, -3.), 'Referenced sentinel geometry')
    if kind in (0, 7):
        require(entity.dxf.text == 'FIXED', 'Primary text changed')
        text, = entity.get_geometry_block().query('MTEXT'); require(text.text == 'FIXED', 'Fixed label changed')
    if placement >= 2:
        require(len(doc.modelspace().query('INSERT')) == (1 if placement == 2 else 0), 'Block reference policy')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors or repairs')
    return controls


def main(directory):
    specs = [s for s in itertools.product(PROFILES, (False, True), (0,7,8,9), range(4),
                    ('source','1-False','1-True','2-False','2-True')) if not (s[0] == 2000 and s[2] == 7)]
    def name(s):
        year, binary, kind, placement, phase = s
        return f'dimension-xdata-AutoCad{year}-{binary}-{kind}-{placement}-{phase}.dxf'
    expected = {name(s) for s in specs}
    def inventory(actual): require(actual == expected, f'XData inventory: missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimension-xdata-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)})); reject(lambda: inventory(expected | {'dimension-xdata-extra.dxf'}))
    controls = 0
    for s in specs:
        year, binary, kind, placement, phase = s
        stage = 0 if phase == 'source' else int(phase[0])
        transport = binary if phase == 'source' else phase.endswith('True')
        controls += inspect(directory/name(s), year, transport, kind, placement, stage)
    print(f'PASS: {len(specs)} DSTYLE preservation drawings; {controls} actual-packet corruptions and two inventory controls rejected; '
          'independent XData, ownership and graph checks have zero errors/repairs. No native renderer qualification is implied.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimension_xdata_preservation.py ARTIFACTS')
    main(Path(sys.argv[1]))
