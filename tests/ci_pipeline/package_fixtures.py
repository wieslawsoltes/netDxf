"""Synthetic NuGet archives for validator tests; never runtime execution evidence."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
import zipfile
import package_catalog
import pipeline


def write_package(path: Path, item=package_catalog.PACKAGES[0], selected='3.0.2', commit='a'*40,
                  symbols=False, frameworks=None, dlls=None, mutate=None):
    frameworks = item.frameworks if frameworks is None else frameworks
    root = ET.Element('package'); metadata = ET.SubElement(root, 'metadata')
    for key, value in (('id', item.id), ('version', selected), ('authors', 'Fixture'),
                       ('description', 'Synthetic validator fixture'), ('readme', 'README.md')):
        ET.SubElement(metadata, key).text = value
    ET.SubElement(metadata, 'repository', type='git', url=pipeline.REPOSITORY, commit=commit)
    ET.SubElement(metadata, 'license', type='expression').text = 'MIT'
    dependencies = ET.SubElement(metadata, 'dependencies')
    for target in frameworks:
        group = ET.SubElement(dependencies, 'group', targetFramework=pipeline.NUSPEC_FRAMEWORKS[target])
        for name, value in [(name, selected) for name in item.internal] + list(item.external):
            ET.SubElement(group, 'dependency', id=name, version=value)
    if mutate: mutate(metadata)
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr(item.id + '.nuspec', ET.tostring(root))
        if not symbols:
            archive.writestr('README.md', 'Synthetic validator fixture.')
            archive.writestr('LICENSE', 'MIT test fixture.')
            if item.id in ('netDxf.Shadow.Exchange.Dwg', 'netDxf.Shadow.Markdown'):
                archive.writestr('THIRD-PARTY-NOTICES.txt', 'Synthetic third-party notice fixture.')
        if not symbols and (assets := pipeline.static_web_assets(item)):
            for name, source in assets.items():
                archive.writestr('staticwebassets/' + name, source.read_bytes())
            for name, target in {
                'build/' + item.id + '.props': 'Microsoft.AspNetCore.StaticWebAssets.props',
                'buildMultiTargeting/' + item.id + '.props': '../build/' + item.id + '.props',
                'buildTransitive/' + item.id + '.props': '../buildMultiTargeting/' + item.id + '.props',
            }.items():
                project = ET.Element('Project'); ET.SubElement(project, 'Import', Project=target)
                archive.writestr(name, ET.tostring(project))
            project = ET.Element('Project'); group = ET.SubElement(project, 'ItemGroup')
            for name in assets:
                entry = ET.SubElement(group, 'StaticWebAsset')
                for key, value in {'RelativePath': name, 'BasePath': 'markdown' if item.id.endswith('.Markdown') else '/',
                                   'SourceId': item.id, 'SourceType': 'Package', 'CopyToPublishDirectory': 'PreserveNewest'}.items():
                    ET.SubElement(entry, key).text = value
            archive.writestr('build/Microsoft.AspNetCore.StaticWebAssets.props', ET.tostring(project))
        for target in frameworks:
            stem = f'lib/{target}/{item.assembly}'
            if symbols:
                archive.writestr(stem + '.pdb', b'BSJB' + b'0'*101)
            else:
                archive.writestr(stem + '.dll', dlls[target] if dlls else b'MZ' + (item.id + target).encode()*20)
                archive.writestr(stem + '.xml', f'<doc><assembly><name>{item.assembly}</name></assembly></doc>')


def write_catalog(directory: Path, selected='3.0.2', commit='a'*40):
    for item in package_catalog.PACKAGES:
        for extension in ('nupkg', 'snupkg'):
            write_package(directory / f'{item.id}.{selected}.{extension}', item, selected, commit,
                          symbols=extension == 'snupkg')


def write_shadow_receipt(directory: Path, selected='3.0.2', source=None):
    import shadow_packages
    source = source or {'commit':'a'*40, 'tree':'b'*40}
    log = directory / shadow_packages.LOG
    log.write_text('Synthetic unit-test receipt only; no native execution claimed.\n')
    packages, hashes = shadow_packages.package_hashes(directory, selected)
    data = {'schema':1, 'passed':True, 'version':selected, **source, 'host':'Linux', 'runtime_version':'8.0.31',
            'scenarios':list(shadow_packages.SCENARIOS), 'packages':packages, 'log_sha256':pipeline.digest(log),
            'assemblies':[{'id':key, 'sha256':value, 'framework':'.NETCoreApp,Version=v8.0',
                           'informational_version':selected+'+'+source['commit']} for key,value in hashes.items()]}
    (directory / shadow_packages.RECEIPT).write_text(json.dumps(data))
    return shadow_packages.verify(directory, selected, source)
