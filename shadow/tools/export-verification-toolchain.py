"""Temporary, allow-listed public SDK/package export for offline continuation tests.

Never reads credentials, user configuration or arbitrary home-directory files.
Removed with its MSBuild hook before final qualification.
"""
import json
import os
import pathlib
import subprocess
import tarfile

repo = pathlib.Path(__file__).resolve().parents[2]
archive = repo / 'artifacts' / 'offline-toolchain' / 'shadow-offline-toolchain.tar.gz'
if not archive.exists():
    sdks = []
    for line in subprocess.check_output(['dotnet', '--list-sdks'], text=True).splitlines():
        version, directory = line.split(' [', 1)
        if version.startswith('8.'):
            sdks.append((tuple(map(int, version.split('.'))), version, pathlib.Path(directory[:-1]).parent))
    _, version, root = max(sdks)
    entries = [root / 'dotnet', root / 'sdk' / version]
    for group in ['host/fxr', 'shared/Microsoft.NETCore.App', 'shared/Microsoft.AspNetCore.App',
                  'packs/Microsoft.NETCore.App.Ref', 'packs/Microsoft.AspNetCore.App.Ref',
                  'packs/Microsoft.NETCore.App.Host.linux-x64']:
        entries.append(max((root / group).glob('8.*'), key=lambda p: tuple(map(int, p.name.split('.')))))
    entries.extend((root / 'packs/NETStandard.Library.Ref').glob('*'))
    entries.extend(p for p in [root / 'LICENSE.txt', root / 'ThirdPartyNotices.txt'] if p.exists())
    assets = json.loads((repo / 'shadow/samples/netDxf.Shadow.Studio/obj/project.assets.json').read_text())
    def include(info):
        return None if pathlib.Path(info.name).suffix.lower() in {'.ttf', '.otf', '.woff', '.woff2'} else info
    archive.parent.mkdir(parents=True, exist_ok=True)
    temporary = archive.with_suffix('.tmp')
    with tarfile.open(temporary, 'w:gz', compresslevel=1, dereference=True) as tar:
        for entry in entries:
            tar.add(entry, arcname='dotnet/' + str(entry.relative_to(root)), filter=include)
        for name, item in assets['libraries'].items():
            if item['type'] != 'package':
                continue
            package, package_version = name.lower().split('/')
            filename = package + '.' + package_version + '.nupkg'
            candidates = [pathlib.Path(folder) / item['path'] / filename for folder in assets['packageFolders']]
            tar.add(next(p for p in candidates if p.is_file()), arcname='feed/' + filename)
    os.replace(temporary, archive)
    print('Public verification SDK/package archive:', archive.stat().st_size, 'bytes; SDK', version)
