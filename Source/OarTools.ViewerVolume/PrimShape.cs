/* Copyright (c) 2026 Legion Builds
 *
 * SPDX-License-Identifier: LGPL-2.1-only
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
 */

using OpenMetaverse;
using OpenSim.Archive.Inspection;
using OpenSim.Framework;

namespace OpenSim.Archive.GltfExport;

/// <summary>A shape with its fields decoded to the values the mesher takes.</summary>
public sealed class PrimParams
{
    /// <summary>0 circle, 1 square, 2 isometric triangle, 3 equilateral triangle, 4 right triangle, 5 half circle.</summary>
    public int ProfileCurve;
    /// <summary>0 same as the profile, 16 circle, 32 square, 48 triangle.</summary>
    public int HoleType;
    /// <summary>16 straight line, 32 and 48 circular, 128 flexible.</summary>
    public int PathCurve;
    public float ProfileBegin, ProfileEnd, ProfileHollow;
    public float PathBegin, PathEnd;
    public float PathScaleX, PathScaleY;
    public float PathShearX, PathShearY;
    public float PathTwist, PathTwistBegin;
    public float PathRadiusOffset, PathTaperX, PathTaperY;
    public float PathRevolutions, PathSkew;

    /// <summary>
    /// Decodes the stored byte and short fields the way the viewer protocol defines them: a profile or path cut is
    /// the stored value times 0.00002 (an end is counted from the far side), scale is 2 minus the stored value over
    /// 100, shear is a signed byte over 100, twist, taper, radius and skew are signed bytes over 100, revolutions
    /// are 1 plus 0.015 per step.
    /// </summary>
    public static PrimParams FromShape(ShapeData s)
    {
        int curve = s.ProfileCurve;
        int profile = curve & 0x0F;
        int hole = curve & 0xF0;
        if (!string.IsNullOrEmpty(s.ProfileShapeName) && Enum.TryParse(s.ProfileShapeName, true, out ProfileShape ps))
            profile = (int)ps;
        if (!string.IsNullOrEmpty(s.HollowShapeName) && Enum.TryParse(s.HollowShapeName, true, out HollowShape hs))
            hole = (int)hs;

        return new PrimParams
        {
            ProfileCurve = profile,
            HoleType = hole,
            PathCurve = s.PathCurve,
            ProfileBegin = s.ProfileBegin * 2.0e-5f,
            ProfileEnd = 1.0f - s.ProfileEnd * 2.0e-5f,
            ProfileHollow = s.ProfileHollow * 2.0e-5f,
            PathBegin = s.PathBegin * 2.0e-5f,
            PathEnd = 1.0f - s.PathEnd * 2.0e-5f,
            PathScaleX = (200 - s.PathScaleX) * 0.01f,
            PathScaleY = (200 - s.PathScaleY) * 0.01f,
            PathShearX = ShearOf(s.PathShearX),
            PathShearY = ShearOf(s.PathShearY),
            PathTwist = SByteOf(s.PathTwist) * 0.01f,
            PathTwistBegin = SByteOf(s.PathTwistBegin) * 0.01f,
            PathRadiusOffset = SByteOf(s.PathRadiusOffset) * 0.01f,
            PathTaperX = SByteOf(s.PathTaperX) * 0.01f,
            PathTaperY = SByteOf(s.PathTaperY) * 0.01f,
            PathRevolutions = 1.0f + 0.015f * (s.PathRevolutions & 0xFF),
            PathSkew = SByteOf(s.PathSkew) * 0.01f
        };
    }

    private static float ShearOf(int stored)
    {
        int b = stored & 0xFF;
        return b < 128 ? b * 0.01f : (b - 256) * 0.01f;
    }

    // The XML holds signed bytes as plain numbers; a file written by another tool may hold them as 0..255.
    private static int SByteOf(int stored) => (sbyte)(stored & 0xFF);
}

/// <summary>One prim face: non-indexed triangles in the prim's own axes, before the part's size is applied.</summary>
public sealed class PrimFace
{
    /// <summary>The face number a texture entry uses for this face.</summary>
    public int Number;
    public PrimFaceKind Kind;
    /// <summary>The number the mesher gave the first triangle of this face; for information only.</summary>
    public int MesherNumber;
    public float[] Positions;
    public float[] Normals;
    public float[] Uvs;
    public int TriangleCount => Positions.Length / 9;
}
