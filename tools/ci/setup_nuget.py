#!/usr/bin/env python3
"""Configure GitHub environments and variables for NuGet trusted publishing.

The NuGet policy itself is configured on nuget.org; see doc/CI-RELEASE.md.
This helper uses the existing GitHub CLI authentication and never publishes.
"""
from __future__ import annotations

import argparse
from collections.abc import Callable
import fnmatch
import json
from pathlib import Path
import re
import subprocess
import sys
from typing import Any
from urllib.parse import quote
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_REPOSITORY = 'wieslawsoltes/netDxf'
ENVIRONMENTS = ('release', 'nuget')
TAG_POLICY = {'name': 'v*', 'type': 'tag'}
NEW_ENVIRONMENT = {
    'deployment_branch_policy': {
        'protected_branches': False,
        'custom_branch_policies': True,
    },
}


class SetupError(RuntimeError):
    """An invalid configuration or an unsuccessful GitHub operation."""


class GitHubApi:
    """Small, fail-closed wrapper over authenticated ``gh api`` requests."""

    def request(self, method: str, endpoint: str, *, body: dict[str, Any] | None = None,
                paginate: bool = False) -> Any:
        command = [
            'gh', 'api', '--hostname', 'github.com', '--method', method,
            '--header', 'Accept: application/vnd.github+json',
            '--header', 'X-GitHub-Api-Version: 2022-11-28', endpoint,
        ]
        if paginate:
            if method != 'GET' or body is not None:
                raise SetupError('Pagination is only supported for read-only requests.')
            command.extend(['--paginate', '--slurp'])
        if body is not None:
            command.extend(['--input', '-'])
        try:
            result = subprocess.run(
                command, input=json.dumps(body) if body is not None else None,
                text=True, capture_output=True, check=False, shell=False, timeout=45,
            )
        except FileNotFoundError as error:
            raise SetupError("GitHub CLI 'gh' is not installed; install it and run 'gh auth login'.") from error
        except subprocess.TimeoutExpired as error:
            raise SetupError(f'GitHub API {method} {endpoint} timed out; verify its state before retrying.') from error
        if result.returncode:
            detail = result.stderr.strip() or f'gh exited with status {result.returncode}'
            raise SetupError(f'GitHub API {method} {endpoint} failed: {detail}')
        if not result.stdout.strip():
            return None
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise SetupError(f'GitHub API {method} {endpoint} returned invalid JSON.') from error

    def records(self, endpoint: str, key: str) -> list[dict[str, Any]]:
        pages = self.request('GET', endpoint + '?per_page=100', paginate=True)
        if not isinstance(pages, list) or not pages:
            raise SetupError(f'GitHub API {endpoint} returned an invalid paginated response.')
        result = []
        for page in pages:
            items = page.get(key) if isinstance(page, dict) else None
            if not isinstance(items, list) or any(not isinstance(item, dict) for item in items):
                raise SetupError(f'GitHub API {endpoint} returned an invalid {key} collection.')
            result.extend(items)
        return result


def validate_inputs(repository: str, nuget_user: str, tag: str) -> None:
    owner, separator, name = repository.partition('/')
    if (not separator or not re.fullmatch(r'[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?', owner)
            or not re.fullmatch(r'[A-Za-z0-9_.-]{1,100}', name) or name in {'.', '..'}):
        raise SetupError('Repository must be a GitHub owner/name, without a URL, extra slash or whitespace.')
    if not re.fullmatch(r'[A-Za-z0-9_.-]{1,64}', nuget_user) or nuget_user in {'.', '..'}:
        raise SetupError('NuGet user must be a profile username, not an email address, URL or whitespace.')
    numeric = r'(?:0|[1-9][0-9]*)'
    suffix = r'(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?'
    if not re.fullmatch(rf'v{numeric}\.{numeric}\.{numeric}{suffix}', tag):
        raise SetupError('Release tag must be vMAJOR.MINOR.PATCH with an optional prerelease suffix.')
    for identifier in tag.split('-', 1)[1].split('.') if '-' in tag else []:
        if identifier.isdecimal() and len(identifier) > 1 and identifier.startswith('0'):
            raise SetupError('Numeric prerelease identifiers must not have leading zeroes.')


def default_tag() -> str:
    path = ROOT / 'Directory.Build.props'
    try:
        version = ET.parse(path).findtext('./PropertyGroup/Version')
    except (OSError, ET.ParseError) as error:
        raise SetupError(f'Cannot read the shared release version from {path}; provide --tag explicitly.') from error
    if not version:
        raise SetupError(f'Missing shared Version in {path}; provide --tag explicitly.')
    return 'v' + version


def environments(api: GitHubApi, repository: str) -> dict[str, dict[str, Any]]:
    result = {}
    # A failed GET must never be mistaken for an absent environment. A complete
    # successful collection read is the only evidence that creation is needed.
    for item in api.records(f'repos/{repository}/environments', 'environments'):
        name = item.get('name')
        if not isinstance(name, str) or not name or name.casefold() in result:
            raise SetupError('GitHub returned missing or duplicate environment names.')
        result[name.casefold()] = item
    return result


def variables(api: GitHubApi, repository: str) -> dict[str, str]:
    result = {}
    for item in api.records(f'repos/{repository}/actions/variables', 'variables'):
        name, value = item.get('name'), item.get('value')
        if not isinstance(name, str) or not name or not isinstance(value, str) or name.upper() in result:
            raise SetupError('GitHub returned invalid or duplicate repository variables.')
        result[name.upper()] = value
    return result


def matches_tag(pattern: str, tag: str) -> bool:
    # Release tags contain no slash. For these names the common Ruby fnmatch
    # syntax agrees with Python's fnmatchcase. Do not claim to verify patterns
    # whose escaping or character-class syntax may have different semantics.
    if any(token in pattern for token in ('/', '\\', '{', '}', '[^')):
        return False
    return fnmatch.fnmatchcase(tag, pattern)


def environment_endpoint(repository: str, name: str) -> str:
    return f'repos/{repository}/environments/{quote(name, safe="")}'


def inspect_environment(api: GitHubApi, repository: str, name: str, tag: str,
                        *, allow_empty_new: bool = False) -> list[dict[str, Any]]:
    endpoint = environment_endpoint(repository, name)
    detail = api.request('GET', endpoint)
    if (not isinstance(detail, dict) or not isinstance(detail.get('name'), str)
            or detail['name'].casefold() != name.casefold() or 'deployment_branch_policy' not in detail):
        raise SetupError(f'GitHub returned incomplete environment settings for {name}.')
    policy = detail['deployment_branch_policy']
    if policy is None:
        if allow_empty_new:
            raise SetupError(f'New environment {name} did not retain the requested custom deployment policy.')
        return []  # Existing unrestricted environments are preserved unchanged.
    if (not isinstance(policy, dict) or type(policy.get('protected_branches')) is not bool
            or type(policy.get('custom_branch_policies')) is not bool
            or policy['protected_branches'] == policy['custom_branch_policies']):
        raise SetupError(f'Environment {name} has an unsupported deployment restriction; it was not changed.')
    if policy['protected_branches']:
        raise SetupError(
            f'Environment {name} uses protected-branches-only deployment restrictions; '
            f'tag {tag} cannot be verified as permitted. Review the restriction in GitHub; it was not changed.')
    rules = api.records(endpoint + '/deployment-branch-policies', 'branch_policies')
    if any(not isinstance(rule.get('name'), str) or not rule['name'] for rule in rules):
        raise SetupError(f'Environment {name} returned an invalid deployment rule.')
    if not rules and allow_empty_new:
        return rules
    if not any(rule.get('type') == 'tag' and matches_tag(rule['name'], tag) for rule in rules):
        raise SetupError(
            f'Environment {name} has no verified tag rule allowing {tag}. '
            'Review its existing deployment rules in GitHub; no restrictions were broadened.')
    return rules


def create_environment(api: GitHubApi, repository: str, name: str, tag: str,
                       report: Callable[[str], None]) -> None:
    # Recheck the collection immediately before creation so a newly observed
    # environment is preserved rather than overwritten by the API's PUT upsert.
    current = environments(api, repository)
    if name in current:
        inspect_environment(api, repository, current[name]['name'], tag)
        report(f'Preserved environment {name}, which was created during setup.')
        return
    endpoint = environment_endpoint(repository, name)
    api.request('PUT', endpoint, body=NEW_ENVIRONMENT)
    rules = inspect_environment(api, repository, name, tag, allow_empty_new=True)
    if not rules:
        api.request('POST', endpoint + '/deployment-branch-policies', body=TAG_POLICY)
    # Read back before any new opt-in. An environment with no tag rules must not
    # be described as restricted: the UI can permit all refs in that state. A
    # failed tag-policy write stops setup so the partial result can be inspected.
    inspect_environment(api, repository, name, tag)
    report(f'Created and verified environment {name} for release tags.')


def set_variable(api: GitHubApi, repository: str, name: str, value: str,
                 report: Callable[[str], None]) -> None:
    current = variables(api, repository)
    if current.get(name) == value:
        report(f'Repository variable {name} is already configured.')
        return
    endpoint = f'repos/{repository}/actions/variables'
    method = 'POST'
    if name in current:
        endpoint += '/' + name
        method = 'PATCH'
    api.request(method, endpoint, body={'name': name, 'value': value})
    if variables(api, repository).get(name) != value:
        raise SetupError(f'Repository variable {name} did not match after the update.')
    report(f'Configured repository variable {name}.')


def configure(repository: str, nuget_user: str, tag: str, *, dry_run: bool = False,
              api: GitHubApi | None = None, report: Callable[[str], None] = print) -> None:
    validate_inputs(repository, nuget_user, tag)
    api = api if api is not None else GitHubApi()
    current_environments = environments(api, repository)
    current_variables = variables(api, repository)
    missing = []
    # Complete all preflight reads and restriction checks before the first write.
    for name in ENVIRONMENTS:
        if name in current_environments:
            inspect_environment(api, repository, current_environments[name]['name'], tag)
            report(f'Preserve existing environment {name} and all of its protection rules.')
        else:
            missing.append(name)
    desired = [('NUGET_USER', nuget_user), ('NUGET_PUBLISH_ENABLED', 'true')]
    if dry_run:
        for name in missing:
            report(f'Would create environment {name} restricted to tags matching v*.')
        for name, value in desired:
            if current_variables.get(name) != value:
                report(f'Would set repository variable {name} to {value}.')
        report(f'DRY RUN: repository settings checked for {tag}; no changes made.')
        return
    for name in missing:
        create_environment(api, repository, name, tag, report)
    # No environment is PUT/PATCHed once it is known to exist. This preserves
    # reviewers, self-review settings, timers, custom rules and bypass settings.
    for name in ENVIRONMENTS:
        inspect_environment(api, repository, name, tag)
    set_variable(api, repository, 'NUGET_USER', nuget_user, report)
    # Opt in last, after every environment and the username have been verified.
    set_variable(api, repository, 'NUGET_PUBLISH_ENABLED', 'true', report)
    report(f'PASS: GitHub settings configured for {repository} and {tag}. No package was published.')


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default=DEFAULT_REPOSITORY, help='GitHub owner/name (default: %(default)s)')
    parser.add_argument('--nuget-user', required=True, help='NuGet profile username that created the trusted policy')
    parser.add_argument('--tag', help='Release tag to check against existing restrictions (default: shared Version)')
    parser.add_argument('--dry-run', action='store_true', help='Read settings and print the plan without writing')
    args = parser.parse_args(argv)
    try:
        configure(args.repo, args.nuget_user, args.tag if args.tag is not None else default_tag(),
                  dry_run=args.dry_run)
    except SetupError as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
