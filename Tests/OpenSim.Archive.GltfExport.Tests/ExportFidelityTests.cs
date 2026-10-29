/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System.Text.Json;
using OpenMetaverse;
using OpenSim.Archive.Inspection;
using OpenSim.Framework;
using SkiaSharp;
using Xunit;
using static OpenSim.Archive.GltfExport.Tests.TestKit;

namespace OpenSim.Archive.GltfExport.Tests;

/// <summary>
/// What an export keeps: the geometry as built (nothing altered by default), the original texture coordinates as the first set,
/// the exact face mapping in a second set where the first cannot say it, and the identity of every face and part.
/// </summary>
public class ExportFidelityTests
{
    private static readonly UUID TexA = UUID.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly UUID TexB = UUID.Parse("a0000000-0000-4000-8000-0000000000b2");
    private static readonly UUID TexMissing = UUID.Parse("a0000000-0000-4000-8000-0000000000c3");

    private static readonly Dictionary<string, string> Shapes = new Dictionary<string, string>
    {
        ["box"] = Box(),
        ["box-hollow-cut"] = Box(hollow: 20000, profileBegin: 6250, profileEnd: 6250),
        ["cylinder-twist-taper"] = Box(profileCurve: 0, twist: 45, twistBegin: -45, taperX: 40, taperY: -30),
        ["prism"] = Box(profileCurve: 3),
        ["sphere"] = Box(profileCurve: 5, pathCurve: 32),
        ["torus-hollow"] = Box(profileCurve: 0, pathCurve: 32, scaleY: 175, hollow: 15000),
        ["tube-cut"] = Box(profileCurve: 1, pathCurve: 32, scaleY: 175, pathBegin: 2000, pathEnd: 2000),
        ["ring"] = Box(profileCurve: 3, pathCurve: 32, scaleY: 175)
    };

    public static IEnumerable<object[]> ShapeNames() => Shapes.Keys.Select(k => new object[] { k });

    // ------------------------------------------------------------------ helpers

    private static byte[] Jp2()
    {
        using SKBitmap bmp = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgb888x, SKAlphaType.Opaque));
        Assert.True(SkiaImageUtils.TryEncodeToJ2KLossless(bmp, out byte[] j2k));
        return j2k;
    }

    private sealed class Exported
    {
        public Glb Glb;
        public JsonElement Manifest;
        public ExportSummary Summary;
    }

    private static Exported ExportParts(PartSpec[] parts, bool withTextures = true, Action<ExportOptions> tweak = null)
    {
        string dir = NewFolder();
        string file = Path.Combine(dir, "fidelity.oar");
        byte[] j2k = Jp2();
        (UUID, sbyte, byte[])[] assets = withTextures
            ? new[] { (TexA, (sbyte)AssetType.Texture, j2k), (TexB, (sbyte)AssetType.Texture, j2k) }
            : Array.Empty<(UUID, sbyte, byte[])>();
        File.WriteAllBytes(file, OarWithAssets(new[] { ("fidelity", ObjectXml(parts[0], parts.Skip(1).ToArray())) }, assets));
        ExportOptions options = new ExportOptions { SelfContained = true, OutputDirectory = Path.Combine(dir, "out") };
        tweak?.Invoke(options);
        ExportSummary s = ArchiveExporter.Export(file, options);
        SummaryBuild b = Assert.Single(s.BuildList);
        string folder = Path.Combine(dir, "out", b.Folder);
        return new Exported
        {
            Summary = s,
            Glb = b.File == null ? null : ReadGlb(File.ReadAllBytes(Path.Combine(folder, b.File))),
            Manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"))).RootElement.Clone()
        };
    }

    private static IEnumerable<JsonElement> Primitives(Glb g) =>
        g.Root.GetProperty("meshes").EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray());

    private static JsonElement PrimitiveOfFace(Glb g, int face, int mesh = 0) =>
        g.Root.GetProperty("meshes")[mesh].GetProperty("primitives").EnumerateArray().Single(p => p.GetProperty("extras").GetProperty("primFace").GetInt32() == face);

    private static float[] Attribute(Glb g, JsonElement prim, string name) =>
        g.Floats(prim.GetProperty("attributes").Req(name).GetInt32());

    private static JsonElement MaterialOf(Glb g, JsonElement prim) => g.Root.GetProperty("materials")[prim.GetProperty("material").GetInt32()];

    private static string ImageNameOf(Glb g, JsonElement material)
    {
        int texture = material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture").GetProperty("index").GetInt32();
        int image = g.Root.GetProperty("textures")[texture].GetProperty("source").GetInt32();
        return g.Root.GetProperty("images")[image].GetProperty("name").GetString();
    }

    private static void AssertSameBits(float[] expected, float[] actual, string what)
    {
        Assert.True(expected.Length == actual.Length, what + ": " + expected.Length + " values expected, " + actual.Length + " found");
        for (int i = 0; i < expected.Length; i++)
            if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
                Assert.Fail(what + ": value " + i + " is " + actual[i].ToString("R") + ", the generated value is " + expected[i].ToString("R"));
    }

    private static List<PrimFace> Generated(string shapeXml, Vector3 scale)
    {
        ParsedGroup g = Group(new PartSpec { Name = "Part", Scale = scale, Shape = shapeXml });
        return PrimGeometry.BuildFaces(PrimParams.FromShape(g.Parts[0].Shape), DetailLevel.High, out _, out _);
    }

    private static string TextureEntryWith(Action<Primitive.TextureEntry> edit, UUID defaultTexture)
    {
        Primitive.TextureEntry te = new Primitive.TextureEntry(defaultTexture);
        edit(te);
        return Convert.ToBase64String(te.GetBytes());
    }

    private static Primitive.TextureEntryFace Face(Primitive.TextureEntry te, uint n, UUID texture)
    {
        Primitive.TextureEntryFace f = te.CreateFace(n);
        f.TextureID = texture;
        return f;
    }

    // ------------------------------------------------------------------ nothing altered by default

    [Fact]
    public void CleaningIsOffUnlessAskedFor()
    {
        Assert.False(new ExportOptions().Clean);
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void TheDefaultExportWritesTheGeneratedFacesExactlyAsBuilt(string shape)
    {
        Vector3 scale = new Vector3(2, 3, 4);
        PartSpec p = new PartSpec { Name = "Part", Scale = scale, Shape = Shapes[shape] };
        List<PrimFace> faces = Generated(Shapes[shape], scale);
        Exported e = ExportParts(new[] { p }, withTextures: false);

        Assert.False(e.Glb.Root.GetProperty("asset").TryGetProperty("extras", out _), "an unchanged file carries no cleaning stamp");
        JsonElement[] prims = Primitives(e.Glb).ToArray();
        Assert.Equal(faces.Count, prims.Length);
        for (int i = 0; i < faces.Count; i++)
        {
            Assert.False(prims[i].TryGetProperty("indices", out _), "face " + faces[i].Number + " is written as built, not indexed");
            Assert.Equal(faces[i].Number, prims[i].GetProperty("extras").GetProperty("primFace").GetInt32());

            float[] pos = new float[faces[i].Positions.Length];
            for (int k = 0; k < pos.Length; k += 3)
            {
                pos[k] = faces[i].Positions[k] * scale.X;
                pos[k + 1] = faces[i].Positions[k + 2] * scale.Z;
                pos[k + 2] = -(faces[i].Positions[k + 1] * scale.Y);
            }
            AssertSameBits(pos, Attribute(e.Glb, prims[i], "POSITION"), "positions of face " + faces[i].Number);
            AssertSameBits(faces[i].Uvs, Attribute(e.Glb, prims[i], "TEXCOORD_0"), "TEXCOORD_0 of face " + faces[i].Number);
        }
        Assert.Equal(faces.Sum(f => f.TriangleCount), e.Manifest.GetProperty("parts")[0].GetProperty("triangles").GetInt32());
    }

    [Fact]
    public void CleaningIsAnOptInThatStillWorks()
    {
        PartSpec p = new PartSpec { Name = "Part", Shape = Shapes["sphere"] };
        Exported plain = ExportParts(new[] { p }, withTextures: false);
        Exported cleaned = ExportParts(new[] { p }, withTextures: false, o => o.Clean = true);
        Assert.True(cleaned.Glb.Root.GetProperty("asset").GetProperty("extras").GetProperty("oarExport").GetProperty("clean").GetBoolean());
        int Vertices(Glb g) => Primitives(g).Sum(pr => g.Root.GetProperty("accessors")[pr.GetProperty("attributes").GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32());
        Assert.True(Vertices(cleaned.Glb) < Vertices(plain.Glb), "a sphere has duplicate vertices that cleaning merges");
    }

    // ------------------------------------------------------------------ the original UV map

    private static readonly string[] MappingCases = { "plain", "offset-only", "rotated-equal-repeats", "rotated-unequal-repeats", "planar", "planar-rotated-unequal" };

    public static IEnumerable<object[]> Mappings() => MappingCases.Select(c => new object[] { c });

    private static string TextureEntryForMapping(string mapping) => TextureEntryWith(te =>
    {
        for (uint n = 0; n < 6; n++)
        {
            Primitive.TextureEntryFace f = Face(te, n, n % 2 == 0 ? TexA : TexB);
            switch (mapping)
            {
                case "offset-only": f.OffsetU = 0.25f; f.OffsetV = -0.125f; break;
                case "rotated-equal-repeats": f.RepeatU = 2; f.RepeatV = 2; f.Rotation = 0.6f; break;
                case "rotated-unequal-repeats": f.RepeatU = 4; f.RepeatV = 2; f.Rotation = 0.6f; f.OffsetU = 0.1f; f.OffsetV = -0.2f; break;
                case "planar": f.TexMapType = MappingType.Planar; f.RepeatU = 3; break;
                case "planar-rotated-unequal": f.TexMapType = MappingType.Planar; f.RepeatU = 4; f.RepeatV = 2; f.Rotation = 0.6f; break;
            }
        }
    }, TexA);

    [Theory]
    [MemberData(nameof(Mappings))]
    public void TexCoord0IsTheOriginalMapOfEveryFaceWhateverItsTextureMapping(string mapping)
    {
        Vector3 scale = new Vector3(2, 4, 6);
        List<PrimFace> faces = Generated(Shapes["box"], scale);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Scale = scale, Shape = Shapes["box"], TextureEntryBase64 = TextureEntryForMapping(mapping) } });
        JsonElement[] prims = Primitives(e.Glb).ToArray();
        Assert.Equal(6, prims.Length);
        for (int i = 0; i < prims.Length; i++)
            AssertSameBits(faces[i].Uvs, Attribute(e.Glb, prims[i], "TEXCOORD_0"), mapping + ": TEXCOORD_0 of face " + faces[i].Number);
    }

    [Theory]
    [InlineData("sphere")]
    [InlineData("torus-hollow")]
    public void TexCoord0OfACurvedShapeIsTheGeneratedMapEvenWhenItsFacesAreRotatedWithUnequalRepeats(string shape)
    {
        Vector3 scale = new Vector3(1, 2, 3);
        List<PrimFace> faces = Generated(Shapes[shape], scale);
        string te = TextureEntryWith(t =>
        {
            Primitive.TextureEntryFace f = Face(t, 0, TexA);
            f.RepeatU = 3; f.RepeatV = 1; f.Rotation = 1.1f;
        }, TexA);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Scale = scale, Shape = Shapes[shape], TextureEntryBase64 = te } });
        JsonElement[] prims = Primitives(e.Glb).ToArray();
        for (int i = 0; i < prims.Length; i++)
            AssertSameBits(faces[i].Uvs, Attribute(e.Glb, prims[i], "TEXCOORD_0"), "TEXCOORD_0 of face " + faces[i].Number);
    }

    // The viewer's face transform, written out: turn about the middle, repeat, offset, with v up.
    private static (float u, float v) Transform(float u, float vUp, float repU, float repV, float offU, float offV, float rot)
    {
        float s = u - 0.5f, t = vUp - 0.5f;
        float temp = s;
        s = s * MathF.Cos(rot) + t * MathF.Sin(rot);
        t = -temp * MathF.Sin(rot) + t * MathF.Cos(rot);
        return (s * repU + offU + 0.5f, t * repV + offV + 0.5f);
    }

    private static float[] Pair(JsonElement e) => e.EnumerateArray().Select(v => v.GetSingle()).ToArray();

    [Fact]
    public void ARotatedFaceWithUnequalRepeatsHasItsExactMappingInTexCoord1AndOnlyItsMaterialUsesIt()
    {
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Shape = Shapes["box"], TextureEntryBase64 = TextureEntryForMapping("rotated-unequal-repeats") } });
        foreach (JsonElement prim in Primitives(e.Glb))
        {
            float[] uv0 = Attribute(e.Glb, prim, "TEXCOORD_0");
            float[] uv1 = Attribute(e.Glb, prim, "TEXCOORD_1");
            JsonElement material = MaterialOf(e.Glb, prim);
            JsonElement x = material.GetProperty("extras");
            float[] rep = Pair(x.GetProperty("repeat")), off = Pair(x.GetProperty("offset"));
            float rot = x.GetProperty("rotation").GetSingle();
            Assert.Equal(uv0.Length, uv1.Length);
            for (int i = 0; i < uv0.Length; i += 2)
            {
                (float eu, float ev) = Transform(uv0[i], 1f - uv0[i + 1], rep[0], rep[1], off[0], off[1], rot);
                Assert.Equal(eu, uv1[i], 4);
                Assert.Equal(1f - ev, uv1[i + 1], 4);
            }
            JsonElement tex = material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture");
            Assert.Equal(1, tex.Req("texCoord").GetInt32());
            Assert.False(tex.TryGetProperty("extensions", out _), "TEXCOORD_1 is already exact, so no transform is applied on top of it");
            Assert.Equal("TEXCOORD_1", x.Req("uvSet").GetString());
        }
    }

    [Fact]
    public void ABoxWithMixedMappingsUsesTexCoord1OnlyOnTheFacesThatNeedIt()
    {
        string te = TextureEntryWith(t =>
        {
            Face(t, 1, TexA).RepeatU = 4;                                   // faces 1: rotated with unequal repeats
            Primitive.TextureEntryFace f1 = t.FaceTextures[1];
            f1.RepeatV = 2; f1.Rotation = 0.6f;
            Face(t, 2, TexB).TexMapType = MappingType.Planar;               // face 2: planar
            Primitive.TextureEntryFace f3 = Face(t, 3, TexA);               // face 3: rotated, equal repeats: the extension says it exactly
            f3.RepeatU = 2; f3.RepeatV = 2; f3.Rotation = 0.5f;
        }, TexA);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Shape = Shapes["box"], TextureEntryBase64 = te } });
        foreach (JsonElement prim in Primitives(e.Glb))
        {
            int face = prim.GetProperty("extras").GetProperty("primFace").GetInt32();
            bool needs = face == 1 || face == 2;
            Assert.Equal(needs, prim.GetProperty("attributes").TryGetProperty("TEXCOORD_1", out _));
            JsonElement tex = MaterialOf(e.Glb, prim).GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture");
            Assert.Equal(needs, tex.TryGetProperty("texCoord", out JsonElement set) && set.GetInt32() == 1);
            Assert.Equal(needs ? "TEXCOORD_1" : "TEXCOORD_0", MaterialOf(e.Glb, prim).GetProperty("extras").Req("uvSet").GetString());
        }
        // the face the extension can describe keeps the extension
        JsonElement exact = MaterialOf(e.Glb, PrimitiveOfFace(e.Glb, 3)).GetProperty("pbrMetallicRoughness").GetProperty("baseColorTexture");
        Assert.Equal(2f, exact.GetProperty("extensions").GetProperty("KHR_texture_transform").GetProperty("scale")[0].GetSingle());
    }

    [Fact]
    public void APlanarFaceHasTheProjectedMapInTexCoord1AndItsOriginalMapInTexCoord0()
    {
        // a box 2 x 4 x 6 metres, its top face planar with no repeats, offset or rotation. On the top face the binormal is along x and the
        // tangent along -y, and the planar map repeats once every half metre: u = 1 + (2x - 0.5), v (up) = 2y + 0.5 with x and y in metres.
        Vector3 scale = new Vector3(2, 4, 6);
        string te = TextureEntryWith(t => Face(t, 0, TexA).TexMapType = MappingType.Planar, TexA);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Scale = scale, Shape = Shapes["box"], TextureEntryBase64 = te } });
        JsonElement top = PrimitiveOfFace(e.Glb, 0);
        float[] pos = Attribute(e.Glb, top, "POSITION"), normal = Attribute(e.Glb, top, "NORMAL"), uv1 = Attribute(e.Glb, top, "TEXCOORD_1");
        Assert.True(Enumerable.Range(0, normal.Length / 3).All(i => normal[i * 3 + 1] > 0.99f), "face 0 of a box is its top, whose glTF normal is +y");
        for (int i = 0; i < pos.Length / 3; i++)
        {
            float x = pos[i * 3], y = -pos[i * 3 + 2];
            Assert.Equal(1f + (2f * x - 0.5f), uv1[i * 2], 4);
            Assert.Equal(1f - (2f * y + 0.5f), uv1[i * 2 + 1], 4);
        }
        Assert.Equal("planar", MaterialOf(e.Glb, top).GetProperty("extras").GetProperty("mapping").GetString());
        Assert.Equal("planar mapping", MaterialOf(e.Glb, top).GetProperty("extras").Req("uvSetReason").GetString());
    }

    [Fact]
    public void EverySlMappingValueStaysInTheMaterialExtrasWhicheverUvSetIsUsed()
    {
        string te = TextureEntryWith(t =>
        {
            Primitive.TextureEntryFace f = Face(t, 0, TexA);
            f.TexMapType = MappingType.Planar; f.RepeatU = 3; f.RepeatV = 2; f.OffsetU = 0.25f; f.OffsetV = -0.25f; f.Rotation = 0.5f;
        }, TexA);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Shape = Shapes["box"], TextureEntryBase64 = te } });
        JsonElement x = MaterialOf(e.Glb, PrimitiveOfFace(e.Glb, 0)).GetProperty("extras");
        Assert.Equal(TexA.ToString(), x.GetProperty("textureId").GetString());
        Assert.Equal(new[] { 3f, 2f }, Pair(x.GetProperty("repeat")));
        Assert.Equal(0.25f, Pair(x.GetProperty("offset"))[0], 3);
        Assert.Equal(-0.25f, Pair(x.GetProperty("offset"))[1], 3);
        Assert.Equal(0.5f, x.GetProperty("rotation").GetSingle(), 3);
        Assert.Equal("planar", x.GetProperty("mapping").GetString());
    }

    // ------------------------------------------------------------------ identity

    [Fact]
    public void EveryFaceCarriesItsTextureUuidInTheMaterialExtrasAndAsTheImageName()
    {
        UUID[] wanted = { TexA, TexB, TexA, TexB, TexA, TexB };
        string te = TextureEntryWith(t => { for (uint n = 0; n < 6; n++) Face(t, n, wanted[n]); }, TexA);
        PartSpec root = new PartSpec { Name = "Root", Link = 1, Shape = Shapes["box"], TextureEntryBase64 = te };
        PartSpec child = new PartSpec { Name = "Child", Link = 2, Shape = Shapes["cylinder-twist-taper"], TextureEntryBase64 = TextureEntryWith(t => { }, TexB) };
        Exported e = ExportParts(new[] { root, child });
        JsonElement[] rootPrims = e.Glb.Root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray().ToArray();
        for (int i = 0; i < rootPrims.Length; i++)
        {
            JsonElement m = MaterialOf(e.Glb, rootPrims[i]);
            Assert.Equal(wanted[rootPrims[i].GetProperty("extras").GetProperty("primFace").GetInt32()].ToString(), m.GetProperty("extras").GetProperty("textureId").GetString());
            Assert.Equal(m.GetProperty("extras").GetProperty("textureId").GetString(), ImageNameOf(e.Glb, m));
        }
        foreach (JsonElement prim in e.Glb.Root.GetProperty("meshes")[1].GetProperty("primitives").EnumerateArray())
            Assert.Equal(TexB.ToString(), ImageNameOf(e.Glb, MaterialOf(e.Glb, prim)));
    }

    [Fact]
    public void AFaceWhoseTextureIsNotInTheArchiveStillCarriesItsUuidAndIsFlagged()
    {
        string te = TextureEntryWith(t => { Face(t, 1, TexMissing); }, TexA);
        Exported e = ExportParts(new[] { new PartSpec { Name = "Part", Shape = Shapes["box"], TextureEntryBase64 = te } });
        JsonElement x = MaterialOf(e.Glb, PrimitiveOfFace(e.Glb, 1)).GetProperty("extras");
        Assert.Equal(TexMissing.ToString(), x.GetProperty("textureId").GetString());
        Assert.Equal("missing-from-archive", x.GetProperty("textureStatus").GetString());
        Assert.True(x.GetProperty("placeholder").GetBoolean());
        Assert.Equal(TexMissing.ToString(), MaterialOf(e.Glb, PrimitiveOfFace(e.Glb, 1)).GetProperty("name").GetString());
        JsonElement row = e.Manifest.GetProperty("textures").EnumerateArray().Single(t => t.GetProperty("textureId").GetString() == TexMissing.ToString());
        Assert.Equal("missing-from-archive", row.GetProperty("status").GetString());
        Assert.Equal(1, e.Manifest.GetProperty("texturesMissing").GetInt32());
    }

    [Fact]
    public void ANodeCarriesTheCreatorsNameWhenTheArchiveHasOne()
    {
        PartSpec p = new PartSpec { Name = "Named", Link = 1, Creator = Bob, Owner = Bob, CreatorData = "http://grid.example.org:8002;Test Creator" };
        Exported e = ExportParts(new[] { p }, withTextures: false);
        JsonElement x = e.Glb.PartNodes.Single().GetProperty("extras");
        Assert.Equal(Bob.ToString(), x.GetProperty("creatorId").GetString());
        Assert.Equal("Test Creator", x.GetProperty("creatorName").GetString());
    }

    [Fact]
    public void TheObjectNodeCarriesTheRootNameAndTheLinkOrderOfItsParts()
    {
        // file order 1, 4, 2, 3; the link order is what the object node lists
        PartSpec[] parts =
        {
            new PartSpec { Name = "Root", Link = 1 }, new PartSpec { Name = "D", Link = 4 }, new PartSpec { Name = "B", Link = 2 }, new PartSpec { Name = "C", Link = 3 }
        };
        Exported e = ExportParts(parts, withTextures: false);
        JsonElement x = e.Glb.ObjectNode.GetProperty("extras");
        Assert.Equal("Root", x.Req("rootPartName").GetString());
        JsonElement[] order = x.Req("linkOrder").EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 2, 3, 4 }, order.Select(o => o.GetProperty("linkNumber").GetInt32()).ToArray());
        Assert.Equal(new[] { "Root", "B", "C", "D" }, order.Select(o => o.GetProperty("name").GetString()).ToArray());
        Assert.Equal(parts.Select(p => p.Id.ToString()).OrderBy(s => s).ToArray(), order.Select(o => o.GetProperty("partUuid").GetString()).OrderBy(s => s).ToArray());
    }

    [Fact]
    public void AFiftyPartLinksetKeepsEveryNameAndLinkNumberInLinkOrder()
    {
        List<PartSpec> parts = new List<PartSpec>();
        for (int i = 1; i <= 50; i++)
            parts.Add(new PartSpec { Name = i.ToString("00") + " Part " + (char)('A' + i % 26), Link = i, Description = "d" + i, Offset = new Vector3(i, 0, 0) });
        // a different file order from the link order
        parts.Reverse(1, 49);
        Exported e = ExportParts(parts.ToArray(), withTextures: false);
        JsonElement[] nodes = e.Glb.PartNodes.ToArray();
        Assert.Equal(50, nodes.Length);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(i + 1, nodes[i].GetProperty("extras").GetProperty("linkNumber").GetInt32());
            Assert.Equal((i + 1).ToString("00") + " Part " + (char)('A' + (i + 1) % 26), nodes[i].GetProperty("name").GetString());
        }
    }

    // ------------------------------------------------------------------ the export-permission rule

    private const uint ExportBit = (uint)OpenMetaverse.PermissionMask.Export;

    [Fact]
    public void AnUnexportablePartIsNowhereInTheGlbWhileTheRestOfTheObjectIsThere()
    {
        PartSpec root = new PartSpec { Name = "Open root", Link = 1 };
        PartSpec refused = new PartSpec { Name = "Refused part name", Link = 2, Creator = Alice, Owner = Bob, BaseMask = ExportBit | 0x7fff, OwnerMask = 0x7fff, EveryoneMask = 0 };
        PartSpec third = new PartSpec { Name = "Open third", Link = 3 };
        Exported e = ExportParts(new[] { root, refused, third }, withTextures: false);

        Assert.Equal(new[] { "Open root", "Open third" }, e.Glb.PartNodes.Select(n => n.GetProperty("name").GetString()).ToArray());
        string json = e.Glb.Json.RootElement.GetRawText();
        Assert.DoesNotContain("Refused part name", json);
        Assert.DoesNotContain(refused.Id.ToString(), json);

        JsonElement[] listed = e.Manifest.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal("skipped", listed[1].GetProperty("status").GetString());
        Assert.Contains("creator did not allow export", listed[1].GetProperty("reason").GetString());
        Assert.Equal(1, e.Summary.Skipped);
        Assert.Contains(e.Summary.Reasons.Keys, k => k.StartsWith("skipped: creator did not allow export", StringComparison.Ordinal));
    }

    [Fact]
    public void APartExportsNormallyOnceItsPermissionsAllow()
    {
        PartSpec held = new PartSpec { Name = "Held", Link = 1, Creator = Alice, Owner = Bob, BaseMask = ExportBit | 0x7fff, OwnerMask = 0x7fff, EveryoneMask = ExportBit };
        Exported e = ExportParts(new[] { held }, withTextures: false);
        Assert.Equal("exported", e.Manifest.GetProperty("parts")[0].GetProperty("status").GetString());
        Assert.Equal(0, e.Summary.Skipped);
    }
}
