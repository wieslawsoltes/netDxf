"""Independent DOS conversion checker controls; not C# execution evidence."""
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
import ezdxf
import verify_r12_dos_conversion as checker


def quoted(value):
    # The expected logical strings come from the hand-written checker, not library output.
    return ''.join(f'\\U+{ord(c):04X}' if ord(c) < 32 or c in '\\^' else c for c in value)


def oracle(page, text, version):
    doc = ezdxf.new(version)
    doc.header['$DWGCODEPAGE'] = f'ANSI_{page}'
    doc.layers.new('NATIVE', dxfattribs={'flags': 2, 'color': 2})
    doc.styles.new('ENC_STYLE', dxfattribs={'font': 'txt.shx'})
    doc.linetypes.new('NATIONAL', dxfattribs={'description': quoted(checker.description(text))})
    attributes = {'layer': 'NATIVE', 'style': 'ENC_STYLE', 'height': 2.}
    block = doc.blocks.new('NATIVE_BLOCK')
    definition = block.add_attdef('VALUE', (4., 5., 6.), quoted(checker.logical('default', text)),
                                 dxfattribs={**attributes, 'prompt': quoted(checker.logical('prompt', text)), 'flags': 5})
    block.add_text(quoted(checker.logical('nested', text)), dxfattribs={**attributes, 'insert': (7., 8., 9.)})
    block.add_line((0., 0., 0.), (1., 0., 0.), dxfattribs={'layer': 'NATIVE'})
    doc.modelspace().add_text(quoted(checker.logical('root', text)),
                             dxfattribs={**attributes, 'linetype': 'NATIONAL', 'insert': (1., 2., 3.)})
    insert = doc.modelspace().add_blockref('NATIVE_BLOCK', (10., 20., 30.), dxfattribs={'layer': 'NATIVE'})
    insert.add_attrib('VALUE', quoted(checker.logical('instance', text)), (14., 15., 16.),
                      dxfattribs={**attributes, 'flags': 5})
    return doc


class DosConversionCheckerTests(unittest.TestCase):
    def test_exact_missing_extra_inventory(self):
        names = {f'r12-dos-modern-{a}-{v}-{t}.dxf' for a, v, t in
                 checker.itertools.product(checker.PROFILES, checker.VERSIONS, ('text', 'binary'))}
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError): checker.inventory(root)
            for name in names: (root / name).touch()
            self.assertEqual(312, len(checker.inventory(root)))
            missing = root / sorted(names)[0]
            missing.unlink()
            with self.assertRaises(ValueError): checker.inventory(root)
            missing.touch()
            (root / 'r12-dos-modern-unexpected.dxf').touch()
            with self.assertRaises(ValueError): checker.inventory(root)

    def test_all_handwritten_language_oracles(self):
        for alias, (page, text) in checker.PROFILES.items():
            for version in checker.VERSIONS.values():
                with self.subTest(alias=alias, version=version):
                    doc = oracle(page, text, version)
                    checker.check(doc, page, text, version)
                    self.assertFalse(any(any(c.values()) for c in checker.audit_signature(doc)))

    def test_content_header_and_position_corruptions_reject(self):
        doc = oracle(1252, 'Grüße ╬', 'AC1015')
        objects = checker.check(doc, 1252, 'Grüße ╬', 'AC1015')
        root, definition, nested, attribute, pattern = objects
        for obj, field in [(root, 'text'), (definition, 'text'), (nested, 'text'), (attribute, 'text'),
                           (definition, 'prompt'), (pattern, 'description')]:
            old = obj.dxf.get(field)
            setattr(obj.dxf, field, old + '?')
            with self.assertRaises(ValueError): checker.check(doc, 1252, 'Grüße ╬', 'AC1015')
            setattr(obj.dxf, field, old)
        doc.header['$DWGCODEPAGE'] = 'DOS437'
        with self.assertRaises(ValueError): checker.check(doc, 1252, 'Grüße ╬', 'AC1015')
        doc.header['$DWGCODEPAGE'] = 'ANSI_1252'
        root.dxf.insert = (9., 2., 3.)
        with self.assertRaises(ValueError): checker.check(doc, 1252, 'Grüße ╬', 'AC1015')


if __name__ == '__main__':
    unittest.main()
