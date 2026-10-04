#!/usr/bin/env python3
"""Verify modern DOS-origin selections without treating DOS bytes as ANSI bytes."""
import itertools
from pathlib import Path
import sys
import ezdxf
from ezdxf.lldxf.encoding import decode_dxf_unicode
from verify_raw_line_geometry import require, reject, audit_signature
from verify_r12_codepages import VERSIONS, MAGIC, logical, description

PROFILES = {'DOS437': (1252, 'Grüße ╬'), 'DOS850': (1252, 'Café ║'), 'dos932': (932, '日本語')}


def inventory(directory):
    wanted = {f'r12-dos-modern-{alias}-{version}-{transport}.dxf'
              for alias, version, transport in itertools.product(PROFILES, VERSIONS, ('text', 'binary'))}
    require({p.name for p in directory.glob('r12-dos-modern-*.dxf')} == wanted,
            'Missing or extra DOS conversion drawings')
    return wanted


def check(doc, windows_page, text, version):
    require(doc.dxfversion == version, 'DOS target database family')
    require(doc.header.get('$DWGCODEPAGE') == f'ANSI_{windows_page}', 'DOS-to-Windows declaration')
    roots = list(doc.modelspace())
    require([e.dxftype() for e in roots] == ['TEXT', 'INSERT'], 'DOS root inventory/order')
    root, insert = roots
    require(insert.dxf.name == 'NATIVE_BLOCK' and tuple(insert.dxf.insert) == (10., 20., 30.), 'DOS block reference')
    members = list(doc.blocks.get('NATIVE_BLOCK'))
    require([e.dxftype() for e in members] == ['ATTDEF', 'TEXT', 'LINE'], 'DOS block inventory/order')
    definition, nested, line = members
    require(len(insert.attribs) == 1 and insert.seqend is not None, 'DOS attribute sequence')
    attribute = insert.attribs[0]
    for entity, role, xyz in [(root, 'root', (1., 2., 3.)), (definition, 'default', (4., 5., 6.)),
                              (nested, 'nested', (7., 8., 9.)), (attribute, 'instance', (14., 15., 16.))]:
        require(decode_dxf_unicode(entity.dxf.text) == logical(role, text), 'DOS logical text ' + role)
        require(tuple(entity.dxf.insert) == xyz and entity.dxf.height == 2., 'DOS text geometry ' + role)
        require(entity.dxf.layer == 'NATIVE' and entity.dxf.style == 'ENC_STYLE', 'DOS text resource ' + role)
    require(decode_dxf_unicode(definition.dxf.prompt) == logical('prompt', text), 'DOS attribute prompt')
    require(definition.dxf.tag == attribute.dxf.tag == 'VALUE' and definition.dxf.flags == attribute.dxf.flags == 5, 'DOS attribute binding/flags')
    require(tuple(line.dxf.start) == (0., 0., 0.) and tuple(line.dxf.end) == (1., 0., 0.), 'DOS nested line')
    require(root.dxf.linetype == 'NATIONAL', 'DOS linetype reference')
    pattern = doc.linetypes.get('NATIONAL')
    require(decode_dxf_unicode(pattern.dxf.description) == description(text), 'DOS description')
    require(doc.layers.get('NATIVE').dxf.flags == 2 and doc.layers.get('NATIVE').dxf.color == 2, 'DOS layer settings')
    require(doc.styles.get('ENC_STYLE').dxf.font == 'txt.shx', 'DOS style font reference')
    return root, definition, nested, attribute, pattern


def main(directory):
    names = inventory(directory)
    negative = 0
    for alias, version, transport in itertools.product(PROFILES, VERSIONS, ('text', 'binary')):
        windows_page, text = PROFILES[alias]
        path = directory / f'r12-dos-modern-{alias}-{version}-{transport}.dxf'
        data = path.read_bytes()
        require(data.startswith(MAGIC) == (transport == 'binary'), 'DOS modern transport')
        if transport == 'text':
            data.decode('utf-8' if VERSIONS[version] >= 'AC1021' else f'cp{windows_page}', errors='strict')
        doc = ezdxf.readfile(path, errors='strict')
        root, definition, nested, attribute, pattern = check(doc, windows_page, text, VERSIONS[version])
        require(not any(any(c.values()) for c in audit_signature(doc)), 'DOS independent audit errors or repairs')
        for entity, field in [(root, 'text'), (definition, 'text'), (nested, 'text'), (attribute, 'text'),
                              (definition, 'prompt'), (pattern, 'description')]:
            old = entity.dxf.get(field)
            setattr(entity.dxf, field, old + '?')
            try: negative += reject(lambda: check(doc, windows_page, text, VERSIONS[version]))
            finally: setattr(entity.dxf, field, old)
        old = doc.header['$DWGCODEPAGE']; doc.header['$DWGCODEPAGE'] = alias
        try: negative += reject(lambda: check(doc, windows_page, text, VERSIONS[version]))
        finally: doc.header['$DWGCODEPAGE'] = old
        old = root.dxf.insert; root.dxf.insert = (101., 2., 3.)
        try: negative += reject(lambda: check(doc, windows_page, text, VERSIONS[version]))
        finally: root.dxf.insert = old
        check(doc, windows_page, text, VERSIONS[version])
    print(f'PASS: {len(names)} actual DOS-origin modern drawings, six target families and both transports; '
          f'{negative} text/profile/geometry corruptions rejected; no content replacement or audit repairs/errors.')


if __name__ == '__main__':
    require(len(sys.argv) == 2, 'Usage: verify_r12_dos_conversion.py ARTIFACTS')
    main(Path(sys.argv[1]))
