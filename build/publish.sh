#!/bin/sh
# Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
# Publishes oar-tools as a self-contained single-file program for one platform.
# usage: build/publish.sh <win-x64|linux-x64> <output folder>
# The folder must be given as a path the dotnet tool understands (D:/work/x on Windows, /work/x on Linux).
set -e
rid=$1
out=$2
if [ -z "$rid" ] || [ -z "$out" ]; then
  echo "usage: build/publish.sh <win-x64|linux-x64> <output folder>" >&2
  exit 64
fi
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
dotnet publish "$root/Source/OpenSim.ArchiveInspector/OpenSim.ArchiveInspector.csproj" \
  -c Release -r "$rid" --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:OarToolsPublish=true \
  -nodeReuse:false -p:UseSharedCompilation=false \
  -o "$out"
