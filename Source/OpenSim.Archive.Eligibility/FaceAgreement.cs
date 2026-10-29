/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenMetaverse;
using OpenSim.Archive.GltfExport;
using OpenSim.Archive.Inspection;
using OpenSim.Framework;

namespace OpenSim.Archive.Eligibility;

/// <summary>The face count the region server gives a shape and the one the viewer's rule gives it.</summary>
public readonly struct FaceCounts
{
    /// <summary>What <c>llGetNumberOfSides</c> returns (PrimFaceCount.GetNumberOfSides).</summary>
    public int Server { get; }
    /// <summary>The number of faces the viewer builds (ViewerFaceRule): the texture entry slots a converted part can use.</summary>
    public int Viewer { get; }

    public FaceCounts(int server, int viewer)
    {
        Server = server;
        Viewer = viewer;
    }

    public bool Agree => Server == Viewer;
}

/// <summary>
/// Compares the two face counts for a stored shape. Textures land by the viewer's face numbers and scripts count by the
/// server's, so a shape on which they differ changes what a script sees when the part becomes a mesh with the viewer's
/// number of faces.
/// </summary>
public static class FaceAgreement
{
    /// <summary>Builds the server's shape object from the stored fields, with the same text-form override the loader applies.</summary>
    public static PrimitiveBaseShape ToServerShape(ShapeData s) => PrimFaceCount.ToServerShape(s);

    /// <summary>What the server reports for the shape: <see cref="PrimFaceCount.GetNumberOfSides"/>, the region server's rule.</summary>
    public static int ServerCount(ShapeData s) => PrimFaceCount.GetNumberOfSides(ToServerShape(s));

    /// <summary>
    /// The faces a prim of this shape has by the viewer's rule. A sculpt is one face, except a circle profile on a circular
    /// path (what a sculpt is made with), whose profile cut, hollow or open path give it the faces the viewer's rule lists.
    /// </summary>
    public static int ViewerCount(ShapeData s)
    {
        PrimParams p = PrimParams.FromShape(s);
        if (s.SculptEntry && !SculptGeometry.Described(p))
            return 1;
        return ViewerFaceRule.Faces(p).Count;
    }

    public static FaceCounts Measure(ShapeData s) => new FaceCounts(ServerCount(s), ViewerCount(s));
}
