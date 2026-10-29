/*
 * $LicenseInfo:firstyear=2002&license=viewerlgpl$
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
 * Adapted 2026 by Legion Builds. This file re-implements, in C#, the viewer's point counting for a path and a
 * profile (LLPath::getNumPoints, LLProfile::getNumPoints in indra/llmath/llvolume.cpp).
 */

namespace OpenSim.Archive.GltfExport;

/// <summary>How many points the viewer puts on a path and on a profile at a detail scale (single-precision arithmetic, as the viewer has it).</summary>
public static class ViewerPointCounts
{
    private const int MinDetailFaces = 6;   // MIN_DETAIL_FACES in llvolume.h

    private static int Floor(float x) => (int)MathF.Floor(x);

    // LLPath::getNumPoints
    public static int PathPoints(PrimParams p, float detail)
    {
        switch (p.PathCurve & 0xf0)
        {
            case 0x20:
            {
                float twist = MathF.Abs(p.PathTwistBegin - p.PathTwist);
                int sides = Floor(Floor(MinDetailFaces * detail + twist * 3.5f * (detail - 0.5f)) * p.PathRevolutions);
                return sides;
            }
            case 0x30:
                return PathNGonPoints(p, Floor(MinDetailFaces * detail));
            case 0x40:
                return 5;
            default:
                return Floor(MathF.Abs(p.PathTwistBegin - p.PathTwist) * 3.5f * (detail - 0.5f)) + 2;
        }
    }

    // LLPath::getNumNGonPoints
    private static int PathNGonPoints(PrimParams p, int sides)
    {
        float step = 1.0f / sides;
        float t = p.PathBegin;
        int ret = 1;
        t += step;
        t = (int)(t * sides) / (float)sides;
        while (t < p.PathEnd)
        {
            ret++;
            t += step;
        }
        return ret + 1;
    }

    // LLProfile::getNumPoints
    public static int ProfilePoints(PrimParams p, float detail)
    {
        float hollow = p.ProfileHollow;
        int np;
        switch (p.ProfileCurve & 0x0f)
        {
            case 1:
                np = ProfileNGonPoints(p, 4, 1f);
                return hollow != 0 ? np * 2 : np;
            case 2:
            case 3:
            case 4:
                np = ProfileNGonPoints(p, 3, 1f);
                return hollow != 0 ? np * 2 : np;
            case 0:
            {
                float circle = MinDetailFaces * detail;
                if (hollow != 0 && p.HoleType == 0x20)
                    circle = MathF.Ceiling(circle / 4.0f) * 4.0f;
                np = ProfileNGonPoints(p, (int)circle, 1f);
                return hollow != 0 ? np * 2 : np;
            }
            case 5:
            {
                float circle = MinDetailFaces * detail * 0.5f;
                if (hollow != 0 && p.HoleType == 0x20)
                    circle = MathF.Ceiling(circle / 2.0f) * 2.0f;
                np = ProfileNGonPoints(p, Floor(circle), 0.5f);
                if (hollow != 0)
                    np *= 2;
                // a whole half circle (a sphere's profile) gets one more point; a cut one does not
                if (!(p.ProfileEnd - p.ProfileBegin < 1.0f) && hollow == 0)
                    np++;
                return np;
            }
            default:
                return 0;
        }
    }

    // LLProfile::getNumNGonPoints
    private static int ProfileNGonPoints(PrimParams p, int sides, float angScale)
    {
        int np = 0;
        float begin = p.ProfileBegin, end = p.ProfileEnd;
        float step = 1.0f / sides;
        float first = MathF.Floor(begin * sides) / sides;
        float t = first;
        t += step;
        float fraction = (begin - first) * sides;
        if (fraction < 0.9999f)
            np++;
        while (t < end)
        {
            np++;
            t += step;
        }
        fraction = (end - (t - step)) * sides;
        if (fraction > 0.0001f)
            np++;
        // a cut profile is open; without a hollow it gets a centre point
        if ((end - begin) * angScale < 0.99f && p.ProfileHollow <= 0)
            np++;
        return np;
    }
}
