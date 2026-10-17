import copy
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/ci'))
import setup_nuget as s


REPOSITORY = 'wieslawsoltes/netDxf'
PREFIX = 'repos/' + REPOSITORY
TAG = 'v4.0.0-preview.1'


def environment(name, mode='custom'):
    return {
        'id': 123,
        'name': name,
        'can_admins_bypass': False,
        'protection_rules': [
            {'id': 1, 'type': 'required_reviewers', 'prevent_self_review': True,
             'reviewers': [{'type': 'User', 'reviewer': {'id': 42, 'login': 'maintainer'}}]},
            {'id': 2, 'type': 'wait_timer', 'wait_timer': 15},
            {'id': 3, 'type': 'branch_policy'},
            {'id': 4, 'type': 'custom_rule', 'app': {'id': 17}},
        ],
        'deployment_branch_policy': None if mode == 'unrestricted' else {
            'protected_branches': mode == 'protected',
            'custom_branch_policies': mode == 'custom',
        },
    }


class FakeGitHub(s.GitHubApi):
    """Stateful API double: exercise setup against persisted settings, not call order."""

    def __init__(self, existing=True):
        self.environments = ({name: environment(name) for name in s.ENVIRONMENTS} if existing else {})
        self.rules = ({name: [dict(s.TAG_POLICY)] for name in s.ENVIRONMENTS} if existing else {})
        self.variables = {}
        self.calls = []
        self.failures = {}
        self.page_size = 100
        self.ignore_variable_writes = False
        self.before_request = None

    @property
    def writes(self):
        return [call for call in self.calls if call[0] != 'GET']

    def pages(self, key, entries):
        return [{key: copy.deepcopy(entries[index:index + self.page_size]), 'total_count': len(entries)}
                for index in range(0, max(len(entries), 1), self.page_size)]

    def request(self, method, endpoint, *, body=None, paginate=False):
        self.calls.append((method, endpoint, copy.deepcopy(body), paginate))
        if self.before_request:
            self.before_request(method, endpoint)
        if (method, endpoint) in self.failures:
            raise self.failures[(method, endpoint)]
        if endpoint == PREFIX + '/environments?per_page=100' and method == 'GET':
            assert paginate
            return self.pages('environments', list(self.environments.values()))
        if endpoint == PREFIX + '/actions/variables?per_page=100' and method == 'GET':
            assert paginate
            return self.pages('variables', [{'name': name, 'value': value} for name, value in self.variables.items()])
        env_prefix = PREFIX + '/environments/'
        if endpoint.startswith(env_prefix):
            relative = endpoint[len(env_prefix):]
            name, _, operation = relative.partition('/')
            name = name.lower()
            if not operation and method == 'GET':
                if name not in self.environments:
                    raise s.SetupError('GET environment failed: HTTP 404')
                return copy.deepcopy(self.environments[name])
            if not operation and method == 'PUT':
                # Emulate the dangerous upsert accurately: resetting an existing
                # environment would destroy reviewers and fail preservation tests.
                self.environments[name] = {'name': name, 'protection_rules': [], **copy.deepcopy(body)}
                self.rules[name] = []
                return copy.deepcopy(self.environments[name])
            if operation == 'deployment-branch-policies?per_page=100' and method == 'GET':
                assert paginate
                return self.pages('branch_policies', self.rules[name])
            if operation == 'deployment-branch-policies' and method == 'POST':
                self.rules[name].append(copy.deepcopy(body))
                return dict(body)
        if endpoint.startswith(PREFIX + '/actions/variables') and method in ('POST', 'PATCH'):
            if not self.ignore_variable_writes:
                self.variables[body['name']] = body['value']
            return None
        raise AssertionError((method, endpoint, body, paginate))


class SetupNugetTests(unittest.TestCase):
    def configure(self, api, **kwargs):
        s.configure(REPOSITORY, 'wieslawsoltes', TAG, api=api, report=lambda _: None, **kwargs)

    def test_new_environments_are_tag_restricted_and_opt_in_is_last(self):
        api = FakeGitHub(existing=False)
        self.configure(api)
        self.assertEqual({'NUGET_USER': 'wieslawsoltes', 'NUGET_PUBLISH_ENABLED': 'true'}, api.variables)
        for name in s.ENVIRONMENTS:
            self.assertEqual(s.NEW_ENVIRONMENT['deployment_branch_policy'], api.environments[name]['deployment_branch_policy'])
            self.assertEqual([s.TAG_POLICY], api.rules[name])
        self.assertEqual(6, len(api.writes))
        self.assertEqual({'name': 'NUGET_PUBLISH_ENABLED', 'value': 'true'}, api.writes[-1][2])
        self.assertEqual(['PUT', 'POST', 'PUT', 'POST', 'POST', 'POST'], [entry[0] for entry in api.writes])

    def test_completed_setup_is_idempotent(self):
        api = FakeGitHub(existing=False)
        self.configure(api)
        before = copy.deepcopy((api.environments, api.rules, api.variables))
        api.calls.clear()
        self.configure(api)
        self.assertFalse(api.writes)
        self.assertEqual(before, (api.environments, api.rules, api.variables))

    def test_existing_reviewers_timers_custom_rules_and_narrower_tag_rules_are_preserved(self):
        api = FakeGitHub()
        api.rules['release'] = [{'name': 'v4.*', 'type': 'tag'}, {'name': 'main', 'type': 'branch'}]
        api.variables = {'NUGET_USER': 'old-profile', 'NUGET_PUBLISH_ENABLED': 'false', 'UNRELATED': 'keep'}
        before = copy.deepcopy((api.environments, api.rules))
        self.configure(api)
        self.assertEqual(before, (api.environments, api.rules))
        self.assertEqual('keep', api.variables['UNRELATED'])
        self.assertEqual(['PATCH', 'PATCH'], [entry[0] for entry in api.writes])
        self.assertEqual('NUGET_PUBLISH_ENABLED', api.writes[-1][2]['name'])

    def test_existing_unrestricted_environments_are_not_reconfigured(self):
        api = FakeGitHub()
        api.environments = {name: environment(name, 'unrestricted') for name in s.ENVIRONMENTS}
        before = copy.deepcopy(api.environments)
        self.configure(api)
        self.assertEqual(before, api.environments)
        self.assertTrue(all('/actions/variables' in call[1] for call in api.writes))

    def test_conflicting_existing_restrictions_fail_before_any_write(self):
        cases = [
            ('custom', []),
            ('custom', [{'type': 'branch', 'name': 'v*'}]),
            ('custom', [{'type': 'tag', 'name': 'v3.*'}]),
            ('custom', [{'name': 'v*'}]),
            ('protected', [dict(s.TAG_POLICY)]),
        ]
        for mode, rules in cases:
            with self.subTest(mode=mode, rules=rules):
                api = FakeGitHub(existing=False)
                api.environments['nuget'] = environment('nuget', mode)
                api.rules['nuget'] = rules
                before = copy.deepcopy((api.environments, api.rules))
                with self.assertRaises(s.SetupError):
                    self.configure(api)
                self.assertFalse(api.writes)  # Do not create release before checking nuget.
                self.assertEqual(before, (api.environments, api.rules))

    def test_dry_run_reads_and_plans_without_mutations(self):
        api = FakeGitHub(existing=False)
        messages = []
        s.configure(REPOSITORY, 'wieslawsoltes', TAG, api=api, report=messages.append, dry_run=True)
        self.assertTrue(api.calls)
        self.assertFalse(api.writes)
        self.assertFalse(api.environments)
        self.assertFalse(api.variables)
        self.assertTrue(any('Would create environment nuget' in line for line in messages))
        self.assertTrue(any('NUGET_PUBLISH_ENABLED to true' in line for line in messages))

    def test_all_preflight_permission_and_not_found_errors_are_fatal(self):
        endpoints = [PREFIX + '/environments?per_page=100', PREFIX + '/actions/variables?per_page=100',
                     PREFIX + '/environments/release',
                     PREFIX + '/environments/nuget/deployment-branch-policies?per_page=100']
        for endpoint in endpoints:
            for status in (403, 404, 500):
                with self.subTest(endpoint=endpoint, status=status):
                    api = FakeGitHub()
                    api.failures[('GET', endpoint)] = s.SetupError(f'permission/transport failure HTTP {status}')
                    with self.assertRaisesRegex(s.SetupError, str(status)):
                        self.configure(api)
                    self.assertFalse(api.writes)

    def test_failed_environment_or_rule_write_never_enables_publication(self):
        failures = [('PUT', PREFIX + '/environments/nuget'),
                    ('POST', PREFIX + '/environments/release/deployment-branch-policies')]
        for operation in failures:
            with self.subTest(operation=operation):
                api = FakeGitHub(existing=False)
                api.failures[operation] = s.SetupError('administration permission denied')
                with self.assertRaisesRegex(s.SetupError, 'permission denied'):
                    self.configure(api)
                self.assertFalse(api.variables)
                self.assertFalse(any('/actions/variables' in call[1] for call in api.writes))

    def test_variable_failure_or_failed_readback_stops_before_opt_in(self):
        for ignore_write in (False, True):
            with self.subTest(ignore_write=ignore_write):
                api = FakeGitHub()
                api.ignore_variable_writes = ignore_write
                if not ignore_write:
                    api.failures[('POST', PREFIX + '/actions/variables')] = s.SetupError('variables write denied')
                with self.assertRaises(s.SetupError):
                    self.configure(api)
                self.assertNotIn('NUGET_PUBLISH_ENABLED', api.variables)
                self.assertTrue(all(call[2].get('name') != 'NUGET_PUBLISH_ENABLED' for call in api.writes))

    def test_paginated_inventory_preserves_later_environments_and_variables(self):
        api = FakeGitHub()
        api.environments = {'github-pages': environment('github-pages'), **api.environments}
        api.variables = {'UNRELATED': 'first', 'NUGET_USER': 'wieslawsoltes', 'NUGET_PUBLISH_ENABLED': 'true'}
        api.page_size = 1
        self.configure(api)
        self.assertFalse(api.writes)

    def test_environment_created_during_preflight_is_not_overwritten(self):
        api = FakeGitHub()
        del api.environments['release']
        del api.rules['release']
        checks = 0
        created = environment('release')

        def inject(method, endpoint):
            nonlocal checks
            if method == 'GET' and endpoint == PREFIX + '/environments?per_page=100':
                checks += 1
                if checks == 2:
                    api.environments['release'] = copy.deepcopy(created)
                    api.rules['release'] = [{'name': 'v4.*', 'type': 'tag'}]

        api.before_request = inject
        self.configure(api)
        self.assertEqual(created, api.environments['release'])
        self.assertFalse(any('/environments/' in call[1] for call in api.writes))

    def test_environment_names_are_case_insensitive(self):
        api = FakeGitHub()
        api.environments['nuget']['name'] = 'NuGet'
        self.configure(api)
        self.assertFalse(any(call[0] == 'PUT' for call in api.writes))
        self.assertEqual('NuGet', api.environments['nuget']['name'])

    def test_malformed_environment_or_variable_data_fails_closed(self):
        for invalid in ('policy-missing', 'policy-invalid', 'name-missing', 'variable-invalid'):
            with self.subTest(invalid=invalid):
                api = FakeGitHub()
                if invalid == 'policy-missing':
                    del api.environments['nuget']['deployment_branch_policy']
                elif invalid == 'policy-invalid':
                    api.environments['nuget']['deployment_branch_policy']['protected_branches'] = 'false'
                elif invalid == 'name-missing':
                    del api.environments['nuget']['name']
                else:
                    api.variables['NUGET_USER'] = None
                with self.assertRaises(s.SetupError):
                    self.configure(api)
                self.assertFalse(api.writes)

    def test_invalid_identities_and_tags_do_not_call_github(self):
        invalid = [
            ('https://github.com/wieslawsoltes/netDxf', 'wieslawsoltes', TAG),
            ('a/b/c', 'wieslawsoltes', TAG),
            ('a/..', 'wieslawsoltes', TAG),
            ('a/b\n', 'wieslawsoltes', TAG),
            ('--repo/name', 'wieslawsoltes', TAG),
            (REPOSITORY, 'user@example.org', TAG),
            (REPOSITORY, ' username ', TAG),
            (REPOSITORY, '$(anything)', TAG),
            (REPOSITORY, 'user\nname', TAG),
            (REPOSITORY, 'x' * 65, TAG),
            (REPOSITORY, '', TAG),
            (REPOSITORY, 'wieslawsoltes', 'main'),
            (REPOSITORY, 'wieslawsoltes', 'v4.0.0/../main'),
            (REPOSITORY, 'wieslawsoltes', 'v4.0.0-preview.01'),
        ]
        for repository, username, tag in invalid:
            with self.subTest(repository=repository, username=username, tag=tag):
                api = FakeGitHub()
                with self.assertRaises(s.SetupError):
                    s.configure(repository, username, tag, api=api)
                self.assertFalse(api.calls)

    def test_default_tag_comes_from_shared_version(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(s, 'ROOT', Path(directory)):
            path = Path(directory) / 'Directory.Build.props'
            path.write_text('<Project><PropertyGroup><Version>5.1.0-preview.2</Version></PropertyGroup></Project>')
            self.assertEqual('v5.1.0-preview.2', s.default_tag())
            path.unlink()
            with self.assertRaisesRegex(s.SetupError, 'provide --tag'):
                s.default_tag()

    def test_cli_propagates_explicit_tag_and_dry_run(self):
        with patch.object(s, 'configure') as configure:
            self.assertEqual(0, s.main(['--nuget-user', 'wieslawsoltes', '--tag', TAG, '--dry-run']))
        configure.assert_called_once_with(REPOSITORY, 'wieslawsoltes', TAG, dry_run=True)
        with patch.object(s, 'configure', side_effect=s.SetupError('denied')), patch('sys.stderr', new=io.StringIO()) as stderr:
            self.assertEqual(1, s.main(['--nuget-user', 'wieslawsoltes', '--tag', TAG]))
            self.assertIn('ERROR: denied', stderr.getvalue())


class GitHubApiTests(unittest.TestCase):
    def test_structured_subprocess_arguments_and_json_stdin(self):
        response = subprocess.CompletedProcess(['gh'], 0, stdout='{}', stderr='')
        with patch.object(s.subprocess, 'run', return_value=response) as run:
            s.GitHubApi().request('PUT', PREFIX + '/environments/nuget', body=s.NEW_ENVIRONMENT)
        command = run.call_args.args[0]
        self.assertIsInstance(command, list)
        self.assertEqual(['gh', 'api', '--hostname', 'github.com'], command[:4])
        self.assertIn('--input', command)
        self.assertEqual(s.NEW_ENVIRONMENT, json.loads(run.call_args.kwargs['input']))
        self.assertIs(False, run.call_args.kwargs['shell'])
        self.assertEqual(45, run.call_args.kwargs['timeout'])
        self.assertTrue(run.call_args.kwargs['capture_output'])

    def test_pagination_collects_all_pages(self):
        response = subprocess.CompletedProcess(['gh'], 0,
            stdout=json.dumps([{'environments': [{'name': 'release'}]}, {'environments': [{'name': 'nuget'}]}]), stderr='')
        with patch.object(s.subprocess, 'run', return_value=response) as run:
            actual = s.GitHubApi().records(PREFIX + '/environments', 'environments')
        self.assertEqual(['release', 'nuget'], [item['name'] for item in actual])
        self.assertIn('--paginate', run.call_args.args[0])
        self.assertIn('--slurp', run.call_args.args[0])

    def test_nonzero_exit_is_not_treated_as_missing_environment(self):
        response = subprocess.CompletedProcess(['gh'], 1, stdout='', stderr='HTTP 404: resource not accessible by integration')
        with patch.object(s.subprocess, 'run', return_value=response) as run:
            with self.assertRaisesRegex(s.SetupError, '404.*not accessible'):
                s.configure(REPOSITORY, 'wieslawsoltes', TAG)
        self.assertEqual(1, run.call_count)
        self.assertIn('GET', run.call_args.args[0])

    def test_invalid_json_and_collection_responses_are_errors(self):
        for text in ('not-json', '{}', 'null', '[]', '[{}]', '[{"environments": [null]}]'):
            with self.subTest(text=text):
                response = subprocess.CompletedProcess(['gh'], 0, stdout=text, stderr='')
                with patch.object(s.subprocess, 'run', return_value=response), self.assertRaises(s.SetupError):
                    s.GitHubApi().records(PREFIX + '/environments', 'environments')

    def test_missing_cli_timeout_and_empty_mutation_response(self):
        for error in (FileNotFoundError('gh'), subprocess.TimeoutExpired(['gh'], 45)):
            with self.subTest(error=error):
                with patch.object(s.subprocess, 'run', side_effect=error), self.assertRaises(s.SetupError):
                    s.GitHubApi().request('GET', PREFIX + '/environments')
        response = subprocess.CompletedProcess(['gh'], 0, stdout='', stderr='')
        with patch.object(s.subprocess, 'run', return_value=response):
            self.assertIsNone(s.GitHubApi().request('PATCH', PREFIX + '/actions/variables/NUGET_USER',
                                                  body={'name': 'NUGET_USER', 'value': 'wieslawsoltes'}))


if __name__ == '__main__':
    unittest.main()
