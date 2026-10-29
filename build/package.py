# Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
"""Packs the published oar-tools programs into the release archives.

usage: python build/package.py --win <publish folder> --linux <publish folder> --out <output folder> [--version 0.5.0]

Writes oar-tools-<version>-windows-x64.zip, oar-tools-<version>-linux-x64.tar.gz and SHA256SUMS.txt into the output folder.
Each archive holds one top folder with the program, the native libraries it loads, the wrapper scripts for the old program names,
and the licence files. Times, owners and modes are fixed, so the same input gives the same archive. Debug symbols are left out.
"""
import argparse
import gzip
import hashlib
import io
import os
import re
import tarfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WRAPPED = ('inspect', 'export', 'restore', 'convert')
FIXED_TIME = 1704067200  # 2024-01-01 00:00:00 UTC; any fixed value does
DOCS = ('README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt', os.path.join('LICENSES', 'MPL-2.0.txt'))


def version_from_source():
    text = open(os.path.join(ROOT, 'Source', 'OpenSim.ArchiveInspector', 'ToolRelease.cs'), encoding='utf-8-sig').read()
    return re.search(r'Version = "([^"]+)"', text).group(1)


def program_files(folder, windows):
    """(archive name, bytes, executable) for each file that belongs in the package."""
    out = []
    for name in sorted(os.listdir(folder)):
        path = os.path.join(folder, name)
        if not os.path.isfile(path) or name.endswith('.pdb'):
            continue
        out.append((name, open(path, 'rb').read(), name == 'oar-tools' or name == 'oar-tools.exe'))
    for sub in WRAPPED:
        wrapper = 'oar-%s.cmd' % sub if windows else 'oar-%s' % sub
        data = open(os.path.join(ROOT, 'wrappers', wrapper), 'rb').read()
        if not windows:
            data = data.replace(b'\r\n', b'\n')
        out.append((wrapper, data, not windows))
    for doc in DOCS:
        out.append((os.path.basename(doc), open(os.path.join(ROOT, doc), 'rb').read(), False))
    return out


def write_zip(path, top, files):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name, data, _ in files:
            info = zipfile.ZipInfo(top + '/' + name, date_time=(2024, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, data)


def write_targz(path, top, files):
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.GNU_FORMAT) as t:
        d = tarfile.TarInfo(top)
        d.type, d.mode, d.mtime, d.uid, d.gid, d.uname, d.gname = tarfile.DIRTYPE, 0o755, FIXED_TIME, 0, 0, 'root', 'root'
        t.addfile(d)
        for name, data, executable in files:
            i = tarfile.TarInfo(top + '/' + name)
            i.size, i.mode, i.mtime, i.uid, i.gid, i.uname, i.gname = len(data), 0o755 if executable else 0o644, FIXED_TIME, 0, 0, 'root', 'root'
            t.addfile(i, io.BytesIO(data))
    with open(path, 'wb') as f:
        with gzip.GzipFile(fileobj=f, mode='wb', mtime=0, compresslevel=9) as g:
            g.write(raw.getvalue())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--win', required=True)
    ap.add_argument('--linux', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--version', default=None)
    a = ap.parse_args()
    version = a.version or version_from_source()
    os.makedirs(a.out, exist_ok=True)

    zip_path = os.path.join(a.out, 'oar-tools-%s-windows-x64.zip' % version)
    tgz_path = os.path.join(a.out, 'oar-tools-%s-linux-x64.tar.gz' % version)
    write_zip(zip_path, 'oar-tools-%s-windows-x64' % version, program_files(a.win, True))
    write_targz(tgz_path, 'oar-tools-%s-linux-x64' % version, program_files(a.linux, False))

    lines = []
    for p in (zip_path, tgz_path):
        h = hashlib.sha256(open(p, 'rb').read()).hexdigest()
        lines.append('%s  %s' % (h, os.path.basename(p)))
        print('%s  %d bytes  %s' % (h, os.path.getsize(p), os.path.basename(p)))
    open(os.path.join(a.out, 'SHA256SUMS.txt'), 'w', newline='\n').write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
