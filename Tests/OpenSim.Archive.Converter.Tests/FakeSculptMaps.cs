/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.IO.Compression;
using System.Text;
using OpenMetaverse;
using OpenSim.Archive.Converter;
using OpenSim.Archive.Eligibility;
using OpenSim.Archive.GltfExport;
using OpenSim.Archive.Inspection;
using Xunit;

namespace OpenSim.Archive.Eligibility.Tests;

/// <summary>A sculpt map made in code, so a sculpt part can be converted without a JPEG 2000 file.</summary>
internal sealed class FakeSculptMaps : ISculptMapSource
{
    public SculptMap Get(Guid textureId, out string problem)
    {
        problem = null;
        const int n = 32;
        byte[] rgb = new byte[n * n * 3];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double th = Math.PI * y / (n - 1), ph = 2 * Math.PI * x / n;
                int i = (y * n + x) * 3;
                rgb[i] = (byte)(127.5 + 127.5 * Math.Sin(th) * Math.Cos(ph));
                rgb[i + 1] = (byte)(127.5 + 127.5 * Math.Sin(th) * Math.Sin(ph));
                rgb[i + 2] = (byte)(127.5 + 127.5 * Math.Cos(th));
            }
        return new SculptMap { Width = n, Height = n, Rgb = rgb };
    }
}
