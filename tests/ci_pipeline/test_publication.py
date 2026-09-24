import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import publication as p


class PublicationTests(unittest.TestCase):
    def test_live_lightweight_and_nested_annotated_tags(self):
        for kinds in (['commit'], ['tag', 'commit'], ['tag', 'tag', 'commit']):
            replies = [json.dumps({'object': {'type': kind, 'sha': hex(i + 1)[2:] * 40}})
                       for i, kind in enumerate(kinds)]
            with self.subTest(kinds=kinds), patch.object(p.subprocess, 'check_output', side_effect=replies) as call:
                self.assertEqual(hex(len(kinds))[2:] * 40, p.remote_tag_commit('wieslawsoltes/netDxf', 'v3.0.2'))
                self.assertEqual(['gh', 'api', 'repos/wieslawsoltes/netDxf/git/ref/tags/v3.0.2'], call.call_args_list[0].args[0])
                for i in range(1, len(kinds)):
                    self.assertEqual(['gh', 'api', f'repos/wieslawsoltes/netDxf/git/tags/{hex(i)[2:] * 40}'], call.call_args_list[i].args[0])
                    self.assertEqual(30, call.call_args_list[i].kwargs['timeout'])

    def test_untrusted_tags_and_objects_fail_closed(self):
        for response in ([], {}, {'object': None}, {'object': {'type': 'tree', 'sha': 'a' * 40}},
                         {'object': {'type': 'commit', 'sha': '../bad'}}, {'object': {'type': 'commit', 'sha': 1}}):
            with self.subTest(response=response), patch.object(p.subprocess, 'check_output', return_value=json.dumps(response)), self.assertRaises(ValueError):
                p.remote_tag_commit('wieslawsoltes/netDxf', 'v3.0.2')
        for repo, tag in (('wrong/repo/extra', 'v3.0.2'), ('https://evil/x', 'v3.0.2'),
                          ('wieslawsoltes/netDxf', 'main'), ('wieslawsoltes/netDxf', 'v3.0.2/../../x')):
            with self.subTest(repo=repo, tag=tag), patch.object(p.subprocess, 'check_output') as call, self.assertRaises(ValueError):
                try: p.remote_tag_commit(repo, tag)
                finally: call.assert_not_called()

    def test_cycles_depth_and_transport_failures(self):
        repeated = json.dumps({'object': {'type': 'tag', 'sha': 'a' * 40}})
        with patch.object(p.subprocess, 'check_output', return_value=repeated), self.assertRaises(ValueError):
            p.remote_tag_commit('wieslawsoltes/netDxf', 'v3.0.2')
        replies = [json.dumps({'object': {'type': 'tag', 'sha': f'{i:040x}'}}) for i in range(16)]
        with patch.object(p.subprocess, 'check_output', side_effect=replies), self.assertRaises(ValueError):
            p.remote_tag_commit('wieslawsoltes/netDxf', 'v3.0.2')
        for failure in (subprocess.CalledProcessError(1, ['gh']), subprocess.TimeoutExpired(['gh'], 30)):
            with patch.object(p.subprocess, 'check_output', side_effect=failure), self.assertRaises(type(failure)):
                p.remote_tag_commit('wieslawsoltes/netDxf', 'v3.0.2')

    def test_moved_tag_and_asset_failure_stop_publication(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp); (directory / 'build.json').write_text(json.dumps({'commit': 'a' * 40}))
            with patch.dict(os.environ, {'RELEASE_TAG': 'v3.0.2'}), patch.object(p.pipeline, 'verify_release') as verify, patch.object(p, 'remote_tag_commit', return_value='a' * 40) as remote:
                p.verify_publication(directory, 'wieslawsoltes/netDxf', 'v3.0.2')
                verify.assert_called_once_with(directory)
                remote.return_value = 'b' * 40
                with self.assertRaisesRegex(ValueError, 'Live release tag moved'):
                    p.verify_publication(directory, 'wieslawsoltes/netDxf', 'v3.0.2')
                remote.reset_mock(); verify.side_effect = ValueError('damaged assets')
                with self.assertRaisesRegex(ValueError, 'damaged assets'):
                    p.verify_publication(directory, 'wieslawsoltes/netDxf', 'v3.0.2')
                remote.assert_not_called()
                with self.assertRaises(ValueError): p.verify_publication(directory, 'wrong/repo', 'v3.0.2')
                with self.assertRaises(ValueError): p.verify_publication(directory, 'wieslawsoltes/netDxf', 'v3.0.3')

    def test_real_workflows_gate_both_publication_steps(self):
        for job, publish in (('github-release', 'gh release create'), ('publish-nuget', 'dotnet nuget push')):
            workflow = (ROOT / '.github/workflows/release.yml').read_text()
            text = workflow.split('  ' + job + ':\n', 1)[1].split('\n  publish-nuget:', 1)[0]
            self.assertEqual(1, text.count('python tools/ci/publication.py'))
            self.assertLess(text.index('python tools/ci/publication.py'), text.index(publish))
            before = text[:text.index('python tools/ci/publication.py')].rsplit('- name:', 1)[-1]
            self.assertIn('GH_TOKEN:', before)
            self.assertIn('RELEASE_TAG:', before)


if __name__ == '__main__': unittest.main()
