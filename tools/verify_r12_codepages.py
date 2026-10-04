#!/usr/bin/env python3
"""Independent code-page bytes, complete R12 packets and typed seven-family selections."""
import io
import itertools
from pathlib import Path
import struct
import sys
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader
from ezdxf.lldxf.types import cast_tag_value
from ezdxf.tools.text import caret_decode
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import require, reject, audit_signature, key
from verify_r12_primitives import point

PROFILES = {
    874: 'ภาษาไทย', 932: '日本語', 936: '中文简体', 949: '한국어', 950: '繁體中文',
    1250: 'Zażółć gęślą jaźń', 1251: 'Привет мир', 1252: 'Café déjà vu', 1253: 'Ελληνικά',
    1254: 'İstanbul ı ş ğ', 1255: 'שלום', 1256: 'مرحبا', 1257: 'Āžu čūska', 1258: 'Viê\u0323t'
}
VERSIONS = {'AutoCad2000': 'AC1015', 'AutoCad2004': 'AC1018', 'AutoCad2007': 'AC1021',
            'AutoCad2010': 'AC1024', 'AutoCad2013': 'AC1027', 'AutoCad2018': 'AC1032'}
STAGES = ('source', 'output', 'resave')
MAGIC = b'AutoCAD Binary DXF\r\n\x1a\0'


def logical(role, text):
    return role + ':' + text + '\t^J\n\0\\U+0041'


def description(text):
    return text + r' | literal \U+0041 ^J'


def caret_encode(text):
    return ''.join('^' + chr(ord(c) + 64) if ord(c) < 32 else '^ ' if c == '^' else c for c in text)


def common(kind, handle, pattern='BYLAYER'):
    return [(0, kind), (5, f'{handle:X}'), (8, 'NATIVE'), (6, pattern), (62, 256)]


def text_fields(role, text, xyz, vertical=73):
    return [(1, caret_encode(logical(role, text)))] + point(10, xyz) + [
        (40, 2.), (41, 1.), (50, 0.), (51, 0.), (7, 'ENC_STYLE')
    ] + point(210, (0, 0, 1)) + [(71, 0), (72, 0), (vertical, 0)]


def expected(page):
    text = PROFILES[page]
    tags = [(0, 'SECTION'), (2, 'HEADER'), (9, '$ACADVER'), (1, 'AC1009'),
            (9, '$DWGCODEPAGE'), (3, f'ANSI_{page}'), (9, '$HANDSEED'), (5, '109'),
            (0, 'ENDSEC'), (0, 'SECTION'), (2, 'TABLES'), (0, 'TABLE'), (2, 'LTYPE'), (70, 2)]
    for name, desc in [('CONTINUOUS', 'Solid line'), ('NATIONAL', description(text))]:
        tags += [(0, 'LTYPE'), (2, name), (70, 0), (3, desc), (72, 65), (73, 0), (40, 0.)]
    tags += [(0, 'ENDTAB'), (0, 'TABLE'), (2, 'LAYER'), (70, 2)]
    for name, flags, color in [('0', 0, 7), ('NATIVE', 2, 2)]:
        tags += [(0, 'LAYER'), (2, name), (70, flags), (62, color), (6, 'CONTINUOUS')]
    tags += [(0, 'ENDTAB'), (0, 'TABLE'), (2, 'STYLE'), (70, 2)]
    for name, font in [('Standard', 'simplex.shx'), ('ENC_STYLE', 'txt.shx')]:
        tags += [(0, 'STYLE'), (2, name), (70, 0), (40, 0.), (41, 1.), (50, 0.), (71, 0), (3, font), (4, '')]
    tags += [(0, 'ENDTAB'), (0, 'ENDSEC'), (0, 'SECTION'), (2, 'BLOCKS'),
             (0, 'BLOCK'), (5, '104'), (8, 'NATIVE'), (2, 'NATIVE_BLOCK'), (70, 2)]
    tags += point(10, (0, 0, 0)) + [(3, 'NATIVE_BLOCK'), (1, '')]
    tags += common('ATTDEF', 0x105) + [(2, 'VALUE'), (3, caret_encode(logical('prompt', text))), (70, 5), (73, 0)]
    tags += text_fields('default', text, (4, 5, 6), 74)
    tags += common('TEXT', 0x106) + text_fields('nested', text, (7, 8, 9))
    tags += common('LINE', 0x107) + point(10, (0, 0, 0)) + point(11, (1, 0, 0)) + [(39, 0.)] + point(210, (0, 0, 1))
    tags += [(0, 'ENDBLK'), (5, '108'), (8, 'NATIVE'), (0, 'ENDSEC'), (0, 'SECTION'), (2, 'ENTITIES')]
    tags += common('TEXT', 0x100, 'NATIONAL') + text_fields('root', text, (1, 2, 3))
    tags += common('INSERT', 0x101) + [(66, 1), (2, 'NATIVE_BLOCK')] + point(10, (10, 20, 30))
    tags += [(41, 1.), (42, 1.), (43, 1.), (50, 0.), (70, 1), (71, 1), (44, 0.), (45, 0.)] + point(210, (0, 0, 1))
    tags += common('ATTRIB', 0x102) + [(2, 'VALUE'), (70, 5), (73, 0)] + text_fields('instance', text, (14, 15, 16), 74)
    return tags + [(0, 'SEQEND'), (5, '103'), (8, 'NATIVE'), (0, 'ENDSEC'), (0, 'EOF')]


def binary_packet(tags, page):
    # All fields in this independently specified R12 profile use one-byte group codes.
    result = bytearray(MAGIC)
    for code, value in tags:
        require(0 <= code < 255, 'Oracle group-code range')
        result.append(code)
        if isinstance(value, str):
            result.extend(value.encode(f'cp{page}', errors='strict')); result.append(0)
        elif isinstance(value, float):
            result.extend(struct.pack('<d', value))
        else:
            result.extend(struct.pack('<h', value))
    return bytes(result)


def load_r12_tags(data, page, binary):
    if not binary:
        tags = ascii_tags_loader(io.StringIO(data.decode(f'cp{page}', errors='strict'), newline=None))
        return [(t.code, cast_tag_value(t.code, t.value)) for t in tags]
    require(data.startswith(MAGIC), 'R12 binary signature')
    result = []
    offset = len(MAGIC)
    while offset < len(data):
        code = data[offset]; offset += 1
        if code in range(10, 60) or code in range(210, 240):
            require(offset + 8 <= len(data), 'Truncated R12 double')
            value = struct.unpack_from('<d', data, offset)[0]; offset += 8
        elif code in range(60, 80):
            require(offset + 2 <= len(data), 'Truncated R12 integer')
            value = struct.unpack_from('<h', data, offset)[0]; offset += 2
        elif 0 <= code <= 9:
            end = data.find(b'\0', offset)
            require(end >= offset, 'Unterminated R12 string')
            value = data[offset:end].decode(f'cp{page}', errors='strict'); offset = end + 1
        else:
            raise ValueError(f'Unexpected code {code} in independent R12 profile')
        result.append((code, value))
    return result


def check_packet(wanted, actual):
    require(len(actual) == len(wanted), 'R12 code-page packet cardinality')
    for i, (a, b) in enumerate(zip(wanted, actual)):
        require(key(a) == key(b), f'R12 code-page packet field {i}: {a!r} != {b!r}')


def decode_content(value, legacy):
    return caret_decode(value) if legacy else decode_dxf_unicode(value)


def check_document(doc, page, version):
    require(doc.dxfversion == version, 'Independent target family')
    require(doc.header.get('$DWGCODEPAGE') == f'ANSI_{page}', 'Independent encoding declaration')
    text = PROFILES[page]
    roots = list(doc.modelspace())
    require([e.dxftype() for e in roots] == ['TEXT', 'INSERT'], 'Root inventory/order')
    root, insert = roots
    require(insert.dxf.name == 'NATIVE_BLOCK', 'Root block reference')
    require(tuple(insert.dxf.insert) == (10., 20., 30.), 'Root block location')
    require(len(insert.attribs) == 1 and insert.seqend is not None, 'Attribute sequence inventory')
    block = doc.blocks.get('NATIVE_BLOCK')
    members = list(block)
    require([e.dxftype() for e in members] == ['ATTDEF', 'TEXT', 'LINE'], 'Block inventory/order')
    definition, nested, line = members
    attribute = insert.attribs[0]
    legacy = version == 'AC1009'
    for entity, role, xyz in [(root, 'root', (1., 2., 3.)), (definition, 'default', (4., 5., 6.)),
                              (nested, 'nested', (7., 8., 9.)), (attribute, 'instance', (14., 15., 16.))]:
        require(decode_content(entity.dxf.text, legacy) == logical(role, text), 'Independent logical text ' + role)
        require(tuple(entity.dxf.insert) == xyz and entity.dxf.height == 2., 'Text geometry ' + role)
        require(entity.dxf.layer == 'NATIVE' and entity.dxf.style == 'ENC_STYLE', 'Text resources ' + role)
    require(decode_content(definition.dxf.prompt, legacy) == logical('prompt', text), 'Independent attribute prompt')
    require(definition.dxf.tag == attribute.dxf.tag == 'VALUE', 'Attribute tag binding')
    require(definition.dxf.flags == attribute.dxf.flags == 5, 'Attribute flags')
    require(tuple(line.dxf.start) == (0., 0., 0.) and tuple(line.dxf.end) == (1., 0., 0.), 'Nested line geometry')
    layer = doc.layers.get('NATIVE')
    require(layer.dxf.flags == 2 and layer.dxf.color == 2, 'Selected layer state')
    require(doc.styles.get('ENC_STYLE').dxf.font == 'txt.shx', 'Stored font reference')
    desc = doc.linetypes.get('NATIONAL').dxf.description
    require((desc if legacy else decode_dxf_unicode(desc)) == description(text), 'Independent linetype description')
    require(root.dxf.linetype == 'NATIONAL', 'Root linetype reference')
    require(not any(any(c.values()) for c in audit_signature(doc)), 'Independent audit errors or repairs')


def inventory(directory):
    names = {f'r12-codepage-{p}-{t}-{s}.dxf' for p, t, s in itertools.product(PROFILES, ('text', 'binary'), STAGES)}
    names |= {f'r12-codepage-modern-{p}-{v}-{t}.dxf' for p, v, t in itertools.product(PROFILES, VERSIONS, ('text', 'binary'))}
    actual = {p.name for p in directory.glob('r12-codepage-*.dxf')}
    require(actual == names, f'Code-page inventory missing={names-actual}, extra={actual-names}')
    return names


def main(directory):
    names = inventory(directory)
    corruptions = 0
    for page, text in PROFILES.items():
        wanted = expected(page)
        for transport, stage in itertools.product(('text', 'binary'), STAGES):
            path = directory / f'r12-codepage-{page}-{transport}-{stage}.dxf'
            binary = (transport == 'binary') != (stage == 'output')
            data = path.read_bytes()
            require(data.startswith(MAGIC) == binary, 'R12 transport')
            require(text.encode(f'cp{page}', errors='strict') in data, 'R12 did not write native code-page bytes')
            actual = load_r12_tags(data, page, binary)
            check_packet(wanted, actual)
            if binary:
                require(data == binary_packet(wanted, page), 'Exact binary R12 encoding/framing differs')
            check_document(ezdxf.readfile(path, errors='strict'), page, 'AC1009')
            for index, (code, value) in enumerate(actual):
                for mode in ('remove', 'duplicate', 'change'):
                    changed = list(actual)
                    if mode == 'remove': changed.pop(index)
                    elif mode == 'duplicate': changed.insert(index, changed[index])
                    else: changed[index] = (code, value + '_bad' if isinstance(value, str) else value + 1)
                    corruptions += reject(lambda: check_packet(wanted, changed))
        for version, transport in itertools.product(VERSIONS, ('text', 'binary')):
            path = directory / f'r12-codepage-modern-{page}-{version}-{transport}.dxf'
            data = path.read_bytes()
            require(data.startswith(MAGIC) == (transport == 'binary'), 'Modern transport')
            if transport == 'text':
                data.decode('utf-8' if VERSIONS[version] >= 'AC1021' else f'cp{page}', errors='strict')
            check_document(ezdxf.readfile(path, errors='strict'), page, VERSIONS[version])
    print(f'PASS: {len(names)} actual C# drawings, {len(PROFILES)} code pages, seven database families, '
          f'{corruptions} packet corruptions rejected; exact R12 bytes/packets and zero independent audit errors/repairs.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_r12_codepages.py ARTIFACTS')
    main(Path(sys.argv[1]))
