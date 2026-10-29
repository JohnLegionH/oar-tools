/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using OpenSim.Archive.Inspection;
using Xunit;

namespace OpenSim.Archive.GltfExport.Tests.ViewerGeometry;

/// <summary>
/// The export's geometry against what the Second Life viewer's own volume code builds, shape by shape and level of detail by
/// level of detail. The reference data (Fixtures/ViewerReference) holds, for each shape, every face's vertices, normals,
/// texture coordinates and triangles in the viewer's face order, as printed by the viewer's volume code built as a test
/// program (see docs/GEOMETRY.md). Shapes are given by their stored fields; sculpts by synthetic maps.
///
/// A face matches when, after merging vertices that agree, it has the same number of vertices and triangles, every vertex
/// lies within 1e-4 of the unit box of a vertex of the reference and the other way round, texture coordinates agree to 1e-4,
/// unit normals to 2e-3, and the triangles join the same vertices with the same winding. Face numbers match by position
/// in the list.
/// </summary>
public class ViewerGeometryTests
{
    private static readonly Lazy<Dictionary<string, RefCase>>[] Reference =
        Enumerable.Range(0, 4).Select(lod => new Lazy<Dictionary<string, RefCase>>(() => ViewerReference.Load("dump-lod" + lod + ".txt.gz"))).ToArray();

    private static readonly Lazy<Dictionary<string, ShapeData>> Shapes =
        new Lazy<Dictionary<string, ShapeData>>(() => ViewerReference.LoadShapes("cases.tsv").ToDictionary(s => s.Name, s => s.Shape));

    private static readonly PrimParams PlainSculpt = new PrimParams
    {
        ProfileCurve = 0, PathCurve = 32, ProfileEnd = 1f, PathEnd = 1f, PathScaleX = 1f, PathScaleY = 1f, PathRevolutions = 1f
    };

    public static IEnumerable<object[]> PrimCases()
    {
        foreach ((string name, ShapeData _) in ViewerReference.LoadShapes("cases.tsv"))
            for (int lod = 0; lod < 4; lod++)
                yield return new object[] { lod, name };
    }

    public static IEnumerable<object[]> SculptCases()
    {
        foreach ((string name, int _, int _, int _, string _) in ViewerReference.LoadSculpts("sculpt-cases.tsv"))
            for (int lod = 0; lod < 4; lod++)
                yield return new object[] { lod, name };
    }

    public static IEnumerable<object[]> CleaningCases()
    {
        // the clean-up does not depend on the level of detail; the highest has the most vertices
        foreach ((string name, ShapeData _) in ViewerReference.LoadShapes("cases.tsv"))
            yield return new object[] { 3, name };
    }

    private static List<string> Differences(RefCase reference, Func<int, RefFace> exportFace, int exportFaceCount)
    {
        List<string> bad = new List<string>();
        if (reference.Faces.Count != exportFaceCount)
            bad.Add("the viewer has " + reference.Faces.Count + " faces, the export " + exportFaceCount);
        for (int i = 0; i < reference.Faces.Count; i++)
        {
            RefFace expected = MeshCompare.Weld(reference.Faces[i]);
            RefFace actual = exportFace(i) ?? new RefFace();
            FaceDiff d = MeshCompare.Compare(i, expected, actual);
            if (!d.Matches(Tolerance.Default))
                bad.Add(d.ToString());
        }
        return bad;
    }

    [Theory]
    [MemberData(nameof(PrimCases))]
    public void AShapeIsTessellatedAsTheViewerDoesAtEachLevelOfDetail(int level, string name)
    {
        PrimParams p = PrimParams.FromShape(Shapes.Value[name]);
        List<PrimFace> faces = PrimGeometry.BuildFaces(p, ViewerReference.Details[level], out int count, out int unnumbered);
        RefCase reference = Reference[level].Value[name];

        List<string> bad = Differences(reference, i =>
        {
            PrimFace f = faces.FirstOrDefault(x => x.Number == i);
            return f == null ? null : MeshCompare.Weld(f, true);
        }, count);
        Assert.True(bad.Count == 0, name + " at level " + level + ": " + string.Join("; ", bad));
        Assert.Equal(0, unnumbered);
    }

    [Theory]
    [MemberData(nameof(SculptCases))]
    public void ASculptIsTessellatedAsTheViewerDoesAtEachLevelOfDetail(int level, string name)
    {
        (string _, int type, int width, int height, string mapFile) = ViewerReference.LoadSculpts("sculpt-cases.tsv").First(s => s.Name == name);
        SculptResult r = SculptGeometry.BuildVolume(ViewerReference.LoadMap(mapFile, width, height), type, PlainSculpt, ViewerReference.Details[level]);
        RefCase reference = Reference[level].Value[name];

        List<string> bad = Differences(reference, i =>
        {
            PrimFace f = r.Faces.FirstOrDefault(x => x.Number == i);
            return f == null ? null : MeshCompare.Weld(f, true);
        }, r.ViewerFaceCount);
        Assert.True(bad.Count == 0, name + " at level " + level + ": " + string.Join("; ", bad));
    }

    /// <summary>
    /// The export can be written with its vertex clean-up off or on. The clean-up merges vertices and drops triangles with
    /// no area; the faces' positions, texture coordinates, normals, triangles and numbers must stay those of the viewer.
    /// </summary>
    [Theory]
    [MemberData(nameof(CleaningCases))]
    public void CleaningTheFacesKeepsThemAsTheViewerBuildsThem(int level, string name)
    {
        PrimParams p = PrimParams.FromShape(Shapes.Value[name]);
        List<PrimFace> faces = PrimGeometry.BuildFaces(p, ViewerReference.Details[level], out int count, out _);
        RefCase reference = Reference[level].Value[name];

        List<string> bad = Differences(reference, i =>
        {
            PrimFace f = faces.FirstOrDefault(x => x.Number == i);
            if (f == null)
                return null;
            CleanedFace c = GeometryCleaner.Clean(f.Positions, f.Normals, f.Uvs);
            RefFace r = new RefFace();
            for (int v = 0; v < c.VertexCount; v++)
                r.Vertices.Add(new RefVertex(c.Positions[v * 3], c.Positions[v * 3 + 1], c.Positions[v * 3 + 2],
                    c.Normals[v * 3], c.Normals[v * 3 + 1], c.Normals[v * 3 + 2], c.Uvs[v * 2], 1f - c.Uvs[v * 2 + 1]));
            for (int t = 0; t < c.TriangleCount; t++)
                r.Triangles.Add(new[] { c.Indices[t * 3], c.Indices[t * 3 + 1], c.Indices[t * 3 + 2] });
            return MeshCompare.Weld(r);
        }, count);
        Assert.True(bad.Count == 0, name + " cleaned: " + string.Join("; ", bad));
    }

    [Fact]
    public void TheHighestLevelOfDetailIsTheViewersLastDetailScale()
    {
        Assert.Equal(4f, PrimGeometry.HighestDetail);
        Assert.Equal(ViewerReference.Details[3], PrimGeometry.HighestDetail);
        // the overloads that take a level use the highest
        PrimParams p = PrimParams.FromShape(Shapes.Value["cylinder-plain"]);
        Assert.Equal(PrimGeometry.BuildFaces(p, 4f, out int a, out _).Sum(f => f.TriangleCount), PrimGeometry.BuildFaces(p, DetailLevel.High, out int b, out _).Sum(f => f.TriangleCount));
        Assert.Equal(a, b);
    }
}
