"""Keep the independent source-hygiene gate in the fast CI tooling suite."""
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'tools/verify_repository_hygiene.py'


class RepositoryHygieneTests(unittest.TestCase):
    def test_full_tracked_repository_hygiene(self):
        # Run the same CLI as the independent verifier, early on every build OS.
        # No .NET, DXF fixture generation, or external reader is required here.
        result = subprocess.run(
            [sys.executable, str(SCRIPT)], cwd=ROOT,
            capture_output=True, text=True, encoding='utf-8', timeout=60,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn('two core workflows plus the isolated Pages workflow', result.stdout)
        self.assertIn('12 negative controls rejected', result.stdout)


if __name__ == '__main__':
    unittest.main()
