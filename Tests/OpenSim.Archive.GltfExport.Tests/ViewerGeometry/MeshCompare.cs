/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

namespace OpenSim.Archive.GltfExport.Tests.ViewerGeometry;

/// <summary>How one face of the export differs from the same face of the reference.</summary>
public sealed class FaceDiff
{
    public int Face;
    public int RefVertices, ExportVertices, RefTriangles, ExportTriangles;
    /// <summary>Largest distance (per axis) from a vertex of one mesh to the nearest vertex of the other, both ways.</summary>
    public float MaxPosition;
    /// <summary>Largest texture coordinate difference at matched positions (the closest of the vertices at that position).</summary>
    public float MaxUv;
    /// <summary>Largest normal difference (length of the difference of the unit normals) at matched positions.</summary>
    public float MaxNormal;
    /// <summary>Triangles, as sets of reference vertices with their winding, present in only one of the two meshes.</summary>
    public int TrianglesOnlyInRef, TrianglesOnlyInExport;

    public bool CountsEqual => RefVertices == ExportVertices && RefTriangles == ExportTriangles;

    public bool Matches(Tolerance t) => CountsEqual && MaxPosition <= t.Position && MaxUv <= t.Uv && MaxNormal <= t.Normal
        && TrianglesOnlyInRef == 0 && TrianglesOnlyInExport == 0;

    public override string ToString() =>
        "face " + Face + " verts " + RefVertices + "/" + ExportVertices + " tris " + RefTriangles + "/" + ExportTriangles +
        " pos " + MaxPosition.ToString("0.#####") + " uv " + MaxUv.ToString("0.#####") + " nrm " + MaxNormal.ToString("0.#####") +
        " triOnlyRef " + TrianglesOnlyInRef + " triOnlyExp " + TrianglesOnlyInExport;
}

public readonly record struct Tolerance(float Position, float Uv, float Normal)
{
    /// <summary>Positions and texture coordinates agree to 1e-4 of the unit box; normals to 2e-3 (the viewer's own normals are not unit length to better than that).</summary>
    public static readonly Tolerance Default = new Tolerance(1e-4f, 1e-4f, 2e-3f);
}

public static class MeshCompare
{
    // Vertices are merged when they agree to the comparison tolerance, found through a grid of cells as wide as the position tolerance.
    private sealed class Welder
    {
        public readonly RefFace Result = new RefFace();
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();

        private static int Cell(float v) => (int)MathF.Floor(v / 1e-3f);

        public int Add(RefVertex v)
        {
            int cx = Cell(v.X), cy = Cell(v.Y), cz = Cell(v.Z);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                        if (_cells.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> list))
                            foreach (int id in list)
                            {
                                RefVertex o = Result.Vertices[id];
                                if (Dist(v, o) <= 1e-4f && MathF.Abs(v.U - o.U) <= 1e-4f && MathF.Abs(v.V - o.V) <= 1e-4f && NormalDiff(v, o) <= 1e-3f)
                                    return id;
                            }
            int fresh = Result.Vertices.Count;
            Result.Vertices.Add(v);
            if (!_cells.TryGetValue((cx, cy, cz), out List<int> own))
                _cells[(cx, cy, cz)] = own = new List<int>();
            own.Add(fresh);
            return fresh;
        }
    }

    /// <summary>The export's face as an indexed mesh: its non-indexed triangles with equal vertices merged.</summary>
    public static RefFace Weld(PrimFace f, bool flipV)
    {
        Welder w = new Welder();
        int tris = f.Positions.Length / 9;
        for (int t = 0; t < tris; t++)
        {
            int[] ids = new int[3];
            for (int k = 0; k < 3; k++)
            {
                int p = t * 9 + k * 3, u = t * 6 + k * 2;
                float v = flipV ? 1f - f.Uvs[u + 1] : f.Uvs[u + 1];
                ids[k] = w.Add(new RefVertex(f.Positions[p], f.Positions[p + 1], f.Positions[p + 2], f.Normals[p], f.Normals[p + 1], f.Normals[p + 2], f.Uvs[u], v));
            }
            w.Result.Triangles.Add(ids);
        }
        return w.Result;
    }

    /// <summary>The reference face with equal vertices merged the same way, so counts compare like with like.</summary>
    public static RefFace Weld(RefFace f)
    {
        Welder w = new Welder();
        int[] map = new int[f.Vertices.Count];
        for (int i = 0; i < map.Length; i++)
        {
            // the viewer keeps normals as sums (their length follows the area around the vertex) and scales them to unit length when it draws
            RefVertex v = f.Vertices[i];
            var (nx, ny, nz) = Unit(v);
            map[i] = w.Add(v with { Nx = nx, Ny = ny, Nz = nz });
        }
        foreach (int[] t in f.Triangles)
            w.Result.Triangles.Add(new[] { map[t[0]], map[t[1]], map[t[2]] });
        return w.Result;
    }

    private static bool Degenerate(RefFace f, int[] t)
    {
        if (t[0] == t[1] || t[1] == t[2] || t[0] == t[2]) return true;
        RefVertex a = f.Vertices[t[0]], b = f.Vertices[t[1]], c = f.Vertices[t[2]];
        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
        float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
        return 0.5f * MathF.Sqrt(cx * cx + cy * cy + cz * cz) < 1e-6f;
    }

    private static float Dist(RefVertex a, RefVertex b) => MathF.Max(MathF.Abs(a.X - b.X), MathF.Max(MathF.Abs(a.Y - b.Y), MathF.Abs(a.Z - b.Z)));

    private static (float, float, float) Unit(RefVertex v)
    {
        float l = MathF.Sqrt(v.Nx * v.Nx + v.Ny * v.Ny + v.Nz * v.Nz);
        return l < 1e-9f ? (0, 0, 0) : (v.Nx / l, v.Ny / l, v.Nz / l);
    }

    private static float NormalDiff(RefVertex a, RefVertex b)
    {
        var (ax, ay, az) = Unit(a);
        var (bx, by, bz) = Unit(b);
        return MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
    }

    /// <summary>Compares an export face with a reference face. Both must already be welded. Vertices that a triangle with area uses are compared.</summary>
    public static FaceDiff Compare(int face, RefFace reference, RefFace export)
    {
        List<int[]> rt = reference.Triangles.Where(t => !Degenerate(reference, t)).ToList();
        List<int[]> et = export.Triangles.Where(t => !Degenerate(export, t)).ToList();
        List<int> rv = rt.SelectMany(t => t).Distinct().ToList(), ev = et.SelectMany(t => t).Distinct().ToList();
        FaceDiff d = new FaceDiff { Face = face, RefVertices = rv.Count, ExportVertices = ev.Count, RefTriangles = rt.Count, ExportTriangles = et.Count };

        // Each side's vertex nearest the other's by position (the largest of these is the position difference).
        if (ev.Count > 0)
        {
            foreach (int e in ev)
                d.MaxPosition = MathF.Max(d.MaxPosition, rv.Min(r => Dist(export.Vertices[e], reference.Vertices[r])));
            foreach (int r in rv)
                d.MaxPosition = MathF.Max(d.MaxPosition, ev.Min(e => Dist(export.Vertices[e], reference.Vertices[r])));
        }

        // Pair each export vertex with the reference vertex it most resembles: same place first, then the same texture
        // coordinates and normal, so vertices that share a place (the two sides of a hard edge) are told apart.
        int[] exportToRef = new int[export.Vertices.Count];
        Array.Fill(exportToRef, -1);
        foreach (int e in ev)
        {
            float best = float.MaxValue;
            int bi = -1;
            foreach (int r in rv)
            {
                RefVertex ex = export.Vertices[e], re = reference.Vertices[r];
                float pd = Dist(ex, re);
                if (pd > 1e-3f) continue;
                float ud = MathF.Max(MathF.Abs(ex.U - re.U), MathF.Abs(ex.V - re.V));
                float cost = pd + ud + NormalDiff(ex, re);
                if (cost < best) { best = cost; bi = r; }
            }
            if (bi < 0) continue;
            exportToRef[e] = bi;
            RefVertex a1 = export.Vertices[e], b1 = reference.Vertices[bi];
            d.MaxUv = MathF.Max(d.MaxUv, MathF.Max(MathF.Abs(a1.U - b1.U), MathF.Abs(a1.V - b1.V)));
            d.MaxNormal = MathF.Max(d.MaxNormal, NormalDiff(a1, b1));
        }

        // triangle topology: triangles as rotations of reference vertex numbers, which keeps the winding
        HashSet<(int, int, int)> refSet = new(rt.Select(Canon)), expSet = new();
        int unmapped = 0;
        foreach (int[] t in et)
        {
            int a = exportToRef[t[0]], b = exportToRef[t[1]], c = exportToRef[t[2]];
            if (a < 0 || b < 0 || c < 0) { unmapped++; continue; }
            expSet.Add(Canon(new[] { a, b, c }));
        }
        d.TrianglesOnlyInRef = refSet.Count(x => !expSet.Contains(x));
        d.TrianglesOnlyInExport = expSet.Count(x => !refSet.Contains(x)) + unmapped;
        return d;
    }

    private static (int, int, int) Canon(int[] t)
    {
        int a = t[0], b = t[1], c = t[2];
        if (b < a && b < c) return (b, c, a);
        if (c < a && c < b) return (c, a, b);
        return (a, b, c);
    }
}
