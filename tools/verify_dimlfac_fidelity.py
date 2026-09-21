"""DIMLFAC real-value packets, independent loads, and complete inventory."""
import itertools
import math
from pathlib import Path
import sys
import ezdxf
from verify_raw_line_geometry import load_tags, key, require, reject

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021',
            2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}
VALUES = (float.fromhex('0x0.0000000000001p-1022'), -float.fromhex('0x0.0000000000001p-1022'),
          1e-300, -1e-300, 1e-13, -1e-13, 1e-6, -1e-6, 1., -1.,
          1e300, -1e300, sys.float_info.max, -sys.float_info.max)
STYLE = 'DIMLFAC_FIDELITY'


def same(actual, wanted, label):
    require(isinstance(actual, float) and math.isfinite(actual), label + ': nonfinite/nonreal')
    require(key((144, actual)) == key((144, wanted)), label + ': double bits differ')


def records(tags):
    starts = [i for i, t in enumerate(tags) if t[0] == 0] + [len(tags)]
    return [(a, b) for a, b in zip(starts, starts[1:])]


def packet(tags, year, value, source):
    def one_index(predicate, label):
        indexes = [i for i, tag in enumerate(tags) if predicate(tag)]
        require(len(indexes) == 1, label + ': absent/duplicate')
        return indexes[0]
    av = one_index(lambda t: t == (9, '$ACADVER'), 'version')
    require(tags[av + 1] == (1, PROFILES[year]), 'Wrong physical version')
    header = one_index(lambda t: t == (9, '$DIMLFAC'), 'header') + 1
    end = next(i for i in range(header, len(tags)) if tags[i][0] in (0, 9))
    require(end == header + 1 and tags[header][0] == 40, 'Wrong/duplicate header group')
    same(tags[header][1], 1. if source else value, 'Header DIMLFAC')
    ranges = records(tags)
    styles = [(a, b) for a, b in ranges if tags[a] == (0, 'DIMSTYLE') and (2, STYLE) in tags[a:b]]
    require(len(styles) == 1, 'Named style absent/duplicate')
    a, b = styles[0]
    scales = [i for i in range(a, b) if tags[i][0] == 144]
    require(len(scales) == 1, 'Style group144 absent/duplicate')
    scale = scales[0]; same(tags[scale][1], value, 'Style DIMLFAC')
    dimensions = [(a, b) for a, b in ranges if tags[a] == (0, 'DIMENSION')]
    require(len(dimensions) == 1, 'Dimension absent/duplicate')
    a, b = dimensions[0]
    require((3, STYLE) in tags[a:b] and (1001, 'ACAD') in tags[a:b]
            and (1000, 'DSTYLE') in tags[a:b], 'Override framing/style')
    ids = [i for i in range(a, b) if tags[i] == (1070, 144)]
    require(len(ids) == 1, 'DIMLFAC override identifier absent/duplicate')
    override = ids[0] + 1
    require(override < b and tags[override][0] == 1040, 'Override must be a real value')
    require(sum(tags[i][0] == 1040 for i in range(a, b)) == 1, 'Duplicate override real')
    same(tags[override][1], -value, 'Override DIMLFAC')
    return header, scale, override


def independent(path, year, value, source, block):
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == PROFILES[year], 'Independent version')
    style = doc.dimstyles.get(STYLE)
    same(style.dxf.dimlfac, value, 'Independent style')
    same(doc.header['$DIMLFAC'], 1. if source else value, 'Independent header')
    space = doc.blocks['DIMLFAC_HOLDER'] if block else doc.modelspace()
    dimensions = list(space.query('DIMENSION'))
    require(len(dimensions) == 1, 'Independent dimension placement/count')
    dimension = dimensions[0]
    require(dimension.dxf.dimstyle == STYLE and dimension.dxf.text == 'FIXED', 'Dimension metadata')
    overrides = dimension.get_acad_dstyle(style)
    require(set(overrides) == {'dimlfac'}, 'Unexpected override set')
    same(overrides['dimlfac'], -value, 'Independent override')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and
            tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following entity damaged')
    # This audits stored database integrity, not dimension rendering or cache regeneration.
    audit = doc.audit()
    require(not audit.errors and not audit.fixes, 'Independent graph errors or repairs')


def main(directory):
    specs = list(itertools.product(PROFILES, (False, True), (False, True), range(len(VALUES)), ('source', 'False', 'True')))
    def name(spec):
        year, binary, block, index, output = spec
        return f'dimlfac-fidelity-AutoCad{year}-{binary}-{block}-{index}-{output}.dxf'
    expected = {name(spec) for spec in specs}
    def inventory(actual):
        require(actual == expected, f'DIMLFAC inventory: missing={len(expected-actual)}, extra={len(actual-expected)}')
    inventory({p.name for p in directory.glob('dimlfac-fidelity-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'dimlfac-fidelity-unexpected.dxf'}))
    corruptions = 0
    for spec in specs:
        year, binary, block, index, output = spec
        path = directory / name(spec); source = output == 'source'; value = VALUES[index]
        transport = binary if source else output == 'True'
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == transport, 'Wrong transport')
        tags = load_tags(path)
        positions = packet(tags, year, value, source)
        independent(path, year, value, source, block)
        for at in positions:
            for operation in ('zero', 'remove', 'duplicate', 'wrong-group'):
                damaged = list(tags)
                if operation == 'zero': damaged[at] = (damaged[at][0], 0.)
                elif operation == 'remove': del damaged[at]
                elif operation == 'duplicate': damaged.insert(at, damaged[at])
                else: damaged[at] = (1071, damaged[at][1])
                corruptions += reject(lambda: packet(damaged, year, value, source))
    print(f'PASS: {len(specs)} DIMLFAC drawings, physical and independently loaded style/header/override bits; '
          f'{corruptions} actual-tag corruptions and two inventory controls rejected; zero graph errors/repairs. '
          'Stored-scale checks do not certify dimension rendering at extreme scales.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_dimlfac_fidelity.py ARTIFACTS')
    main(Path(sys.argv[1]))
