"""Release must run for target-consumer changes and await the complete runtime gate."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RuntimeReleaseGateTests(unittest.TestCase):
    def test_release_routes_target_sources_through_required_build(self):
        release = (ROOT / '.github/workflows/release.yml').read_text()
        build = (ROOT / '.github/workflows/ci-build.yml').read_text()
        triggers = release.split('  pull_request:', 1)[1].split('  workflow_dispatch:', 1)[0]
        self.assertIn("'tests/netDxf.TargetSmoke/**'", triggers)
        self.assertIn("'tests/netDxf.PackageSmoke/**'", triggers)
        self.assertIn("'tools/ci/**'", triggers)
        self.assertRegex(release, r'(?m)^  build:\n    needs: plan\n    uses: \./\.github/workflows/ci-build\.yml$')
        qualify = release.split('  qualify:\n', 1)[1].split('  github-release:\n', 1)[0]
        self.assertIn('needs: [plan, build, test]', qualify)
        self.assertNotIn('always()', qualify)
        runtime_jobs = build.split('  runtime-smoke:\n', 1)[1]
        self.assertIn('needs: package', runtime_jobs)
        self.assertIn('needs: runtime-smoke', runtime_jobs)
        self.assertIsNone(re.search(r'continue-on-error\s*:', runtime_jobs))
        self.assertNotIn('if: always()', runtime_jobs.split('  runtime-evidence:\n', 1)[1])


if __name__ == '__main__':
    unittest.main()
