#!/usr/bin/env python3
"""Build/release validation. No publication or credentials are handled here."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import package_catalog

# These two legacy fields describe the core runtime matrix, not the entire release.
TFMS = package_catalog.CORE_FRAMEWORKS
PACKAGE = package_catalog.CORE
REPOSITORY = 'https://github.com/wieslawsoltes/netDxf'
VERSION = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\Z')
NUSPEC_FRAMEWORKS = {'netstandard2.0': '.NETStandard2.0', 'net471': '.NETFramework4.7.1',
                    'net48': '.NETFramework4.8', 'net6.0': 'net6.0', 'net8.0': 'net8.0'}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def version(value: str) -> str:
    match = VERSION.fullmatch(value)
    require(match is not None and len(value) <= 100, 'Invalid release/package version')
    require(all(int(part) <= 65534 for part in match.groups()[:3]), 'Version exceeds assembly version range')
    for part in (match[4] or '').split('.'):
        require(not (part.isdigit() and len(part) > 1 and part[0] == '0'), 'Noncanonical numeric prerelease')
    return value


def git(*args: str) -> str:
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def identity() -> dict:
    require(not git('status', '--porcelain=v1', '--untracked-files=all'),
            'Source checkout is dirty; commit source changes before binding artifacts to HEAD')
    return {'commit': git('rev-parse', 'HEAD'), 'tree': git('rev-parse', 'HEAD^{tree}')}


def digest(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def emit(values: dict) -> None:
    print(json.dumps(values, indent=2))
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as stream:
            for key, value in values.items():
                require('\n' not in str(value) and '\r' not in str(value), 'Invalid output')
                stream.write(f'{key}={str(value).lower() if isinstance(value, bool) else value}\n')


def build_version(requested: str = '') -> str:
    if requested:
        return version(requested)
    base = source_version()
    run = os.environ.get('GITHUB_RUN_NUMBER', '0')
    require(run.isdigit(), 'Run number must be numeric')
    # SemVer has one prerelease separator: preview.1.ci.77, never preview.1-ci.77.
    return version(f'{base}{"." if "-" in base else "-"}ci.{int(run)}')


def source_version() -> str:
    values = ET.parse(ROOT / 'Directory.Build.props').findall('./PropertyGroup/Version')
    require(len(values) == 1 and values[0].text is not None, 'Missing/ambiguous shared package version')
    return version(values[0].text)


def release_plan(ref_type: str, ref_name: str, sha: str, dry_run: bool, event_name: str = "") -> dict:
    # A queue/PR is validation-only, even if a caller misconfigures DRY_RUN.
    if event_name in ("pull_request", "merge_group"):
        dry_run = True
    require(re.fullmatch(r'[0-9a-f]{40}', sha) is not None, 'Invalid expected commit')
    require(git('rev-parse', 'HEAD') == sha, 'Checkout is not the triggering source')
    if ref_type == 'tag':
        require(ref_name.startswith('v'), 'Release tag must start with v')
        chosen = version(ref_name[1:])
        require(chosen == source_version(), 'Release tag must match the committed shared package version')
        require(git('rev-parse', f'refs/tags/{ref_name}^{{commit}}') == sha, 'Tag moved')
        subprocess.run(['git', 'merge-base', '--is-ancestor', sha, 'refs/remotes/origin/netstandard'], cwd=ROOT, check=True)
    else:
        require(dry_run, 'Publishing requires an existing vVERSION tag on netstandard history')
        chosen = build_version()
    return {'version': chosen, 'tag': ref_name if ref_type == 'tag' else '',
            'publish': not dry_run, 'prerelease': '-' in chosen, **identity()}


def inspect_package(path: Path, selected: str, commit: str, symbols: bool = False,
                    package_id: str = PACKAGE) -> None:
    selected = version(selected)
    require(package_id in package_catalog.BY_ID, 'Unreviewed package identity')
    item = package_catalog.BY_ID[package_id]
    require(path.is_file() and not path.is_symlink(), 'Missing/linked package')
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)) and archive.testzip() is None, 'Damaged/duplicate package entries')
        require(all(not n.startswith(('/', '\\')) and '\\' not in n and '..' not in Path(n).parts for n in names), 'Unsafe package entry')
        specs = [n for n in names if n.endswith('.nuspec')]
        require(len(specs) == 1, 'Missing/extra nuspec')
        root = ET.fromstring(archive.read(specs[0]))
        metadata = root.find('{*}metadata')
        require(metadata is not None, 'Missing metadata')
        require(metadata.findtext('{*}id') == item.id and metadata.findtext('{*}version') == selected, 'Package identity mismatch')
        repo = metadata.find('{*}repository')
        require(repo is not None and repo.get('url') == REPOSITORY and repo.get('commit') == commit and
                repo.get('type') == 'git', 'Package source mismatch')
        require(bool((metadata.findtext('{*}description') or '').strip()), 'Missing package description')
        # The SDK intentionally omits package documentation/license metadata from snupkg.
        if not symbols:
            require(bool((metadata.findtext('{*}authors') or '').strip()), 'Missing package authors')
            license_info = metadata.find('{*}license')
            require(license_info is not None and license_info.text == 'MIT' and
                    license_info.get('type') == 'expression', 'License metadata mismatch')
            require(metadata.findtext('{*}readme') == 'README.md' and 'README.md' in names and
                    archive.getinfo('README.md').file_size > 0, 'Missing package README')
            require('LICENSE' in names and archive.getinfo('LICENSE').file_size > 0, 'Missing package license file')
        dependencies = metadata.find('{*}dependencies')
        groups = list(dependencies) if dependencies is not None else []
        require(all(group.tag.rsplit('}', 1)[-1] == 'group' for group in groups), 'Ungrouped package dependency')
        require(len(groups) == len(item.frameworks) and
                {group.get('targetFramework') for group in groups} == {NUSPEC_FRAMEWORKS[tfm] for tfm in item.frameworks},
                'Incorrect dependency target groups')
        expected_dependencies = dict([(name, selected) for name in item.internal] + list(item.external))
        for group in groups:
            entries = list(group)
            require(all(entry.tag.rsplit('}', 1)[-1] == 'dependency' for entry in entries) and
                    len(entries) == len(expected_dependencies) and
                    {entry.get('id') for entry in entries} == set(expected_dependencies), 'Package dependency inventory mismatch')
            for entry in entries:
                pinned = expected_dependencies[entry.attrib['id']]
                require(entry.get('version') in (pinned, f'[{pinned}]'), 'Package dependency version mismatch: ' + entry.attrib['id'])
                require(not entry.get('include') or entry.get('include').lower() == 'all', 'Restricted package dependency assets')
                excluded = {asset.strip().lower() for asset in (entry.get('exclude') or '').split(',') if asset.strip()}
                require(excluded <= {'build', 'analyzers'}, 'Excluded package dependency runtime/compile assets')
        extension = 'pdb' if symbols else 'dll'
        wanted = {f'lib/{tfm}/{item.assembly}.{extension}' for tfm in item.frameworks}
        actual = {n for n in names if n.startswith('lib/') and n.endswith('.' + extension)}
        require(actual == wanted, f'Incorrect {extension} target inventory: {actual}')
        for name in wanted:
            require(archive.getinfo(name).file_size > 100, 'Empty assembly/symbols')
            if symbols:
                require(archive.read(name)[:4] == b'BSJB', 'Symbols are not portable PDBs')
            else:
                require(archive.read(name)[:2] == b'MZ', 'Invalid managed assembly image')
        if not symbols:
            documentation = {f'lib/{tfm}/{item.assembly}.xml' for tfm in item.frameworks}
            require({n for n in names if n.startswith('lib/') and n.endswith('.xml')} == documentation, 'Missing/extra XML documentation')
            for name in documentation:
                doc = ET.fromstring(archive.read(name))
                require(doc.tag == 'doc' and doc.findtext('./assembly/name') == item.assembly, 'Wrong XML documentation assembly')
        if item.id == 'netDxf.Shadow.Exchange.Dwg' and not symbols:
            require('THIRD-PARTY-NOTICES.txt' in names and archive.getinfo('THIRD-PARTY-NOTICES.txt').file_size > 0,
                    'Missing DWG provider third-party notices')


def inspect_packages(directory: Path, selected: str, commit: str) -> None:
    for extension in ('nupkg', 'snupkg'):
        wanted = {f'{item.id}.{selected}.{extension}' for item in package_catalog.PACKAGES}
        actual = {path.name for path in directory.glob('*.' + extension)}
        require(actual == wanted, f'Missing/extra {extension} package inventory: {sorted(actual ^ wanted)}')
        for item in package_catalog.PACKAGES:
            inspect_package(directory / f'{item.id}.{selected}.{extension}', selected, commit,
                            extension == 'snupkg', item.id)


def validate_build_manifest(build: dict) -> None:
    selected = version(build['version'])
    require(build.get('package') == PACKAGE and tuple(build.get('frameworks', ())) == TFMS,
            'Release core package or target mismatch')
    require(build.get('packages') == package_catalog.manifest(selected), 'Release package catalog mismatch')


def pack(directory: Path, selected: str) -> None:
    selected = version(selected)
    package_catalog.validate_source(ROOT)
    source = identity()
    directory.mkdir(parents=True, exist_ok=True)
    require(not any(directory.iterdir()), 'Pack output must be empty; refusing mixed release artifacts')
    for item in package_catalog.PACKAGES:
        subprocess.run(['dotnet', 'pack', item.project, '-c', 'Release', '-o', str(directory.resolve()),
                        f'-p:Version={selected}', f'-p:PackageVersion={selected}', '-p:ContinuousIntegrationBuild=true',
                        f'-p:RepositoryCommit={source["commit"]}', f'-p:SourceRevisionId={source["commit"]}',
                        f'-p:RepositoryUrl={REPOSITORY}', f'-p:PackageProjectUrl={REPOSITORY}', '-p:RepositoryType=git',
                        '-p:IncludeSymbols=true', '-p:SymbolPackageFormat=snupkg', '-p:DebugType=portable',
                        '-p:DebugSymbols=true'], cwd=ROOT, check=True)
    require(identity() == source, 'Source changed during package build')
    inspect_packages(directory, selected, source['commit'])


def seal(directory: Path) -> None:
    paths = sorted(p for p in directory.iterdir() if p.name != 'SHA256SUMS')
    require(paths and all(p.is_file() and not p.is_symlink() for p in paths), 'Only ordinary flat release files are allowed')
    require(all(re.fullmatch(r'[A-Za-z0-9_.-]+', p.name) for p in paths), 'Unsafe asset name')
    (directory / 'SHA256SUMS').write_text(''.join(f'{digest(p)}  {p.name}\n' for p in paths), encoding='utf-8')


def verify(directory: Path) -> None:
    expected = {}
    for line in (directory / 'SHA256SUMS').read_text(encoding='utf-8').splitlines():
        match = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9_.-]+)', line)
        require(match is not None, 'Invalid checksum line')
        checksum, name = match.groups()
        require(name not in expected and name not in ('.', '..', 'SHA256SUMS'), 'Duplicate/invalid checksum name')
        expected[name] = checksum
    require(expected and set(expected) == {p.name for p in directory.iterdir() if p.name != 'SHA256SUMS'}, 'Asset inventory mismatch')
    for name, checksum in expected.items():
        path = directory / name
        require(path.is_file() and not path.is_symlink() and digest(path) == checksum, f'Checksum mismatch: {name}')


def package_manifest(directory: Path, selected: str) -> None:
    selected = version(selected)
    source = identity()
    package_catalog.validate_source(ROOT)
    inspect_packages(directory, selected, source['commit'])
    import shadow_packages
    shadow = shadow_packages.verify(directory, selected, source)
    subprocess.run(['git', 'archive', '--format=tar.gz', '-o', str(directory.resolve() / f'netDxf-{selected}-source.tar.gz'), 'HEAD'], cwd=ROOT, check=True)
    metadata = {**source, 'package': PACKAGE, 'version': selected, 'frameworks': TFMS,
                'packages': package_catalog.manifest(selected), 'shadow_smoke': shadow}
    (directory / 'build.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    seal(directory)
    verify(directory)


def test_results(path: Path) -> list:
    data = json.loads(path.read_text(encoding='utf-8'))
    require(isinstance(data, list) and data, 'No C# results')
    names = [r['name'] for r in data]
    require(all(isinstance(n, str) and n for n in names), 'Missing C# case identity')
    require(len(names) == len(set(names)), 'Duplicate C# cases')
    require(all(r.get('passed') is True for r in data), 'C# failure')
    return data


def qualify(packages: Path, evidence: Path, runtime_evidence: Path | None = None) -> None:
    verify(packages)
    build = json.loads((packages / 'build.json').read_text(encoding='utf-8'))
    validate_build_manifest(build)
    require(build['commit'] == identity()['commit'] and build['tree'] == identity()['tree'], 'Wrong release source')
    import shadow_packages
    shadow = shadow_packages.verify(packages, build['version'], {key: build[key] for key in ('commit', 'tree')})
    require(build.get('shadow_smoke') == shadow, 'Shadow package smoke evidence differs from build receipt')
    results = []
    for host in ('ubuntu-latest', 'windows-latest'):
        debug_records = None
        for config in ('Debug', 'Release'):
            directory = evidence / f'dxf-conformance-{host}-{config}'
            source = json.loads((directory / 'ci-source.json').read_text(encoding='utf-8'))
            require(source == identity(), 'Conformance source mismatch')
            path = directory / 'conformance/results.json'
            records = test_results(path)
            keyed = {record['name']: record for record in records}
            if debug_records is None:
                debug_records = keyed
            else:
                require(keyed == debug_records, 'Debug/Release case identities or values diverge')
            results.append({'host': host, 'configuration': config, 'count': len(records), 'sha256': digest(path)})
    require(len({r['count'] for r in results}) == 1, 'Incomplete platform matrix')
    independent = evidence / 'dxf-conformance-ubuntu-latest-Release/independent/results.json'
    data = json.loads(independent.read_text(encoding='utf-8'))
    expected = {p.relative_to(ROOT).as_posix() for p in (ROOT / 'tools').glob('verify_*.py')}
    records = data['results']
    require(expected and len(records) == len(expected) and {r['script'] for r in records} == expected, 'Incomplete independent verification')
    require(data['failed'] == 0 and data['passed'] == len(expected) and all(r.get('passed') is True and r.get('exit_code') == 0 and r.get('timed_out') is False for r in records), 'Independent verification failure')
    import runtime_release
    runtime_path = runtime_evidence if runtime_evidence is not None else ROOT / 'artifacts/release-runtime/runtime-evidence.zip'
    runtime = runtime_release.verify(packages, runtime_path)
    payload = runtime_path.read_bytes()
    require(hashlib.sha256(payload).hexdigest() == runtime['sha256'], 'Runtime archive changed after validation')
    (packages / runtime_release.ARCHIVE_NAME).write_bytes(payload)
    report = {**build, 'conformance': results, 'independent_verifiers': len(records),
              'independent_report_sha256': digest(independent), 'runtime_evidence': runtime}
    (packages / 'qualification.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    (packages / 'RELEASE-NOTES.md').write_text(f"# netDxf {build['version']}\n\nSource: `{build['commit']}`\n\nAll {len(package_catalog.PACKAGES)} packages share this version: the core targets five frameworks and {len(package_catalog.PACKAGES) - 1} Shadow libraries target .NET 8. Every package includes XML documentation, a README, MIT licensing and separate portable symbols. Each of four Linux/Windows Debug/Release configurations passed {results[0]['count']:,} conformance cases; {len(records)} independent verifiers passed. All eight core packaged target/runtime profiles and 96 smoke-scenario executions were revalidated; their receipts and process logs are retained in runtime-evidence.zip. The installed-only Shadow consumer verified all {shadow['packages']} package assemblies and {shadow['scenarios']} scenarios, including native Skia/HarfBuzz rendering and DWG exchange; source-bound evidence is retained in shadow-smoke.json and shadow-smoke.log.\n\nSee doc/dxf-conformance for scoped contracts and remaining limitations.\n", encoding='utf-8')
    seal(packages)
    verify(packages)


def validate_qualification(report: dict, build: dict) -> None:
    expected = {(host, config) for host in ('ubuntu-latest', 'windows-latest') for config in ('Debug', 'Release')}
    records = report['conformance']
    require(len(records) == 4 and {(r['host'], r['configuration']) for r in records} == expected, 'Incomplete release matrix')
    require(all(type(r['count']) is int and r['count'] > 0 and re.fullmatch(r'[0-9a-f]{64}', r['sha256']) for r in records), 'Invalid release result receipt')
    require(len({r['count'] for r in records}) == 1, 'Release platform case count mismatch')
    scripts = list((ROOT / 'tools').glob('verify_*.py'))
    require(scripts and report['independent_verifiers'] == len(scripts), 'Incomplete release independent inventory')
    require(re.fullmatch(r'[0-9a-f]{64}', report['independent_report_sha256']) is not None, 'Missing independent report hash')
    for manifest in (build, report):
        validate_build_manifest(manifest)
    require(report.get('shadow_smoke') == build.get('shadow_smoke') and
            isinstance(build.get('shadow_smoke'), dict), 'Release Shadow consumer evidence mismatch')


def verify_release(directory: Path) -> None:
    verify(directory)
    build = json.loads((directory / 'build.json').read_text(encoding='utf-8'))
    qualification = json.loads((directory / 'qualification.json').read_text(encoding='utf-8'))
    for key, value in identity().items():
        require(build[key] == value and qualification[key] == value, 'Release source mismatch')
    require(qualification['version'] == build['version'], 'Release version mismatch')
    validate_qualification(qualification, build)
    package_catalog.validate_source(ROOT)
    inspect_packages(directory, version(build['version']), build['commit'])
    import shadow_packages
    shadow = shadow_packages.verify(directory, build['version'], {key: build[key] for key in ('commit', 'tree')})
    require(build['shadow_smoke'] == shadow, 'Release Shadow consumer evidence differs from archived execution')
    import runtime_release
    runtime = runtime_release.verify(directory, directory / runtime_release.ARCHIVE_NAME)
    require(qualification.get('runtime_evidence') == runtime, 'Release runtime evidence receipt differs from archived execution evidence')
    tag = os.environ.get('RELEASE_TAG', '')
    require(tag.startswith('v') and version(tag[1:]) == build['version'], 'Release tag/package version mismatch')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('metadata', 'release-plan', 'provenance', 'pack', 'shadow-smoke', 'package', 'verify', 'verify-release', 'qualify'))
    parser.add_argument('--version', default='')
    parser.add_argument('--directory', type=Path, default=ROOT / 'artifacts/packages')
    parser.add_argument('--evidence', type=Path, default=ROOT / 'artifacts/release-tests')
    args = parser.parse_args()
    if args.command == 'metadata':
        emit({'version': build_version(args.version), **identity()})
    elif args.command == 'release-plan':
        emit(release_plan(os.environ['REF_TYPE'], os.environ['REF_NAME'], os.environ['EXPECTED_SHA'], os.environ.get('DRY_RUN', 'true') == 'true', os.environ.get('GITHUB_EVENT_NAME', '')))
    elif args.command == 'provenance':
        args.directory.mkdir(parents=True, exist_ok=True)
        (args.directory / 'ci-source.json').write_text(json.dumps(identity(), indent=2) + '\n', encoding='utf-8')
    elif args.command == 'package':
        package_manifest(args.directory, args.version)
    elif args.command == 'pack':
        pack(args.directory, args.version)
    elif args.command == 'shadow-smoke':
        import shadow_packages
        emit(shadow_packages.run(args.directory, version(args.version)))
    elif args.command == 'verify':
        verify(args.directory)
    elif args.command == 'verify-release':
        verify_release(args.directory)
    else:
        qualify(args.directory, args.evidence)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        sys.exit(1)
