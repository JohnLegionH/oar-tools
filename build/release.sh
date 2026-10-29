#!/bin/sh
# Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
# Builds the release packages: the Windows zip and the Linux tar.gz with SHA256SUMS.txt. Nothing is uploaded.
# usage: build/release.sh <output folder> [work folder]
# The folders are given as paths the dotnet tool understands (D:/work/x on Windows, /work/x on Linux).
# THIRD-PARTY-NOTICES.txt is regenerated from the published programs' dependency lists when python and the NuGet cache are
# available (set NUGET_PACKAGES to the cache folder; its default is ~/.nuget/packages).
set -e
out=$1
work=${2:-$1/work}
if [ -z "$out" ]; then
  echo "usage: build/release.sh <output folder> [work folder]" >&2
  exit 64
fi
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# the first of python3 and python that really runs (on Windows, python3 can be a Store shortcut that does not)
python=${PYTHON:-}
if [ -z "$python" ]; then
  for candidate in python3 python; do
    if "$candidate" -c "import sys" >/dev/null 2>&1; then python=$candidate; break; fi
  done
fi
if [ -z "$python" ]; then
  echo "no working python found; set PYTHON" >&2
  exit 69
fi

for rid in win-x64 linux-x64; do
  sh "$root/build/publish.sh" "$rid" "$work/$rid"
done

nuget=${NUGET_PACKAGES:-$HOME/.nuget/packages}
"$python" -I "$root/build/gen-notices.py" --nuget "$nuget" \
  --deps "$root/Source/OpenSim.ArchiveInspector/obj/Release/net10.0/win-x64/oar-tools.deps.json" \
  --deps "$root/Source/OpenSim.ArchiveInspector/obj/Release/net10.0/linux-x64/oar-tools.deps.json" \
  --out "$root/THIRD-PARTY-NOTICES.txt"

"$python" -I "$root/build/package.py" --win "$work/win-x64" --linux "$work/linux-x64" --out "$out"
