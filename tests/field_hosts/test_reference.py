"""Oracle admission controls; these do not substitute for actual C# output tests."""
import copy
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools'))
import verify_field_text_hosts as v


class HostOracleTests(unittest.TestCase):
    def test_literal_policy(self):
        self.assertEqual('a\\\\b\\{c\\}\\Pd', v.literal_value('a\\b{c}\r\nd', 'MTEXT'))
        self.assertEqual(v.LITERAL, v.literal_value(v.LITERAL, 'TEXT'))

    def test_profile_encoding(self):
        self.assertEqual('\\U+03A9 \\path \\U+D83D\\U+DE00', v.host_wire_text('Ω \\path 😀', 'AC1015'))
        self.assertEqual('Ω \\path 😀', v.host_wire_text('Ω \\path 😀', 'AC1032'))

    @staticmethod
    def proxy():
        return [[0, 'TEXT'], [5, 'A'], [100, 'AcDbEntity'], [8, '0'],
                [160, 2], [310, {'hex': '0102'}], [100, 'AcDbText'], [1, 'text']]

    def test_complete_proxy_only(self):
        original = self.proxy() + [[310, {'hex': '00'}]]
        self.assertEqual(original[:4] + original[6:], v.remove_proxy(original))
        empty = original[:4] + original[6:]
        self.assertEqual(empty, v.remove_proxy(empty))

    def test_bad_proxy_admission(self):
        variants = []
        for count in (-1, 1, 3, 1.5):
            tags = self.proxy(); tags[4] = [160, count]; variants.append(tags)
        tags = self.proxy(); tags.insert(4, [92, 2]); variants.append(tags)
        tags = self.proxy(); del tags[4]; variants.append(tags)
        tags = self.proxy(); tags[5] = [310, {'private': '0102'}]; variants.append(tags)
        for tags in variants:
            with self.subTest(tags=tags), self.assertRaises(ValueError): v.remove_proxy(tags)

    @staticmethod
    def sequences():
        result = {}
        for insert, attrib, end in [('A', 'B', 'C'), ('D', 'E', 'F')]:
            result[insert] = [[0, 'INSERT'], [5, insert], [100, 'AcDbEntity'], [8, '0'], [100, 'AcDbBlockReference'], [66, 1]]
            result[attrib] = [[0, 'ATTRIB'], [5, attrib], [330, insert], [100, 'AcDbEntity'], [100, 'AcDbText'], [1, 'x']]
            result[end] = [[0, 'SEQEND'], [5, end], [330, insert], [100, 'AcDbEntity'], [8, '0']]
        return result

    def test_stored_sequence_identity_preserved(self):
        source = self.sequences(); original = copy.deepcopy(source)
        result = v.validate_attribute_sequences(source, 'ATTRIB')
        self.assertEqual(original, source)
        self.assertIs(source, result)
        self.assertEqual(original, result)
        self.assertEqual(['A', 'B', 'C', 'D', 'E', 'F'], list(result))

    def test_changed_sequence_identity_is_rejected(self):
        source = self.sequences()
        changed = {('10' if key == 'C' else key): copy.deepcopy(row) for key, row in source.items()}
        changed['10'][1] = [5, '10']
        # Each graph is individually well-formed. The complete before/after
        # comparison must reject the identity change previously normalized away.
        before = v.validate_attribute_sequences(source, 'ATTRIB')
        after = v.validate_attribute_sequences(changed, 'ATTRIB')
        with self.assertRaises(ValueError): v.compare(before, after)

    def test_sequence_payload_not_normalized(self):
        for variant in ('extra', 'missing', 'wrong-layer', 'owner', 'missing-owner', 'duplicate-owner',
                        'zero-owner', 'foreign-owner', 'wrong-identity'):
            source = self.sequences()
            if variant == 'extra': source['C'].append([999, 'private'])
            if variant == 'missing': del source['C']
            if variant == 'wrong-layer': source['C'][-1] = [8, 'different']
            if variant == 'owner': source['B'][2] = [330, 'D']
            if variant == 'missing-owner': del source['C'][2]
            if variant == 'duplicate-owner': source['C'].insert(2, [330, 'A'])
            if variant == 'zero-owner': source['C'][2] = [330, '0']
            if variant == 'foreign-owner': source['C'][2] = [330, 'D']
            if variant == 'wrong-identity': source['C'][1] = [5, 'F']
            with self.subTest(variant=variant), self.assertRaises(ValueError): v.validate_attribute_sequences(source, 'ATTRIB')

    def test_referenced_sequence_identity_not_normalized(self):
        for code in (320, 330, 340, 360, 390, 480, 1005):
            source = self.sequences(); source['X'] = [[0, 'XRECORD'], [5, 'X'], [code, 'C']]
            with self.subTest(code=code), self.assertRaises(ValueError): v.validate_attribute_sequences(source, 'ATTRIB')

    def test_exact_file_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            with self.assertRaises(ValueError): v.check_inventory(directory)
            self.assertEqual(96, len(v.inventory()))
            for name in v.inventory(): (directory / name).touch()
            v.check_inventory(directory)
            extra = directory / 'field-hosts-extra.dxf'; extra.touch()
            with self.assertRaises(ValueError): v.check_inventory(directory)
            extra.unlink(); next(directory.glob('*.dxf')).unlink()
            with self.assertRaises(ValueError): v.check_inventory(directory)


if __name__ == '__main__':
    unittest.main()
