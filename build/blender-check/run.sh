#!/bin/sh
# Copyright (c) 2026 Legion Builds
#
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# Runs the Blender import check (check_import.py) in a throwaway Ubuntu 24.04 container, in two Blender versions: the distribution's
# package and the current long-term-support build from blender.org. Nothing is installed on the machine that starts the container.
#
#   docker run --rm -v <repository>:/src:ro -v <fixtures folder>:/fixtures:ro -v <results folder>:/results ubuntu:24.04 sh /src/build/blender-check/run.sh
#
# <fixtures folder> is the folder BlenderFixtureTests writes when OAR_TOOLS_BLENDER_FIXTURES names it. The results folder receives one
# JSON file and one log per version and layout. The exit status is 0 only when every check passed in both versions.
#
# BLENDER_LTS_URL names the long-term-support download (the link on https://www.blender.org/download/lts/ points at blender.org's mirror); check that page for the current one before a release.

BLENDER_LTS_URL=${BLENDER_LTS_URL:-https://mirror.blender.org/release/Blender5.2/blender-5.2.3-linux-x64.tar.xz}

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq >/dev/null || exit 2
# the package does not pull in numpy, which the glTF importer needs
apt-get install -y -qq curl ca-certificates xz-utils blender python3-numpy >/dev/null || exit 2
# libraries the downloaded build needs to start without a display
apt-get install -y -qq libxi6 libxxf86vm1 libxfixes3 libxrender1 libgl1 libxkbcommon0 libsm6 libegl1 >/dev/null || exit 2

mkdir -p /results /opt/blender-lts
curl -sL -o /opt/blender-lts.tar.xz "$BLENDER_LTS_URL" || exit 2
tar -xf /opt/blender-lts.tar.xz -C /opt/blender-lts --strip-components=1 || exit 2

status=0
for pair in "ubuntu:blender" "lts:/opt/blender-lts/blender"; do
    label=${pair%%:*}
    exe=${pair#*:}
    for layout in default clean; do
        "$exe" --background --factory-startup --python /src/build/blender-check/check_import.py -- /fixtures "$layout" "/results/$label-$layout.json" > "/results/$label-$layout.log" 2>&1
        code=$?
        grep -E '^(Blender|RESULT)' "/results/$label-$layout.log"
        grep -E '^FAIL' "/results/$label-$layout.log" | head -20
        [ $code -eq 0 ] || status=1
    done
done
exit $status
