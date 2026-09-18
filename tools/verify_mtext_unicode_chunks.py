#!/usr/bin/env python3
"""Independently check MTEXT UTF-8 chunk boundaries and exact joined content."""
import argparse
import io
from pathlib import Path
import re
import ezdxf
from ezdxf.lldxf.tagger import ascii_tags_loader, binary_tags_loader

PROFILES = {2000: 'AC1015', 2004: 'AC1018', 2007: 'AC1021', 2010: 'AC1024', 2013: 'AC1027', 2018: 'AC1032'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def samples():
    values = ['a' * n for n in (0, 1, 248, 249, 250, 251, 499, 500, 501)]
    values += ['x' * n + '\U0001f680' + 'y' * 503 for n in (0, 1, 246, 247, 248, 249, 250, 497, 498, 499)]
    values += ['Zażółć gęślą jaźń 東京 \U0001f680 e\u0301 ' * 31, '\U00020000\U0010ffff' * 130,
               '東' * 333, 'é' * 257, 'a' * 248 + r'\P{\C1;red}\S1/2;end']
    return values


def scalar_text(value):
    # Pre-2007 uses one escape per UTF-16 code unit. Reassemble valid pairs
    # without replacing malformed halves or changing normalization form.
    return value.encode('utf-16-le', 'surrogatepass').decode('utf-16-le')


def decode_legacy(value):
    return scalar_text(re.sub(r'\\U\+([0-9a-fA-F]{4})', lambda m: chr(int(m[1], 16)), value))


def check_packet(packet, expected, legacy):
    require(packet and packet[-1][0] == 1 and all(code == 3 for code, _ in packet[:-1]), 'Wrong continuation/terminal framing')
    require(all(len(text.encode('utf-8', 'strict')) <= 250 for _, text in packet), 'Encoded chunk exceeds 250 bytes')
    joined = ''.join(text for _, text in packet)
    if legacy:
        require(joined.isascii(), 'Legacy output must use ASCII escapes')
        joined = decode_legacy(joined)
    require(joined == expected, 'Exact Unicode content differs')


def reject(callback, value):
    try:
        callback(value)
    except (ValueError, UnicodeError):
        return 1
    raise AssertionError('Corruption escaped the positive validator')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    directory = parser.parse_args().directory
    cases = {}
    for year in PROFILES:
        for binary in (False, True):
            for i, value in enumerate(samples()):
                cases[f'mtext-unicode-AutoCad{year}-{binary}-{i}.dxf'] = year, binary, value, False
            cases[f'mtext-unicode-nested-AutoCad{year}-{binary}.dxf'] = year, binary, samples()[19], True
    for binary in (False, True):
        cases[f'mtext-unicode-large-{binary}.dxf'] = 2018, binary, 'x\U0001f680東京' * 32768, False
    expected = set(cases)
    actual = {p.name for p in directory.glob('mtext-unicode-*.dxf')}
    check_inventory = lambda names: require(names == expected, 'Fixture inventory differs')
    check_inventory(actual)
    inventory_controls = reject(check_inventory, actual - {next(iter(actual))})
    inventory_controls += reject(check_inventory, actual | {'mtext-unicode-extra.dxf'})
    corruptions = chunks = 0
    for name, (year, binary, value, nested) in cases.items():
        path = directory / name
        data = path.read_bytes()
        require(data.startswith(b'AutoCAD Binary DXF') == binary, 'Transport differs')
        tags = binary_tags_loader(data) if binary else ascii_tags_loader(io.StringIO(data.decode('utf-8-sig'), newline=None))
        selected = []; current = []
        for tag in tags:
            if tag.code == 0:
                if current and current[0] == (0, 'MTEXT'):
                    selected.append(current)
                current = []
            current.append((tag.code, tag.value))
        require(len(selected) == 1, 'Expected one physical MTEXT')
        packet = [(c, v) for c, v in selected[0] if c in (1, 3)]
        check = lambda p: check_packet(p, value, year < 2007)
        check(packet); chunks += len(packet)
        corruptions += reject(check, packet[:-1])
        corruptions += reject(check, packet + [(1, '')])
        changed = list(packet); changed[-1] = 1, changed[-1][1] + '?'
        corruptions += reject(check, changed)
        changed = list(packet); changed[-1] = 1, changed[-1][1] + '\ud800'
        corruptions += reject(check, changed)
        if len(packet) > 1:
            merged = [(packet[1][0], packet[0][1] + packet[1][1])] + packet[2:]
            corruptions += reject(check, merged)
        document = ezdxf.readfile(path)
        require(document.dxfversion == PROFILES[year], 'Version changed')
        layout = document.blocks['UNICODE_BLOCK'] if nested else document.modelspace()
        text = layout.query('MTEXT').first
        require(text is not None and (decode_legacy(text.text) if year < 2007 else scalar_text(text.text)) == value, 'Independent model text differs')
        require(tuple(text.dxf.insert) == (2, -3, 4) and text.dxf.char_height == 2.5 and text.dxf.width == 40, 'Placement changed')
        require(str(text.get_xdata('CHUNK_APP')[0].value) == 'tail', 'XData boundary lost')
        require(len(document.modelspace().query('LINE')) == 1, 'Following LINE lost')
        audit = document.audit()
        require(not audit.errors and not audit.fixes, 'DXF requires repairs')
    print(f'PASS: {len(cases)} drawings; {chunks} strict UTF-8 chunks; {corruptions} actual-packet corruptions and '
          f'{inventory_controls} inventory corruptions rejected; zero audit errors/repairs; ezdxf {ezdxf.__version__}')


if __name__ == '__main__':
    main()
