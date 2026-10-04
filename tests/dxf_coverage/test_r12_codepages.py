"""Self-tests of handwritten encoding oracles, distinct from actual C# conformance."""
from pathlib import Path
import io
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
import ezdxf
import verify_r12_codepages as checker


class R12CodePageCheckerTests(unittest.TestCase):
    def test_all_handwritten_text_and_binary_oracles(self):
        with tempfile.TemporaryDirectory() as temporary:
            for page in checker.PROFILES:
                tags = checker.expected(page)
                text = ''.join(f'{code}\n{value}\n' for code, value in tags)
                for binary in (False, True):
                    with self.subTest(page=page, binary=binary):
                        data = checker.binary_packet(tags, page) if binary else text.encode(f'cp{page}', errors='strict')
                        checker.check_packet(tags, checker.load_r12_tags(data, page, binary))
                        path = Path(temporary) / f'{page}-{binary}.dxf'
                        path.write_bytes(data)
                        checker.check_document(ezdxf.readfile(path, errors='strict'), page, 'AC1009')

    def test_every_packet_field_is_protected(self):
        for page in (932, 1250):
            wanted = checker.expected(page)
            for index, (code, value) in enumerate(wanted):
                for mode in ('remove', 'duplicate', 'change'):
                    changed = list(wanted)
                    if mode == 'remove': changed.pop(index)
                    elif mode == 'duplicate': changed.insert(index, changed[index])
                    else: changed[index] = (code, value + '_wrong' if isinstance(value, str) else value + 1)
                    with self.assertRaises(ValueError, msg=f'{page}/{index}/{mode}'):
                        checker.check_packet(wanted, changed)

    def test_exact_inventory_missing_extra_and_wrong_family(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError): checker.inventory(root)
            names = {f'r12-codepage-{p}-{t}-{s}.dxf' for p, t, s in
                     checker.itertools.product(checker.PROFILES, ('text', 'binary'), checker.STAGES)}
            names |= {f'r12-codepage-modern-{p}-{v}-{t}.dxf' for p, v, t in
                      checker.itertools.product(checker.PROFILES, checker.VERSIONS, ('text', 'binary'))}
            for name in names: (root / name).touch()
            self.assertEqual(252, len(checker.inventory(root)))
            removed = root / sorted(names)[0]; removed.unlink()
            with self.assertRaises(ValueError): checker.inventory(root)
            removed.touch()
            wrong = root / 'r12-codepage-modern-1250-AutoCad14-text.dxf'; wrong.touch()
            with self.assertRaises(ValueError): checker.inventory(root)

    def test_wrong_encoding_and_malformed_bytes_are_not_repaired(self):
        tags = checker.expected(932)
        text = ''.join(f'{code}\n{value}\n' for code, value in tags)
        with self.assertRaises(ValueError):
            checker.check_packet(tags, checker.load_r12_tags(text.encode('utf-8'), 932, False))
        data = checker.binary_packet(tags, 932)
        with self.assertRaises(ValueError): checker.load_r12_tags(data[:-1], 932, True)
        # A dangling multibyte lead byte inside a string is rejected, not replaced.
        data = data.replace('日本語'.encode('cp932'), b'\x82', 1)
        with self.assertRaises(UnicodeDecodeError): checker.load_r12_tags(data, 932, True)

    def test_decoding_preserves_literal_sequences(self):
        for page, text in checker.PROFILES.items():
            value = checker.logical('root', text)
            self.assertEqual(value, checker.decode_content(checker.caret_encode(value), True))
        self.assertEqual(r'\U+0041', checker.decode_content(r'\U+005CU+0041', False))
        self.assertEqual('^J', checker.decode_content(r'\U+005EJ', False))


if __name__ == '__main__':
    unittest.main()
