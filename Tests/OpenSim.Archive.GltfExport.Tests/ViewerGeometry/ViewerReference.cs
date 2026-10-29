/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Globalization;
using System.IO.Compression;
using OpenSim.Archive.Inspection;

namespace OpenSim.Archive.GltfExport.Tests.ViewerGeometry;

/// <summary>One vertex of a reference face: position, normal and texture coordinates as the viewer's volume code made them.</summary>
public readonly record struct RefVertex(float X, float Y, float Z, float Nx, float Ny, float Nz, float U, float V);

/// <summary>A face of the reference: the viewer's vertex list and triangle indices.</summary>
public sealed class RefFace
{
    public List<RefVertex> Vertices = new List<RefVertex>();
    public List<int[]> Triangles = new List<int[]>();
}

/// <summary>A shape's reference faces, in the viewer's face order.</summary>
public sealed class RefCase
{
    public string Name;
    public List<RefFace> Faces = new List<RefFace>();
}

/// <summary>
/// Reads the reference data: the faces the viewer's own volume code (indra/llmath/llvolume.cpp, built as a command-line
/// program for tests and never shipped) produced for each shape and level of detail. Files are text, optionally gzip,
/// one case after another: "CASE name faces N", then per face "FACE i verts V tris T", "V x y z nx ny nz u v" and "T a b c".
/// </summary>
public static class ViewerReference
{
    /// <summary>The viewer's detail scales for its four levels of detail, lowest first (LLVolumeLODGroup::mDetailScales).</summary>
    public static readonly float[] Details = { 1.0f, 1.5f, 2.5f, 4.0f };

    public static string FixtureDirectory
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("VIEWER_REFERENCE_DIR");
            return string.IsNullOrEmpty(env) ? Path.Combine(AppContext.BaseDirectory, "Fixtures", "ViewerReference") : env;
        }
    }

    public static Dictionary<string, RefCase> Load(string fileName)
    {
        string path = Path.Combine(FixtureDirectory, fileName);
        Stream raw = File.OpenRead(path);
        using Stream s = path.EndsWith(".gz", StringComparison.Ordinal) ? new GZipStream(raw, CompressionMode.Decompress) : raw;
        using StreamReader r = new StreamReader(s);
        Dictionary<string, RefCase> cases = new Dictionary<string, RefCase>();
        RefCase current = null;
        RefFace face = null;
        string line;
        while ((line = r.ReadLine()) != null)
        {
            string[] t = line.Split(' ');
            switch (t[0])
            {
                case "CASE":
                    current = new RefCase { Name = t[1] };
                    cases[current.Name] = current;
                    break;
                case "FACE":
                    face = new RefFace();
                    current.Faces.Add(face);
                    break;
                case "V":
                    face.Vertices.Add(new RefVertex(F(t[1]), F(t[2]), F(t[3]), F(t[4]), F(t[5]), F(t[6]), F(t[7]), F(t[8])));
                    break;
                case "T":
                    face.Triangles.Add(new[] { int.Parse(t[1], CultureInfo.InvariantCulture), int.Parse(t[2], CultureInfo.InvariantCulture), int.Parse(t[3], CultureInfo.InvariantCulture) });
                    break;
            }
        }
        return cases;
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    /// <summary>The shape cases: name and the stored fields a region keeps, one line per shape (tab separated, header first).</summary>
    public static List<(string Name, ShapeData Shape)> LoadShapes(string fileName)
    {
        List<(string, ShapeData)> list = new List<(string, ShapeData)>();
        string[] lines = File.ReadAllLines(Path.Combine(FixtureDirectory, fileName));
        string[] head = lines[0].Split('\t');
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('\t');
            Dictionary<string, int> v = new Dictionary<string, int>();
            for (int k = 1; k < head.Length; k++)
                v[head[k]] = int.Parse(c[k], CultureInfo.InvariantCulture);
            ShapeData s = new ShapeData
            {
                ProfileCurve = v["pc"], PathCurve = v["path"], ProfileBegin = v["pb"], ProfileEnd = v["pe"], ProfileHollow = v["ph"],
                PathBegin = v["pathb"], PathEnd = v["pathe"], PathScaleX = v["sx"], PathScaleY = v["sy"], PathShearX = v["shx"],
                PathShearY = v["shy"], PathTwist = v["tw"], PathTwistBegin = v["twb"], PathRadiusOffset = v["ro"], PathTaperX = v["tx"],
                PathTaperY = v["ty"], PathRevolutions = v["rev"], PathSkew = v["skew"]
            };
            list.Add((c[0], s));
        }
        return list;
    }

    /// <summary>The sculpt cases: name, sculpt type byte (stitching with the mirror and invert flags), map size and the map's file.</summary>
    public static List<(string Name, int Type, int Width, int Height, string MapFile)> LoadSculpts(string fileName)
    {
        List<(string, int, int, int, string)> list = new List<(string, int, int, int, string)>();
        string[] lines = File.ReadAllLines(Path.Combine(FixtureDirectory, fileName));
        for (int i = 1; i < lines.Length; i++)
        {
            string[] c = lines[i].Split('	');
            list.Add((c[0], int.Parse(c[1], CultureInfo.InvariantCulture), int.Parse(c[2], CultureInfo.InvariantCulture), int.Parse(c[3], CultureInfo.InvariantCulture), c[4]));
        }
        return list;
    }

    /// <summary>
    /// A sculpt map from its file: raw red, green, blue bytes with the first row at the bottom of the picture, the order the
    /// viewer's image buffers hold. The exporter's maps start at the top of the picture, so the rows are turned over.
    /// </summary>
    public static SculptMap LoadMap(string mapFile, int width, int height)
    {
        byte[] raw = File.ReadAllBytes(Path.Combine(FixtureDirectory, mapFile));
        byte[] rgb = new byte[raw.Length];
        for (int y = 0; y < height; y++)
            Array.Copy(raw, y * width * 3, rgb, (height - 1 - y) * width * 3, width * 3);
        return new SculptMap { Width = width, Height = height, Rgb = rgb };
    }
}
