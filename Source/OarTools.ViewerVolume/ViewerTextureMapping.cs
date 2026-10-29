/*
 * $LicenseInfo:firstyear=2001&license=viewerlgpl$
 * Second Life Viewer Source Code
 * Copyright (C) 2010, Linden Research, Inc.
 *
 * This library is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation;
 * version 2.1 of the License only.
 *
 * This library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public
 * License along with this library; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 *
 * Linden Research, Inc., 945 Battery Street, San Francisco, CA  94111  USA
 * $/LicenseInfo$
 *
 * Adapted 2026 by Legion Builds. This file re-implements, in C#, the viewer's texture coordinate steps for a face
 * (planarProjection and xform in indra/newview/llface.cpp).
 */

namespace OpenSim.Archive.GltfExport;

/// <summary>
/// The texture coordinates the viewer draws a face with when glTF's KHR_texture_transform cannot say them: a planar face, and a face
/// that is both rotated and repeated by unequal amounts. They go in a second texture coordinate set, and the face's original
/// coordinates stay untouched in the first.
/// </summary>
public static class ViewerTextureMapping
{
    /// <summary>Smallest |sin(rotation) * (repeatU - repeatV)| at which KHR_texture_transform no longer matches the viewer's order of steps.</summary>
    private const float ExactnessLimit = 1e-6f;

    /// <summary>
    /// True when the face's mapping needs the second set: it is planar, or it is rotated with unequal repeats (the viewer turns the
    /// coordinates before it repeats them, KHR_texture_transform repeats before it turns).
    /// </summary>
    public static bool NeedsSecondSet(bool planar, float repeatU, float repeatV, float rotation) =>
        planar || MathF.Abs(MathF.Sin(rotation) * (repeatU - repeatV)) >= ExactnessLimit;

    /// <summary>The reason the face needs the second set, or null when it does not.</summary>
    public static string SecondSetReason(bool planar, float repeatU, float repeatV, float rotation) =>
        planar ? "planar mapping" : NeedsSecondSet(false, repeatU, repeatV, rotation) ? "rotation with unequal repeats" : null;

    /// <summary>
    /// The face's coordinates as the viewer draws them, in glTF's convention (v down), two values for each vertex. The positions and
    /// normals are the face's own (the unit volume), <paramref name="sizeX"/> and the two after it are the part's size; a planar face is
    /// projected from them and ignores <paramref name="uvs"/> (the face's default coordinates, v down).
    /// </summary>
    public static float[] SecondSet(float[] positions, float[] normals, float[] uvs, bool planar, float sizeX, float sizeY, float sizeZ,
        float repeatU, float repeatV, float offsetU, float offsetV, float rotation)
    {
        int n = uvs.Length / 2;
        float[] result = new float[n * 2];
        float cos = (float)Math.Cos(rotation), sin = (float)Math.Sin(rotation);
        for (int i = 0; i < n; i++)
        {
            float s, t;
            if (planar)
                Project(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2], positions[i * 3] * sizeX, positions[i * 3 + 1] * sizeY, positions[i * 3 + 2] * sizeZ, out s, out t);
            else
            {
                s = uvs[i * 2];
                t = 1f - uvs[i * 2 + 1];
            }

            // the face's transform: about the middle of the texture, turn, then repeat, then offset
            s -= 0.5f;
            t -= 0.5f;
            float turned = s;
            s = s * cos + t * sin;
            t = -turned * sin + t * cos;
            s *= repeatU;
            t *= repeatV;
            s += offsetU + 0.5f;
            t += offsetV + 0.5f;

            result[i * 2] = float.IsFinite(s) ? s : 0f;
            result[i * 2 + 1] = float.IsFinite(t) ? 1f - t : 0f;
        }
        return result;
    }

    /// <summary>Planar projection of one vertex: a normal mostly along x projects on the y axis, any other normal on the x axis; the texture repeats every half metre.</summary>
    private static void Project(float nx, float ny, float nz, float px, float py, float pz, out float u, out float v)
    {
        float bx, by, bz;
        if (nx >= 0.5f || nx <= -0.5f)
        {
            bx = 0f; by = nx < 0f ? -1f : 1f; bz = 0f;
        }
        else
        {
            bx = ny > 0f ? -1f : 1f; by = 0f; bz = 0f;
        }
        // the tangent is the binormal crossed with the normal
        float tx = by * nz - bz * ny, ty = bz * nx - bx * nz, tz = bx * ny - by * nx;
        v = -((tx * px + ty * py + tz * pz) * 2f - 0.5f);
        u = 1f + ((bx * px + by * py + bz * pz) * 2f - 0.5f);
    }
}
