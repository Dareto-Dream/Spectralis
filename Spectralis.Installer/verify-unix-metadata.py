#!/usr/bin/env python3
"""Check the native identity inside extracted release artifacts before publication."""
import argparse
import configparser
import os
from pathlib import Path
import plistlib
import struct
import subprocess
import xml.etree.ElementTree as ET


def verify_macos(root, version, expected_icon, rid='osx-x64'):
    plists = [path for path in root.rglob('Info.plist')
              if path.parent.name == 'Contents' and path.parent.parent.name == 'Spectralis.app']
    if root.name == 'Spectralis.app':
        assert plists, 'Spectralis.app has no Info.plist'
    assert len(plists) == 1, f'Expected one Spectralis.app, found {len(plists)}'
    contents = plists[0].parent
    with plists[0].open('rb') as stream:
        info = plistlib.load(stream)
    for key, value in {
        'CFBundleName': 'Spectralis',
        'CFBundleDisplayName': 'Spectralis',
        'CFBundleIdentifier': 'com.deltavdevs.spectralis',
        'CFBundleIconFile': 'Spectralis.icns',
        'CFBundlePackageType': 'APPL',
        'CFBundleVersion': version.split('-')[0],
        'CFBundleShortVersionString': version.split('-')[0],
        'SpectralisReleaseVersion': version,
        'LSMinimumSystemVersion': '13.0' if rid == 'osx-arm64' else '12.0',
        'LSApplicationCategoryType': 'public.app-category.music',
    }.items():
        assert info.get(key) == value, f'{key}: {info.get(key)!r} != {value!r}'
    assert any('spectralis' in item.get('CFBundleURLSchemes', [])
               for item in info.get('CFBundleURLTypes', [])), 'Missing spectralis:// registration'
    executable = contents / 'MacOS' / info['CFBundleExecutable']
    assert executable.is_file() and os.access(executable, os.X_OK), f'Missing executable: {executable}'
    icon = (contents / 'Resources' / info['CFBundleIconFile']).read_bytes()
    assert icon == expected_icon.read_bytes(), 'Bundle icon is not the generated Spectralis icon'
    assert icon[:4] == b'icns' and struct.unpack('>I', icon[4:8])[0] == len(icon), 'Invalid ICNS'
    types = set()
    offset = 8
    while offset < len(icon):
        kind, size = struct.unpack('>4sI', icon[offset:offset + 8])
        assert size >= 8 and offset + size <= len(icon), 'Invalid ICNS entry'
        types.add(kind)
        offset += size
    assert {b'ic07', b'ic08', b'ic09', b'ic10'} <= types, 'Missing high-resolution icon representations'
    arch = 'arm64' if rid == 'osx-arm64' else 'x86_64'
    macos = contents / 'MacOS'
    ns = {'n': 'http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd'}
    metadata = ET.parse(macos / 'sq.version').getroot()
    assert metadata.findtext('n:metadata/n:rid', namespaces=ns) == rid, 'Wrong update package runtime'
    assert metadata.findtext('n:metadata/n:machineArchitecture', namespaces=ns) == rid.split('-')[1], 'Wrong update package architecture'
    assert metadata.findtext('n:metadata/n:version', namespaces=ns) == version, 'Wrong update package version'
    for native in ['Spectralis.App', 'UpdateMac', 'libcef.dylib', 'libwgpu_host.dylib', 'ffmpeg',
                   'Updater/UpdateSpectralis', 'Tools/deno/deno', 'Tools/yt-dlp/yt-dlp_macos']:
        assert (macos / native).is_file(), f'Missing native payload: {native}'
    magic = {b'\xcf\xfa\xed\xfe', b'\xfe\xed\xfa\xcf', b'\xca\xfe\xba\xbe', b'\xbe\xba\xfe\xca'}
    checked = 0
    for path in macos.rglob('*'):
        if not path.is_file(): continue
        with path.open('rb') as stream: header = stream.read(4)
        if header not in magic: continue
        subprocess.run(['/usr/bin/lipo', str(path), '-verify_arch', arch], check=True)
        checked += 1
    print(f'Verified {checked} Mach-O payloads for {arch}; no mixed-architecture app or helpers')
    for relative, argument in [('Tools/yt-dlp/yt-dlp_macos', '--version'),
                               ('Tools/deno/deno', '--version'),
                               ('Updater/UpdateSpectralis', '--version'), ('ffmpeg', '-version')]:
        helper = macos / relative
        assert os.access(helper, os.X_OK), f'Packaged helper is not executable: {relative}'
        result = subprocess.run([str(helper), argument], check=True, timeout=30,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        print(f'Packaged {relative}: {result.stdout.splitlines()[0]}')
    print(f'Verified macOS identity, protocol, version and Spectralis icon: {contents.parent}')


def verify_linux(root, version, expected_icon):
    desktop = configparser.ConfigParser(interpolation=None)
    desktop.optionxform = str
    desktop.read(root / 'Spectralis.desktop')
    entry = desktop['Desktop Entry']
    for key, value in {'Name': 'Spectralis', 'Icon': 'Spectralis',
                       'StartupWMClass': 'Spectralis', 'X-AppImage-Version': version}.items():
        assert entry.get(key) == value, f'{key}: {entry.get(key)!r} != {value!r}'
    assert entry['Exec'].split()[0] == 'Spectralis.App', 'Wrong desktop executable'
    assert {'AudioVideo', 'Audio', 'Player'} <= set(entry['Categories'].split(';')), 'Wrong app categories'
    icon = (root / 'Spectralis.png').read_bytes()
    assert icon == expected_icon.read_bytes(), 'AppImage icon is not Assets/icon.png'
    assert icon[:8] == b'\x89PNG\r\n\x1a\n', 'Invalid PNG'
    if (root / '.DirIcon').exists():
        assert (root / '.DirIcon').read_bytes() == icon, 'AppImage file icon does not match'
    assert (root / 'usr/bin/Spectralis.App').is_file(), 'Missing app executable'
    print(f'Verified Linux identity, desktop grouping, version and Spectralis icon: {root}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('platform', choices=['macos', 'linux'])
    parser.add_argument('root', type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--expected-icon', required=True, type=Path)
    parser.add_argument('--rid', default='osx-x64', choices=['osx-x64', 'osx-arm64'])
    args = parser.parse_args()
    if args.platform == 'macos': verify_macos(args.root, args.version, args.expected_icon, args.rid)
    else: verify_linux(args.root, args.version, args.expected_icon)
