"""Queue candidates run complete qualification but can never authorize publication."""
import importlib.util
import os
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('queue_pipeline', ROOT / 'tools/ci/pipeline.py')
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class MergeQueueTests(unittest.TestCase):
    def test_all_required_workflows_run_requested_queue_checks(self):
        for filename in ('ci-build.yml',):
            text = (ROOT / '.github/workflows' / filename).read_text()
            events = text.split('\non:\n', 1)[1].split('\npermissions:', 1)[0]
            with self.subTest(workflow=filename):
                self.assertIn('  merge_group:\n    types: [checks_requested]\n    branches: [netstandard]\n', events)
                self.assertNotIn('paths:', events.split('  merge_group:\n', 1)[1].split('\n  ', 1)[0])
                self.assertIn('  pull_request:', events)

    def test_queue_uses_complete_existing_build_and_test_gates(self):
        release = (ROOT / '.github/workflows/release.yml').read_text()
        self.assertIn('uses: ./.github/workflows/ci-build.yml', release)
        build = (ROOT / '.github/workflows/ci-build.yml').read_text()
        self.assertIn('needs: [conformance, runtime-evidence]', build)
        self.assertIn('needs: [plan, build]', release)
        self.assertNotIn('  pull_request:', release)
        self.assertNotIn('  merge_group:', release)
        self.assertIn("github.event_name == 'merge_group' || (github.event_name == 'workflow_dispatch'", release)
        self.assertIn("if: needs.plan.outputs.publish == 'true'", release)
        self.assertFalse((ROOT / '.github/workflows/nuget-publish.yml').exists())
        self.assertFalse((ROOT / '.github/workflows/dxf-conformance.yml').exists())

    def test_queue_and_pr_force_dry_run_for_every_ref_kind(self):
        sha = 'a' * 40
        with patch.object(p, 'git', side_effect=lambda *args: '' if args[0] == 'status' else sha), \
                patch.object(p.subprocess, 'run'), patch.object(p, 'build_version', return_value='3.0.1-ci.123'):
            for event in ('merge_group', 'pull_request'):
                for kind, ref in (('branch', 'gh-readonly-queue/netstandard/pr-190-deadbeef'), ('tag', 'v3.0.1')):
                    for requested in (False, True):
                        with self.subTest(event=event, kind=kind, requested=requested):
                            result = p.release_plan(kind, ref, sha, requested, event)
                            self.assertIs(result['publish'], False)
                            self.assertEqual(sha, result['commit'])

    def test_queue_cannot_validate_the_wrong_checkout(self):
        with patch.object(p, 'git', return_value='b' * 40):
            with self.assertRaises(ValueError):
                p.release_plan('branch', 'gh-readonly-queue/netstandard/pr-190-x', 'a' * 40, True, 'merge_group')

    def test_tagged_push_and_manual_publication_contract_stays_opt_in(self):
        sha = 'a' * 40
        with patch.object(p, 'git', side_effect=lambda *args: '' if args[0] == 'status' else sha), patch.object(p.subprocess, 'run'):
            for event in ('push', 'workflow_dispatch'):
                self.assertIs(p.release_plan('tag', 'v3.0.1', sha, False, event)['publish'], True)
                self.assertIs(p.release_plan('tag', 'v3.0.1', sha, True, event)['publish'], False)
                with self.assertRaises(ValueError):
                    p.release_plan('branch', 'netstandard', sha, False, event)

    def test_cli_passes_trigger_identity_to_plan(self):
        env = {'REF_TYPE': 'branch', 'REF_NAME': 'gh-readonly-queue/netstandard/pr-190-x',
               'EXPECTED_SHA': 'a' * 40, 'DRY_RUN': 'false', 'GITHUB_EVENT_NAME': 'merge_group'}
        with patch.dict(os.environ, env), patch('sys.argv', ['pipeline.py', 'release-plan']), \
                patch.object(p, 'release_plan', return_value={}) as plan, patch.object(p, 'emit'):
            p.main()
            plan.assert_called_once_with(env['REF_TYPE'], env['REF_NAME'], env['EXPECTED_SHA'], False, 'merge_group')
