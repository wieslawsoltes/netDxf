"""Release must run for target-consumer changes and await the complete runtime gate."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RuntimeReleaseGateTests(unittest.TestCase):
    def test_release_routes_target_sources_through_required_build(self):
        release = (ROOT / '.github/workflows/release.yml').read_text()
        build = (ROOT / '.github/workflows/ci-build.yml').read_text()
        triggers = build.split('on:\n', 1)[1].split('permissions:', 1)[0]
        self.assertIn('  pull_request:\n    branches: [netstandard]', triggers)
        self.assertNotIn('paths:', triggers)  # Every source or consumer change is tested.
        self.assertRegex(release, r'(?m)^  build:\n    needs: plan\n    uses: \./\.github/workflows/ci-build\.yml$')
        qualify = build.split('  qualify:\n', 1)[1]
        self.assertIn('needs: [conformance, runtime-evidence]', qualify)
        self.assertIn('needs: [plan, build]', release)
        self.assertNotIn('always()', qualify)
        runtime_jobs = build.split('  runtime-smoke:\n', 1)[1]
        self.assertIn('needs: package', runtime_jobs)
        self.assertIn('needs: runtime-smoke', runtime_jobs)
        self.assertIsNone(re.search(r'continue-on-error\s*:', runtime_jobs))
        self.assertNotIn('if: always()', runtime_jobs.split('  runtime-evidence:\n', 1)[1].split('  conformance:\n', 1)[0])

    def test_only_obsolete_pull_request_rehearsals_can_be_cancelled(self):
        release = (ROOT / '.github/workflows/release.yml').read_text()
        concurrency = release.split('concurrency:\n', 1)[1].split('jobs:\n', 1)[0]
        self.assertEqual("  group: release-${{ github.event.release.tag_name || github.ref }}\n  cancel-in-progress: false\n", concurrency)
        build = (ROOT / '.github/workflows/ci-build.yml').read_text()
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' || github.event_name == 'merge_group' }}", build)
        self.assertNotIn('  pull_request:', release)
        self.assertNotIn('  merge_group:', release)



if __name__ == '__main__':
    unittest.main()
