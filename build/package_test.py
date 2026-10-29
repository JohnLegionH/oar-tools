# Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
"""Tests for build/package.py: the staged copy of each package and the list of its files.

usage: python -I build/package_test.py
"""
import hashlib
import os
import subprocess
import sys
import tarfile
import tempfile
import unittest
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PACKAGE = os.path.join(HERE, 'package.py')
VERSION = '9.9.9'
WIN = 'oar-tools-%s-windows-x64' % VERSION
LIN = 'oar-tools-%s-linux-x64' % VERSION


def sha(data):
    return hashlib.sha256(data).hexdigest()


class PackageStageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = self.tmp.name
        for rid, exe in (('win', 'oar-tools.exe'), ('lin', 'oar-tools')):
            os.makedirs(os.path.join(root, rid))
            with open(os.path.join(root, rid, exe), 'wb') as f:
                f.write(b'program ' + rid.encode() * 1000)
            with open(os.path.join(root, rid, 'native.' + ('dll' if rid == 'win' else 'so')), 'wb') as f:
                f.write(b'native ' + rid.encode())
            with open(os.path.join(root, rid, 'oar-tools.pdb'), 'wb') as f:
                f.write(b'symbols')
        self.out = os.path.join(root, 'dist')

    def run_package(self):
        return subprocess.run([sys.executable, '-I', PACKAGE, '--win', os.path.join(self.tmp.name, 'win'),
                               '--linux', os.path.join(self.tmp.name, 'lin'), '--out', self.out, '--version', VERSION],
                              capture_output=True, text=True)

    def staged(self, package):
        folder = os.path.join(self.out, 'stage', package)
        found = {}
        for base, _, names in os.walk(folder):
            for n in names:
                full = os.path.join(base, n)
                found[package + '/' + os.path.relpath(full, folder).replace(os.sep, '/')] = open(full, 'rb').read()
        return found

    def test_the_windows_stage_holds_exactly_the_files_of_the_zip(self):
        self.assertEqual(0, self.run_package().returncode)
        with zipfile.ZipFile(os.path.join(self.out, WIN + '.zip')) as z:
            entries = {i.filename: z.read(i) for i in z.infolist()}
        self.assertTrue(entries)
        self.assertEqual(entries, self.staged(WIN))

    def test_the_linux_stage_holds_exactly_the_files_of_the_tar(self):
        self.assertEqual(0, self.run_package().returncode)
        with tarfile.open(os.path.join(self.out, LIN + '.tar.gz')) as t:
            entries = {m.name: t.extractfile(m).read() for m in t if m.isfile()}
        self.assertTrue(entries)
        self.assertEqual(entries, self.staged(LIN))

    def test_the_file_list_gives_the_digest_and_size_of_every_staged_file(self):
        self.assertEqual(0, self.run_package().returncode)
        with open(os.path.join(self.out, 'STAGED-FILES.txt'), encoding='utf-8') as f:
            rows = [line.rstrip('\n').split('  ', 2) for line in f if line.strip()]
        expected = {}
        for package in (WIN, LIN):
            for name, data in self.staged(package).items():
                expected[name] = (sha(data), str(len(data)))
        self.assertTrue(expected)
        self.assertEqual(expected, {r[2]: (r[0], r[1]) for r in rows})
        self.assertEqual(len(expected), len(rows))

    def test_a_second_run_into_the_same_folder_is_refused_and_changes_nothing(self):
        self.assertEqual(0, self.run_package().returncode)
        before = self.staged(WIN)
        again = self.run_package()
        self.assertNotEqual(0, again.returncode)
        self.assertIn('stage', again.stderr)
        self.assertEqual(before, self.staged(WIN))


if __name__ == '__main__':
    unittest.main()
