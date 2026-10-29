/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Text.Json;
using System.Text.Json.Nodes;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using SkiaSharp;
using Xunit;
using static OpenSim.Archive.GltfExport.Tests.TestKit;

namespace OpenSim.Archive.GltfExport.Tests;

/// <summary>
/// The archive that the Blender import check exports (build/blender-check, RELEASE.md): every prim type, a sculpt, a mesh part, a part
/// with six differently textured faces, a face rotated with unequal repeats, planar faces, a face whose texture is not in the archive, a
/// fifty-part linkset whose parts have numeric link prefixes, and an object with a part whose permissions do not allow export. It also
/// writes what each part and face must come back as (expected.json), worked out from the stored values here and not from the export.
/// </summary>
internal static class BlenderFixtures
{
    public static readonly UUID TexA = UUID.Parse("f1000000-0000-4000-8000-0000000000a1");
    public static readonly UUID TexB = UUID.Parse("f1000000-0000-4000-8000-0000000000b2");
    public static readonly UUID TexC = UUID.Parse("f1000000-0000-4000-8000-0000000000c3");
    public static readonly UUID TexMissing = UUID.Parse("f1000000-0000-4000-8000-0000000000e5");
    public static readonly UUID TexNormal = UUID.Parse("f1000000-0000-4000-8000-0000000000d4");
    private static readonly UUID MapId = UUID.Parse("f1000000-0000-4000-8000-0000000000f6");
    private static readonly UUID MeshId = UUID.Parse("f1000000-0000-4000-8000-0000000000f7");
    private static readonly UUID Creator1 = UUID.Parse("f2000000-0000-4000-8000-000000000001");
    private static readonly UUID Creator2 = UUID.Parse("f2000000-0000-4000-8000-000000000002");
    private const uint ExportBit = (uint)OpenMetaverse.PermissionMask.Export;

    private sealed class Fixture
    {
        public string Name;
        public PartSpec[] Parts;
        /// <summary>The texture a face has when it is not named in <see cref="Faces"/>.</summary>
        public UUID Default = TexA;
        /// <summary>Per part name: face number to texture, for faces that differ from the default.</summary>
        public Dictionary<string, Dictionary<int, UUID>> Faces = new Dictionary<string, Dictionary<int, UUID>>();
        /// <summary>Per part name: the faces whose exact mapping is in TEXCOORD_1.</summary>
        public Dictionary<string, int[]> SecondSet = new Dictionary<string, int[]>();
        /// <summary>Per part name: face number to the coordinate set (0 is TEXCOORD_0, 1 is TEXCOORD_1, ...) each of its maps must read, worked out by hand from the stored values.</summary>
        public Dictionary<string, Dictionary<int, Dictionary<string, int>>> MapSets = new Dictionary<string, Dictionary<int, Dictionary<string, int>>>();
    }

    /// <summary>The legacy materials (normal maps) of the "maps" fixture, to be added to the archive as assets.</summary>
    private static readonly List<(UUID, sbyte, byte[])> MaterialAssets = new List<(UUID, sbyte, byte[])>();

    /// <summary>A face's legacy material with a normal map of the given repeats, offset and rotation; its asset is kept for the archive.</summary>
    private static UUID NormalMaterial(float repeatU, float repeatV, float offsetU, float offsetV, float rotation)
    {
        FaceMaterial fm = new FaceMaterial { NormalMapID = TexNormal, NormalRepeatX = repeatU, NormalRepeatY = repeatV, NormalOffsetX = offsetU, NormalOffsetY = offsetV, NormalRotation = rotation };
        fm.genID();
        MaterialAssets.Add((fm.ID, (sbyte)AssetType.OSMaterial, fm.toLLSDxml()));
        return fm.ID;
    }

    private static byte[] Texture(byte r, byte g, byte b)
    {
        using SKBitmap bmp = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgb888x, SKAlphaType.Opaque));
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                bmp.SetPixel(x, y, new SKColor((byte)(r + x * 4), (byte)(g + y * 4), b));
        Assert.True(SkiaImageUtils.TryEncodeToJ2KLossless(bmp, out byte[] j2k));
        return j2k;
    }

    private static string Entry(UUID defaultTexture, Action<Primitive.TextureEntry> edit = null)
    {
        Primitive.TextureEntry te = new Primitive.TextureEntry(defaultTexture);
        edit?.Invoke(te);
        return Convert.ToBase64String(te.GetBytes());
    }

    private static Primitive.TextureEntryFace Face(Primitive.TextureEntry te, uint n, UUID texture)
    {
        Primitive.TextureEntryFace f = te.CreateFace(n);
        f.TextureID = texture;
        return f;
    }

    private static PartSpec Part(string name, int link, string shape, string te, UUID? creator = null, string creatorData = "", Vector3? offset = null, Vector3? scale = null) => new PartSpec
    {
        Name = name, Description = "description of " + name, Link = link, Shape = shape, TextureEntryBase64 = te,
        Creator = creator ?? Creator1, Owner = creator ?? Creator1, CreatorData = creatorData,
        Offset = offset ?? Vector3.Zero, Scale = scale ?? new Vector3(1, 1, 1)
    };

    private const string CreatorOne = "http://grid.example.org:8002;Test Creator";

    private static List<Fixture> Fixtures()
    {
        MaterialAssets.Clear();
        List<Fixture> all = new List<Fixture>();
        (string, string)[] prims =
        {
            ("prim-box", Box()), ("prim-cylinder", Box(profileCurve: 0)), ("prim-prism", Box(profileCurve: 3)), ("prim-sphere", Box(profileCurve: 5, pathCurve: 32)),
            ("prim-torus", Box(profileCurve: 0, pathCurve: 32, scaleY: 175)), ("prim-tube", Box(profileCurve: 1, pathCurve: 32, scaleY: 175)), ("prim-ring", Box(profileCurve: 3, pathCurve: 32, scaleY: 175)),
            ("prim-hollow-cut-twist", Box(hollow: 30000, profileBegin: 6250, profileEnd: 6250, twist: 45, twistBegin: -45))
        };
        foreach ((string name, string shape) in prims)
            all.Add(new Fixture { Name = name, Parts = new[] { Part(name, 1, shape, Entry(TexA), creator: Creator1, creatorData: CreatorOne, scale: new Vector3(1.5f, 2, 2.5f)) } });

        all.Add(new Fixture
        {
            Name = "sculpt",
            Parts = new[] { Part("sculpt", 1, Box(profileCurve: 0, pathCurve: 32, sculptType: 1, sculptTexture: MapId.ToString(), sculptEntry: true), Entry(TexB), scale: new Vector3(2, 3, 4)) },
            Default = TexB
        });
        all.Add(new Fixture
        {
            Name = "mesh",
            Parts = new[] { Part("mesh", 1, Box(profileCurve: 0, pathCurve: 16, sculptType: 5, sculptTexture: MeshId.ToString(), sculptEntry: true), Entry(TexB)) },
            Default = TexB
        });

        Fixture multi = new Fixture { Name = "multiface" };
        multi.Parts = new[]
        {
            Part("multiface", 1, Box(), Entry(TexA, te =>
            {
                Face(te, 1, TexB).RGBA = new Color4(1f, 0.5f, 0.5f, 1f);
                Face(te, 2, TexC);
                Face(te, 3, TexA).RGBA = new Color4(0.5f, 1f, 0.5f, 1f);
                Face(te, 4, TexB);
                Face(te, 5, TexC);
            }))
        };
        multi.Faces["multiface"] = new Dictionary<int, UUID> { [1] = TexB, [2] = TexC, [3] = TexA, [4] = TexB, [5] = TexC };
        all.Add(multi);

        Fixture rotated = new Fixture { Name = "rotated-unequal-repeats" };
        rotated.Parts = new[]
        {
            Part("rotated-unequal-repeats", 1, Box(), Entry(TexA, te =>
            {
                Primitive.TextureEntryFace a = Face(te, 1, TexB); a.RepeatU = 4; a.RepeatV = 2; a.Rotation = 0.6f; a.OffsetU = 0.1f; a.OffsetV = -0.2f;
                Primitive.TextureEntryFace b = Face(te, 2, TexC); b.RepeatU = 2; b.RepeatV = 2; b.Rotation = 0.5f;            // exact in the extension
                Primitive.TextureEntryFace c = Face(te, 3, TexA); c.RepeatU = 3; c.RepeatV = 1; c.Rotation = 1.1f;            // rotated, unequal
            }))
        };
        rotated.Faces["rotated-unequal-repeats"] = new Dictionary<int, UUID> { [1] = TexB, [2] = TexC, [3] = TexA };
        rotated.SecondSet["rotated-unequal-repeats"] = new[] { 1, 3 };
        all.Add(rotated);

        Fixture planar = new Fixture { Name = "planar" };
        planar.Parts = new[]
        {
            Part("planar", 1, Box(), Entry(TexA, te =>
            {
                Primitive.TextureEntryFace a = Face(te, 0, TexB); a.TexMapType = MappingType.Planar; a.RepeatU = 2; a.Rotation = 0.4f;
                Primitive.TextureEntryFace b = Face(te, 2, TexC); b.TexMapType = MappingType.Planar;
            }), scale: new Vector3(2, 4, 6))
        };
        planar.Faces["planar"] = new Dictionary<int, UUID> { [0] = TexB, [2] = TexC };
        planar.SecondSet["planar"] = new[] { 0, 2 };
        all.Add(planar);

        // Several maps on one face. Face 0 is planar (diffuse and normal map projected, with different repeats), face 1 is rotated with unequal
        // repeats on both maps with different values, face 2 has an exact diffuse mapping and an inexact normal map, and face 3 gives both maps
        // the same inexact mapping, so they share a set. The largest number of sets a face needs is three (TEXCOORD_0 and two more).
        Fixture maps = new Fixture { Name = "maps" };
        maps.Parts = new[]
        {
            Part("maps", 1, Box(), Entry(TexA, te =>
            {
                Primitive.TextureEntryFace a = Face(te, 0, TexB); a.TexMapType = MappingType.Planar; a.RepeatU = 2; a.RepeatV = 1; a.MaterialID = NormalMaterial(3, 3, 0, 0, 0);
                Primitive.TextureEntryFace b = Face(te, 1, TexB); b.RepeatU = 3; b.RepeatV = 1.5f; b.Rotation = 0.6f; b.OffsetU = 0.1f; b.OffsetV = -0.2f; b.MaterialID = NormalMaterial(5, 2, -0.3f, 0, -1.1f);
                Primitive.TextureEntryFace c = Face(te, 2, TexB); c.RepeatU = 2; c.RepeatV = 2; c.OffsetU = 0.25f; c.MaterialID = NormalMaterial(4, 2, 0, 0, 0.5f);
                Primitive.TextureEntryFace d = Face(te, 3, TexB); d.RepeatU = 4; d.RepeatV = 2; d.Rotation = 0.6f; d.OffsetU = 0.1f; d.OffsetV = -0.2f; d.MaterialID = NormalMaterial(4, 2, 0.1f, -0.2f, 0.6f);
            }), scale: new Vector3(2, 3, 4))
        };
        maps.Faces["maps"] = new Dictionary<int, UUID> { [0] = TexB, [1] = TexB, [2] = TexB, [3] = TexB };
        maps.MapSets["maps"] = new Dictionary<int, Dictionary<string, int>>
        {
            [0] = new Dictionary<string, int> { ["diffuse"] = 1, ["normal"] = 2 },
            [1] = new Dictionary<string, int> { ["diffuse"] = 1, ["normal"] = 2 },
            [2] = new Dictionary<string, int> { ["diffuse"] = 0, ["normal"] = 1 },
            [3] = new Dictionary<string, int> { ["diffuse"] = 1, ["normal"] = 1 }
        };
        all.Add(maps);

        Fixture missing = new Fixture { Name = "missing-texture" };
        missing.Parts = new[] { Part("missing-texture", 1, Box(), Entry(TexA, te => Face(te, 1, TexMissing))) };
        missing.Faces["missing-texture"] = new Dictionary<int, UUID> { [1] = TexMissing };
        all.Add(missing);

        List<PartSpec> fifty = new List<PartSpec>();
        for (int i = 1; i <= 50; i++)
            fifty.Add(Part(i.ToString("00") + " Part " + (char)('A' + i % 26), i, i % 2 == 0 ? Box(profileCurve: 0) : Box(), Entry(i % 3 == 0 ? TexB : TexA),
                creator: i % 5 == 0 ? Creator2 : Creator1, creatorData: i % 5 == 0 ? "http://grid.example.org:8002;Second Creator" : CreatorOne, offset: new Vector3(i % 10, i / 10, 0), scale: new Vector3(0.5f, 0.5f, 0.5f)));
        Fixture linkset = new Fixture { Name = "linkset-50", Parts = fifty.ToArray() };
        foreach (PartSpec p in fifty)
            if (p.Link % 3 == 0)
                linkset.Faces[p.Name] = Enumerable.Range(0, 6).ToDictionary(f => f, f => TexB);
        all.Add(linkset);

        PartSpec refusedRoot = Part("permissions", 1, Box(), Entry(TexA));
        PartSpec refused = Part("refused child", 2, Box(), Entry(TexA), offset: new Vector3(2, 0, 0));
        refused.Creator = Creator1; refused.Owner = Creator2; refused.BaseMask = ExportBit | 0x7fff; refused.OwnerMask = 0x7fff; refused.EveryoneMask = 0;
        PartSpec allowed = Part("allowed child", 3, Box(profileCurve: 0), Entry(TexB), offset: new Vector3(0, 2, 0));
        // a part whose base mask has no Export bit (older content) exports only when its owner has modify, copy and transfer
        PartSpec oldAllowed = Part("old content allowed", 4, Box(), Entry(TexA), offset: new Vector3(0, 0, 2));
        oldAllowed.Owner = Creator2; oldAllowed.BaseMask = 0xe000; oldAllowed.OwnerMask = 0xe000; oldAllowed.EveryoneMask = 0;
        PartSpec oldRefused = Part("old content refused", 5, Box(), Entry(TexA), offset: new Vector3(0, 0, 4));
        oldRefused.Owner = Creator2; oldRefused.BaseMask = 0xe000; oldRefused.OwnerMask = 0x6000; oldRefused.EveryoneMask = 0xe000;
        all.Add(new Fixture { Name = "permissions", Parts = new[] { refusedRoot, refused, allowed, oldAllowed, oldRefused }, Faces = { ["allowed child"] = Enumerable.Range(0, 3).ToDictionary(f => f, f => TexB) } });
        return all;
    }

    /// <summary>The faces of a part that have coordinate set <paramref name="n"/>: those whose maps read it.</summary>
    private static int[] SetFaces(Fixture f, string part, int n)
    {
        SortedSet<int> faces = new SortedSet<int>();
        if (n == 1 && f.SecondSet.TryGetValue(part, out int[] second))
            faces.UnionWith(second);
        if (f.MapSets.TryGetValue(part, out Dictionary<int, Dictionary<string, int>> byFace))
            foreach (KeyValuePair<int, Dictionary<string, int>> kv in byFace)
                if (kv.Value.Values.Contains(n))
                    faces.Add(kv.Key);
        return faces.ToArray();
    }

    private static JsonObject SetFacesObject(Fixture f, string part)
    {
        JsonObject o = new JsonObject();
        for (int n = 1; n <= 3; n++)
            o[n.ToString()] = new JsonArray(SetFaces(f, part, n).Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
        return o;
    }

    /// <summary>Face number to role to the set that role must read; faces of the older fixtures only name the diffuse texture.</summary>
    private static JsonObject MapSetsObject(Fixture f, string part)
    {
        JsonObject o = new JsonObject();
        if (f.MapSets.TryGetValue(part, out Dictionary<int, Dictionary<string, int>> byFace))
            foreach (KeyValuePair<int, Dictionary<string, int>> kv in byFace)
            {
                JsonObject roles = new JsonObject();
                foreach (KeyValuePair<string, int> r in kv.Value)
                    roles[r.Key] = r.Value;
                o[kv.Key.ToString()] = roles;
            }
        return o;
    }

    /// <summary>Face number to role to the texture each role shows (the maps fixture has a normal map on every face).</summary>
    private static JsonObject MapTexturesObject(Fixture f, string part)
    {
        JsonObject o = new JsonObject();
        if (f.MapSets.TryGetValue(part, out Dictionary<int, Dictionary<string, int>> byFace))
            foreach (int face in byFace.Keys)
                o[face.ToString()] = new JsonObject { ["diffuse"] = f.Faces[part][face].ToString(), ["normal"] = TexNormal.ToString() };
        return o;
    }

    private static JsonObject Describe(Fixture f)
    {
        JsonArray parts = new JsonArray();
        foreach (PartSpec p in f.Parts)
        {
            const uint FullBits = 0xe000;      // transfer, modify and copy
            bool refused = p.Creator != p.Owner && ((p.BaseMask & ExportBit) != 0 ? (p.OwnerMask & ExportBit) == 0 && (p.EveryoneMask & ExportBit) == 0 : (p.OwnerMask & FullBits) != FullBits);
            JsonObject faces = new JsonObject();
            if (f.Faces.TryGetValue(p.Name, out Dictionary<int, UUID> over))
                foreach (KeyValuePair<int, UUID> kv in over)
                    faces[kv.Key.ToString()] = kv.Value.ToString();
            string[] creatorData = p.CreatorData.Split(';');
            parts.Add(new JsonObject
            {
                ["name"] = p.Name, ["description"] = p.Description, ["linkNumber"] = p.Link, ["partUuid"] = p.Id.ToString(),
                ["creatorId"] = p.Creator.ToString(), ["creatorName"] = creatorData.Length > 1 ? creatorData[1] : "",
                ["status"] = refused ? "skipped" : "exported",
                ["defaultTexture"] = f.Default.ToString(),
                ["faceTextures"] = faces,
                ["secondSetFaces"] = new JsonArray(SetFaces(f, p.Name, 1).Select(n => (JsonNode)JsonValue.Create(n)).ToArray()),
                ["setFaces"] = SetFacesObject(f, p.Name),
                ["mapSets"] = MapSetsObject(f, p.Name),
                ["mapTextures"] = MapTexturesObject(f, p.Name),
                ["textureMissing"] = new JsonArray((f.Faces.TryGetValue(p.Name, out Dictionary<int, UUID> m) ? m.Where(kv => kv.Value == TexMissing).Select(kv => (JsonNode)JsonValue.Create(kv.Key)) : Array.Empty<JsonNode>()).ToArray())
            });
        }
        return new JsonObject { ["object"] = f.Parts[0].Name, ["parts"] = parts };
    }

    public static byte[] Archive(out JsonArray expected)
    {
        List<Fixture> fixtures = Fixtures();
        expected = new JsonArray();
        List<(string, string)> objects = new List<(string, string)>();
        foreach (Fixture f in fixtures)
        {
            expected.Add(Describe(f));
            objects.Add((f.Name, ObjectXml(f.Parts[0], f.Parts.Skip(1).ToArray())));
        }
        SculptMap map = SculptTests.SphereMap();
        (UUID, sbyte, byte[])[] assets =
        {
            (TexNormal, (sbyte)AssetType.Texture, Texture(128, 128, 255)),
            (TexA, (sbyte)AssetType.Texture, Texture(200, 40, 40)), (TexB, (sbyte)AssetType.Texture, Texture(40, 200, 40)), (TexC, (sbyte)AssetType.Texture, Texture(40, 40, 200)),
            (MapId, (sbyte)AssetType.Texture, SculptTests.J2k(map)),
            (MeshId, (sbyte)AssetType.Mesh, MeshTests.Asset(new[] { MeshTests.Triangle(), MeshTests.Triangle(0.25f) }))
        };
        return OarWithAssets(objects.ToArray(), assets.Concat(MaterialAssets).ToArray());
    }

    /// <summary>
    /// Objects whose parts share names, for the check of what a round trip through Blender keeps: five parts all named "Object", and parts whose
    /// names collide only after Blender has renamed the others ("Cube", "Cube", "Cube.001", "Cube.001").
    /// </summary>
    private static (byte[] oar, JsonArray expected) IdentityArchive()
    {
        JsonArray expected = new JsonArray();
        List<(string, string)> objects = new List<(string, string)>();
        foreach ((string name, string[] partNames) in new[] { ("dup-five", new[] { "Object", "Object", "Object", "Object", "Object" }), ("dup-collide", new[] { "Cube", "Cube", "Cube.001", "Cube.001" }) })
        {
            PartSpec[] parts = partNames.Select((n, i) => Part(n, i + 1, i % 2 == 0 ? Box() : Box(profileCurve: 0), Entry(TexA), offset: new Vector3(i * 2, 0, 0), scale: new Vector3(1, 1 + i * 0.25f, 1))).ToArray();
            expected.Add(new JsonObject
            {
                ["object"] = parts[0].Name,
                ["fixture"] = name,
                ["parts"] = new JsonArray(parts.Select(p => (JsonNode)new JsonObject { ["name"] = p.Name, ["linkNumber"] = p.Link, ["partUuid"] = p.Id.ToString() }).ToArray())
            });
            objects.Add((name, ObjectXml(parts[0], parts.Skip(1).ToArray())));
        }
        return (OarWithAssets(objects.ToArray(), new[] { (TexA, (sbyte)AssetType.Texture, Texture(200, 40, 40)) }), expected);
    }

    /// <summary>Writes fixtures.oar, expected.json and the export of the archive into the folder: <c>default</c> as the command writes it and <c>clean</c> with --clean.</summary>
    public static void Write(string folder)
    {
        // an export folder that already holds builds would be mixed with these, so the folder must be new or empty
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException("the fixtures folder " + folder + " is not empty");
        Directory.CreateDirectory(folder);
        byte[] oar = Archive(out JsonArray expected);
        string file = Path.Combine(folder, "fixtures.oar");
        File.WriteAllBytes(file, oar);
        File.WriteAllText(Path.Combine(folder, "expected.json"), expected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        foreach ((string name, bool clean) in new[] { ("default", false), ("clean", true) })
        {
            ExportSummary s = ArchiveExporter.Export(file, new ExportOptions { OutputDirectory = Path.Combine(folder, name), Clean = clean });
            Assert.Equal(0, s.ExitCode);
        }

        string identity = Path.Combine(folder, "identity");
        Directory.CreateDirectory(identity);
        (byte[] identityOar, JsonArray identityExpected) = IdentityArchive();
        File.WriteAllBytes(Path.Combine(identity, "identity.oar"), identityOar);
        File.WriteAllText(Path.Combine(identity, "expected.json"), identityExpected.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(0, ArchiveExporter.Export(Path.Combine(identity, "identity.oar"), new ExportOptions { OutputDirectory = Path.Combine(identity, "export") }).ExitCode);
    }
}

/// <summary>The archive of <see cref="BlenderFixtures"/> exports, and what it comes out as is what the Blender check expects.</summary>
public class BlenderFixtureTests
{
    /// <summary>Set to a folder to keep the fixtures for the Blender import check (build/blender-check); otherwise they are written to a temporary folder.</summary>
    public const string FolderVariable = "OAR_TOOLS_BLENDER_FIXTURES";

    /// <summary>The .glb of the build whose manifest names the object (a folder is named from the object with its spaces and symbols replaced).</summary>
    private static string GlbOf(string exportFolder, string objectName)
    {
        foreach (string dir in Directory.GetDirectories(exportFolder))
        {
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest))
                continue;
            JsonElement m = JsonDocument.Parse(File.ReadAllText(manifest)).RootElement;
            if (m.GetProperty("name").GetString() == objectName)
                return Path.Combine(dir, m.GetProperty("file").GetString());
        }
        throw new InvalidOperationException("no build named " + objectName + " in " + exportFolder);
    }

    [Fact]
    public void EveryFixtureExportsWithItsPartsFacesTexturesAndSecondCoordinateSets()
    {
        string folder = Environment.GetEnvironmentVariable(FolderVariable);
        if (string.IsNullOrEmpty(folder))
            folder = Path.Combine(NewFolder(), "fixtures");
        BlenderFixtures.Write(folder);

        JsonArray expected = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "expected.json"))).AsArray();
        foreach (string layout in new[] { "default", "clean" })
        {
            foreach (JsonNode objectNode in expected)
            {
                string name = objectNode["object"].GetValue<string>();
                string glbPath = GlbOf(Path.Combine(folder, layout), name);
                Glb glb = ReadGlb(File.ReadAllBytes(glbPath));
                JsonNode[] want = objectNode["parts"].AsArray().ToArray();
                string[] exported = want.Where(p => p["status"].GetValue<string>() == "exported").Select(p => p["name"].GetValue<string>()).ToArray();
                Assert.Equal(exported, glb.PartNodes.Select(n => n.GetProperty("name").GetString()).ToArray());
                Assert.Equal(want.Count(p => p["status"].GetValue<string>() == "skipped"), want.Length - exported.Length);

                int meshes = 0;
                foreach (JsonElement node in glb.PartNodes)
                {
                    JsonNode part = want.Single(p => p["name"].GetValue<string>() == node.GetProperty("name").GetString());
                    JsonElement x = node.GetProperty("extras");
                    Assert.Equal(part["linkNumber"].GetValue<int>(), x.GetProperty("linkNumber").GetInt32());
                    Assert.Equal(part["creatorId"].GetValue<string>(), x.GetProperty("creatorId").GetString());
                    Assert.Equal(part["creatorName"].GetValue<string>(), x.GetProperty("creatorName").GetString());
                    if (!node.TryGetProperty("mesh", out JsonElement meshIndex))
                        continue;
                    meshes++;
                    foreach (JsonElement prim in glb.Root.GetProperty("meshes")[meshIndex.GetInt32()].GetProperty("primitives").EnumerateArray())
                    {
                        int face = prim.GetProperty("extras").GetProperty("primFace").GetInt32();
                        JsonNode over = part["faceTextures"][face.ToString()];
                        string texture = over != null ? over.GetValue<string>() : part["defaultTexture"].GetValue<string>();
                        JsonElement material = glb.Root.GetProperty("materials")[prim.GetProperty("material").GetInt32()].GetProperty("extras");
                        Assert.Equal(texture, material.GetProperty("textureId").GetString());
                        for (int set = 1; set <= 3; set++)
                        {
                            bool has = part["setFaces"][set.ToString()].AsArray().Any(n => n.GetValue<int>() == face);
                            Assert.Equal(has, prim.GetProperty("attributes").TryGetProperty("TEXCOORD_" + set, out _));
                        }
                    }
                }
                Assert.Equal(exported.Length, meshes);
            }
        }
    }
}
