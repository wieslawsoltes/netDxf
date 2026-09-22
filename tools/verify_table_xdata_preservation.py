#!/usr/bin/env python3
"""Check complete table XData, DesignCenter units and selected layer projections."""
import itertools
from pathlib import Path
import sys
import ezdxf
from verify_dimension_xdata_preservation import load
from verify_dimlfac_fidelity import PROFILES, records
from verify_raw_line_geometry import key, require, reject


def text(value, year):
    return ''.join(f'\\U+{ord(c):04X}' if ord(c) > 127 else c for c in value) if year < 2007 else value


def units_packet(year, units):
    return [(1000, text('BEFORE_Ł', year)), (1002, '{'), (1000, 'DesignCenter Data'), (1002, '{'),
            (1070, 1), (1070, 7), (1002, '}'), (1002, '}'), (1000, 'dEsIgNcEnTeR dAtA'),
            (1002, '{'), (1070, 7), (1070, units), (1070, 24), (1002, '{'),
            (1000, 'extension'), (1002, '}'), (1002, '}'), (1004, bytes([0, 255, 10, 125])), (1000, 'AFTER')]


def layer_packets(year, stage):
    description = ('Initial_Ł', 'Updated_Ł', '')[stage]
    # Independent packed-alpha expectations for the three exact fixture percentages.
    alpha = 0x02000000 | (160, 96, 255)[stage]
    return [('TABLEX_BEFORE', [(1000, 'first')]),
            ('AcAecLayerStandard', [(1000, 'private-first'), (1070, 17), (1000, 'earlier-string'),
                (1040, -0.), (1000, text(description, year)), (1004, bytes([0, 1, 255])), (1071, 8123)]),
            ('AcCmTransparency', [(1000, 'alpha-prefix'), (1071, 0x020000AB), (1002, '{'),
                (1000, 'opaque'), (1002, '}'), (1071, alpha), (1004, bytes([1, 255, 0])), (1040, 1e-13)]),
            ('TABLEX_AFTER', [(1000, 'last')])]


def block_packets(year, units):
    return [('TABLEX_BEFORE', [(1000, 'first')]), ('ACAD', units_packet(year, units)),
            ('TABLEX_AFTER', [(1000, 'last')])]


def one(record, code):
    found = [v for c, v in record if c == code]
    require(len(found) == 1, f'Missing/duplicate group {code}')
    return found[0]


def packet(record, expected):
    start = next((i for i, tag in enumerate(record) if tag[0] == 1001), None)
    require(start is not None, 'Missing table XData')
    wanted = [(code, value) for app, values in expected for code, value in [(1001, app)] + values]
    require(list(map(key, record[start:])) == list(map(key, wanted)), 'Table XData content/order/presence or binary64 bits')
    return range(start, len(record))


def corrupt_packet(record, expected):
    controls = 0
    for at in packet(record, expected):
        for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
            damaged = list(record); code, value = damaged[at]
            if operation == 'change':
                value = value + b'!' if isinstance(value, bytes) else value + '_BAD' if isinstance(value, str) else value + 1
                damaged[at] = (code, value)
            elif operation == 'remove': del damaged[at]
            elif operation == 'duplicate': damaged.insert(at, damaged[at])
            else: damaged[at] = (999, value)
            controls += reject(lambda: packet(damaged, expected))
    return controls


def independent_xdata(entity, expected):
    # Inspect complete actual XData. Convenience properties assume canonical payload
    # shapes and do not interpret the ancillary records deliberately retained here.
    for app, values in expected:
        require(entity.has_xdata(app), 'Independent missing application ' + app)
        actual = [(t.code, t.value) for t in entity.get_xdata(app)]
        require(list(map(key, actual)) == list(map(key, values)), 'Independent table XData ' + app)


def inspect(path, year, binary, placement, stage, legacy_units=None, source=False):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Wrong transport')
    tags = load(path, year)
    at = tags.index((9, '$ACADVER')); require(tags[at + 1] == (1, PROFILES[year]), 'Wrong physical version')
    entries = [tags[a:b] for a, b in records(tags)]
    layers = [r for r in entries if r[0] == (0, 'LAYER') and (2, 'TABLEX_LAYER') in r]
    targets = [r for r in entries if r[0] == (0, 'BLOCK_RECORD') and (1001, 'TABLEX_BEFORE') in r]
    require(len(layers) == 1 and len(targets) == 1, 'Table object count')
    layer, block = layers[0], targets[0]
    units = legacy_units if legacy_units is not None else (4, 6, 0)[stage] if placement < 3 else 0
    expected_layer = layer_packets(year, stage)
    expected_block = block_packets(year, units)
    controls = corrupt_packet(layer, expected_layer) + corrupt_packet(block, expected_block)
    native = [] if placement >= 3 or legacy_units is not None and source else [(70, units)]
    def native_packet(record):
        require([t for t in record if t[0] == 70] == native, 'Native unit value/presence')
    native_packet(block)
    damaged = list(block)
    if native:
        at = next(i for i, t in enumerate(damaged) if t[0] == 70)
        for op in ('change', 'remove', 'duplicate'):
            bad = list(block)
            if op == 'change': bad[at] = (70, (units + 1) % 25)
            elif op == 'remove': del bad[at]
            else: bad.insert(at, bad[at])
            controls += reject(lambda: native_packet(bad))
    else:
        damaged.insert(1, (70, units)); controls += reject(lambda: native_packet(damaged))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent profile')
    loaded_layer = doc.layers.get('TABLEX_LAYER')
    if placement < 3:
        space = doc.blocks['TABLEX_BLOCK']
    elif placement == 3:
        space = doc.modelspace()
    else:
        space = doc.layouts.get('TABLEX_PAPER')
    loaded_block = space.block_record
    require(loaded_layer.dxf.handle == one(layer, 5) and loaded_block.dxf.handle == one(block, 5), 'Independent table identity')
    independent_xdata(loaded_layer, expected_layer); independent_xdata(loaded_block, expected_block)
    if native:
        require(loaded_block.dxf.units == units, 'Independent native units')
    # ezdxf does not project legacy DesignCenter units. Those source packets are
    # checked in full above; do not report its default dxf.units as a legacy decode.
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    require(line.dxf.layer == 'TABLEX_LAYER' and line.dxf.owner == doc.modelspace().block_record_handle, 'Following layer/owner')
    if placement < 3:
        line, = space.query('LINE')
        require(tuple(line.dxf.start) == (1., 2., 3.) and tuple(line.dxf.end) == (4., 5., 6.), 'Stored block geometry')
        require(line.dxf.owner == space.block_record_handle and line.dxf.layer == 'TABLEX_LAYER', 'Block line owner/layer')
        model_inserts = list(doc.modelspace().query('INSERT'))
        require(len(model_inserts) == (1 if placement == 0 else 0), 'Model instance count')
        if placement == 2:
            insert, = doc.layouts.get('TABLEX_PAPER').query('INSERT')
            require(insert.dxf.name == 'TABLEX_BLOCK', 'Paper instance target')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors/repairs')
    return controls


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), range(5), ('source','1-False','1-True','2-False','2-True')))
    legacy = list(itertools.product(PROFILES, (False, True), range(25), ('source','output')))
    def name(s):
        year, binary, placement, phase = s
        return f'table-xdata-AutoCad{year}-{binary}-{placement}-{phase}.dxf'
    def legacy_name(s):
        year, binary, unit, phase = s
        return f'table-legacy-units-AutoCad{year}-{binary}-{unit}-{phase}.dxf'
    expected = {name(s) for s in specs} | {legacy_name(s) for s in legacy}
    def inventory(actual): require(actual == expected, f'Table inventory missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for pattern in ('table-xdata-*.dxf','table-legacy-units-*.dxf') for p in directory.glob(pattern)})
    reject(lambda: inventory(expected - {min(expected)})); reject(lambda: inventory(expected | {'table-xdata-extra.dxf'}))
    controls = 0
    for s in specs:
        year, binary, placement, phase = s
        controls += inspect(directory/name(s), year, binary if phase == 'source' else phase.endswith('True'),
                            placement, 0 if phase == 'source' else int(phase[0]))
    for s in legacy:
        year, binary, unit, phase = s
        controls += inspect(directory/legacy_name(s), year, binary, 1, 0, legacy_units=unit, source=phase == 'source')
    print(f'PASS: {len(expected)} table-XData drawings, {2*len(expected)} independently loaded table records; '
          f'{controls} actual-packet corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Legacy units and ancillary layer records receive full packet checks, not inferred native application semantics.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_table_xdata_preservation.py ARTIFACTS')
    main(Path(sys.argv[1]))
