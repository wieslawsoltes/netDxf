"""Real release-plan subprocesses for merge queues, source identity and tags."""
import json
import os
from pathlib import Path
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
        shutil.copyfile(ROOT/'tools/ci/package_catalog.py', root/'tools/ci/package_catalog.py')
        (root/'netDxf').mkdir()
        (root/'Directory.Build.props').write_text('<Project><PropertyGroup><Version>4.0.0-preview.1</Version></PropertyGroup></Project>')
        (root/'netDxf/netDxf.csproj').write_text('<Project/>')
        (root/'.gitignore').write_text('__pycache__/\n')
        self.git(root, 'init', '-q')
        self.git(root, 'config', 'user.name', 'Queue test')
        self.git(root, 'config', 'user.email', 'queue-test@example.invalid')
        self.git(root, 'add', '.')
        self.git(root, 'commit', '-qm', 'Fixture source')
        sha = self.git(root, 'rev-parse', 'HEAD')
        self.git(root, 'update-ref', 'refs/remotes/origin/netstandard', sha)
        self.git(root, 'tag', 'v4.0.0-preview.1')
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

    def test_actual_merge_queue_cli_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)/'source'; root.mkdir(); output=Path(temp)/'output'
            sha = self.checkout(root); result = self.plan(root, output, sha)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual({'version':'4.0.0-preview.1.ci.77', 'tag':'', 'publish':False, 'prerelease':True,
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
                        result=self.plan(root,output,sha,event=event,dry=dry,ref_type=kind,ref_name='v4.0.0-preview.1')
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
                    result=self.plan(root,output,sha,event=event,dry=dry,ref_type='tag',ref_name='v4.0.0-preview.1')
                    self.assertEqual(0,result.returncode,result.stderr)
                    receipt=json.loads(result.stdout)
                    self.assertEqual(wanted,receipt['publish']);self.assertEqual('v4.0.0-preview.1',receipt['tag'])
                    self.assertEqual(sha,receipt['commit']);self.assertEqual('4.0.0-preview.1',receipt['version'])
                    self.assertEqual('',self.git(root,'status','--porcelain'))
        # These tests only calculate plans in disposable repositories; no publication occurs.


if __name__ == '__main__':
    unittest.main()
