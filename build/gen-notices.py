# Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
"""Writes THIRD-PARTY-NOTICES.txt for a published oar-tools.

usage: python build/gen-notices.py --nuget <NuGet packages folder> --out <file> --deps <oar-tools.deps.json> [--deps ...]

The libraries listed are the ones the publish step bundled (the "libraries" of each platform's deps.json). For each NuGet package
the notice names the package, its version, the licence the package declares and the copyright line from its .nuspec, and
carries the licence text: the package's own licence file when it has one, otherwise the text kept in ThirdPartyLicenses/ for
that library, otherwise the standard text of the declared licence with the package's copyright line. The text of the licences
of the source carried in this repository (OpenSimulator BSD, Mozilla Public License 2.0) follows the library list.
"""
import argparse
import glob
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

MIT = """Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation
files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify,
merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished
to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."""

BSD3 = """Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following
conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in
   the documentation and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote products derived
   from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE."""

# libraries whose licence text is kept in ThirdPartyLicenses/ because the package carries none
KEPT = {
    'nini-core': 'Nini.txt',
    'log4net': 'log4net-Apache-2.0.txt',
    'McMaster.NETCore.Plugins': 'log4net-Apache-2.0.txt',
    'LukeSkywalker.IPNetwork': 'IPNetwork.txt',
    'xmlrpc': 'XML-RPC.NET.txt',
}
KEPT_PREFIX = {
    'UtopiaSkye.OpenMetaverse': 'libsl.txt',
    'CoreJ2K': 'CSJ2K.txt',
}


def text_of(path):
    raw = open(path, 'rb').read()
    for enc in ('utf-8', 'cp1252'):
        try:
            return raw.decode(enc).replace('\r\n', '\n').strip()
        except UnicodeDecodeError:
            pass
    return raw.decode('latin-1').strip()


def nuspec(nuget, pid, ver):
    base = os.path.join(nuget, pid.lower(), ver)
    found = glob.glob(os.path.join(base, '*.nuspec'))
    if not found:
        return None, base
    x = open(found[0], encoding='utf-8').read()

    def tag(t):
        m = re.search(r'<%s[^>]*>(.*?)</%s>' % (t, t), x, re.S)
        return m.group(1).strip() if m else ''
    return {'license': tag('license'), 'copyright': tag('copyright'), 'authors': tag('authors'), 'url': tag('projectUrl')}, base


def licence_text(pid, base, info):
    for name in KEPT_PREFIX:
        if pid == name or pid.startswith(name + '.'):
            extra = text_of(os.path.join(ROOT, 'ThirdPartyLicenses', KEPT_PREFIX[name]))
            return extra
    if pid in KEPT:
        return text_of(os.path.join(ROOT, 'ThirdPartyLicenses', KEPT[pid]))
    for pattern in ('LICENSE', 'LICENSE.md', 'LICENSE.txt', 'license.txt', 'License.txt'):
        p = os.path.join(base, pattern)
        if os.path.isfile(p):
            return text_of(p)
    lic = info['license'] if info else ''
    holder = (info or {}).get('copyright') or ('Copyright (c) ' + ((info or {}).get('authors') or pid))
    if lic == 'MIT':
        return holder + '\n\n' + MIT
    if lic == 'BSD-3-Clause':
        return holder + '\n\n' + BSD3
    return '(no licence text could be found for this package; its declared licence is "%s")' % lic



# the one project of this repository that is not BSD-3-Clause: it re-implements the Second Life viewer's volume code
LGPL_ITEM_HEAD = ('-' * 100 + '\nSecond Life Viewer Source Code, GNU Lesser General Public License 2.1 only, for Source/OarTools.ViewerVolume\n' + '-' * 100 + '\n'
                  'Source/OarTools.ViewerVolume re-implements, in C#, the volume generation of the Second Life viewer (indra/llmath/llvolume.cpp):\n'
                  'the profile and path points, the faces, their triangles, normals and texture coordinates, and the sculpt grid. Its files carry the\n'
                  "viewer's notice: Second Life Viewer Source Code, Copyright (C) 2010, Linden Research, Inc., licensed under version 2.1 of the\n"
                  'GNU Lesser General Public License only, and Adapted 2026 by Legion Builds. That project is licensed LGPL-2.1 only; the rest of\n'
                  'this repository is BSD-3-Clause. The full source of the program is in this repository, so anyone can change that library and\n'
                  'rebuild the program. The licence text follows.\n\n')

FIXTURE_ITEM = ('-' * 100 + "\nTest data generated with the Second Life viewer's volume code\n" + '-' * 100 + '\n'
                'The geometry fixtures under Tests/OpenSim.Archive.GltfExport.Tests/Fixtures/ViewerReference are output generated with the Second Life\n'
                "viewer's volume code (indra/llmath/llvolume.cpp, Copyright (C) 2010, Linden Research, Inc., LGPL-2.1 only), built unchanged, and are used\n"
                'as test data. They hold numbers (vertices, normals, texture coordinates and triangles), not viewer source.\n')
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--nuget', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--deps', action='append', required=True)
    a = ap.parse_args()

    libs = {}
    for d in a.deps:
        data = json.load(open(d, encoding='utf-8'))
        for key, v in data['libraries'].items():
            if v['type'] not in ('package', 'reference'):
                continue
            norm = re.sub(r'\.linux-x64$|\.win-x64$', '', key).replace('NativeAssets.Linux.NoDependencies', 'NativeAssets.<platform>').replace('NativeAssets.Win32', 'NativeAssets.<platform>')
            libs.setdefault(norm, (key, v))

    out = []
    out.append('THIRD-PARTY NOTICES for oar-tools\n')
    out.append('oar-tools is licensed as described in LICENSE. It carries source from the OpenSimulator project and bundles the libraries below.\n')
    out.append('Where a library is changed before it is bundled, this is said. The OpenMetaverse libraries create MD5, SHA-1 and SHA-256 objects when they\n'
               'start; the publish step (OpenSim.Archive.PackagePatch) rewrites those calls in the bundled copy to use OpenSim.Archive.ManagedHash, so the\n'
               'program starts on a Linux system without OpenSSL. Nothing else in them is changed, and their notices are as published.\n')
    out.append('=' * 100 + '\nBUNDLED LIBRARIES\n' + '=' * 100 + '\n')
    for norm in sorted(libs, key=str.lower):
        key, v = libs[norm]
        pid, ver = key.split('/', 1)
        if v['type'] == 'reference':
            base, info = os.path.join(ROOT, 'Library'), None
            lic = 'see text'
            body = licence_text(pid, base, info)
            out.append('-' * 100 + '\n%s %s (prebuilt library carried in Library/)\n' % (pid, ver) + '-' * 100 + '\n' + body + '\n')
            continue
        info, base = nuspec(a.nuget, pid, ver)
        head = '%s %s' % (pid, ver)
        lines = [head]
        if info:
            lines.append('Licence declared by the package: ' + (info['license'] or 'not stated'))
            if info['copyright']:
                lines.append('Copyright: ' + info['copyright'])
            elif info['authors']:
                lines.append('Authors: ' + info['authors'])
            if info['url']:
                lines.append('Project: ' + info['url'])
        out.append('-' * 100 + '\n' + '\n'.join(lines) + '\n' + '-' * 100 + '\n' + licence_text(pid, base, info) + '\n')

    out.append('-' * 100 + '\n.NET runtime (the published program is self-contained and bundles it)\n' + '-' * 100 + '\n'
               'Copyright (c) .NET Foundation and Contributors\n\n' + MIT + '\n\nThe runtime carries further notices of its own; they are published at\n'
               'https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT\n')
    out.append('=' * 100 + '\nSOURCE CARRIED IN THIS REPOSITORY\n' + '=' * 100 + '\n')
    out.append('-' * 100 + '\nOpenSimulator (BSD-3-Clause), the source under Source/OpenSim.Framework and the files adapted from OpenSim\n' + '-' * 100 + '\n'
               + text_of(os.path.join(ROOT, 'ThirdPartyLicenses', 'OpenSimulator.txt')) + '\n')
    out.append('-' * 100 + '\nMozilla Public License 2.0, for the files whose header names it\n' + '-' * 100 + '\n'
               + text_of(os.path.join(ROOT, 'LICENSES', 'MPL-2.0.txt')) + '\n')
    out.append(LGPL_ITEM_HEAD + text_of(os.path.join(ROOT, 'LICENSES', 'LGPL-2.1.txt')) + '\n')
    out.append(FIXTURE_ITEM)
    open(a.out, 'w', encoding='utf-8', newline='\n').write('\n'.join(out))


if __name__ == '__main__':
    main()
