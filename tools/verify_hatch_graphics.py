#!/usr/bin/env python3
"""Verify HATCH cache presence independently from complete boundary packets."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.entities.boundary_paths import PolylinePath
from verify_raw_line_geometry import key, require, reject
from verify_ellipse_axis_proxies import load_visibility_tags, VERSIONS
from verify_dimlfac_fidelity import records

PROXY = bytes((i * 37) & 255 for i in range(300))


def geometry(row, polyline):
    elevation = 5. if row in (5, 8) else 15.5 if row in (6, 7) else 2.5
    pattern = 'LINE' if row == 9 else 'SOLID'
    offsets = [0., 10.] if row == 10 else [3.] if row in (12, 13) else [0.]
    loops = []
    for offset in offsets:
        points = [(offset, 0.), (offset + 4., 0.), (offset + 4., 3.), (offset, 3.)]
        if row in (6, 7): points = [(x + 7., y - 11.) for x, y in points]
        if row == 8: points = [(2. * x, 2. * y) for x, y in points]
        loops.append(points)
    seed = (9., -10.) if row in (6, 7) else (4., 2.) if row == 8 else (2., 1.)
    tags = [(10, 0.), (20, 0.), (30, elevation), (210, 0.), (220, 0.), (230, 1.),
            (2, pattern), (70, 0 if row == 9 else 1), (71, 0), (91, len(loops))]
    for points in loops:
        if polyline:
            tags += [(92, 7), (72, 1), (73, 1), (93, 4)]
            for x, y in points: tags += [(10, x), (20, y), (42, 0.)]
        else:
            tags += [(92, 5), (93, 4)]
            for i, (x, y) in enumerate(points):
                x2, y2 = points[(i + 1) % 4]
                tags += [(72, 1), (10, x), (20, y), (11, x2), (21, y2)]
        tags += [(97, 0)]
    tags += [(75, 0), (76, 1)]
    if row == 9:
        tags += [(52, 0.), (41, 1.), (77, 0), (78, 1), (53, 0.), (43, 0.),
                 (44, 0.), (45, 0.), (46, .125), (79, 0)]
    tags += [(47, .0625), (98, 1), (10, seed[0]), (20, seed[1]),
             (1001, 'HATCH_GRAPHICS_KEEP'), (1000, 'unchanged'),
             (1001, 'ACAD'), (1010, 0.), (1020, 0.), (1030, 0.)]
    return tags, elevation, pattern, loops, seed


def packet(record, row, polyline, version):
    require(record[0] == (0, 'HATCH'), 'Entity type')
    common_at = [i for i, t in enumerate(record) if t == (100, 'AcDbEntity')]
    hatch_at = [i for i, t in enumerate(record) if t == (100, 'AcDbHatch')]
    require(len(common_at) == len(hatch_at) == 1 and common_at[0] < hatch_at[0], 'Subclass framing')
    start, end = common_at[0] + 1, hatch_at[0]
    common = record[start:end]
    require([t for t in common if t[0] == 8] == [(8, f'HG_{row:02d}')], 'Layer identity')
    require([t for t in common if t[0] == 62] == [(62, 4)], 'Color')
    length_code = 160 if version in ('AutoCad2013', 'AutoCad2018') else 92
    proxies = [t for t in common if t[0] in (92, 160, 310)]
    expected = [] if row >= 5 else [(length_code, len(PROXY))] + [(310, PROXY[i:i + 127]) for i in range(0, len(PROXY), 127)]
    require(proxies == expected, 'Common proxy presence, length, framing or bytes')
    wanted = geometry(row, polyline)[0]
    require([key(t) for t in record[end + 1:]] == [key(t) for t in wanted], 'Complete HATCH geometry/topology/seeds/XData')
    return end + 1, [i for i in range(start, end) if record[i][0] in (92, 160, 310)]


def inspect(path, version, binary, polyline, placement):
    require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Transport')
    tags = load_visibility_tags(path)
    av = [i for i, t in enumerate(tags) if t == (9, '$ACADVER')]
    require(len(av) == 1 and tags[av[0] + 1] == (1, VERSIONS[version]), 'Version')
    entries = [tags[a:b] for a, b in records(tags)]
    hatches = [r for r in entries if r[0] == (0, 'HATCH')]
    require(len(hatches) == 14, 'Hatch inventory')
    require({next(v for c, v in r if c == 8) for r in hatches} == {f'HG_{i:02d}' for i in range(14)}, 'Hatch row inventory')
    controls = 0
    for record in hatches:
        row = int(next(v for c, v in record if c == 8).split('_')[1])
        start, proxy_positions = packet(record, row, polyline, version)
        for at in list(range(start, len(record))) + proxy_positions:
            for operation in ('change', 'remove', 'duplicate', 'wrong-code'):
                bad = list(record); code, value = bad[at]
                if operation == 'change':
                    bad[at] = (code, value + b'!' if isinstance(value, bytes) else value + '_BAD' if isinstance(value, str) else value + 1)
                elif operation == 'remove': del bad[at]
                elif operation == 'duplicate': bad.insert(at, bad[at])
                else: bad[at] = (999, value)
                controls += reject(lambda: packet(bad, row, polyline, version))
        if row >= 5:
            bad = record[:start - 1] + [(92, 1), (310, b'X')] + record[start - 1:]
            controls += reject(lambda: packet(bad, row, polyline, version))
    doc = ezdxf.readfile(path)
    require(doc.dxfversion == VERSIONS[version], 'Independent version')
    space = doc.modelspace() if placement == 0 else doc.layouts.get('HG_PAPER') if placement == 1 else doc.blocks['HG_HOLDER']
    loaded = list(space.query('HATCH'))
    require(len(loaded) == 14, 'Independent placement/inventory')
    for hatch in loaded:
        row = int(hatch.dxf.layer.split('_')[1]); _, elevation, pattern, loops, seed = geometry(row, polyline)
        require(hatch.dxf.owner == space.block_record_handle, 'Independent owner')
        require(hatch.proxy_graphic == (PROXY if row < 5 else None), 'Independent proxy state')
        require(tuple(hatch.dxf.elevation) == (0., 0., elevation) and tuple(hatch.dxf.extrusion) == (0., 0., 1.), 'Independent OCS plane')
        require(hatch.dxf.pattern_name == pattern and hatch.dxf.color == 4, 'Independent appearance')
        require(len(hatch.paths) == len(loops) and list(map(tuple, hatch.seeds)) == [seed], 'Independent topology/seeds')
        for boundary, wanted in zip(hatch.paths, loops):
            require(isinstance(boundary, PolylinePath) == polyline, 'Independent boundary representation')
            if polyline:
                require(boundary.is_closed and list(boundary.vertices) == [(x, y, 0.) for x, y in wanted], 'Independent polyline vertices/bulges')
            else:
                require(len(boundary.edges) == 4, 'Independent edge count')
                for i, edge in enumerate(boundary.edges):
                    require(edge.EDGE_TYPE == 'LineEdge' and tuple(edge.start) == wanted[i] and tuple(edge.end) == wanted[(i + 1) % 4], 'Independent line geometry')
        require([(t.code, t.value) for t in hatch.get_xdata('HATCH_GRAPHICS_KEEP')] == [(1000, 'unchanged')], 'Unrelated XData')
    line, = doc.modelspace().query('LINE')
    require(tuple(line.dxf.start) == (17.25, -4.5, 2.) and tuple(line.dxf.end) == (18.5, 9.25, -3.), 'Following geometry')
    inserts = list(doc.modelspace().query('INSERT'))
    require(len(inserts) == (1 if placement == 2 else 0), 'Block instantiation policy')
    audit = doc.audit(); require(not audit.errors and not audit.fixes, 'Independent graph errors or repairs')
    return controls


def main(directory):
    specs = list(itertools.product(VERSIONS, (False, True), (False, True), range(4), ('source', 'False', 'True')))
    def name(spec):
        version, binary, polyline, placement, output = spec
        return f'hatch-graphics-{version}-{binary}-{polyline}-{placement}-{output}.dxf'
    expected = {name(spec) for spec in specs}
    def inventory(actual): require(actual == expected, 'Missing or extra hatch-graphics fixtures')
    inventory({p.name for p in directory.glob('hatch-graphics-*.dxf')})
    reject(lambda: inventory(expected - {min(expected)}))
    reject(lambda: inventory(expected | {'hatch-graphics-extra.dxf'}))
    controls = 0
    for spec in specs:
        version, binary, polyline, placement, output = spec
        controls += inspect(directory / name(spec), version, binary if output == 'source' else output == 'True', polyline, placement)
    print(f'PASS: {len(specs)} HATCH drawings / {14 * len(specs)} records; {controls} actual-packet corruptions '
          'and two inventory controls rejected; zero independent graph errors/repairs. Cache invalidation is not native hatch rendering.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_hatch_graphics.py ARTIFACTS')
    main(Path(sys.argv[1]))
