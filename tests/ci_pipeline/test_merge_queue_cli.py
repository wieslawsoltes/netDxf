"""Real release-plan subprocesses and exact merge-queue workflow routing."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class MergeQueueCliTests(unittest.TestCase):
    def git(self, root, *args):
        return subprocess.check_output(['git', *args], cwd=root, text=True, stderr=subprocess.STDOUT).strip()

    def checkout(self, root):
        (root/'tools/ci').mkdir(parents=True)
        shutil.copyfile(ROOT/'tools/ci/pipeline.py', root/'tools/ci/pipeline.py')
        (root/'netDxf').mkdir()
        (root/'netDxf/netDxf.csproj').write_text('<Project><PropertyGroup><Version>3.0.1</Version></PropertyGroup></Project>')
        self.git(root, 'init', '-q')
        self.git(root, 'config', 'user.name', 'Queue test')
        self.git(root, 'config', 'user.email', 'queue-test@example.invalid')
        self.git(root, 'add', '.')
        self.git(root, 'commit', '-qm', 'Fixture source')
        sha = self.git(root, 'rev-parse', 'HEAD')
        self.git(root, 'update-ref', 'refs/remotes/origin/netstandard', sha)
        self.git(root, 'tag', 'v3.0.1')
        return sha

    def plan(self, root, output, sha, event='merge_group', dry='true', ref_type='branch', ref_name='gh-readonly-queue/netstandard/pr-7', **overrides):
        env = dict(os.environ)
        env.update(EXPECTED_SHA=sha, GITHUB_EVENT_NAME=event, DRY_RUN=dry, REF_TYPE=ref_type,
                   REF_NAME=ref_name, GITHUB_RUN_NUMBER='77', GITHUB_OUTPUT=str(output))
        env.update(overrides)
        return subprocess.run([sys.executable, str(root/'tools/ci/pipeline.py'), 'release-plan'],
                              cwd=root, env=env, text=True, capture_output=True)

    def rejected(self, result, output, message):
        self.assertNotEqual(0, result.returncode)
        self.assertIn(message, result.stderr)
        self.assertFalse(output.exists(), 'Rejected plan emitted workflow outputs')

    def test_workflow_queue_routing_and_permissions(self):
        for name in ('ci-build.yml', 'dxf-conformance.yml', 'release.yml'):
            with self.subTest(name=name):
                text = (ROOT/'.github/workflows'/name).read_text()
                section = re.search(r'^  merge_group:\n((?:    .*\n)+)', text, re.M)
                self.assertIsNotNone(section)
                self.assertEqual('    types: [checks_requested]\n    branches: [netstandard]\n', section[1])
                self.assertIn('permissions:\n  contents: read\n', text)
                self.assertNotIn('pull_request_target:', text)
        self.assertNotIn('merge_group:', (ROOT/'.github/workflows/nuget-publish.yml').read_text())

    def test_release_queue_is_nonpublishing_without_cancelling_tags(self):
        text = (ROOT/'.github/workflows/release.yml').read_text()
        self.assertIn("DRY_RUN: ${{ github.event_name == 'pull_request' || github.event_name == 'merge_group' || (github.event_name == 'workflow_dispatch' && inputs.dry_run) }}", text)
        self.assertIn('EXPECTED_SHA: ${{ github.sha }}', text)
        self.assertIn("if: needs.plan.outputs.publish == 'true'", text)
        self.assertIn("cancel-in-progress: ${{ github.event_name == 'pull_request' || github.event_name == 'merge_group' }}", text)
        self.assertIn("tags: ['v[0-9]*']", text)

    def test_actual_merge_queue_cli_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)/'source'; root.mkdir(); output=Path(temp)/'output'
            sha = self.checkout(root); result = self.plan(root, output, sha)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual({'version':'3.0.1-ci.77', 'tag':'', 'publish':False, 'prerelease':True,
                              'commit':sha, 'tree':self.git(root,'rev-parse','HEAD^{tree}')}, json.loads(result.stdout))
            self.assertIn('publish=false\n', output.read_text())
            self.assertEqual('', self.git(root, 'status', '--porcelain'))

    def test_validation_events_force_nonpublishing_for_every_ref(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'source';root.mkdir();output=Path(temp)/'output';sha=self.checkout(root)
            for event in ('merge_group','pull_request'):
                for dry, kind in (('false','branch'),('false','tag'),('true','tag'),('true','branch')):
                    with self.subTest(event=event,dry=dry,kind=kind):
                        if output.exists():output.unlink()
                        result=self.plan(root,output,sha,event=event,dry=dry,ref_type=kind,ref_name='v3.0.1')
                        self.assertEqual(0,result.returncode,result.stderr)
                        self.assertIs(json.loads(result.stdout)['publish'],False)
                        self.assertEqual(sha,json.loads(result.stdout)['commit'])
                        self.assertIn('publish=false\n',output.read_text())
                        self.assertEqual('',self.git(root,'status','--porcelain'))

    def test_actual_wrong_source_rejects(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'source';root.mkdir();output=Path(temp)/'output';self.checkout(root)
            self.rejected(self.plan(root,output,'0'*40),output,'Checkout is not the triggering source')

    def test_actual_dirty_queue_checkout_rejects(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'source';root.mkdir();output=Path(temp)/'output';sha=self.checkout(root)
            (root/'netDxf/netDxf.csproj').write_text('<Project><PropertyGroup><Version>3.0.2</Version></PropertyGroup></Project>')
            self.rejected(self.plan(root,output,sha),output,'Source checkout is dirty')

    def test_real_tag_planning_policy_retained(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'source';root.mkdir();output=Path(temp)/'output';sha=self.checkout(root)
            for event,dry,wanted in (('push','false',True),('workflow_dispatch','false',True),('workflow_dispatch','true',False)):
                with self.subTest(event=event,dry=dry):
                    if output.exists():output.unlink()
                    result=self.plan(root,output,sha,event=event,dry=dry,ref_type='tag',ref_name='v3.0.1')
                    self.assertEqual(0,result.returncode,result.stderr)
                    receipt=json.loads(result.stdout)
                    self.assertEqual(wanted,receipt['publish']);self.assertEqual('v3.0.1',receipt['tag'])
                    self.assertEqual(sha,receipt['commit']);self.assertEqual('3.0.1',receipt['version'])
                    self.assertEqual('',self.git(root,'status','--porcelain'))
        # These tests only calculate plans in disposable repositories; no publication occurs.


if __name__ == '__main__':
    unittest.main()
