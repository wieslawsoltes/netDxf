"""Check consolidated workflow routing, dependencies and publication boundaries."""
import ast
import hashlib
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / '.github/workflows'


def job(text, name):
    match = re.search(r'^  ' + re.escape(name) + r':\n(.*?)(?=^  [\w-]+:\n|\Z)', text.split('jobs:\n', 1)[1], re.M | re.S)
    if match is None:
        raise AssertionError('Missing job: ' + name)
    return match[1]


def gate(section):
    match = re.search(r'^    if: (.+)$', section, re.M)
    if match is None:
        raise AssertionError('Missing job gate')
    return match[1]


def evaluate(expression, values):
    """Evaluate only the Boolean/attribute subset used by the real routing gates."""
    expression = expression.replace('&&', ' and ').replace('||', ' or ')
    expression = re.sub(r'!(?!=)', 'not ', expression)
    def visit(node):
        if isinstance(node, ast.Constant): return node.value
        if isinstance(node, ast.Name): return values[node.id]
        if isinstance(node, ast.Attribute): return visit(node.value)[node.attr]
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, ast.Not): return not visit(node.operand)
        if isinstance(node, ast.BoolOp):
            operands = [bool(visit(value)) for value in node.values]
            return all(operands) if isinstance(node.op, ast.And) else any(operands)
        if isinstance(node, ast.Compare) and len(node.ops) == 1:
            left, right = visit(node.left), visit(node.comparators[0])
            if isinstance(node.ops[0], ast.Eq): return left == right
            if isinstance(node.ops[0], ast.NotEq): return left != right
        raise AssertionError('Unsupported gate syntax: ' + ast.dump(node))
    return bool(visit(ast.parse(expression, mode='eval').body))


class CoreWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.build = (WORKFLOWS / 'ci-build.yml').read_text()
        self.release = (WORKFLOWS / 'release.yml').read_text()

    def test_exactly_two_core_workflows_and_valid_local_calls(self):
        names = {p.name for p in WORKFLOWS.iterdir() if p.suffix in ('.yml', '.yaml')}
        self.assertEqual({'ci-build.yml', 'release.yml'}, names)
        for text in (self.build, self.release):
            for path in re.findall(r'uses: (\./\.github/workflows/[^\s]+)', text):
                self.assertTrue((ROOT / path).is_file(), path)
            self.assertNotIn('dxf-conformance.yml', text)
            self.assertNotIn('nuget-publish.yml', text)
            self.assertNotIn('pull_request_target:', text)

    def test_only_ci_handles_pr_queue_and_main_push(self):
        triggers = self.build.split('on:\n', 1)[1].split('permissions:', 1)[0]
        self.assertIn('  push:\n    branches: [netstandard]\n', triggers)
        self.assertIn('  pull_request:\n    branches: [netstandard]\n', triggers)
        self.assertIn('  merge_group:\n    types: [checks_requested]\n    branches: [netstandard]\n', triggers)
        self.assertIn('  workflow_call:', triggers)
        self.assertNotIn('paths:', triggers)
        release_triggers = self.release.split('on:\n', 1)[1].split('permissions:', 1)[0]
        self.assertNotIn('pull_request:', release_triggers)
        self.assertNotIn('merge_group:', release_triggers)
        self.assertIn("  push:\n    tags: ['v[0-9]*']", release_triggers)
        self.assertIn('  release:\n    types: [published]', release_triggers)

    def test_complete_conformance_job_is_preserved_verbatim(self):
        # SHA-256 of the whole job body at upstream be67372, ignoring trailing blank lines.
        expected = 'aad4288be1be383e9b9bd6341272218f6c080b1f17ab39c27da48cda89fdfe41'
        self.assertEqual(expected, hashlib.sha256(job(self.build, 'conformance').rstrip().encode()).hexdigest())

    def test_qualification_requires_tests_and_every_runtime(self):
        self.assertIn('needs: [conformance, runtime-evidence]', job(self.build, 'qualify'))
        self.assertIn('needs: runtime-smoke', job(self.build, 'runtime-evidence'))
        self.assertIn('needs: package', job(self.build, 'runtime-smoke'))
        self.assertIn('needs: build', job(self.build, 'package'))
        for name in ('qualify', 'runtime-evidence'):
            self.assertNotIn('always()', job(self.build, name))
        self.assertNotIn('continue-on-error:', self.build)
        self.assertIn('needs: [plan, build]', job(self.release, 'github-release'))
        self.assertIn('uses: ./.github/workflows/ci-build.yml', job(self.release, 'build'))
        for text in (self.build, self.release):
            jobs = dict(re.findall(r'^  ([\w-]+):\n(.*?)(?=^  [\w-]+:\n|\Z)', text.split('jobs:\n', 1)[1], re.M | re.S))
            dependencies = {}
            for name, body in jobs.items():
                match = re.search(r'^    needs: (.+)$', body, re.M)
                dependencies[name] = re.findall(r'[\w-]+', match[1]) if match else []
                self.assertTrue(set(dependencies[name]) <= jobs.keys(), (name, dependencies[name]))
            def walk(name, active):
                self.assertNotIn(name, active, 'Cyclic job dependencies')
                for dependency in dependencies[name]: walk(dependency, active | {name})
            for name in jobs: walk(name, set())

    def test_no_duplicate_test_or_qualifier_execution(self):
        text = self.build + self.release
        self.assertEqual(1, text.count('run: python tools/run_independent_verifiers.py artifacts/conformance'))
        self.assertEqual(1, text.count('run: python tools/ci/pipeline.py qualify'))
        self.assertEqual(1, text.count('run: dotnet run --project tests/netDxf.Conformance/'))
        self.assertEqual(1, text.count('python tools/ci/runtime_release.py capture'))

    def test_published_assets_do_not_rebuild_or_create_another_draft(self):
        plan = gate(job(self.release, 'plan'))
        publish = gate(job(self.release, 'publish-nuget'))
        for event in ('release', 'workflow_dispatch', 'push', 'pull_request', 'merge_group'):
            for opted_in in (True, False):
                for manual in (True, False):
                    values = {'github': {'event_name': event}, 'inputs': {'publish_nuget': manual},
                              'vars': {'NUGET_PUBLISH_ENABLED': 'true' if opted_in else 'false'}}
                    selected = evaluate(publish, values)
                    expected = opted_in and (event == 'release' or (event == 'workflow_dispatch' and manual))
                    self.assertEqual(expected, selected)
                    if selected:
                        self.assertFalse(evaluate(plan, values), 'Publication-only event would rebuild')
        self.assertNotIn('dotnet pack', job(self.release, 'publish-nuget'))
        self.assertIn('environment: nuget', job(self.release, 'publish-nuget'))
        self.assertIn('environment: release', job(self.release, 'github-release'))

    def test_nuget_manual_dry_run_cannot_push(self):
        section = job(self.release, 'publish-nuget').split('      - name: Publish the verified package', 1)[1]
        expression = re.search(r'^        if: (.+)$', section, re.M)[1]
        for event, dry, expected in (('release', True, True), ('workflow_dispatch', True, False), ('workflow_dispatch', False, True)):
            self.assertEqual(expected, evaluate(expression, {'github': {'event_name': event}, 'inputs': {'dry_run': dry}}))
        self.assertIn('default: true', self.release.split('      dry_run:', 1)[1].split('      publish_nuget:', 1)[0])
        self.assertIn('default: false', self.release.split('      publish_nuget:', 1)[1].split('permissions:', 1)[0])

    def test_credentials_and_pinned_actions_are_scoped(self):
        self.assertNotIn('secrets.', self.build)
        self.assertNotIn('contents: write', self.build)
        self.assertEqual(1, self.release.count('contents: write'))
        self.assertEqual(1, self.release.count('secrets.NUGET_API_KEY'))
        for text in (self.build, self.release):
            for action in re.findall(r'uses: ([^\s]+)', text):
                self.assertTrue(action.startswith('./.github/workflows/') or re.fullmatch(r'[\w.-]+/[\w./-]+@[0-9a-f]{40}', action), action)
            self.assertNotIn('persist-credentials: true', text)


if __name__ == '__main__': unittest.main()
