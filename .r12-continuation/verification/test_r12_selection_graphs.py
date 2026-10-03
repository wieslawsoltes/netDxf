"""Self-tests for the independent graph checker; these are not C# execution evidence."""
from pathlib import Path
import io
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
import ezdxf
import verify_r12_selection_graphs as checker


class R12GraphCheckerTests(unittest.TestCase):
    def test_expected_packets_have_unique_record_handles(self):
        for family in ('blocks', 'attributes', 'selection'):
            for stage in checker.STAGES:
                with self.subTest(family=family, stage=stage):
                    tags = checker.expected(family, stage)
                    # Exclude HEADER's HANDSEED, which is not a record identity.
                    first_record = next(i for i, tag in enumerate(tags) if tag == (0, 'BLOCK'))
                    handles = [value for code, value in tags[first_record:] if code == 5]
                    self.assertEqual(len(handles), len(set(handles)))
                    checker.check_packet(tags, list(tags))

    def test_every_oracle_tag_is_protected(self):
        for family in ('blocks', 'attributes', 'selection'):
            tags = checker.expected(family)
            for index, (code, value) in enumerate(tags):
                for mode in ('remove', 'duplicate', 'change'):
                    altered = list(tags)
                    if mode == 'remove':
                        altered.pop(index)
                    elif mode == 'duplicate':
                        altered.insert(index, altered[index])
                    else:
                        altered[index] = (code, value + '_changed' if isinstance(value, str) else value + 1)
                    with self.assertRaises(AssertionError, msg=f'{family}/{index}/{mode}'):
                        checker.check_packet(tags, altered)

    def test_exact_fixture_inventory_is_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            with self.assertRaises(AssertionError):
                checker.inventory(directory)
            names = {f'r12-{family}-{transport}-{stage}.dxf' for family, transport, stage in
                     checker.itertools.product(('blocks', 'attributes'), ('text', 'binary'), checker.STAGES)}
            names |= {f'r12-selection-{version}-{transport}.dxf' for version, transport in
                      checker.itertools.product(checker.VERSIONS, ('text', 'binary'))}
            for name in names:
                (directory / name).touch()
            self.assertEqual(26, len(checker.inventory(directory)))
            missing = directory / sorted(names)[0]
            missing.unlink()
            with self.assertRaises(AssertionError):
                checker.inventory(directory)
            missing.touch()
            (directory / 'r12-blocks-extra.dxf').touch()
            with self.assertRaises(AssertionError):
                checker.inventory(directory)

    def test_handwritten_oracles_load_without_audit_repairs(self):
        for family in ('blocks', 'attributes', 'selection'):
            for stage in checker.STAGES:
                with self.subTest(family=family, stage=stage):
                    text = ''.join(f'{code}\n{value}\n' for code, value in checker.expected(family, stage))
                    document = ezdxf.read(io.StringIO(text))
                    if family != 'attributes':
                        checker.check_graph(document, family == 'blocks' and stage != 'source')
                    if family != 'blocks':
                        checker.check_attributes(document, family == 'attributes' and stage != 'source')
                    self.assertFalse(any(any(values.values()) for values in checker.audit_signature(document)))


if __name__ == '__main__':
    unittest.main()
