"""Keep the independent source-hygiene gate in the fast CI tooling suite."""
import importlib.util
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'tools/verify_repository_hygiene.py'
SPEC = importlib.util.spec_from_file_location('repository_hygiene', SCRIPT)
hygiene = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(hygiene)
EXPECTED = {
    '.github/workflows/ci-build.yml',
    '.github/workflows/release.yml',
    '.github/workflows/shadow-studio-pages.yml',
}


class RepositoryHygieneTests(unittest.TestCase):
    def test_exact_inventory_includes_pages_and_both_core_workflows(self):
        actual = {
            path.relative_to(ROOT).as_posix()
            for path in (ROOT / '.github/workflows').rglob('*')
            if path.is_file() and path.suffix.lower() in ('.yml', '.yaml')
        }
        self.assertEqual(EXPECTED, actual)
        self.assertEqual(EXPECTED, hygiene.WORKFLOWS)
        hygiene.workflow_inventory(actual)

    def test_every_missing_workflow_is_rejected(self):
        for path in sorted(EXPECTED):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, 'missing'):
                hygiene.workflow_inventory(EXPECTED - {path})

    def test_unknown_workflows_are_rejected(self):
        for path in ('.github/workflows/temporary-publish.yml',
                     '.github/workflows/other-pages.yaml'):
            with self.subTest(path=path), self.assertRaisesRegex(ValueError, 'unexpected'):
                hygiene.workflow_inventory(EXPECTED | {path})

    def test_replacement_and_empty_inventories_are_rejected(self):
        replacements = (
            set(),
            EXPECTED - {'.github/workflows/shadow-studio-pages.yml'}
            | {'.github/workflows/shadow-studio-pages.yaml'},
        )
        for paths in replacements:
            with self.subTest(paths=paths), self.assertRaises(ValueError):
                hygiene.workflow_inventory(paths)

    def test_built_in_negative_controls(self):
        # Missing workflows, unknown publisher, empty/oversized documents,
        # undocumented pipeline commands, and invalid local documentation links.
        self.assertEqual(11, hygiene.self_test())

    def test_full_tracked_repository_hygiene(self):
        # Run the same CLI as the independent verifier, early on every build OS.
        # No .NET, DXF fixture generation, or external reader is required here.
        result = subprocess.run(
            [sys.executable, str(SCRIPT)], cwd=ROOT,
            capture_output=True, text=True, encoding='utf-8', timeout=60,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn('two core workflows plus the isolated Pages workflow', result.stdout)
        self.assertIn('11 negative controls rejected', result.stdout)


if __name__ == '__main__':
    unittest.main()
