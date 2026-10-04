#!/usr/bin/env python3
"""Validate actual C# block/attribute/selection fixtures, never output-derived expectations."""
from collections import Counter
from pathlib import Path
import itertools
import math
import sys
import ezdxf
from ezdxf.tools.text import caret_decode
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import load_tags, key, require, reject, audit_signature
from verify_r12_primitives import point, expected as primitive_packet, KINDS

STAGES = ('source', 'output', 'resave')
VERSIONS = dict(zip(('AutoCad12', 'AutoCad2000', 'AutoCad2004', 'AutoCad2007', 'AutoCad2010', 'AutoCad2013', 'AutoCad2018'),
                    ('AC1009', 'AC1015', 'AC1018', 'AC1021', 'AC1024', 'AC1027', 'AC1032')))


def entity(kind, handle, layer='0', pattern='BYLAYER', color=256):
    return [(0, kind), (5, f'{handle:X}'), (8, layer), (6, pattern), (62, color)]


def insert(handle, name, position, scale=(1., 1., 1.), rotation=0., columns=1, rows=1,
           column_spacing=0., row_spacing=0., color=256, pattern='BYLAYER', attributes=False):
    tags = entity('INSERT', handle, pattern=pattern, color=color)
    if attributes:
        tags += [(66, 1)]
    return tags + [(2, name)] + point(10, position) + [(41, scale[0]), (42, scale[1]), (43, scale[2]),
        (50, rotation), (70, columns), (71, rows), (44, column_spacing), (45, row_spacing)] + point(210, (0., 0., 1.))


def block(handle, name, origin, layer='BLOCK_ONLY', flags=0):
    return [(0, 'BLOCK'), (5, f'{handle:X}'), (8, layer), (2, name), (70, flags)] + point(10, origin) + [(3, name), (1, '')]


def endblock(handle, layer='BLOCK_ONLY'):
    return [(0, 'ENDBLK'), (5, f'{handle:X}'), (8, layer)]


def primitive_body(start):
    tags = primitive_packet(0)
    index = next(i for i in range(len(tags)-1) if tags[i:i+2] == [(0, 'SECTION'), (2, 'ENTITIES')]) + 2
    return [(code, f'{start + int(value, 16) - 256:X}' if code == 5 else value) for code, value in tags[index:-2]]


def text_fields(value, position, normal, height, width, rotation, oblique, backward=2):
    # MiddleCenter uses independent group 72=1 / group 74=2, not TEXT group 73.
    return [(1, value)] + point(10, position) + [(40, height), (41, width), (50, rotation), (51, oblique), (7, 'ATTR_STYLE')] \
        + point(11, position) + point(210, normal) + [(71, backward), (72, 1), (74, 2)]


def attribute(handle, edited=False):
    return entity('ATTRIB', handle, 'ATTR_ONLY', 'BYBLOCK', 5) + [(2, 'IDENTIFIER'), (70, 5), (73, 0)] \
        + text_fields('Edited café' if edited else 'Actual café^I^ ^Jvalue',
                      (-6., 7., -8.) if edited else (4., 8., -12.), (0., 0., -1.), 3.5, 1.2, 133., 17.)


def definition(handle):
    return entity('ATTDEF', handle, 'ATTR_ONLY', 'BYBLOCK', 5) + [(2, 'IDENTIFIER'), (3, 'Café ^  prompt^Ientry'), (70, 5), (73, 0)] \
        + text_fields('Default ^  text', (9., 10., 8.), (1., 0., 0.), 2.5, .9, 32., -13.)


def attribute_block(start):
    return block(start, 'ATTR_BLOCK', (3., 4., 5.), '0', 2) + definition(start+1) \
        + entity('LINE', start+2) + point(10, (0., 0., 0.)) + point(11, (1., 0., 0.)) \
        + [(39, 0.)] + point(210, (0., 0., 1.)) + endblock(start+3, '0')


def outer_block(start):
    return block(start, 'OUTER', (-2., 7., 1.)) \
        + insert(start+1, 'LEAF', (5., 6., 7.), (2., -3., .5), 30.) \
        + insert(start+2, 'LEAF', (-5., 8., 2.), rotation=170., columns=3, rows=2, column_spacing=-7., row_spacing=9.) \
        + endblock(start+3)


def leaf_block(start):
    return block(start, 'LEAF', (2., 3., 4.)) + primitive_body(start+1) + endblock(start+8)


def prefix(handseed, graph, attrs):
    layers = [('0', 0, 7)]
    if graph:
        layers.append(('BLOCK_ONLY', 0, 3))
    if attrs:
        layers.append(('ATTR_ONLY', 2, 4))
    if graph:
        layers.append(('SHAPES', 5, 2))
    tags = [(0, 'SECTION'), (2, 'HEADER'), (9, '$ACADVER'), (1, 'AC1009'), (9, '$DWGCODEPAGE'), (3, 'ANSI_1252'),
            (9, '$HANDSEED'), (5, f'{handseed:X}'), (0, 'ENDSEC'), (0, 'SECTION'), (2, 'TABLES'),
            (0, 'TABLE'), (2, 'LTYPE'), (70, 1), (0, 'LTYPE'), (2, 'CONTINUOUS'), (70, 0),
            (3, 'Solid line'), (72, 65), (73, 0), (40, 0.), (0, 'ENDTAB'), (0, 'TABLE'), (2, 'LAYER'), (70, len(layers))]
    for name, flags, color in layers:
        tags += [(0, 'LAYER'), (2, name), (70, flags), (62, color), (6, 'CONTINUOUS')]
    tags += [(0, 'ENDTAB')]
    if attrs:
        tags += [(0, 'TABLE'), (2, 'STYLE'), (70, 2)]
        for name, width, oblique, font in [('Standard', 1., 0., 'simplex.shx'), ('ATTR_STYLE', .8, 5., 'txt.shx')]:
            tags += [(0, 'STYLE'), (2, name), (70, 0), (40, 0.), (41, width), (50, oblique), (71, 0), (3, font), (4, '')]
        tags += [(0, 'ENDTAB')]
    return tags + [(0, 'ENDSEC'), (0, 'SECTION'), (2, 'BLOCKS')]


def expected(family, stage='source'):
    graph, attrs = family != 'attributes', family != 'blocks'
    roots, bodies = [], []
    next_handle = 256
    if graph:
        roots += insert(next_handle, 'OUTER', (13., 24., 35.) if family == 'blocks' and stage != 'source' else (10., 20., 30.),
                        rotation=75., columns=4, rows=3, column_spacing=-12.5, row_spacing=8.25)
        roots += insert(next_handle+1, 'OUTER', (40., 50., 60.), color=6, pattern='BYBLOCK')
        next_handle += 2
    if attrs:
        roots += insert(next_handle, 'ATTR_BLOCK', (20., 30., 40.), (-2., 3., .5), 71., 3, 2, -7., 9., attributes=True)
        roots += attribute(next_handle+1, family == 'attributes' and stage != 'source')
        roots += [(0, 'SEQEND'), (5, f'{next_handle+2:X}'), (8, '0')]
        next_handle += 3
    if graph:
        bodies += outer_block(next_handle)
        next_handle += 4
    if attrs:
        bodies += attribute_block(next_handle)
        next_handle += 4
    if graph:
        bodies += leaf_block(next_handle)
        next_handle += 9
    return prefix(next_handle, graph, attrs) + bodies + [(0, 'ENDSEC'), (0, 'SECTION'), (2, 'ENTITIES')] + roots + [(0, 'ENDSEC'), (0, 'EOF')]


def check_packet(wanted, actual):
    require(len(wanted) == len(actual), f'Packet length {len(actual)} != {len(wanted)}')
    for index, (a, b) in enumerate(zip(wanted, actual)):
        require(key(a) == key(b), f'Physical packet mismatch at {index}: {a!r} != {b!r}')


def near(a, b, label):
    if isinstance(a, (tuple, list)):
        require(len(a) == len(b), label + ' cardinality')
        for x, y in zip(a, b):
            near(x, y, label)
    else:
        require(math.isfinite(float(b)) and math.isclose(float(a), float(b), rel_tol=2e-11, abs_tol=2e-11), label)


def check_insert(value, name, position, scale=(1., 1., 1.), rotation=0., columns=1, rows=1, spacing=(0., 0.)):
    require(value.dxftype() == 'INSERT' and value.dxf.name == name, 'Typed block reference')
    near(position, tuple(value.ocs().to_wcs(value.dxf.insert)), 'INSERT WCS')
    near(scale, (value.dxf.xscale, value.dxf.yscale, value.dxf.zscale), 'INSERT scale')
    near(rotation, value.dxf.rotation, 'INSERT rotation')
    require(value.dxf.column_count == columns and value.dxf.row_count == rows, 'Array cardinality')
    near(spacing, (value.dxf.column_spacing, value.dxf.row_spacing), 'Array spacing')


def check_graph(doc, edited=False):
    roots = [e for e in doc.modelspace() if e.dxftype() == 'INSERT' and e.dxf.name == 'OUTER']
    require(len(roots) == 2, 'Shared OUTER root inventory')
    check_insert(roots[0], 'OUTER', (13., 24., 35.) if edited else (10., 20., 30.), rotation=75., columns=4, rows=3, spacing=(-12.5, 8.25))
    check_insert(roots[1], 'OUTER', (40., 50., 60.))
    require(roots[1].dxf.color == 6 and roots[1].dxf.linetype.upper() == 'BYBLOCK', 'Root appearance')
    outer, leaf = doc.blocks.get('OUTER'), doc.blocks.get('LEAF')
    near((-2., 7., 1.), tuple(outer.block.dxf.base_point), 'OUTER origin')
    near((2., 3., 4.), tuple(leaf.block.dxf.base_point), 'LEAF origin')
    children = list(outer)
    require(len(children) == 2, 'Nested block inventory')
    check_insert(children[0], 'LEAF', (5., 6., 7.), (2., -3., .5), 30.)
    check_insert(children[1], 'LEAF', (-5., 8., 2.), rotation=170., columns=3, rows=2, spacing=(-7., 9.))
    values = list(leaf)
    require(tuple(e.dxftype() for e in values) == KINDS, 'Leaf inventory')
    require(all(e.dxf.layer == 'SHAPES' and e.dxf.color == i+1 for i, e in enumerate(values)), 'Leaf common fields')
    near((0., 2., 3.), tuple(values[0].dxf.start), 'Leaf LINE start'); near((4., 5., 6.), tuple(values[0].dxf.end), 'Leaf LINE end')
    near(-2., values[0].dxf.thickness, 'Leaf LINE thickness')
    near((7., 8., 9.), tuple(values[1].dxf.location), 'Leaf POINT'); near(330., values[1].dxf.angle, 'Leaf POINT angle')
    for i, xyz, radius in ((2, (10., 20., 30.), 2.), (3, (11., 21., 31.), 3.)):
        near(xyz, tuple(values[i].ocs().to_wcs(values[i].dxf.center)), 'Leaf curve center'); near(radius, values[i].dxf.radius, 'Leaf curve radius')
    near(15., values[3].dxf.start_angle, 'Leaf ARC start'); near(275., values[3].dxf.end_angle, 'Leaf ARC end')
    require(values[4].dxf.invisible_edges == 5, 'Leaf face flags')
    for i, points in ((4, ((0.,0.,0.),(2.,0.,1.),(2.,3.,2.),(0.,3.,1.))),
                      (5, ((0.,0.,4.),(2.,0.,4.),(0.,3.,4.),(2.,3.,4.))),
                      (6, ((0.,0.,-2.),(2.,0.,-2.),(0.,3.,-2.),(2.,3.,-2.)))):
        for j, xyz in enumerate(points):
            near(xyz, tuple(getattr(values[i].dxf, f'vtx{j}')), 'Leaf corner')
    require(doc.layers.get('SHAPES').dxf.flags == 5 and doc.layers.get('SHAPES').dxf.color == 2, 'Selected layer settings')


def content(value, version):
    # R12 selections encode logical controls with caret pairs. The modern
    # plan protects literal backslashes and uses one-pass DXF Unicode escapes.
    # Never caret-decode after Unicode decoding: an escaped literal ^J must
    # remain two literal characters, not turn into a line feed.
    return caret_decode(value) if version == 'AC1009' else decode_dxf_unicode(value)


def check_attributes(doc, edited=False):
    roots = [e for e in doc.modelspace() if e.dxftype() == 'INSERT' and e.dxf.name == 'ATTR_BLOCK']
    require(len(roots) == 1, 'Attribute root inventory')
    root = roots[0]
    check_insert(root, 'ATTR_BLOCK', (20., 30., 40.), (-2., 3., .5), 71., 3, 2, (-7., 9.))
    require(len(root.attribs) == 1 and root.seqend is not None, 'Attribute sequence not preserved')
    attr = root.attribs[0]
    definitions = list(doc.blocks.get('ATTR_BLOCK').query('ATTDEF'))
    require(len(definitions) == 1, 'Definition inventory')
    definition = definitions[0]
    for value, xyz, height, width, rotation, oblique, text in (
        (definition, (8.,9.,10.), 2.5, .9, 32., -13., 'Default ^ text'),
        (attr, (6.,7.,8.) if edited else (-4.,8.,12.), 3.5, 1.2, 133., 17., 'Edited café' if edited else 'Actual café\t^\nvalue')):
        require(value.dxf.tag == 'IDENTIFIER' and value.dxf.flags == 5, 'Attribute tag/flags')
        require(value.dxf.halign == 1 and value.dxf.valign == 2, 'Attribute alignment')
        require(value.dxf.style == 'ATTR_STYLE' and value.dxf.layer == 'ATTR_ONLY' and value.dxf.color == 5, 'Attribute resources')
        near(xyz, tuple(value.ocs().to_wcs(value.dxf.align_point)), 'Attribute WCS anchor')
        near(height, value.dxf.height, 'Attribute height'); near(width, value.dxf.width, 'Attribute width factor')
        near(rotation, value.dxf.rotation, 'Attribute rotation'); near(oblique, value.dxf.oblique, 'Attribute oblique')
        require(content(value.dxf.text, doc.dxfversion) == text, 'Attribute value')
    require(content(definition.dxf.prompt, doc.dxfversion) == 'Café ^ prompt\tentry', 'Attribute prompt')
    require(doc.layers.get('ATTR_ONLY').dxf.flags == 2, 'Attribute-only layer defaults')
    style = doc.styles.get('ATTR_STYLE')
    require(style.dxf.font == 'txt.shx', 'Attribute font reference')
    near(.8, style.dxf.width, 'Attribute style width'); near(5., style.dxf.oblique, 'Attribute style oblique')


def integrity_text():
    return ''.join(chr(i) for i in range(32)) + r'|\U+0041|\U+000A|\u+00e9|^J|^ |\\|%%d|café|' + '\x7f'


def check_integrity(doc):
    roots = list(doc.modelspace())
    require([e.dxftype() for e in roots] == ['TEXT', 'INSERT'], 'Integrity root inventory/order')
    outer, leaf = doc.blocks.get('RAW_OUTER'), doc.blocks.get('RAW_LEAF')
    require([e.dxftype() for e in outer] == ['INSERT', 'TEXT'], 'Integrity outer inventory')
    require([e.dxftype() for e in leaf] == ['ATTDEF', 'TEXT'], 'Integrity leaf inventory')
    nested, outer_text = list(outer)
    definition, leaf_text = list(leaf)
    require(len(nested.attribs) == 1 and nested.seqend is not None, 'Integrity attribute sequence')
    check_insert(roots[1], 'RAW_OUTER', (0., 0., 0.))
    check_insert(nested, 'RAW_LEAF', (7., 8., 9.))
    attr = nested.attribs[0]
    for value, prefix, xyz in ((roots[0], 'root:', (13., 14., 15.)),
                              (outer_text, 'outer:', (10., 11., 12.)),
                              (leaf_text, 'leaf:', (4., 5., 6.)),
                              (definition, 'default:', (1., 2., 3.)),
                              (attr, 'instance:', (16., 17., 18.))):
        require(content(value.dxf.text, doc.dxfversion) == prefix + integrity_text(), 'Integrity logical text')
        require(not any(ord(c) < 32 for c in value.dxf.text), 'Unescaped physical C0 value')
        require(r'\U+005CU+0041' in value.dxf.text and r'\U+005EJ' in value.dxf.text, 'Unprotected literal escape')
        near(xyz, tuple(value.dxf.insert), 'Integrity text position')
        near(2., value.dxf.height, 'Integrity text height')
        require(value.dxf.style.lower() == 'standard', 'Integrity text style')
    require(content(definition.dxf.prompt, doc.dxfversion) == 'prompt:' + integrity_text(), 'Integrity prompt')
    require(definition.dxf.tag == attr.dxf.tag == 'VALUE', 'Integrity attribute binding')
    require(doc.styles.get('Standard').dxf.font == 'txt.shx', 'Integrity default font reference')
    require(content(doc.linetypes.get('LITERAL').dxf.description, doc.dxfversion) == r'Pattern café \U+0041 ^J', 'Integrity literal resource description')
    require(roots[0].dxf.linetype == 'LITERAL', 'Integrity root linetype reference')


def inventory(directory):
    names = {f'r12-{family}-{transport}-{stage}.dxf' for family, transport, stage in
             itertools.product(('blocks', 'attributes'), ('text', 'binary'), STAGES)}
    names |= {f'r12-selection-{version}-{transport}.dxf' for version, transport in itertools.product(VERSIONS, ('text', 'binary'))}
    names |= {f'r12-plan-integrity-{version}-{transport}.dxf' for version, transport in itertools.product(tuple(VERSIONS)[1:], ('text', 'binary'))}
    actual = {p.name for pattern in ('r12-blocks-*.dxf', 'r12-attributes-*.dxf', 'r12-selection-*.dxf', 'r12-plan-integrity-*.dxf') for p in directory.glob(pattern)}
    require(actual == names, f'Graph fixture inventory differs: missing={names-actual}, extra={actual-names}')
    return names


def main(directory):
    names = inventory(directory)
    rejected = 0
    for name in sorted(names):
        path = directory / name
        family = name.split('-')[1]
        stage = next((s for s in STAGES if name.endswith('-' + s + '.dxf')), 'source')
        version = next((v for v in VERSIONS if f'-{v}-' in name), 'AutoCad12')
        binary = ('-binary' in name) != (stage == 'output')
        require(path.read_bytes().startswith(b'AutoCAD Binary DXF') == binary, 'Graph transport')
        doc = ezdxf.readfile(path)
        require(doc.dxfversion == VERSIONS[version], 'Graph target family')
        if family == 'plan':
            check_integrity(doc)
            require(not any(any(values.values()) for values in audit_signature(doc)), 'Integrity audit errors or repairs')
            continue
        require(len(list(doc.modelspace())) == (3 if family == 'selection' else 2 if family == 'blocks' else 1), 'Root inventory')
        if family != 'attributes':
            check_graph(doc, family == 'blocks' and stage != 'source')
        if family != 'blocks':
            check_attributes(doc, family == 'attributes' and stage != 'source')
        require(not any(any(values.values()) for values in audit_signature(doc)), 'Independent audit errors or repairs')
        if version == 'AutoCad12':
            wanted, actual = expected(family, stage), load_tags(path)
            check_packet(wanted, actual)
            for index, (code, value) in enumerate(actual):
                for mode in ('remove', 'duplicate', 'change'):
                    damaged = list(actual)
                    if mode == 'remove': damaged.pop(index)
                    elif mode == 'duplicate': damaged.insert(index, damaged[index])
                    else: damaged[index] = (code, value + '_bad' if isinstance(value, str) else value + 1)
                    rejected += reject(lambda: check_packet(wanted, damaged))
    print(f'PASS: {len(names)} actual C# graph drawings; complete R12 packets, typed seven-family graphs, '
          f'{rejected} packet corruptions rejected and zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_r12_selection_graphs.py ARTIFACTS')
    main(Path(sys.argv[1]))
