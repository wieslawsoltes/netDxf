#!/usr/bin/env python3
"""Revalidate qualified assets and the live tag immediately before publication."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import subprocess

import pipeline


def remote_tag_commit(repository: str, tag: str) -> str:
    """Peel a live GitHub tag without trusting response URLs or the checkout's refs."""
    pipeline.require(re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository) is not None,
                     'Invalid repository identity')
    pipeline.require(tag.startswith('v'), 'Publication requires a version tag')
    pipeline.version(tag[1:])
    endpoint = f'repos/{repository}/git/ref/tags/{tag}'
    seen: set[str] = set()
    for _ in range(16):
        # gh uses the job-scoped token. No credentials are written to the checkout.
        response = json.loads(subprocess.check_output(
            ['gh', 'api', endpoint], text=True, timeout=30))
        obj = response.get('object') if isinstance(response, dict) else None
        pipeline.require(isinstance(obj, dict), 'Missing tag target object')
        sha, kind = obj.get('sha'), obj.get('type')
        pipeline.require(isinstance(sha, str) and re.fullmatch(r'[0-9a-f]{40}', sha) is not None,
                         'Invalid tag target identity')
        if kind == 'commit':
            return sha
        pipeline.require(kind == 'tag' and sha not in seen, 'Non-commit or cyclic tag target')
        seen.add(sha)
        endpoint = f'repos/{repository}/git/tags/{sha}'
    raise ValueError('Annotated tag nesting limit exceeded')


def verify_publication(directory: Path, repository: str, tag: str) -> None:
    pipeline.require(pipeline.REPOSITORY == f'https://github.com/{repository}',
                     'Publication repository does not match the package repository')
    pipeline.require(os.environ.get('RELEASE_TAG') == tag, 'Inconsistent publication tag')
    # Checks the complete receipt, packages, symbols, checksums, and clean source.
    pipeline.verify_release(directory)
    build = json.loads((directory / 'build.json').read_text(encoding='utf-8'))
    pipeline.require(remote_tag_commit(repository, tag) == build['commit'],
                     'Live release tag moved away from the qualified source')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, default=pipeline.ROOT / 'artifacts/packages')
    args = parser.parse_args()
    verify_publication(args.directory, os.environ['GITHUB_REPOSITORY'], os.environ['RELEASE_TAG'])
    print('PASS: qualified assets and live remote tag refer to the same source commit')


if __name__ == '__main__':
    main()
