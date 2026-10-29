/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Numerics;

namespace OpenSim.Archive.GltfExport;

/// <summary>
/// Builds a prim's visual mesh the way the Second Life viewer tessellates it: the same points on the profile and the path at
/// a given level of detail, the same faces in the same order, the same triangles, texture coordinates and normals.
/// The method follows the public description and source of the viewer's volume code (the LLProfile, LLPath and LLVolumeFace
/// classes in indra/llmath/llvolume.cpp): this file states each rule in its own words and its own structure and carries none of
/// that code. It was checked against a build of the viewer's volume code for every shape of the test set at every level of
/// detail (see docs/GEOMETRY.md).
///
/// Coordinates are the prim's own, in the unit box before the part's size is applied; texture coordinates have v up, as the
/// viewer has them.
/// </summary>
internal static class ViewerVolume
{
    /// <summary>One face of the volume, as an indexed triangle list.</summary>
    internal sealed class VolumeFace
    {
        public PrimFaceKind Kind;
        public List<Vector3> Positions = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector2> Uvs = new List<Vector2>();
        public List<int> Indices = new List<int>();
    }

    // A profile face is a run of profile points. The ids say what the face is.
    private const int IdPathBegin = 1, IdPathEnd = 2, IdInner = 4, IdProfileBegin = 8, IdProfileEnd = 16, IdOuter = 32;

    private sealed class ProfileFace
    {
        public int Index, Count;
        public bool Cap, Flat;
        public int Id;
    }

    private sealed class Profile
    {
        public List<Vector3> Points = new List<Vector3>();   // x, y and the parameter along the profile (which texture u follows)
        public List<ProfileFace> Faces = new List<ProfileFace>();
        public bool Open;
        public int Total, TotalOut;
    }

    private sealed class PathPoint
    {
        public Vector3 Position;
        public Vector2 Scale;
        public Quaternion First, Then;   // the rotation is First, then Then
        public float TexT;
    }

    private sealed class Path
    {
        public List<PathPoint> Points = new List<PathPoint>();
        public bool Open = true;
    }

    private static readonly float[] CornerScale = { 1f, 1f, 1f, 0.5f, 0.707107f, 0.53f, 0.525f, 0.5f };

    private static float Lerp(float a, float b, float u) => a + (b - a) * u;

    // the point a fraction of the way from a to b, worked out as a + (b - a) * u, which is not always the same float as a * (1 - u) + b * u
    private static Vector3 Between(Vector3 a, Vector3 b, float u) => a + (b - a) * u;

    // The viewer multiplies the sine or cosine of a float angle, taken in double precision, by a float and rounds once to float; doing
    // the same keeps the points that lie on a straight line (where a triangulation decision turns on the last bit) alike.
    private static float Scaled(Func<double, double> trig, float angle, float scale) => (float)(trig(angle) * scale);

    public static List<VolumeFace> Generate(PrimParams p, float detail)
    {
        int split = (int)(detail * 0.66f);
        int profileCurve = p.ProfileCurve & 0x0F;
        bool straight = p.PathCurve == 16 || p.PathCurve == 128;
        // a profile with a straight edge keeps its points when the path is scaled, so the tessellation is not split
        if (straight && (p.PathScaleX != 1f || p.PathScaleY != 1f) && profileCurve >= 1 && profileCurve <= 4)
            split = 0;

        Path path = straight ? LinePath(p, detail, split) : CirclePath(p, detail, (p.PathCurve & 0xF0) == 0x30);
        Profile profile = MakeProfile(p, path.Open, detail, split);

        int rows = path.Points.Count, cols = profile.Points.Count;
        Vector3[] mesh = new Vector3[rows * cols];
        for (int s = 0; s < rows; s++)
        {
            PathPoint pt = path.Points[s];
            for (int j = 0; j < cols; j++)
            {
                Vector3 v = new Vector3(profile.Points[j].X * pt.Scale.X, profile.Points[j].Y * pt.Scale.Y, 0f);
                v = Vector3.Transform(Vector3.Transform(v, pt.First), pt.Then);
                mesh[s * cols + j] = v + pt.Position;
            }
        }

        List<VolumeFace> faces = new List<VolumeFace>();
        bool hollow = p.ProfileHollow > 0f;
        foreach (ProfileFace pf in profile.Faces)
        {
            VolumeFace vf;
            if (pf.Cap)
            {
                vf = Cap(p, path, profile, mesh, pf.Id == IdPathBegin, hollow);
                vf.Kind = pf.Id == IdPathBegin ? PrimFaceKind.Top : PrimFaceKind.Bottom;
            }
            else
            {
                bool end = (pf.Id & (IdProfileBegin | IdProfileEnd)) != 0;
                bool inner = (pf.Id & IdInner) != 0;
                vf = Side(p, path, profile, mesh, pf, end, inner);
                vf.Kind = (pf.Id & IdProfileBegin) != 0 ? PrimFaceKind.ProfileCutBegin
                    : (pf.Id & IdProfileEnd) != 0 ? PrimFaceKind.ProfileCutEnd
                    : inner ? PrimFaceKind.Inner : PrimFaceKind.Outer;
            }
            faces.Add(vf);
        }
        return faces;
    }

    // ------------------------------------------------------------------ the profile

    private static void AddPointsBetween(List<Vector3> pts, Vector3 to, int split)
    {
        if (pts.Count == 0)
            return;
        Vector3 from = pts[pts.Count - 1];
        for (int i = 0; i < split; i++)
            pts.Add(from + (to - from) * (1f / (split + 1) * (i + 1)));
    }

    /// <summary>
    /// Points around an n-sided figure, from the start of the cut to its end. A point sits at every whole step of the profile
    /// parameter between the cuts; the cuts add a point at their own parameter. Between two neighbours <paramref name="split"/>
    /// further points are placed on the straight line. A figure that is cut open gets its centre as a last point unless it is hollow.
    /// </summary>
    private static void FigurePoints(Profile pr, float begin, float end, bool hollow, int sides, float offset, float angleScale, int split)
    {
        float step = 1f / sides;
        float angleStep = 2f * MathF.PI * step * angleScale;
        int corners = (int)MathF.Floor(sides / angleScale + 0.5f);
        float scale = corners < 8 ? CornerScale[corners] : 0.5f;

        float first = MathF.Floor(begin * sides) / sides;
        float t = first;
        float ang = 2f * MathF.PI * (t * angleScale + offset);
        Vector3 a = new Vector3(Scaled(Math.Cos, ang, scale), Scaled(Math.Sin, ang, scale), t);
        t += step;
        ang += angleStep;
        Vector3 b = new Vector3(Scaled(Math.Cos, ang, scale), Scaled(Math.Sin, ang, scale), t);

        float fraction = (begin - first) * sides;
        if (fraction < 0.9999f)
            pr.Points.Add(Between(a, b, fraction));

        while (t < end)
        {
            a = new Vector3(Scaled(Math.Cos, ang, scale), Scaled(Math.Sin, ang, scale), t);
            AddPointsBetween(pr.Points, a, split);
            pr.Points.Add(a);
            t += step;
            ang += angleStep;
        }

        b = new Vector3(Scaled(Math.Cos, ang, scale), Scaled(Math.Sin, ang, scale), t);
        fraction = (end - (t - step)) * sides;
        if (fraction > 0.0001f)
        {
            Vector3 last = Between(a, b, fraction);
            AddPointsBetween(pr.Points, last, split);
            pr.Points.Add(last);
        }

        if ((end - begin) * angleScale < 0.99f)
        {
            pr.Open = true;
            if (!hollow)
                pr.Points.Add(Vector3.Zero);
        }
        else
        {
            pr.Open = false;
        }
        pr.Total = pr.Points.Count;
    }

    private static void AddFace(Profile pr, int index, int count, int id, bool flat) =>
        pr.Faces.Add(new ProfileFace { Index = index, Count = count, Id = id, Flat = flat });

    private static void AddCap(Profile pr, int id) =>
        pr.Faces.Add(new ProfileFace { Index = 0, Count = pr.Total, Id = id, Cap = true });

    /// <summary>
    /// The hollow: a second figure walked backwards after the outer one, scaled by the hollow fraction (the parameter along the
    /// profile too), and a face for its wall. Cap faces then run over both figures.
    /// </summary>
    private static void AddHole(Profile pr, PrimParams p, bool flat, float sides, float offset, float hollowScale, float angleScale, int split)
    {
        pr.TotalOut = pr.Total;
        FigurePoints(pr, p.ProfileBegin, p.ProfileEnd, true, (int)MathF.Floor(sides), offset, angleScale, split);
        AddFace(pr, pr.TotalOut, pr.Total - pr.TotalOut, IdInner, flat);

        int n = pr.Total - pr.TotalOut;
        Vector3[] hole = new Vector3[n];
        for (int i = 0; i < n; i++)
            hole[i] = pr.Points[pr.TotalOut + i] * hollowScale;
        for (int i = 0; i < n; i++)
            pr.Points[pr.TotalOut + i] = hole[n - 1 - i];

        foreach (ProfileFace f in pr.Faces)
            if (f.Cap)
                f.Count *= 2;
    }

    private static Profile MakeProfile(PrimParams p, bool pathOpen, float detail, int split)
    {
        Profile pr = new Profile();
        float begin = p.ProfileBegin, end = p.ProfileEnd, hollow = p.ProfileHollow;
        bool isHollow = hollow > 0f;
        int hole = p.HoleType & 0xF0;

        switch (p.ProfileCurve & 0x0F)
        {
            case 1: // square
            {
                FigurePoints(pr, begin, end, isHollow, 4, -0.375f, 1f, split);
                if (pathOpen)
                    AddCap(pr, IdPathBegin);
                int faceNumber = 0;
                for (int i = (int)MathF.Floor(begin * 4f); i < (int)MathF.Floor(end * 4f + .999f); i++)
                    AddFace(pr, faceNumber++ * (split + 1), split + 2, IdOuter << i, true);
                for (int i = 0; i < pr.Points.Count; i++)
                    pr.Points[i] = new Vector3(pr.Points[i].X, pr.Points[i].Y, pr.Points[i].Z * 4f);
                if (isHollow)
                {
                    if (hole == 48)
                        AddHole(pr, p, true, 3, -0.375f, hollow, 1f, split);
                    else if (hole == 16)
                        AddHole(pr, p, false, 6f * detail, -0.375f, hollow, 1f, 0);
                    else
                        AddHole(pr, p, true, 4, -0.375f, hollow, 1f, split);
                }
                if (pathOpen)
                    pr.Faces[0].Count = pr.Total;
                break;
            }
            case 2:
            case 3:
            case 4: // triangles
            {
                FigurePoints(pr, begin, end, isHollow, 3, 0f, 1f, split);
                for (int i = 0; i < pr.Points.Count; i++)
                    pr.Points[i] = new Vector3(pr.Points[i].X, pr.Points[i].Y, pr.Points[i].Z * 3f);
                if (pathOpen)
                    AddCap(pr, IdPathBegin);
                int faceNumber = 0;
                for (int i = (int)MathF.Floor(begin * 3f); i < (int)MathF.Floor(end * 3f + .999f); i++)
                    AddFace(pr, faceNumber++ * (split + 1), split + 2, IdOuter << i, true);
                if (isHollow)
                {
                    // a triangle fills less of its box, so its hollow fraction is halved
                    float h = hollow / 2f;
                    if (hole == 16)
                        AddHole(pr, p, false, 6f * detail, 0f, h, 1f, 0);
                    else if (hole == 32)
                        AddHole(pr, p, true, 4, 0f, h, 1f, split);
                    else
                        AddHole(pr, p, true, 3, 0f, h, 1f, split);
                }
                break;
            }
            case 0: // circle
            {
                float circle = 6f * detail;
                if (isHollow && hole == 32)
                    circle = MathF.Ceiling(circle / 4f) * 4f;
                FigurePoints(pr, begin, end, isHollow, (int)circle, 0f, 1f, 0);
                if (pathOpen)
                    AddCap(pr, IdPathBegin);
                AddFace(pr, 0, pr.Open && !isHollow ? pr.Total - 1 : pr.Total, IdOuter, false);
                if (isHollow)
                {
                    if (hole == 32)
                        AddHole(pr, p, true, 4, 0f, hollow, 1f, split);
                    else if (hole == 48)
                        AddHole(pr, p, true, 3, 0f, hollow, 1f, split);
                    else
                        AddHole(pr, p, false, circle, 0f, hollow, 1f, 0);
                }
                break;
            }
            case 5: // half circle
            {
                float circle = 6f * detail * 0.5f;
                if (isHollow && hole == 32)
                    circle = MathF.Ceiling(circle / 2f) * 2f;
                FigurePoints(pr, begin, end, isHollow, (int)MathF.Floor(circle), 0.5f, 0.5f, 0);
                if (pathOpen)
                    AddCap(pr, IdPathBegin);
                AddFace(pr, 0, pr.Open && !isHollow ? pr.Total - 1 : pr.Total, IdOuter, false);
                if (isHollow)
                {
                    if (hole == 32)
                        AddHole(pr, p, true, 2, 0.5f, hollow, 0.5f, split);
                    else if (hole == 48)
                        AddHole(pr, p, true, 3, 0.5f, hollow, 0.5f, split);
                    else
                        AddHole(pr, p, false, circle, 0.5f, hollow, 0.5f, 0);
                }
                // a whole, solid half circle closes on itself; a cut or hollow one is open
                if (end - begin < 1f)
                {
                    pr.Open = true;
                }
                else if (!isHollow)
                {
                    pr.Open = false;
                    pr.Points.Add(pr.Points[0]);
                    pr.Total++;
                }
                break;
            }
        }

        if (pathOpen)
            AddCap(pr, IdPathEnd);
        if (pr.Open)
        {
            AddFace(pr, pr.Total - 1, 2, IdProfileBegin, true);
            AddFace(pr, isHollow ? pr.TotalOut - 1 : pr.Total - 2, 2, IdProfileEnd, true);
        }
        return pr;
    }

    // ------------------------------------------------------------------ the path

    private static Path LinePath(PrimParams p, float detail, int split)
    {
        Path path = new Path { Open = true };
        int np = (int)MathF.Floor(MathF.Abs(p.PathTwistBegin - p.PathTwist) * 3.5f * (detail - 0.5f)) + 2;
        if (np < split + 2)
            np = split + 2;
        float step = 1f / (np - 1);

        // a scale below 1 narrows the far end, a scale above 1 narrows the near end
        float beginX = p.PathScaleX > 1f ? 2f - p.PathScaleX : 1f, beginY = p.PathScaleY > 1f ? 2f - p.PathScaleY : 1f;
        float endX = p.PathScaleX < 1f ? p.PathScaleX : 1f, endY = p.PathScaleY < 1f ? p.PathScaleY : 1f;

        for (int i = 0; i < np; i++)
        {
            float t = Lerp(p.PathBegin, p.PathEnd, i * step);
            path.Points.Add(new PathPoint
            {
                Position = new Vector3(Lerp(0f, p.PathShearX, t), Lerp(0f, p.PathShearY, t), t - 0.5f),
                Scale = new Vector2(Lerp(beginX, endX, t), Lerp(beginY, endY, t)),
                First = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Lerp(MathF.PI * p.PathTwistBegin, MathF.PI * p.PathTwist, t)),
                Then = Quaternion.Identity,
                TexT = t
            });
        }
        return path;
    }

    /// <param name="alternating">The second circular path: no revolutions or twist detail, and its points swing between x = 0.5 and x = -0.5.</param>
    private static Path CirclePath(PrimParams p, float detail, bool alternating)
    {
        Path path = new Path();
        float twistMagnitude = MathF.Abs(p.PathTwistBegin - p.PathTwist);
        int sides = alternating
            ? (int)MathF.Floor(6f * detail)
            : (int)MathF.Floor(MathF.Floor(6f * detail + twistMagnitude * 3.5f * (detail - 0.5f)) * p.PathRevolutions);
        if (sides <= 0)
            return path;

        float revolutions = p.PathRevolutions, skew = p.PathSkew, skewMagnitude = MathF.Abs(skew);
        float holeX = p.PathScaleX * (1f - skewMagnitude), holeY = p.PathScaleY;

        float taperXBegin = 1f, taperXEnd = 1f - p.PathTaperX, taperYBegin = 1f, taperYEnd = 1f - p.PathTaperY;
        if (taperXEnd > 1f) { taperXBegin = 2f - taperXEnd; taperXEnd = 1f; }
        if (taperYEnd > 1f) { taperYBegin = 2f - taperYEnd; taperYEnd = 1f; }

        float radiusStart = sides < 8 ? CornerScale[sides] : 0.5f;
        radiusStart *= 1f - holeY;
        float radiusEnd = radiusStart;
        if (p.PathRadiusOffset < 0f)
            radiusStart *= 1f + p.PathRadiusOffset;
        else
            radiusEnd *= 1f - p.PathRadiusOffset;

        path.Open = p.PathEnd - p.PathBegin < 1f || skewMagnitude > 0.001f
            || MathF.Abs(taperXEnd - taperXBegin) > 0.001f || MathF.Abs(taperYEnd - taperYBegin) > 0.001f
            || MathF.Abs(radiusEnd - radiusStart) > 0.001f;

        void Add(float t)
        {
            float ang = 2f * MathF.PI * revolutions * t;
            float r = Lerp(radiusStart, radiusEnd, t);
            float c = Scaled(Math.Cos, ang, r), s = Scaled(Math.Sin, ang, r);
            path.Points.Add(new PathPoint
            {
                Position = new Vector3(Lerp(0f, p.PathShearX, s) + Lerp(-skew, skew, t) * 0.5f, c + Lerp(0f, p.PathShearY, s), s),
                Scale = new Vector2(holeX * Lerp(taperXBegin, taperXEnd, t), holeY * Lerp(taperYBegin, taperYEnd, t)),
                First = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, Lerp(p.PathTwistBegin, p.PathTwist, t) * 2f * MathF.PI - MathF.PI),
                Then = Quaternion.CreateFromAxisAngle(Vector3.UnitX, ang),
                TexT = t
            });
        }

        float step = 1f / sides;
        float u = p.PathBegin;
        Add(u);
        u += step;
        // the steps are whole multiples of 1 / sides, so a cut does not move the others
        u = (int)(u * sides) / (float)sides;
        while (u < p.PathEnd)
        {
            Add(u);
            u += step;
        }
        Add(p.PathEnd);
        if (alternating)
        {
            float x = 0.5f;
            foreach (PathPoint pt in path.Points)
            {
                pt.Position = new Vector3(x, pt.Position.Y, pt.Position.Z);
                x = x == 0.5f ? -0.5f : 0.5f;
            }
        }
        // different twist at the two ends also opens the path, whatever the other settings
        if (p.PathTwist != p.PathTwistBegin)
            path.Open = true;
        return path;
    }

    // ------------------------------------------------------------------ the faces

    private static Vector3 Unit(Vector3 v, Vector3 fallback)
    {
        float l = v.Length();
        return l > 1e-12f ? v / l : fallback;
    }

    private static VolumeFace Side(PrimParams p, Path path, Profile profile, Vector3[] mesh, ProfileFace pf, bool end, bool inner)
    {
        VolumeFace vf = new VolumeFace();
        int maxS = profile.Total, rows = path.Points.Count;
        bool flat = pf.Flat;
        bool doubled = inner && flat && pf.Count > 2;   // a flat wall repeats each point so every flat piece has its own normals
        int beginS = pf.Index;
        int countS = doubled ? pf.Count * 2 : pf.Count;
        int numS = doubled ? countS / 2 : countS;
        float beginTex = MathF.Floor(profile.Points[beginS].Z);

        for (int t = 0; t < rows; t++)
        {
            float tt = path.Points[t].TexT;
            for (int s = 0; s < numS; s++)
            {
                float ss;
                if (end)
                {
                    ss = s != 0 ? 1f : 0f;
                }
                else
                {
                    int index = beginS + s;
                    if (index >= profile.Points.Count)
                        ss = flat ? 1f - beginTex : 1f;
                    else
                        ss = flat ? profile.Points[index].Z - beginTex : profile.Points[index].Z;
                }
                // a point past the end of the profile is the first point of the same row
                int mi = beginS + s >= maxS ? beginS + s + maxS * (t - 1) : beginS + s + maxS * t;
                vf.Positions.Add(mesh[mi]);
                vf.Uvs.Add(new Vector2(ss, tt));
                if (doubled && s > 0)
                {
                    vf.Positions.Add(mesh[mi]);
                    vf.Uvs.Add(new Vector2(ss, tt));
                }
            }
            if (doubled)
            {
                int s = profile.Open ? numS - 1 : 0;
                vf.Positions.Add(mesh[beginS + s + maxS * t]);
                vf.Uvs.Add(new Vector2(profile.Points[beginS + s].Z - beginTex, tt));
            }
        }

        int cols = countS;
        for (int t = 0; t < rows - 1; t++)
            for (int s = 0; s < cols - 1; s++)
            {
                vf.Indices.Add(s + cols * t);
                vf.Indices.Add(s + 1 + cols * (t + 1));
                vf.Indices.Add(s + cols * (t + 1));
                vf.Indices.Add(s + cols * t);
                vf.Indices.Add(s + 1 + cols * t);
                vf.Indices.Add(s + 1 + cols * (t + 1));
            }

        // normals: each triangle adds its (area weighted) normal to its three corners, with one corner of the quad counted twice
        Vector3[] sum = new Vector3[vf.Positions.Count];
        for (int i = 0; i < vf.Indices.Count / 3; i++)
        {
            int i0 = vf.Indices[i * 3], i1 = vf.Indices[i * 3 + 1], i2 = vf.Indices[i * 3 + 2];
            Vector3 b = vf.Positions[i0];
            Vector3 n = Vector3.Cross(b - vf.Positions[i1], b - vf.Positions[i2]);
            sum[i0] += n;
            sum[i1] += n;
            sum[i2] += n;
            sum[(i % 2) == 0 ? i1 : i2] += n;
        }

        // stitch the normals where the surface closes on itself
        bool pathClosed = !path.Open;
        bool profileClosed = !profile.Open;
        int cc = cols;
        Vector3 bottomGap = vf.Positions[0] - vf.Positions[cc * (rows - 2 < 0 ? 0 : rows - 2)];
        Vector3 topGap = vf.Positions[cc - 1] - vf.Positions[cc * (rows - 2 < 0 ? 0 : rows - 2) + cc - 1];
        bool bottomConverges = Vector3.Dot(bottomGap, bottomGap) < 0.000001f;
        bool topConverges = Vector3.Dot(topGap, topGap) < 0.000001f;

        if (pathClosed)
            for (int i = 0; i < cc; i++)
            {
                Vector3 n = sum[i] + sum[cc * (rows - 1) + i];
                sum[i] = n;
                sum[cc * (rows - 1) + i] = n;
            }
        if (profileClosed && !bottomConverges)
            for (int i = 0; i < rows; i++)
            {
                Vector3 n = sum[cc * i] + sum[cc * i + cc - 1];
                sum[cc * i] = n;
                sum[cc * i + cc - 1] = n;
            }
        if ((p.PathCurve & 0xF0) == 0x20 && (p.ProfileCurve & 0x0F) == 5)
        {
            // the poles of a sphere: every point there shares one normal
            if (bottomConverges)
                for (int i = 0; i < rows; i++)
                    sum[cc * i] = new Vector3(1f, 0f, 0f);
            if (topConverges)
                for (int i = 0; i < rows; i++)
                    sum[cc * i + cc - 1] = new Vector3(-1f, 0f, 0f);
        }

        foreach (Vector3 n in sum)
            vf.Normals.Add(Unit(n, Vector3.UnitZ));
        return vf;
    }

    private static VolumeFace Cap(PrimParams p, Path path, Profile profile, Vector3[] mesh, bool top, bool hollow)
    {
        VolumeFace vf = new VolumeFace();
        int maxS = profile.Total, rows = path.Points.Count;
        int offset = top ? (rows - 1) * maxS : 0;
        int count = profile.Points.Count;

        bool uncutCube = !hollow && !profile.Open && p.PathBegin == 0f && p.PathEnd == 1f
            && p.ProfileCurve == 1 && p.HoleType == 0 && p.PathCurve == 16;
        if (uncutCube)
            return UncutCubeCap(profile, mesh, offset, top);

        Vector3 min = mesh[offset], max = min;
        Vector2 minUv = new Vector2(profile.Points[0].X + 0.5f, top ? profile.Points[0].Y + 0.5f : 0.5f - profile.Points[0].Y), maxUv = minUv;
        for (int k = 0; k < count; k++)
        {
            Vector3 pos = mesh[offset + k];
            Vector2 uv = new Vector2(profile.Points[k].X + 0.5f, top ? profile.Points[k].Y + 0.5f : 0.5f - profile.Points[k].Y);
            vf.Positions.Add(pos);
            vf.Uvs.Add(uv);
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
            minUv = Vector2.Min(minUv, uv);
            maxUv = Vector2.Max(maxUv, uv);
        }

        if (!hollow && !profile.Open)
        {
            vf.Positions.Add((min + max) * 0.5f);
            vf.Uvs.Add((minUv + maxUv) * 0.5f);
        }

        int n = vf.Positions.Count;
        if (hollow)
        {
            // between the outer figure and the hole: a strip that always closes the shorter diagonal
            int p1 = 0, p2 = n - 1;
            while (p2 - p1 > 1)
            {
                Vector3 a = profile.Points[p1], b = profile.Points[p2], c = profile.Points[p1 + 1], d = profile.Points[p2 - 1];
                float areaA = Area(a, c, b), areaB = Area(a, d, c), areaC = Area(b, a, d), areaD = Area(b, c, d);
                bool useFirst = true, firstOk = true, secondOk = true;
                if (areaA < 0f) firstOk = false;
                if (areaD < 0f) firstOk = false;
                if (areaC < 0f) secondOk = false;
                if (areaB < 0f) secondOk = false;
                if (!firstOk)
                    useFirst = false;
                else if (!secondOk)
                    useFirst = true;
                else
                {
                    Vector3 d1 = a - c, d2 = b - d;
                    useFirst = (d1.X * d1.X + d1.Y * d1.Y + d1.Z * d1.Z) < (d2.X * d2.X + d2.Y * d2.Y + d2.Z * d2.Z);
                }

                if (top)
                {
                    if (useFirst) { vf.Indices.AddRange(new[] { p1, p1 + 1, p2 }); p1++; }
                    else { vf.Indices.AddRange(new[] { p1, p2 - 1, p2 }); p2--; }
                }
                else
                {
                    if (useFirst) { vf.Indices.AddRange(new[] { p1, p2, p1 + 1 }); p1++; }
                    else { vf.Indices.AddRange(new[] { p1, p2, p2 - 1 }); p2--; }
                }
            }
        }
        else
        {
            for (int i = 0; i < n - 2; i++)
            {
                vf.Indices.Add(n - 1);
                if (top) { vf.Indices.Add(i); vf.Indices.Add(i + 1); }
                else { vf.Indices.Add(i + 1); vf.Indices.Add(i); }
            }
        }

        // a cap is flat: one normal from its first triangle; a first triangle too small to give a direction makes it point along z
        Vector3 normal = Vector3.Cross(vf.Positions[vf.Indices[1]] - vf.Positions[vf.Indices[0]], vf.Positions[vf.Indices[2]] - vf.Positions[vf.Indices[0]]);
        normal = normal.LengthSquared() > 0.00001f ? Vector3.Normalize(normal) : new Vector3(0f, 0f, normal.Z >= 0f ? 1f : -1f);
        for (int i = 0; i < n; i++)
            vf.Normals.Add(normal);
        return vf;
    }

    // twice the signed area of the triangle a, b, c in the profile plane
    private static float Area(Vector3 a, Vector3 b, Vector3 c) =>
        (a.X * b.Y - b.X * a.Y) + (b.X * c.Y - c.X * b.Y) + (c.X * a.Y - a.X * c.Y);

    /// <summary>The end of an uncut, solid box with a straight path: a square grid with one point per profile point on each edge.</summary>
    private static VolumeFace UncutCubeCap(Profile profile, Vector3[] mesh, int offset, bool top)
    {
        VolumeFace vf = new VolumeFace();
        int grid = (profile.Points.Count - 1) / 4;
        Vector3[] corner = new Vector3[4];
        Vector2[] cornerUv = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            corner[k] = mesh[offset + grid * k];
            cornerUv[k] = new Vector2(profile.Points[grid * k].X + 0.5f, 0.5f - profile.Points[grid * k].Y);
        }
        Vector3 normal = Unit(Vector3.Cross(corner[1] - corner[0], corner[2] - corner[1]), Vector3.UnitZ);
        if (!top)
        {
            normal = -normal;
        }
        else
        {
            // on the top the texture runs the other way along u
            (cornerUv[0], cornerUv[3]) = (cornerUv[3], cornerUv[0]);
            (cornerUv[1], cornerUv[2]) = (cornerUv[2], cornerUv[1]);
        }

        for (int gx = 0; gx <= grid; gx++)
            for (int gy = 0; gy <= grid; gy++)
            {
                float u = (float)gx / grid, v = (float)gy / grid;
                vf.Positions.Add(corner[0] + (corner[1] - corner[0]) * u + (corner[3] - corner[0]) * v);
                vf.Uvs.Add(cornerUv[0] + (cornerUv[1] - cornerUv[0]) * u + (cornerUv[3] - cornerUv[0]) * v);
                vf.Normals.Add(normal);
            }

        int w = grid + 1;
        int[] quad = { 0, 1, w + 1, w + 1, w, 0 };
        for (int gx = 0; gx < grid; gx++)
            for (int gy = 0; gy < grid; gy++)
                for (int k = 0; k < 6; k++)
                    vf.Indices.Add(gy * w + gx + quad[top ? 5 - k : k]);
        return vf;
    }
}
