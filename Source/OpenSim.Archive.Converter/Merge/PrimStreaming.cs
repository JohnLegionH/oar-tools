/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenSim.Archive.GltfExport;
using OpenSim.Archive.Inspection;
using OpenSim.MeshAsset;

namespace OpenSim.Archive.Converter.Merge;

/// <summary>
/// The streaming cost the viewer shows for a plain prim. The viewer has no mesh asset to measure, so it estimates the triangles of the prim's four
/// levels of detail from the points of the path and the profile (llvolume.cpp getLoDTriangleCounts 3693-3709, LLProfile::getNumPoints 624-731,
/// LLPath::getNumPoints 1405-1451) at detail 1, 1.5, 2.5 and 4, takes ten bytes a triangle (llvovolume.cpp getCostData 4393-4416) and prices those
/// sizes as it prices a mesh. The point counting follows the viewer's single-precision arithmetic, because its loops compare running sums.
/// </summary>
public static class PrimStreaming
{
    private static readonly float[] Detail = { 1f, 1.5f, 2.5f, 4f };

    /// <summary>Triangles the viewer estimates for the prim at its four levels of detail, lowest first.</summary>
    public static int[] TriangleCounts(ShapeData shape)
    {
        PrimParams p = PrimParams.FromShape(shape);
        int[] counts = new int[4];
        for (int i = 0; i < 4; i++)
        {
            int pathPoints = ViewerPointCounts.PathPoints(p, Detail[i]);
            int profilePoints = ViewerPointCounts.ProfilePoints(p, Detail[i]);
            counts[i] = (profilePoints - 1) * 2 * (pathPoints - 1) + profilePoints * 2;
        }
        return counts;
    }

    /// <summary>The viewer's streaming cost for the prim at the given size (radius half the length of the scale vector).</summary>
    public static double Cost(ShapeData shape, System.Numerics.Vector3 scale)
    {
        int[] c = TriangleCounts(shape);
        return new StreamingCost(c[0] * 10, c[1] * 10, c[2] * 10, c[3] * 10).StaticCost(0.5 * scale.Length());
    }
}
