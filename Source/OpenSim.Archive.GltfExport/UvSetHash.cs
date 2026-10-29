/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using Org.BouncyCastle.Crypto.Digests;

namespace OpenSim.Archive.GltfExport;

/// <summary>
/// A hash of an extra texture coordinate set of one face, recorded in the file so that a tool reading it back can tell whether the
/// set was edited. It covers the coordinates of the face's triangle corners, rounded to a ten-thousandth and put in order, so
/// neither the float rounding nor the vertex order and welding that a modelling tool applies when it writes the file again change
/// it, while an edit that moves a coordinate visibly does. Geometry edits do not change it either: positions are not part of it.
/// </summary>
public static class UvSetHash
{
    private const float Step = 10000f;

    /// <summary>
    /// The hash (64 lower-case hexadecimal characters) of a face's set: two values per vertex, and the vertex numbers of its
    /// triangles (null when every three vertices are a triangle).
    /// </summary>
    public static string Of(IReadOnlyList<float> uvs, IReadOnlyList<int> indices)
    {
        int corners = indices != null ? indices.Count : uvs.Count / 2 / 3 * 3;
        long[] keys = new long[corners];
        for (int c = 0; c < corners; c++)
        {
            int v = indices != null ? indices[c] : c;
            keys[c] = ((long)Quantize(uvs[v * 2]) << 32) | (uint)Quantize(uvs[v * 2 + 1]);
        }
        Array.Sort(keys);
        byte[] bytes = new byte[keys.Length * 8];
        for (int i = 0; i < keys.Length; i++)
            BitConverter.TryWriteBytes(new Span<byte>(bytes, i * 8, 8), keys[i]);
        // BouncyCastle's digest, not the framework's: the packaged programs must not need the system crypto library
        Sha256Digest digest = new Sha256Digest();
        digest.BlockUpdate(bytes, 0, bytes.Length);
        byte[] result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return Convert.ToHexString(result).ToLowerInvariant();
    }

    private static int Quantize(float v) => float.IsFinite(v) ? (int)MathF.Round(Math.Clamp(v, -100000f, 100000f) * Step) : 0;
}
