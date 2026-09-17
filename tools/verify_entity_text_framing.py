#!/usr/bin/env python3
"""Independently inspect the valid side of the entity text preflight contract.

Malformed-input/stream rollback is exercised by the C# tests, not this checker.
This checker compares original tags, with explicit legacy Unicode escape decoding;
no native font, layout or AutoCAD interpretation is inferred from graph audits.
"""
from __future__ import annotations
import argparse
import io
from pathlib import Path
import re
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = {'AutoCad2000':'AC1015', 'AutoCad2004':'AC1018', 'AutoCad2007':'AC1021',
            'AutoCad2010':'AC1024', 'AutoCad2013':'AC1027', 'AutoCad2018':'AC1032'}
KINDS = {'text':('TEXT', 1), 'mtext':('MTEXT', 1), 'definition-value':('ATTDEF', 1),
         'definition-prompt':('ATTDEF', 3), 'attribute':('ATTRIB', 1), 'dimension':('DIMENSION', 1)}
TEXT = '🚀 Zażółć 東京 e\u0301 \\P \\U+0041'


def require(ok, message):
    if not ok: raise ValueError(message)


def decode_legacy(value):
    # Decode only the escapes netDxf generated for non-ASCII UTF-16 units.
    # The input's literal \\U+0041 remains a literal physical-tag sequence.
    value = re.sub(r'\\U\+([0-9A-Fa-f]{4})',
                   lambda m: chr(int(m[1], 16)) if int(m[1], 16) > 127 else m[0], value)
    return value.encode('utf-16-le', 'surrogatepass').decode('utf-16-le', 'strict')


def records(data):
    tags = (binary_tags_loader(data) if data.startswith(b'AutoCAD Binary DXF') else
            ascii_tags_loader(io.StringIO(data.decode('utf-8-sig', 'strict'))))
    result, current = [], []
    for tag in tags:
        if tag.code == 0:
            if current: result.append(current)
            current = []
        current.append((tag.code, tag.value))
    if current: result.append(current)
    return result


def check_packet(packet, kind, expected, legacy):
    name, code = KINDS[kind]
    require(packet and packet[0] == (0, name), 'Wrong entity packet')
    values = [v for c,v in packet if c == code]
    require(len(values) == 1, 'Missing or repeated selected content field')
    actual = values[0]
    require(isinstance(actual, str), 'Content is not text')
    require((decode_legacy(actual) if legacy else actual) == expected, 'Selected text changed')
    actual.encode('utf-8', 'strict')
    require('\0' not in actual, 'NUL in physical text')


def rejects(action, value):
    try: action(value)
    except (ValueError, UnicodeError): return 1
    raise AssertionError('Corruption escaped the positive checker')


def main():
    parser=argparse.ArgumentParser(description=__doc__); parser.add_argument('directory',type=Path)
    directory=parser.parse_args().directory
    expected_files=set(); files=controls=0
    for version,profile in PROFILES.items():
        legacy=version in ('AutoCad2000','AutoCad2004')
        for binary in (False,True):
            for kind,(name,code) in KINDS.items():
                for nested in (False,True):
                    for sample in (range(4) if binary else range(1)):
                        filename=f'entity-text-framing-{version}-{binary}-{kind}-{nested}-{sample}.dxf'
                        expected_files.add(filename); path=directory/filename; data=path.read_bytes()
                        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport mismatch')
                        expected=TEXT if sample==0 else 'before'+('','\r','\n','\r\n')[sample]+'after'
                        selected=[p for p in records(data) if p[0] == (0,name)]
                        require(len(selected)==1, 'Wrong selected entity inventory')
                        packet=selected[0]
                        check=lambda p:check_packet(p,kind,expected,legacy)
                        check(packet)
                        at=next(i for i,(c,_) in enumerate(packet) if c==code)
                        for operation in ('change','drop','duplicate','surrogate','nul'):
                            changed=list(packet)
                            if operation=='drop': del changed[at]
                            elif operation=='duplicate': changed.insert(at, changed[at])
                            else: changed[at]=(code, str(changed[at][1])+{'change':'x','surrogate':'\ud800','nul':'\0'}[operation])
                            controls+=rejects(check,changed)
                        doc=ezdxf.readfile(path); require(doc.dxfversion==profile,'Version mismatch')
                        audit=doc.audit(); require(not audit.errors and not audit.fixes,'Graph needs repairs')
                        files+=1
    actual={p.name for p in directory.glob('entity-text-framing-*.dxf')}
    check_inventory=lambda value:require(value==expected_files,'Fixture inventory differs')
    check_inventory(actual)
    inv=rejects(check_inventory,actual-{next(iter(actual))})+rejects(check_inventory,actual|{'unexpected'})
    print(f'PASS ezdxf {ezdxf.__version__}: {files} entity text drawings, {controls} actual-packet corruptions '
          f'and {inv} inventory corruptions rejected; zero graph errors/repairs')


if __name__=='__main__': main()
